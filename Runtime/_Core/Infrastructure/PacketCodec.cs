using System;
using System.IO;
using XRLumina.Protocol;

namespace XRLumina._Core.Infrastructure
{
    /// <summary>TCP·USB 스트림에서 표준 size-prefixed FlatBuffers 메시지를 조립한다.</summary>
    internal static class PacketCodec
    {
        /// <summary>분할 수신된 길이 접두사와 본문을 모아 공통 코덱으로 검증한다.</summary>
        public static bool TryRead(Stream stream, out Envelope envelope)
        {
            envelope = default;
            var prefix = new byte[4];
            if (!ReadExact(stream, prefix, 4))
            {
                return false;
            }
            var length = (uint)prefix[0] | ((uint)prefix[1] << 8) | ((uint)prefix[2] << 16) | ((uint)prefix[3] << 24);
            if (length < 12 || length > FlatBufferCodec.MaxMessageBytes)
            {
                throw new InvalidDataException($"invalid message length: {length}");
            }
            var frame = new byte[checked((int)length + 4)];
            Buffer.BlockCopy(prefix, 0, frame, 0, 4);
            if (!ReadExact(stream, frame, (int)length, 4))
            {
                throw new EndOfStreamException("truncated FlatBuffers message");
            }
            envelope = FlatBufferCodec.Decode(frame);
            return true;
        }

        /// <summary>공통 코덱이 작성한 메시지를 추가 커스텀 헤더 없이 전송한다.</summary>
        public static void Write(Stream stream, ArraySegment<byte> frame)
        {
            if (frame.Array == null || frame.Count < 16 || frame.Count - 4 > FlatBufferCodec.MaxMessageBytes)
            {
                throw new InvalidDataException("invalid message buffer");
            }
            stream.Write(frame.Array, frame.Offset, frame.Count);
        }

        /// <summary>요청한 바이트 수를 모두 읽으며 헤더 중간에 종료된 스트림은 거부한다.</summary>
        private static bool ReadExact(Stream stream, byte[] buffer, int count, int offset = 0)
        {
            var readBytes = 0;
            while (readBytes < count)
            {
                var read = stream.Read(buffer, offset + readBytes, count - readBytes);
                if (read <= 0)
                {
                    if (readBytes > 0)
                    {
                        throw new EndOfStreamException("truncated FlatBuffers message");
                    }
                    return false;
                }
                readBytes += read;
            }
            return true;
        }
    }
}
