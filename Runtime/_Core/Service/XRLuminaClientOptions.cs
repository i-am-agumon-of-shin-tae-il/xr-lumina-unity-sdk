namespace XRLumina.Client
{
    /// <summary>엔진이 제공하는 클라이언트 정보와 연결 설정이다. 클라이언트 생성 시 복사된다.</summary>
    public sealed class XRLuminaClientOptions
    {
        public string ClientId { get; set; }
        public string Model { get; set; } = "";
        public string Name { get; set; } = "";
        public string Host { get; set; } = "";
        public int Port { get; set; } = 9999;
        public int DiscoveryPort { get; set; } = 9998;
        public int DiscoveryTimeoutMs { get; set; } = 800;
        public int RetryDelayMs { get; set; } = 1500;
        public bool UsbEnabled { get; set; }
        public int UsbPort { get; set; } = 10000;
    }
}
