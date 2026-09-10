using System;

namespace XRLumina._Core.Model
{
    /// <summary>장비 탐색 응답 JSON 모델.</summary>
    [Serializable]
    internal sealed class DiscoveryResponse
    {
        public string type;
        public int port;
        public string serverInstanceId;
    }
}
