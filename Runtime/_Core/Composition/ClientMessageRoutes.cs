using System;
using System.Collections.Generic;
using XRLumina.Protocol;
using XRLumina._Core.Infrastructure;
using XRLumina._Core.Messaging.Receivers;

namespace XRLumina._Core.Composition
{
    /// <summary>클라이언트 수신 경로와 연결 허용 정책을 외부에서 조립한다.</summary>
    internal static class ClientMessageRoutes
    {
        /// <summary>메시지별 처리 함수와 소유권 정책을 한 곳에서 등록한 라우터를 구성한다.</summary>
        internal static MessageRouter Create(ConnectionMessageReceiver connections,
            SessionMessageReceiver session, ApiMessageReceiver api, Action<Exception> faulted)
        {
            return new MessageRouter(new Dictionary<Message, MessageRouter.Route>
            {
                [Message.Selection] = new MessageRouter.Route(connections.ReceiveSelection, requiresSelection: false),
                [Message.Session] = new MessageRouter.Route(session.ReceiveSession, requiresSelection: true),
                [Message.Command] = new MessageRouter.Route(session.ReceiveCommand, requiresSelection: true),
                [Message.ApiResponse] = new MessageRouter.Route(api.ReceiveResponse, requiresSelection: true),
            }, (transport, requiresSelection) => connections.Contains(transport) &&
                (!requiresSelection || connections.SelectedTransport == transport), faulted);
        }
    }
}
