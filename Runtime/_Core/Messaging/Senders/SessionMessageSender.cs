using System;
using XRLumina.Protocol;
using XRLumina._Core.Infrastructure;

namespace XRLumina._Core.Messaging.Senders
{
    /// <summary>세션 채택 확인과 원격 테스트 생명주기 요청을 작성한다.</summary>
    internal static class SessionMessageSender
    {
        /// <summary>채택한 세션과 선택 식별자를 해당 호스트에 확인한다.</summary>
        internal static void SendSessionAck(TcpTransport transport, string selectionId, string uuid)
        {
            MessageSender.SendTo(transport, MessageUnion.FromSessionAck(new SessionAckT
            {
                SelectionId = selectionId, Uuid = uuid,
            }));
        }

        /// <summary>세션 이벤트가 정상 완료된 연결에서 보존된 측정 데이터 전송을 재개한다.</summary>
        internal static void ResumeMeasurements(TcpTransport transport)
        {
            transport.SendPending();
        }

        /// <summary>선택된 호스트에 테스트 시작 또는 종료 요청을 전송한다.</summary>
        internal static bool SendLifecycleRequest(TcpTransport transport, TestAction action)
        {
            if (action != TestAction.Start && action != TestAction.Finish)
            {
                throw new ArgumentException("invalid lifecycle action", nameof(action));
            }
            return MessageSender.SendTo(transport, MessageUnion.FromLifecycleRequest(new LifecycleRequestT
            {
                Action = action,
            }));
        }
    }
}
