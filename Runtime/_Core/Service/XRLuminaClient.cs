using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using XRLumina.Protocol;
using XRLumina._Core.Composition;
using XRLumina._Core.Infrastructure;
using XRLumina._Core.Messaging.Receivers;
using XRLumina._Core.Messaging.Senders;
using XRLumina._Core.Model;

namespace XRLumina.Client
{
    /// <summary>XRLumina 연결, 세션과 메시지 라우팅을 담당한다.</summary>
    public sealed class XRLuminaClient : IDisposable
    {
        private readonly ConcurrentQueue<Action> _engineActions = new();
        private readonly NetworkEndpointDiscovery _endpointDiscovery;
        private readonly Dictionary<string, TcpTransport> _transports = new();
        private readonly int _retryDelayMs;
        private volatile bool _running;
        private Thread _discoveryThread;
        private ManualResetEvent _discoveryStop;
        private readonly object _discoveryLock = new object();
        private volatile int _generation;
        private bool _disposed;
        private bool _inTick;
        private readonly int _engineThreadId = Thread.CurrentThread.ManagedThreadId;
        private UsbConnectionListener _usbListener;
        private readonly bool _usbEnabled;
        private readonly int _usbPort;
        private readonly MessageSender _messageSender;
        private readonly ConnectionMessageReceiver _connections;
        private readonly SessionMessageReceiver _session;
        private readonly ApiMessageReceiver _api = new();
        private readonly MessageRouter _router;

        public event Action<TestAction> CommandReceived;
        public event Action SessionAccepted;
        public event Action Disconnected;
        public event Action MeasurementStarted;
        public event Action MeasurementFinished;

        public event Action SessionReady;

        public XRLuminaSession Session => _session.Session;
        public bool IsAuthenticated => _session.IsAuthenticated;
        public bool IsMeasuring => _session.IsMeasuring;
        public bool IsRunning => _running;
        public event Action<Exception> Faulted;

        internal MessageSender MessageSender => _messageSender;

