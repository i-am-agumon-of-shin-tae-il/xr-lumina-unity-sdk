using System;
using System.Collections;
using System.Collections.Concurrent;
using UnityEngine;
using XRLumina._Core.Model;
using XRLumina._Core.Session;

namespace XRLumina._Core.Messaging
{
    /// <summary>세션, 명령, API 응답 등 장비 프로토콜의 수신 메시지를 처리한다.</summary>
    internal sealed class DeviceMessageReceiver
    {
        private readonly DeviceMessageSender _sender;
        private readonly XRLuminaSessionState _sessionState;
        private readonly Action _startFrameStream;
        private readonly Action _sessionReady;
        private readonly Action<string> _commandReceived;
        private readonly ConcurrentDictionary<string, DeviceMessage> _flushResponses =
            new ConcurrentDictionary<string, DeviceMessage>();
        private string _lastSessionUuid;

        /// <summary>수신 처리에 필요한 송신 함수와 애플리케이션 이벤트를 구성한다.</summary>
        public DeviceMessageReceiver(
            DeviceMessageSender sender,
            XRLuminaSessionState sessionState,
            Action startFrameStream,
            Action sessionReady,
            Action<string> commandReceived
        )
        {
            _sender = sender;
            _sessionState = sessionState;
            _startFrameStream = startFrameStream;
            _sessionReady = sessionReady;
            _commandReceived = commandReceived;
        }

        /// <summary>세션을 채택하고 연결 복구 또는 새 테스트 시작을 처리한다.</summary>
        internal void ReceiveSession(DeviceMessage message)
        {
            _sessionState.AdoptSession(message.uuid, message.seq);
            _sender.SendJson(JsonUtility.ToJson(new SessionAcknowledgement
            {
                type = "session:ack",
                selectionId = message.selectionId,
                uuid = message.uuid,
            }));
            _startFrameStream();

            if (message.uuid == _lastSessionUuid)
            {
                return;
            }
            _lastSessionUuid = message.uuid;
            _sessionReady();
        }

        /// <summary>원격 명령을 애플리케이션 이벤트로 전달한다.</summary>
        internal void ReceiveCommand(DeviceMessage message)
        {
            _commandReceived(message.action);
        }

        /// <summary>플러시 응답을 송신 모음의 응답 대기열로 전달한다.</summary>
        internal void ReceiveApiResponse(DeviceMessage message)
        {
            if (!string.IsNullOrEmpty(message.id))
            {
                _flushResponses[message.id] = message;
            }
        }

        /// <summary>요청 식별자가 같은 플러시 응답을 제한 시간까지 기다린다.</summary>
        internal IEnumerator WaitForFlushResponse(
            string requestId,
            float timeoutSec,
            Action<bool, string> onResult
        )
        {
            var deadline = Time.realtimeSinceStartup + timeoutSec;
            while (Time.realtimeSinceStartup < deadline)
            {
                if (_flushResponses.TryRemove(requestId, out var response))
                {
                    onResult?.Invoke(
                        response.ok,
                        response.ok ? null : (response.error ?? "error")
                    );
                    yield break;
                }
                yield return null;
            }
            onResult?.Invoke(false, "timeout");
        }

    }
}
