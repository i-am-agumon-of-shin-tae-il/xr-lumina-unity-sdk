using System;
using PacketType = XRLumina.Protocol.Message;

namespace XRLumina._Core.Model
{
    /// <summary>송신 대기열이 보관하는 메시지 타입과 인코딩된 버퍼 구간.</summary>
    internal readonly struct NetworkPacket
    {
        public readonly PacketType Type;
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
            PayloadSegment = payload;
        }
    }
}
