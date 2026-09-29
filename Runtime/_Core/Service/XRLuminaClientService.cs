using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using UnityEngine;
using XRLumina._Core.Infrastructure;
using XRLumina._Core.Messaging;
using XRLumina._Core.Model;
using XRLumina._Core.Session;

namespace XRLumina._Core.Service
{
    /// <summary>XRLumina 연결, 세션과 메시지 라우팅을 담당한다.</summary>
    internal sealed class XRLuminaClientService : IDisposable
    {
        private readonly ConcurrentQueue<Action> _mainThreadActions = new();
        private readonly NetworkEndpointDiscovery _endpointDiscovery;
        private readonly Dictionary<string, TcpTransport> _transports = new();
        private volatile TcpTransport _selectedTransport;
        private readonly int _retryDelayMs;
        private volatile bool _running;
        private Thread _discoveryThread;
        private readonly DeviceMessageSender _messageSender;
        private readonly DeviceMessageReceiver _receiver;
        private readonly DeviceMessageDispatcher _dispatcher;
        private readonly XRLuminaSessionState _sessionState = new();
        private string _helloJson;
        private string _unavailableHelloJson;

        internal event Action<string> CommandReceived;
        internal event Action SessionAccepted;
        internal event Action Disconnected;
        internal event Action MeasurementStarted;
        internal event Action MeasurementFinished;

        internal event Action SessionReady
        {
            add => _sessionState.SessionReady += value;
            remove => _sessionState.SessionReady -= value;
        }

        internal XRLuminaSession Session => _sessionState.Session;
        internal bool IsAuthenticated => _sessionState.IsAuthenticated;
        internal bool IsMeasuring { get; private set; }
        internal DeviceMessageSender MessageSender => _messageSender;

        /// <summary>연결 설정과 기능 실행 함수를 받아 SDK 런타임 서비스를 구성한다.</summary>
        internal XRLuminaClientService(
            string host,
            int port,
            int discoveryPort,
            int discoveryTimeoutMs,
            int retryDelayMs)
        {
            _endpointDiscovery = new NetworkEndpointDiscovery(
                host,
                port,
                discoveryPort,
                discoveryTimeoutMs,
                "XR_LUMINA_DISCOVER",
                ParseDiscoveryResponse);
            _retryDelayMs = retryDelayMs;
            _messageSender = new DeviceMessageSender(() => _selectedTransport);
            _receiver = new DeviceMessageReceiver(
                _messageSender,
                _sessionState,
                HandleSessionAccepted,
                HandleSessionReady,
                HandleCommand);
            _dispatcher = new DeviceMessageDispatcher(_receiver);

        }

        /// <summary>장비 식별 메시지를 생성하고 연결 루프를 시작한다.</summary>
        internal void Start()
        {
            _helloJson = JsonConvert.SerializeObject(new
            {
                type = "hello",
                model = SystemInfo.deviceModel,
                name = SystemInfo.deviceName,
                id = SystemInfo.deviceUniqueIdentifier,
                available = true,
            });
            _unavailableHelloJson = _helloJson.Replace("\"available\":true", "\"available\":false");
            if (_running)
            {
                return;
            }
            _running = true;
            _discoveryThread = new Thread(DiscoverHosts) { IsBackground = true, };
            _discoveryThread.Start();
        }

        /// <summary>백그라운드 통신 작업을 메인 스레드에서 실행한다.</summary>
        internal void Tick()
        {
            while (_mainThreadActions.TryDequeue(out var action))
            {
                action();
            }
        }

        /// <summary>Electron 호스트에 테스트 시작을 요청한다.</summary>
        internal bool RequestStart()
        {
            return _messageSender.SendLifecycleRequest("start");
        }

        /// <summary>Electron 호스트에 테스트 종료를 요청한다.</summary>
        internal bool RequestFinish()
        {
            return _messageSender.SendLifecycleRequest("finish");
        }

        /// <summary>지정 기능 그룹의 누적 데이터를 조합하고 업로드하도록 요청한다.</summary>
        internal string RequestFlush(string group, int projectMemberSeq, float frameIntervalSeconds)
        {
            return _messageSender.SendFlushRequest(group, projectMemberSeq, frameIntervalSeconds);
        }