        /// <summary>엔진이 제공한 클라이언트 정보와 연결 설정으로 공통 클라이언트를 구성한다.</summary>
        public XRLuminaClient(XRLuminaClientOptions options)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }
            if (string.IsNullOrWhiteSpace(options.ClientId))
            {
                throw new ArgumentException("A persistent ClientId is required.", nameof(options));
            }
            if (options.Port < 1 || options.Port > 65535 || options.DiscoveryPort < 1 ||
                options.DiscoveryPort > 65535 || options.UsbPort < 1 || options.UsbPort > 65535 ||
                options.DiscoveryTimeoutMs < 1 || options.RetryDelayMs < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(options), "Invalid connection settings.");
            }
            _endpointDiscovery = new NetworkEndpointDiscovery(options.Host, options.Port,
                options.DiscoveryPort, options.DiscoveryTimeoutMs,
                ConnectionMessageSender.CreateDiscoveryRequest(),
                ConnectionMessageReceiver.ParseDiscoveryResponse);
            _retryDelayMs = options.RetryDelayMs;
            _usbEnabled = options.UsbEnabled;
            _usbPort = options.UsbPort;
            _session = new SessionMessageReceiver(_api, exception => Faulted?.Invoke(exception));
            var connectionSender = new ConnectionMessageSender(options.ClientId, options.Model, options.Name);
            _connections = new ConnectionMessageReceiver(_transports, _session, connectionSender,
                exception => Faulted?.Invoke(exception));
            _messageSender = new MessageSender(() => _connections.SelectedTransport);
            _router = ClientMessageRoutes.Create(_connections, _session, _api,
                exception => Faulted?.Invoke(exception));
            _connections.Disconnected += () => Disconnected?.Invoke();
            _session.SessionReady += () => SessionReady?.Invoke();
            _session.SessionAccepted += () => SessionAccepted?.Invoke();
            _session.CommandReceived += action => CommandReceived?.Invoke(action);
            _session.MeasurementStarted += () => MeasurementStarted?.Invoke();
            _session.MeasurementFinished += () => MeasurementFinished?.Invoke();
        }

        /// <summary>엔진의 시작 시점에 호출하여 연결과 탐색을 시작한다.</summary>
        public void Start()
        {
            VerifyAccess();
            if (_running)
            {
                return;
            }
            _running = true;
            int generation = ++_generation;
            if (_usbEnabled)
            {
                _usbListener = new UsbConnectionListener(_usbPort, client =>
                {
                    if (!_running || generation != _generation)
                    {
                        client.Close();
                        return;
                    }
                    RegisterTransport("usb:" + Guid.NewGuid(), new TcpTransport(client));
                }, exception => QueueFault(generation, exception));
                _usbListener.Start();
            }
            var stop = new ManualResetEvent(false);
            lock (_discoveryLock)
            {
                _discoveryStop = stop;
            }
            _discoveryThread = new Thread(() => DiscoverHosts(generation, stop)) { IsBackground = true, };
            _discoveryThread.Start();
        }

        /// <summary>엔진의 단일 스레드 루프에서 수신 결과, 시간 제한과 사용자 콜백을 진행한다.</summary>
        public void Tick(double deltaSeconds)
        {
            VerifyAccess();
            if (double.IsNaN(deltaSeconds) || double.IsInfinity(deltaSeconds) || deltaSeconds < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(deltaSeconds));
            }
            if (_inTick)
            {
                throw new InvalidOperationException("Tick must not be called recursively.");
            }
            _inTick = true;
            try
            {
                _api.Advance(deltaSeconds);
                _usbListener?.Tick();
                int count = _engineActions.Count;
                for (int i = 0; i < count && !_disposed && _engineActions.TryDequeue(out var action); i++)
                {
                    action();
                }
                if (!_disposed)
                {
                    _api.ExpireRequests();
                    _api.DispatchCompletions();
                }
            }
            finally
            {
                _inTick = false;
            }
        }

        /// <summary>연결을 중단하고 미완료 Flush는 다음 Tick에서 취소 결과를 전달한다.</summary>
        public void Stop()
        {
            VerifyAccess();
            _running = false;
            ++_generation;
            lock (_discoveryLock)
            {
                _discoveryStop?.Set();
                _discoveryStop = null;
            }
            _connections.Reset("client stopped");
            _usbListener?.Dispose();
            _usbListener = null;
            foreach (var transport in _transports.Values)
            {
                transport.Dispose();
            }
            _transports.Clear();
            while (_engineActions.TryDequeue(out _))
            {
            }
        }

        /// <summary>선택한 데스크톱에 테스트 시작을 요청한다.</summary>
        public bool RequestStart()
        {
            VerifyAccess();
            return IsAuthenticated && SessionMessageSender.SendLifecycleRequest(_connections.SelectedTransport, TestAction.Start);
        }

        /// <summary>선택한 데스크톱에 테스트 종료를 요청한다.</summary>
        public bool RequestFinish()
        {
            VerifyAccess();
            return IsAuthenticated && SessionMessageSender.SendLifecycleRequest(_connections.SelectedTransport, TestAction.Finish);
        }

        /// <summary>엔진이 규약에 맞게 구성한 측정 데이터를 선택한 연결로 전송한다.</summary>
        public bool Send(MessageUnion message)
        {
            VerifyAccess();
            if (message == null || message.Type < Message.MirrorFrame || message.Type > Message.HeuristicScreenshot)
            {
                throw new ArgumentException("Only measurement messages may be sent through this API.", nameof(message));
            }
            return IsAuthenticated && _messageSender.SendMessage(message);
        }

        /// <summary>엔진이 인코딩한 JPEG 미러링 이미지를 전송한다.</summary>
        public bool SendMirrorFrame(byte[] jpeg)
        {
            VerifyAccess();
            return IsAuthenticated && _messageSender.SendMirrorFrame(jpeg);
        }

        /// <summary>현재 세션의 그룹을 업로드 요청하고 결과를 Tick 콜백으로 전달한다.</summary>
        public string Flush(FlushGroup group, float frameIntervalSeconds,
            Action<bool, string> onResult, double timeoutSeconds = 10)
        {
            VerifyAccess();
            if (group < FlushGroup.Interaction || group > FlushGroup.Heuristics ||
                float.IsNaN(frameIntervalSeconds) || float.IsInfinity(frameIntervalSeconds) || frameIntervalSeconds < 0 ||
                double.IsNaN(timeoutSeconds) || double.IsInfinity(timeoutSeconds) || timeoutSeconds <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(group), "Invalid flush settings.");
            }
            string id = Guid.NewGuid().ToString("N");
            _api.RegisterFlush(id, timeoutSeconds, onResult);
            if (!IsAuthenticated)
            {
                _api.CompleteFlush(id, false, "session unavailable");
            }
            else if (!ApiMessageSender.SendFlushRequest(_connections.SelectedTransport,
                id, group, Session.seq, frameIntervalSeconds))
            {
                _api.CompleteFlush(id, false, "host disconnected");
            }
            return id;
        }

        /// <summary>모든 연결과 대기 콜백을 폐기하며 이후 Tick이나 재시작을 허용하지 않는다.</summary>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            Stop();
            _disposed = true;
            _api.Clear();
        }

        /// <summary>공개 API가 생성 스레드에서 호출되며 폐기되지 않았는지 확인한다.</summary>
        private void VerifyAccess()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(XRLuminaClient));
            }
            if (Thread.CurrentThread.ManagedThreadId != _engineThreadId)
            {
                throw new InvalidOperationException("Call the client API on the engine thread that created it.");
            }
        }

        /// <summary>이전 실행의 백그라운드 작업이 새 실행에 반영되지 않게 차단한다.</summary>
        private void QueueAction(int generation, Action action)
        {
            _engineActions.Enqueue(() =>
            {
                if (_running && generation == _generation)
                {
                    action();
                }
            });
        }

        /// <summary>통신 오류를 엔진 Tick의 이벤트로 전달한다.</summary>
        private void QueueFault(int generation, Exception exception)
        {
            QueueAction(generation, () => Faulted?.Invoke(exception));
        }

        /// <summary>응답하는 모든 데스크톱을 주기적으로 탐색한다.</summary>
        private void DiscoverHosts(int generation, ManualResetEvent stop)
        {
            try
            {
                while (_running && generation == _generation)
                {
                    try
                    {
                        foreach (var endpoint in _endpointDiscovery.ResolveAll())
                        {
                            var found = endpoint;
                            QueueAction(generation, () => ConnectHost(found));
                        }
                    }
                    catch (Exception exception)
                    {
                        QueueFault(generation, exception);
                    }
                    if (stop.WaitOne(Math.Max(100, _retryDelayMs)))
                    {
                        break;
                    }
                }
            }
            finally
            {
                // 엔진의 Stop이 신호를 보낸 실행만 종료되므로 마지막 대기 후 핸들을 해제한다.
                lock (_discoveryLock)
                {
                    if (_discoveryStop == stop)
                    {
                        _discoveryStop = null;
                    }
                    stop.Dispose();
                }
            }
        }

        /// <summary>발견한 호스트별 연결을 생성하고 클라이언트 정보를 알린다.</summary>
        private void ConnectHost(NetworkEndpoint endpoint)
        {
            if (!_running || _transports.ContainsKey(endpoint.InstanceId))
            {
                return;
            }
            RegisterTransport(endpoint.InstanceId, new TcpTransport(() => endpoint, _retryDelayMs));
        }

        /// <summary>네트워크 및 USB 연결에 동일한 선택과 세션 처리를 등록한다.</summary>
        private void RegisterTransport(string connectionId, TcpTransport transport)
        {
            int generation = _generation;
            _transports.Add(connectionId, transport);
            transport.Connected += () => QueueAction(generation, () => _connections.Connected(transport));
            transport.Disconnected += () => QueueAction(generation, () =>
            {
                if (!_transports.TryGetValue(connectionId, out var current) || current != transport)
                {
                    return;
                }
                _transports.Remove(connectionId);
                transport.Dispose();
                _connections.Disconnect(transport);
            });
            transport.MessageReceived += envelope => QueueAction(generation, () => _router.Dispatch(transport, envelope));
            transport.Faulted += exception => QueueFault(generation, exception);
            transport.Start();
        }

    }
}
