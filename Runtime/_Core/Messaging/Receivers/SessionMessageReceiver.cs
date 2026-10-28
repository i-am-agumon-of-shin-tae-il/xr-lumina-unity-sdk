using System;
using System.IO;
using XRLumina.Protocol;
using XRLumina._Core.Infrastructure;
using XRLumina._Core.Messaging.Senders;
using XRLumina._Core.Model;

namespace XRLumina._Core.Messaging.Receivers
{
    /// <summary>선택된 호스트의 세션과 측정 명령을 엔진 Tick에서 처리한다.</summary>
    internal sealed class SessionMessageReceiver
    {
        private readonly ApiMessageReceiver _api;
        private readonly Action<Exception> _faulted;
        private int _revision;

        internal XRLuminaSession Session { get; private set; }
        internal bool IsAuthenticated => Session != null && !string.IsNullOrEmpty(Session.uuid);
        internal bool IsMeasuring { get; private set; }

        internal event Action SessionReady;
        internal event Action SessionAccepted;
        internal event Action<TestAction> CommandReceived;
        internal event Action MeasurementStarted;
        internal event Action MeasurementFinished;

        /// <summary>세션 변경 시 취소할 요청 수신기와 오류 전달 함수를 연결한다.</summary>
        internal SessionMessageReceiver(ApiMessageReceiver api, Action<Exception> faulted)
        {
            _api = api;
            _faulted = faulted;
        }

        /// <summary>세션을 채택하고 이벤트 중 종료되면 후속 이벤트와 미전송 데이터 처리를 중단한다.</summary>
        internal void ReceiveSession(TcpTransport transport, Envelope envelope)
        {
            var message = envelope.MessageAsSession();
            if (string.IsNullOrEmpty(message.Uuid))
            {
                _faulted(new InvalidDataException("session uuid is required"));
                return;
            }
            bool isNewSession = message.Uuid != Session?.uuid;
            if (isNewSession)
            {
                _api.Cancel("session changed");
                IsMeasuring = false;
            }
            Session = new XRLuminaSession { uuid = message.Uuid, seq = message.Seq, };
            int revision = ++_revision;
            SessionMessageSender.SendSessionAck(transport, message.SelectionId, message.Uuid);
            if (isNewSession)
            {
                SessionReady?.Invoke();
                if (revision != _revision)
                {
                    return;
                }
            }
            SessionAccepted?.Invoke();
            if (revision == _revision)
            {
                SessionMessageSender.ResumeMeasurements(transport);
            }
        }

        /// <summary>측정 상태를 바꾸고 이벤트 중 종료되지 않은 세션에만 명령 이벤트를 전달한다.</summary>
        internal void ReceiveCommand(TcpTransport transport, Envelope envelope)
        {
            var action = envelope.MessageAsCommand().Action;
            int revision = _revision;
            switch (action)
            {
                case TestAction.Start:
                    IsMeasuring = true;
                    MeasurementStarted?.Invoke();
                    break;
                case TestAction.Finish:
                    IsMeasuring = false;
                    MeasurementFinished?.Invoke();
                    break;
                default:
                    _faulted(new InvalidDataException("unknown test action"));
                    return;
            }
            if (revision == _revision)
            {
                CommandReceived?.Invoke(action);
            }
        }

        /// <summary>선택 해제나 연결 종료 시 세션과 측정을 비우고 요청을 취소한다.</summary>
        internal void Reset(string error)
        {
            ++_revision;
            Session = null;
            IsMeasuring = false;
            _api.Cancel(error);
        }
    }
}