        /// <summary>요청 식별자가 같은 플러시 응답을 제한 시간까지 기다린다.</summary>
        internal IEnumerator WaitForFlushResponse(
            string requestId,
            float timeoutSec,
            Action<bool, string> onResult)
        {
            yield return _receiver.WaitForFlushResponse(requestId, timeoutSec, onResult);
        }

        /// <summary>프레임 생산과 연결을 종료하고 이벤트 구독을 해제한다.</summary>
        public void Dispose()
        {
            IsMeasuring = false;
            _running = false;
            _selectedTransport = null;
            foreach (var transport in _transports.Values)
            {
                transport.Dispose();
            }
            _transports.Clear();
        }

        /// <summary>응답하는 모든 데스크톱을 주기적으로 탐색한다.</summary>
        private void DiscoverHosts()
        {
            while (_running)
            {
                try
                {
                    foreach (var endpoint in _endpointDiscovery.ResolveAll())
                    {
                        var found = endpoint;
                        _mainThreadActions.Enqueue(() => ConnectHost(found));
                    }
                }
                catch (Exception exception)
                {
                    if (_running)
                    {
                        HandleTransportFault(exception);
                    }
                }
                Thread.Sleep(Math.Max(100, _retryDelayMs));
            }
        }

        /// <summary>발견한 데스크톱별 연결을 생성하고 장비 정보를 알린다.</summary>
        private void ConnectHost(NetworkEndpoint endpoint)
        {
            if (!_running || _transports.ContainsKey(endpoint.InstanceId))
            {
                return;
            }
            var transport = new TcpTransport(() => endpoint, _retryDelayMs);
            _transports.Add(endpoint.InstanceId, transport);
            transport.Connected += () =>
            {
                // 장비 식별은 Unity Update가 멈춰 있어도 연결 직후 전송한다.
                var hello = _selectedTransport == null || _selectedTransport == transport
                    ? _helloJson : _unavailableHelloJson;
                transport.Send(PacketType.Json, Encoding.UTF8.GetBytes(hello));
            };
            transport.Disconnected += () => _mainThreadActions.Enqueue(() =>
            {
                if (!_running || !_transports.TryGetValue(endpoint.InstanceId, out var current) || current != transport)
                {
                    return;
                }
                _transports.Remove(endpoint.InstanceId);
                transport.Dispose();
                if (_selectedTransport == transport)
                {
                    _selectedTransport = null;
                    IsMeasuring = false;
                    _receiver.Reset();
                    transport.ClearPending();
                    Disconnected?.Invoke();
                    BroadcastAvailability();
                }
            });
            transport.PacketReceived += packet => HandlePacketReceived(transport, packet);
            transport.Faulted += HandleTransportFault;
            transport.Start();
        }

