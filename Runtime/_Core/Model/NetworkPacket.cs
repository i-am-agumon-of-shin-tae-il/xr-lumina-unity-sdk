using System;
using XRLumina._Core.Infrastructure;

namespace XRLumina._Core.Model
{
    /// <summary>프레이밍이 완료된 패킷 타입과 페이로드.</summary>
    internal readonly struct NetworkPacket
    {
        public readonly PacketType Type;
        public readonly byte[] Payload;
        public readonly ArraySegment<byte> PayloadSegment;

        /// <summary>전체 바이트 배열을 유효 페이로드로 보관한다.</summary>
        public NetworkPacket(PacketType type, byte[] payload)
            : this(type, new ArraySegment<byte>(payload))
        {
        }

        /// <summary>버퍼를 복사하지 않고 페이로드의 유효 구간을 보관한다.</summary>
        public NetworkPacket(PacketType type, ArraySegment<byte> payload)
        {
            Type = type;
            Payload = payload.Array;
            PayloadSegment = payload;
        }
    }
}
