using XRLumina.Protocol;
using XRLumina._Core.Infrastructure;

namespace XRLumina._Core.Messaging.Senders
{
    /// <summary>클라이언트 인사, 선택 응답, 사용 가능 상태와 탐색 요청을 작성한다.</summary>
    internal sealed class ConnectionMessageSender
    {
        private readonly byte[] _helloMessage;
        private readonly byte[] _unavailableHelloMessage;

        /// <summary>연결마다 재사용할 클라이언트 인사를 사용 가능 상태별로 작성한다.</summary>
        internal ConnectionMessageSender(string clientId, string model, string name)
        {
            var hello = new HelloT
            {
                Id = clientId, Model = model ?? "", Name = name ?? "", Available = true,
            };
            _helloMessage = FlatBufferCodec.Encode(MessageUnion.FromHello(hello));
            hello.Available = false;
            _unavailableHelloMessage = FlatBufferCodec.Encode(MessageUnion.FromHello(hello));
        }

        /// <summary>새 연결에 현재 사용 가능 상태의 클라이언트 인사를 전송한다.</summary>
        internal void SendHello(TcpTransport transport, bool available)
        {
            transport.Send(Message.Hello, available ? _helloMessage : _unavailableHelloMessage);
        }

        /// <summary>해당 호스트가 클라이언트를 선택할 수 있는지 알린다.</summary>
        internal void SendAvailability(TcpTransport transport, bool available)
        {
            MessageSender.SendTo(transport, MessageUnion.FromAvailability(new AvailabilityT
            {
                Available = available,
            }));
        }

        /// <summary>요청의 action과 식별자에 대응하는 선택 결과를 전송한다.</summary>
        internal void SendSelectionReply(TcpTransport transport, SelectionAction action,
            string selectionId, bool ok, string error = null)
        {
            MessageSender.SendTo(transport, MessageUnion.FromSelectionReply(new SelectionReplyT
            {
                Action = action, SelectionId = selectionId, Ok = ok, Error = error,
            }));
        }

        /// <summary>전송 계층이 브로드캐스트할 표준 탐색 요청 프레임을 작성한다.</summary>
        internal static byte[] CreateDiscoveryRequest()
        {
            return FlatBufferCodec.Encode(MessageUnion.FromDiscoveryRequest(new DiscoveryRequestT()));
        }
    }
}
