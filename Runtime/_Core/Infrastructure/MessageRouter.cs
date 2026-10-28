using System;
using System.Collections.Generic;
using System.IO;
using XRLumina.Protocol;

namespace XRLumina._Core.Infrastructure
{
    /// <summary>주입받은 라우트와 연결 정책에 따라 생성된 메시지를 처리 함수에 전달한다.</summary>
    internal sealed class MessageRouter
    {
        private readonly Func<TcpTransport, bool, bool> _canReceive;
        private readonly Action<Exception> _faulted;
        private readonly IReadOnlyDictionary<Message, Route> _routerMap;

        internal readonly struct Route
        {
            internal readonly Action<TcpTransport, Envelope> Receive;
            internal readonly bool RequiresSelection;

            /// <summary>메시지 처리 함수와 선택된 연결의 필요 여부를 함께 보관한다.</summary>
            internal Route(Action<TcpTransport, Envelope> receive, bool requiresSelection)
            {
                Receive = receive;
                RequiresSelection = requiresSelection;
            }
        }

        /// <summary>조립 계층이 정한 메시지 경로와 연결 허용 정책을 사용한다.</summary>
        internal MessageRouter(IReadOnlyDictionary<Message, Route> routerMap,
            Func<TcpTransport, bool, bool> canReceive, Action<Exception> faulted)
        {
            _routerMap = routerMap;
            _canReceive = canReceive;
            _faulted = faulted;
        }

        /// <summary>Tick에서 유효한 연결과 소유권을 확인한 뒤 등록된 함수 하나만 호출한다.</summary>
        internal void Dispatch(TcpTransport transport, Envelope envelope)
        {
            if (!_routerMap.TryGetValue(envelope.MessageType, out var route))
            {
                if (_canReceive(transport, false))
                {
                    _faulted(new InvalidDataException($"unexpected host message: {envelope.MessageType}"));
                }
                return;
            }
            if (!_canReceive(transport, route.RequiresSelection))
            {
                return;
            }
            route.Receive(transport, envelope);
        }
    }
}
