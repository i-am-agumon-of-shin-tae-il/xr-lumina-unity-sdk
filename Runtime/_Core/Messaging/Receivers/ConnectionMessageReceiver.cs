using System;
using System.Collections.Generic;
using System.IO;
using XRLumina.Protocol;
using XRLumina._Core.Infrastructure;
using XRLumina._Core.Messaging.Senders;
using XRLumina._Core.Model;

namespace XRLumina._Core.Messaging.Receivers
{
    /// <summary>연결별 인사, 선택 소유권과 사용 가능 상태를 처리한다.</summary>
    internal sealed class ConnectionMessageReceiver
    {
        private readonly Dictionary<string, TcpTransport> _transports;
        private readonly SessionMessageReceiver _session;
        private readonly ConnectionMessageSender _sender;
        private readonly Action<Exception> _faulted;

        internal TcpTransport SelectedTransport { get; private set; }
        internal event Action Disconnected;

        /// <summary>연결 목록과 세션 수신기를 사용해 클라이언트 선택을 관리한다.</summary>
        internal ConnectionMessageReceiver(Dictionary<string, TcpTransport> transports,
            SessionMessageReceiver session, ConnectionMessageSender sender, Action<Exception> faulted)
        {
            _transports = transports;
            _session = session;
            _sender = sender;
            _faulted = faulted;
        }

        /// <summary>현재 실행에 등록된 연결인지 확인한다.</summary>
        internal bool Contains(TcpTransport transport)
        {
            return _transports.ContainsValue(transport);
        }

        /// <summary>새 연결에 현재 소유권에 맞는 클라이언트 정보를 전달한다.</summary>
        internal void Connected(TcpTransport transport)
        {
            _sender.SendHello(transport, SelectedTransport == null || SelectedTransport == transport);
        }

        /// <summary>소유한 연결이 끊어지면 세션을 해제하고 다른 호스트에 다시 알린다.</summary>
        internal void Disconnect(TcpTransport transport)
        {
            if (SelectedTransport != transport)
            {
                return;
            }
            Reset("host disconnected");
            transport.ClearPending();
            BroadcastAvailability();
            Disconnected?.Invoke();
        }

        /// <summary>선택 또는 해제 메시지를 생성된 스키마로 읽고 소유권을 변경한다.</summary>
        internal void ReceiveSelection(TcpTransport transport, Envelope envelope)
        {
            var message = envelope.MessageAsSelection();
            if (string.IsNullOrEmpty(message.SelectionId))
            {
                _faulted(new InvalidDataException("selection id is required"));
                return;
            }
            switch (message.Action)
            {
                case SelectionAction.Select:
                    if (SelectedTransport != null && SelectedTransport != transport)
                    {
                        _sender.SendSelectionReply(transport, message.Action, message.SelectionId,
                            false, "다른 호스트에서 선택한 클라이언트입니다.");
                        return;
                    }
                    SelectedTransport = transport;
                    _sender.SendSelectionReply(transport, message.Action, message.SelectionId, true);
                    BroadcastAvailability();
                    break;
                case SelectionAction.Release:
                    bool released = SelectedTransport == transport;
                    if (released)
                    {
                        if (_session.IsMeasuring)
                        {
                            _sender.SendSelectionReply(transport, message.Action, message.SelectionId,
                                false, "측정 중에는 클라이언트 선택을 해제할 수 없습니다.");
                            return;
                        }
                        Reset("host disconnected");
                        transport.ClearPending();
                    }
                    _sender.SendSelectionReply(transport, message.Action, message.SelectionId, true);
                    BroadcastAvailability();
                    if (released)
                    {
                        Disconnected?.Invoke();
                    }
                    break;
                default:
                    _faulted(new InvalidDataException("unknown selection action"));
                    break;
            }
        }

        /// <summary>종료 시 소유권과 세션을 정리하고 요청 취소는 다음 Tick으로 미룬다.</summary>
        internal void Reset(string error)
        {
            SelectedTransport = null;
            _session.Reset(error);
        }

        /// <summary>선택한 호스트 외에는 사용 불가 상태를 보내고 미선택이면 모두에게 알린다.</summary>
        private void BroadcastAvailability()
        {
            foreach (var transport in _transports.Values)
            {
                _sender.SendAvailability(transport, SelectedTransport == null || SelectedTransport == transport);
            }
        }

        /// <summary>탐색 응답을 검증하고 연결 대상의 포트와 인스턴스 식별자를 읽는다.</summary>
        internal static DiscoveryAdvertisement ParseDiscoveryResponse(byte[] bytes)
        {
            var envelope = FlatBufferCodec.Decode(bytes);
            if (envelope.MessageType != Message.DiscoveryResponse)
            {
                throw new InvalidDataException("invalid discovery response");
            }
            var response = envelope.MessageAsDiscoveryResponse();
            return new DiscoveryAdvertisement(response.Port, response.ServerInstanceId);
        }
    }
}
