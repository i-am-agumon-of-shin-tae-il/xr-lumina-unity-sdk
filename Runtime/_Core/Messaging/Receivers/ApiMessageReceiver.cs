using System;
using System.Collections.Generic;
using XRLumina.Protocol;
using XRLumina._Core.Infrastructure;

namespace XRLumina._Core.Messaging.Receivers
{
    /// <summary>API 응답과 엔진 시간 기준 요청 제한을 처리한다.</summary>
    internal sealed class ApiMessageReceiver
    {
        private sealed class FlushRequest
        {
            internal double Deadline;
            internal Action<bool, string> Complete;
        }

        private readonly Dictionary<string, FlushRequest> _flushRequests = new();
        private readonly Queue<Action> _completions = new();
        private double _elapsedSeconds;

        /// <summary>생성된 응답에서 요청 식별자를 읽고 일치하는 요청만 완료한다.</summary>
        internal void ReceiveResponse(TcpTransport transport, Envelope envelope)
        {
            var message = envelope.MessageAsApiResponse();
            CompleteFlush(message.Id, message.Ok, message.Ok ? null : (message.Error ?? "error"));
        }

        /// <summary>송신 전에 Flush 요청과 엔진 경과 시간 기준 제한을 등록한다.</summary>
        internal void RegisterFlush(string id, double timeoutSeconds, Action<bool, string> complete)
        {
            _flushRequests.Add(id, new FlushRequest { Deadline = _elapsedSeconds + timeoutSeconds, Complete = complete, });
        }

        /// <summary>전송 실패나 원격 응답을 요청당 한 번만 완료한다.</summary>
        internal void CompleteFlush(string id, bool ok, string error)
        {
            if (id == null || !_flushRequests.TryGetValue(id, out var request))
            {
                return;
            }
            _flushRequests.Remove(id);
            _completions.Enqueue(() => request.Complete?.Invoke(ok, error));
        }

        /// <summary>엔진이 전달한 초 단위 시간으로 제한 시간을 진행한다.</summary>
        internal void Advance(double deltaSeconds)
        {
            _elapsedSeconds += deltaSeconds;
        }

        /// <summary>수신 결과를 먼저 반영한 뒤 아직 응답이 없는 요청의 제한 시간을 확인한다.</summary>
        internal void ExpireRequests()
        {
            var expired = new List<string>();
            foreach (var entry in _flushRequests)
            {
                if (entry.Value.Deadline <= _elapsedSeconds)
                {
                    expired.Add(entry.Key);
                }
            }
            foreach (var id in expired)
            {
                CompleteFlush(id, false, "timeout");
            }
        }

        /// <summary>진행 중인 요청을 같은 사유로 취소하며 콜백은 Tick까지 지연한다.</summary>
        internal void Cancel(string error)
        {
            var requests = new List<FlushRequest>(_flushRequests.Values);
            _flushRequests.Clear();
            foreach (var request in requests)
            {
                _completions.Enqueue(() => request.Complete?.Invoke(false, error));
            }
        }

        /// <summary>이번 Tick까지 완료된 요청 콜백을 엔진 스레드에 전달한다.</summary>
        internal void DispatchCompletions()
        {
            int count = _completions.Count;
            for (int i = 0; i < count && _completions.Count > 0; i++)
            {
                _completions.Dequeue()();
            }
        }

        /// <summary>폐기 후 사용자 콜백과 요청이 남지 않게 정리한다.</summary>
        internal void Clear()
        {
            _flushRequests.Clear();
            _completions.Clear();
        }
    }
}
