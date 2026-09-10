namespace XRLumina._Core.Model
{
    /// <summary>탐색 응답의 포트와 서버 인스턴스 식별자.</summary>
    internal readonly struct DiscoveryAdvertisement
    {
        public readonly int Port;
        public readonly string InstanceId;

        public DiscoveryAdvertisement(int port, string instanceId)
        {
            Port = port;
            InstanceId = instanceId;
        }
    }
}