        /// <summary>선택한 데스크톱을 제외한 목록에서 장비를 숨기고 해제 시 다시 표시한다.</summary>
        private void BroadcastAvailability()
        {
            foreach (var transport in _transports.Values)
            {
                transport.Send(PacketType.Json, Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new
                {
                    type = "device:availability",
                    available = _selectedTransport == null || _selectedTransport == transport,
                })));
            }
        }

        /// <summary>요청한 연결로 선택 또는 해제 결과를 반환한다.</summary>
        private void ReplySelection(TcpTransport transport, DeviceMessage message, bool ok, string error = null)
        {
            transport.Send(PacketType.Json, Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new
            {
                type = message.type + ":ack",
                selectionId = message.selectionId,
                ok,
                error,
            })));
        }

        /// <summary>선택 요청을 직렬 처리하고 소유한 데스크톱의 메시지만 수신기에 전달한다.</summary>
        private void HandleHostMessage(TcpTransport transport, DeviceMessage message)
        {
            if (!_running || !_transports.ContainsValue(transport))
            {
                return;
            }
            if (message.type == "device:select")
            {
                if (string.IsNullOrEmpty(message.selectionId))
                {
                    return;
                }
                if (_selectedTransport != null && _selectedTransport != transport)
                {
                    ReplySelection(transport, message, false, "다른 데스크톱에서 선택한 HMD입니다.");
                    return;
                }
                _selectedTransport = transport;
                ReplySelection(transport, message, true);
                BroadcastAvailability();
                return;
            }
            if (message.type == "device:release")
            {
                if (_selectedTransport == transport)
                {
                    if (IsMeasuring)
                    {
                        ReplySelection(transport, message, false, "측정 중에는 HMD 선택을 해제할 수 없습니다.");
                        return;
                    }
                    _selectedTransport = null;
                    _receiver.Reset();
                    transport.ClearPending();
                    Disconnected?.Invoke();
                }
                // 미선택 연결의 해제 요청은 다른 데스크톱의 소유권에 영향을 주지 않는다.
                ReplySelection(transport, message, true);
                BroadcastAvailability();
                return;
            }
            if (_selectedTransport == transport)
            {
                _dispatcher.Dispatch(message);
                if (message.type == "session")
                {
                    transport.SendPending();
                }
            }
        }

        /// <summary>백그라운드 통신 오류를 메인 스레드 로그로 전달한다.</summary>
        private void HandleTransportFault(Exception exception)
        {
            _mainThreadActions.Enqueue(() =>
                Debug.LogWarning($"[DeviceTcp] 통신 실패, 재시도: {exception.Message}"));
        }

        /// <summary>호스트 명령을 측정 상태 이벤트로 발행한다.</summary>
        private void HandleCommand(string action)
        {
            if (action == "start")
            {
                IsMeasuring = true;
                MeasurementStarted?.Invoke();
            }
            else if (action == "finish")
            {
                IsMeasuring = false;
                MeasurementFinished?.Invoke();
            }
            CommandReceived?.Invoke(action);
        }

        /// <summary>새 세션의 측정을 대기 상태로 전환하고 준비 이벤트를 발행한다.</summary>
        private void HandleSessionReady()
        {
            IsMeasuring = false;
            _sessionState.ReceiveSessionReady();
        }

        /// <summary>세션 채택을 기능 구독자에게 알린다.</summary>
        private void HandleSessionAccepted()
        {
            SessionAccepted?.Invoke();
        }

        /// <summary>수신 패킷을 타입별 장비 메시지 처리로 전달한다.</summary>
        private void HandlePacketReceived(TcpTransport transport, NetworkPacket packet)
        {
            switch (packet.Type)
            {
                case PacketType.Json:
                    HandleJsonPayload(transport, packet.Payload);
                    break;
                default:
                    _mainThreadActions.Enqueue(() =>
                        Debug.LogWarning($"[DeviceTcp] 알 수 없는 packet type 무시: {(byte)packet.Type}"));
                    break;
            }
        }

        /// <summary>JSON 페이로드를 장비 메시지로 역직렬화해 메인 스레드에 전달한다.</summary>
        private void HandleJsonPayload(TcpTransport transport, byte[] payload)
        {
            var json = Encoding.UTF8.GetString(payload);
            try
            {
                var message = JsonConvert.DeserializeObject<DeviceMessage>(json);
                if (message != null)
                {
                    _mainThreadActions.Enqueue(() =>
                    {
                        Debug.Log($"[DeviceTcp] ◀ RECV: {json}");
                        HandleHostMessage(transport, message);
                    });
                }
            }
            catch (Exception exception)
            {
                _mainThreadActions.Enqueue(() =>
                    Debug.LogWarning($"[DeviceTcp] JSON 파싱 실패: {exception.Message}"));
            }
        }

        /// <summary>장비 프로토콜의 탐색 응답을 범용 탐색 결과로 변환한다.</summary>
        private static DiscoveryAdvertisement ParseDiscoveryResponse(string json)
        {
            var response = JsonConvert.DeserializeObject<DiscoveryResponse>(json);
            if (response == null || response.type != "XR_LUMINA_HOST")
            {
                throw new InvalidDataException("invalid discovery response");
            }
            return new DiscoveryAdvertisement(response.port, response.serverInstanceId);
        }

    }
}
