using System;

namespace XRLumina._Core.Model
{
    /// <summary>세션 채택 결과로 전송하는 확인 메시지.</summary>
    [Serializable]
    internal sealed class SessionAcknowledgement
    {
        public string type;
        public string selectionId;
        public string uuid;
    }
}
