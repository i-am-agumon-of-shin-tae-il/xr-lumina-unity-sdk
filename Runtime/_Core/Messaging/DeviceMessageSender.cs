using System.Threading;
using System.Text;
using Newtonsoft.Json;
using UnityEngine;
using XRLumina._Core.Infrastructure;

namespace XRLumina._Core.Messaging
{
    /// <summary>요청, 스트림, 생명주기, 플러시 등 장비 프로토콜의 송신 메시지를 생성한다.</summary>
    internal sealed class DeviceMessageSender
    {
        private readonly TcpTransport _transport;
        private int _requestId;

        /// <summary>TCP 전송 계층을 사용하는 메시지 센더를 생성한다.</summary>
        internal DeviceMessageSender(TcpTransport transport)
        {
            _transport = transport;
        }

        /// <summary>JSON 문자열을 JSON 타입 패킷으로 전송한다.</summary>
        public bool SendJson(string json)
        {
            if (string.IsNullOrEmpty(json))
            {
                return false;
            }
            if (_transport.Send(PacketType.Json, Encoding.UTF8.GetBytes(json)))
            {
                Debug.Log($"[DeviceTcp] ▷ SEND: {json}");
                return true;
            }
            Debug.LogWarning($"[DeviceTcp] ▷ SEND 실패(미연결): {json}");
            return false;
        }

        /// <summary>미러링 프레임을 바이너리 패킷으로 전송한다.</summary>
        public bool SendMirrorFrame(byte[] payload)
        {
            return payload != null && _transport.Send(PacketType.MirrorFrame, payload);
        }

        /// <summary>측정 바이너리 패킷을 전송하고 미연결이면 재전송 큐에 추가한다.</summary>
        internal void SendStream(PacketType type, byte[] payload)
        {
            if (payload == null)
            {
                return;
            }
            if (!_transport.Send(type, payload))
            {
                _transport.Enqueue(type, payload);
            }
        }

        /// <summary>테스트 시작 또는 종료 요청을 전송한다.</summary>
        internal bool SendLifecycleRequest(string action)
        {
            return SendJson(JsonConvert.SerializeObject(new { type = "lifecycle-request", action, }));
        }

        /// <summary>수집된 스트림의 조합과 업로드 요청을 전송하고 요청 식별자를 반환한다.</summary>
        internal string SendFlushRequest(
            string group,
            int projectMemberSeq,
            float frameIntervalSeconds
        )
        {
            var id = Interlocked.Increment(ref _requestId).ToString();
            var payload = JsonConvert.SerializeObject(new
            {
                type = "flush",
                id,
                group,
                projectMemberSeq,
                frameInterval = frameIntervalSeconds,
            });
            if (!SendJson(payload))
            {
                return null;
            }
            return id;
        }

    }
}
