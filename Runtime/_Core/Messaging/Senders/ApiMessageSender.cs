using XRLumina.Protocol;
using XRLumina._Core.Infrastructure;

namespace XRLumina._Core.Messaging.Senders
{
    /// <summary>분석 업로드 API의 요청 메시지를 작성한다.</summary>
    internal static class ApiMessageSender
    {
        /// <summary>요청 식별자와 세션 번호로 그룹 업로드를 요청하고 전송 여부를 반환한다.</summary>
        internal static bool SendFlushRequest(TcpTransport transport, string id, FlushGroup group,
            int projectMemberSeq, float frameIntervalSeconds)
        {
            return MessageSender.SendTo(transport, MessageUnion.FromFlush(new FlushT
            {
                Id = id, Group = group, ProjectMemberSeq = projectMemberSeq, FrameInterval = frameIntervalSeconds,
            }));
        }
    }
}
