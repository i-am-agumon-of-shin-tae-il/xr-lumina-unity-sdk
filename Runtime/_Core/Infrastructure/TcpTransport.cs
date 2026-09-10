using System;
using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Threading;
using XRLumina._Core.Model;

namespace XRLumina._Core.Infrastructure
{
    /// <summary>
    /// TCP 연결, 재접속, 패킷 송수신만 담당하는 전송 계층이다.
    /// 수신 루프와 이벤트는 백그라운드 스레드에서 실행되므로 Unity API를 호출하지 않는다.
    /// </summary>
    internal sealed class TcpTransport : IDisposable
    {
        private const int MaxPendingPackets = 30000;
        private readonly Func<NetworkEndpoint> _resolveEndpoint;
        private readonly int _retryDelayMs;
        private readonly object _writeLock = new object();
        private readonly ConcurrentQueue<NetworkPacket> _pendingPackets =
            new ConcurrentQueue<NetworkPacket>();
        private readonly ConcurrentQueue<NetworkPacket> _sendQueue =
            new ConcurrentQueue<NetworkPacket>();
        private readonly AutoResetEvent _sendSignal = new AutoResetEvent(false);
        private Thread _thread;
        private Thread _sendThread;
        private TcpClient _client;
        private NetworkStream _stream;
        private byte[] _latestMirrorFrame;
        private volatile bool _running;
        private int _pendingPacketCount;

        public event Action Connected;
        public event Action Disconnected;
        public event Action<NetworkPacket> PacketReceived;
        public event Action<Exception> Faulted;

        /// <summary>주소 해석 함수와 재접속 간격으로 TCP 전송 계층을 생성한다.</summary>
        public TcpTransport(Func<NetworkEndpoint> resolveEndpoint, int retryDelayMs)
        {
            _resolveEndpoint = resolveEndpoint ?? throw new ArgumentNullException(nameof(resolveEndpoint));
            _retryDelayMs = retryDelayMs;
        }

        /// <summary>백그라운드 연결 및 수신 루프를 시작한다.</summary>
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

        /// <summary>연결된 스트림의 비동기 송신 대기열에 패킷 하나를 추가한다.</summary>
        public bool Send(PacketType type, byte[] payload)
        {
            lock (_writeLock)
            {
                if (_stream == null)
                {
                    return false;
                }
            }

            if (type == PacketType.MirrorFrame)
            {
                Interlocked.Exchange(ref _latestMirrorFrame, payload);
            }
            else
            {
                _sendQueue.Enqueue(new NetworkPacket(type, payload));
            }
            _sendSignal.Set();
            return true;
        }

        /// <summary>전송하지 못한 패킷을 제한된 재전송 대기열에 추가한다.</summary>
        public void Enqueue(PacketType type, byte[] payload)
        {
            while (Volatile.Read(ref _pendingPacketCount) >= MaxPendingPackets &&
                   _pendingPackets.TryDequeue(out _))
            {
                Interlocked.Decrement(ref _pendingPacketCount);
            }
            _pendingPackets.Enqueue(new NetworkPacket(type, payload));
            Interlocked.Increment(ref _pendingPacketCount);
        }

        /// <summary>재연결 후 대기 중인 패킷을 발생 순서대로 전송한다.</summary>
        public void SendPending()
        {
            while (_pendingPackets.TryDequeue(out var packet))
            {
                Interlocked.Decrement(ref _pendingPacketCount);
                if (!Send(packet.Type, packet.Payload))
                {
                    Enqueue(packet.Type, packet.Payload);
                    break;
                }
            }
        }

        /// <summary>수신 루프를 중단하고 현재 연결을 닫는다.</summary>
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

        /// <summary>대기 중인 패킷을 전용 송신 스레드에서 순서대로 기록한다.</summary>
        private void SendLoop()
        {
            while (_running)
            {
                if (_sendQueue.TryDequeue(out var packet))
                {
                    WritePacket(packet);
                    continue;
                }

                var mirrorFrame = Interlocked.Exchange(ref _latestMirrorFrame, null);
                if (mirrorFrame != null)
                {
                    WritePacket(new NetworkPacket(PacketType.MirrorFrame, mirrorFrame));
                    continue;
                }

                _sendSignal.WaitOne(100);
            }
        }

        /// <summary>송신 스레드에서 패킷을 기록하고 실패한 측정 패킷은 재전송 대상으로 보존한다.</summary>
        private void WritePacket(NetworkPacket packet)
        {
            NetworkStream stream;
            lock (_writeLock)
            {
                stream = _stream;
            }

            if (stream == null)
            {
                PreserveUnsentPacket(packet);
                return;
            }

            try
            {
                PacketCodec.Write(stream, packet.Type, packet.Payload);
            }
            catch (Exception exception)
            {
                PreserveUnsentPacket(packet);
                Faulted?.Invoke(exception);
                try { _client?.Close(); } catch { }
            }
        }

        /// <summary>미러링을 제외한 미전송 패킷을 연결 복구용 대기열에 저장한다.</summary>
        private void PreserveUnsentPacket(NetworkPacket packet)
        {
            if (packet.Type != PacketType.MirrorFrame)
            {
                Enqueue(packet.Type, packet.Payload);
            }
        }

        /// <summary>접속 실패와 연결 종료를 처리하고 설정된 간격으로 다시 연결한다.</summary>
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
