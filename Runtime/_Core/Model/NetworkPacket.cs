using XRLumina._Core.Infrastructure;

namespace XRLumina._Core.Model
{
    /// <summary>프레이밍이 완료된 패킷 타입과 페이로드.</summary>
    internal readonly struct NetworkPacket
    {
        public readonly PacketType Type;
        public readonly byte[] Payload;

        public NetworkPacket(PacketType type, byte[] payload)
        {
            Type = type;
            Payload = payload;
        }
    }
}
