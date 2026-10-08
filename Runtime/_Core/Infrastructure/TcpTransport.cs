using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Threading;
using XRLumina._Core.Model;

namespace XRLumina._Core.Infrastructure
{
    /// <summary>TCP 재접속과 송수신을 담당하고 세션 복구 전까지 측정 패킷을 보존한다.</summary>
    internal sealed class TcpTransport : IDisposable
    {
        private const long MaxQueuedBytes = 64L * 1024 * 1024;
        private readonly Func<NetworkEndpoint> _resolveEndpoint;
        private readonly int _retryDelayMs;
        private readonly object _writeLock = new object();
        private readonly object _queueLock = new object();
        private readonly Queue<NetworkPacket> _sendQueue = new();
        private readonly Queue<NetworkPacket> _controlQueue = new();
        private readonly AutoResetEvent _sendSignal = new AutoResetEvent(false);
        private Thread _thread;
        private Thread _sendThread;
        private TcpClient _client;
        private NetworkStream _stream;
        private byte[] _latestMirrorFrame;
        private volatile bool _running;
        private volatile bool _sessionReady;
        private long _queuedBytes;
        private string _queueError;

        public event Action Connected;
        public event Action Disconnected;
        public event Action<NetworkPacket> PacketReceived;
        public event Action<Exception> Faulted;
        internal string QueueError => _queueError;

        /// <summary>주소 해석 함수와 재접속 간격으로 TCP 전송 계층을 생성한다.</summary>
        public TcpTransport(Func<NetworkEndpoint> resolveEndpoint, int retryDelayMs)
        {
            _resolveEndpoint = resolveEndpoint ?? throw new ArgumentNullException(nameof(resolveEndpoint));
            _retryDelayMs = retryDelayMs;
        }

        /// <summary>연결·수신 스레드와 전용 송신 스레드를 시작한다.</summary>
        public void Start()
        {
            if (_running)
            {
                return;
            }
            _running = true;
            _thread = new Thread(Run) { IsBackground = true, };
            _sendThread = new Thread(SendLoop) { IsBackground = true, };
            _thread.Start();
            _sendThread.Start();
        }

        /// <summary>측정과 flush는 같은 순서로 보존하고 미러링은 최신 프레임만 유지한다.</summary>
        public bool Send(PacketType type, byte[] payload)
        {
            if (!_running || payload == null)
            {
                return false;
            }
            if (type == PacketType.MirrorFrame)
            {
                if (!_sessionReady)
                {
                    return false;
                }
                Interlocked.Exchange(ref _latestMirrorFrame, payload);
                _sendSignal.Set();
                return true;
            }
            lock (_queueLock)
            {
                if (_queuedBytes + payload.Length > MaxQueuedBytes)
                {
                    _queueError = "SDK 송신 대기 데이터가 64MiB를 초과했습니다. 측정 데이터가 불완전하므로 연결을 확인해 주세요.";
                    return false;
                }
                _sendQueue.Enqueue(new NetworkPacket(type, payload));
                _queuedBytes += payload.Length;
            }
            _sendSignal.Set();
            return true;
        }

        /// <summary>세션 승인·heartbeat를 측정 큐와 분리해 연결 복구가 막히지 않도록 전송한다.</summary>
        internal bool SendControl(PacketType type, byte[] payload)
        {
            lock (_writeLock)
            {
                if (_stream == null || !_running)
                {
                    return false;
                }
            }
            lock (_queueLock)
            {
                if (_controlQueue.Count >= 128)
                {
                    return false;
                }
                _controlQueue.Enqueue(new NetworkPacket(type, payload));
            }
            _sendSignal.Set();
            return true;
        }

        /// <summary>대기 한도 초과를 조용히 버리지 않고 측정 실패로 기록한다.</summary>
        public void Enqueue(PacketType type, byte[] payload)
        {
            if (!Send(type, payload))
            {
                Faulted?.Invoke(new InvalidOperationException(_queueError ?? "Transport unavailable."));
            }
        }

        /// <summary>session ack 뒤에 보존된 측정·flush 패킷 송신을 재개한다.</summary>
        public void SendPending()
        {
            _sessionReady = true;
            _sendSignal.Set();
        }

