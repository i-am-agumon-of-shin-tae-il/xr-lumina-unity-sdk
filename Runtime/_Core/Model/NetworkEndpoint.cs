namespace XRLumina._Core.Model
{
    /// <summary>TCP 연결 대상의 호스트와 포트를 나타낸다.</summary>
    internal readonly struct NetworkEndpoint
    {
        public readonly string Host;
        public readonly int Port;

        public NetworkEndpoint(string host, int port)
        {
            Host = host;
            Port = port;
        }
    }
}
