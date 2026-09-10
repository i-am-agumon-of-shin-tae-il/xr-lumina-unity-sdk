using System;
using System.Collections;
using System.Collections.Concurrent;
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
        private readonly TcpTransport _transport;
        private readonly DeviceMessageSender _messageSender;
        private readonly DeviceMessageReceiver _receiver;
        private readonly DeviceMessageDispatcher _dispatcher;
        private readonly XRLuminaSessionState _sessionState = new();
        private string _helloJson;

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
            _transport = new TcpTransport(_endpointDiscovery.Resolve, retryDelayMs);
            _messageSender = new DeviceMessageSender(_transport);
            _receiver = new DeviceMessageReceiver(
                _messageSender,
                _sessionState,
                HandleSessionAccepted,
                _sessionState.ReceiveSessionReady,
                HandleCommand);
            _dispatcher = new DeviceMessageDispatcher(_receiver);
            _transport.Connected += HandleConnected;
            _transport.Disconnected += HandleDisconnected;
            _transport.PacketReceived += HandlePacketReceived;
            _transport.Faulted += HandleTransportFault;
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
            });
            _transport.Start();
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
            _transport.Connected -= HandleConnected;
            _transport.Disconnected -= HandleDisconnected;
            _transport.PacketReceived -= HandlePacketReceived;
            _transport.Faulted -= HandleTransportFault;
            _transport.Dispose();
        }

        /// <summary>장비 정보를 전송하고 연결 중 쌓인 스트림 전송을 재개한다.</summary>
        private void HandleConnected()
        {
            _messageSender.SendJson(_helloJson);
            _transport.SendPending();
        }

        /// <summary>연결 종료 후 메인 스레드에서 프레임 생산을 중지한다.</summary>
        private void HandleDisconnected()
        {
            _mainThreadActions.Enqueue(() => Disconnected?.Invoke());
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
                MeasurementStarted?.Invoke();
            }
            else if (action == "finish")
            {
                MeasurementFinished?.Invoke();
            }
            CommandReceived?.Invoke(action);
        }

        /// <summary>세션 채택을 기능 구독자에게 알린다.</summary>
        private void HandleSessionAccepted()
        {
            SessionAccepted?.Invoke();
        }

        /// <summary>수신 패킷을 타입별 장비 메시지 처리로 전달한다.</summary>
        private void HandlePacketReceived(NetworkPacket packet)
        {
            switch (packet.Type)
            {
                case PacketType.Json:
                    HandleJsonPayload(packet.Payload);
                    break;
                default:
                    _mainThreadActions.Enqueue(() =>
                        Debug.LogWarning($"[DeviceTcp] 알 수 없는 packet type 무시: {(byte)packet.Type}"));
                    break;
            }
        }

        /// <summary>JSON 페이로드를 장비 메시지로 역직렬화해 메인 스레드에 전달한다.</summary>
        private void HandleJsonPayload(byte[] payload)
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
                        _dispatcher.Dispatch(message);
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
