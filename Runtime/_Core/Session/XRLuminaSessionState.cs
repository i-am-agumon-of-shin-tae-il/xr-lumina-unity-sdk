using System;
using XRLumina._Core.Model;

namespace XRLumina._Core.Session
{
    /// <summary>호스트가 내려준 세션 상태와 변경 이벤트를 보관한다.</summary>
    internal sealed class XRLuminaSessionState
    {
        private XRLuminaSession _session;

        public event Action SessionReady;

        public XRLuminaSession Session => _session;
        public bool IsAuthenticated => _session != null && !string.IsNullOrEmpty(_session.uuid);

        internal void AdoptSession(string uuid, int seq)
        {
            _session = new XRLuminaSession { uuid = uuid, seq = seq, };
        }

        internal void ReceiveSessionReady()
        {
            SessionReady?.Invoke();
        }
    }

}