        /// <summary>명시적인 선택 해제 시에만 이전 측정의 대기 데이터와 오류를 초기화한다.</summary>
        internal void ClearPending()
        {
            lock (_queueLock)
            {
                _sendQueue.Clear();
                _controlQueue.Clear();
                _queuedBytes = 0;
                _queueError = null;
            }
            Interlocked.Exchange(ref _latestMirrorFrame, null);
            _sessionReady = false;
        }

        /// <summary>실행을 중단하고 스트림을 닫아 블로킹 송수신을 해제한다.</summary>
        public void Dispose()
        {
            _running = false;
            _sendSignal.Set();
            try { _client?.Close(); } catch { }
            lock (_writeLock)
            {
                _stream = null;
            }
        }

        /// <summary>승인 패킷을 먼저 보내고 측정 패킷은 성공한 뒤에만 큐에서 제거한다.</summary>
        private void SendLoop()
        {
            while (_running)
            {
                NetworkStream stream;
                lock (_writeLock)
                {
                    stream = _stream;
                }
                NetworkPacket packet = default;
                var hasPacket = false;
                var control = false;
                lock (_queueLock)
                {
                    if (stream != null && _controlQueue.Count > 0)
                    {
                        packet = _controlQueue.Dequeue();
                        hasPacket = true;
                        control = true;
                    }
                    else if (stream != null && _sessionReady && _sendQueue.Count > 0)
                    {
                        packet = _sendQueue.Peek();
                        hasPacket = true;
                    }
                }
                if (hasPacket)
                {
                    if (WritePacket(stream, packet) && !control)
                    {
                        lock (_queueLock)
                        {
                            if (_sendQueue.Count > 0 && ReferenceEquals(_sendQueue.Peek().Payload, packet.Payload))
                            {
                                _sendQueue.Dequeue();
                                _queuedBytes -= packet.Payload.Length;
                            }
                        }
                    }
                    continue;
                }
                var mirror = Interlocked.Exchange(ref _latestMirrorFrame, null);
                if (stream != null && _sessionReady && mirror != null)
                {
                    WritePacket(stream, new NetworkPacket(PacketType.MirrorFrame, mirror));
                    continue;
                }
                _sendSignal.WaitOne(100);
            }
        }

        /// <summary>소켓 쓰기 실패 시 송신을 중지하고 측정 큐는 다음 세션 복구까지 보존한다.</summary>
        private bool WritePacket(NetworkStream stream, NetworkPacket packet)
        {
            try
            {
                PacketCodec.Write(stream, packet.Type, packet.Payload);
                return true;
            }
            catch (Exception exception)
            {
                lock (_writeLock)
                {
                    if (_stream == stream)
                    {
                        _stream = null;
                        _sessionReady = false;
                        try { _client?.Close(); } catch { }
                    }
                }
                Faulted?.Invoke(exception);
                return false;
            }
        }

        /// <summary>연결 종료 후 같은 transport에서 재접속하여 보존된 측정 큐를 유지한다.</summary>
        private void Run()
        {
            while (_running)
            {
                try
                {
                    var endpoint = _resolveEndpoint();
                    _client = new TcpClient();
                    _client.NoDelay = true;
                    _client.SendTimeout = 2000;
                    // heartbeat 지원 여부와 무관하게 정상 대기 연결을 유지한다.
                    _client.ReceiveTimeout = 0;
                    _client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
                    _client.Connect(endpoint.Host, endpoint.Port);
                    using (var stream = _client.GetStream())
                    {
                        lock (_writeLock)
                        {
                            _stream = stream;
                        }
                        Connected?.Invoke();
                        _sendSignal.Set();
                        while (_running && PacketCodec.TryRead(stream, out var packet))
                        {
                            PacketReceived?.Invoke(packet);
                        }
                    }
                }
                catch (Exception exception)
                {
                    if (_running)
                    {
                        Faulted?.Invoke(exception);
                    }
                }
                finally
                {
                    lock (_writeLock)
                    {
                        _stream = null;
                        _sessionReady = false;
                    }
                    lock (_queueLock)
                    {
                        _controlQueue.Clear();
                    }
                    Interlocked.Exchange(ref _latestMirrorFrame, null);
                    try { _client?.Close(); } catch { }
                    Disconnected?.Invoke();
                }
                if (_running)
                {
                    Thread.Sleep(_retryDelayMs);
                }
            }
        }
    }
}
