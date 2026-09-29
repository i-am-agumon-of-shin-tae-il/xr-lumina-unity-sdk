namespace XRLumina._Core.Model
{
    /// <summary>TCP 연결 대상의 호스트와 포트를 나타낸다.</summary>
    internal readonly struct NetworkEndpoint
    {
        public readonly string Host;
        public readonly int Port;
        public readonly string InstanceId;

        /// <summary>발견된 데스크톱 주소와 인스턴스 식별자를 저장한다.</summary>
        public NetworkEndpoint(string host, int port, string instanceId = null)
        {
            Host = host;
            Port = port;
            InstanceId = instanceId ?? $"{host}:{port}";
        }
    }
}
