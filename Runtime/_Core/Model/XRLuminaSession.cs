using System;

namespace XRLumina._Core.Model
{
    /// <summary>호스트가 내려준 세션 정보.</summary>
    [Serializable]
    public sealed class XRLuminaSession
    {
        public string uuid;
        public int seq;
    }
}
