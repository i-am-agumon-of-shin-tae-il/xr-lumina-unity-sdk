using System;
using System.IO;
using XRLumina._Core.Model;

namespace XRLumina._Core.Infrastructure
{
    /// <summary>TCP 스트림과 SDK 패킷 사이의 프레이밍을 처리한다.</summary>
    internal static class PacketCodec
    {
        private const int HeaderLength = 5;
        private const int MaxPayloadLength = 64 * 1024 * 1024;

        public static bool TryRead(Stream stream, out NetworkPacket packet)
        {
            packet = default;
            var header = new byte[HeaderLength];
            if (!ReadExact(stream, header, HeaderLength))
            {
                return false;
            }

            var payloadLength =
                (header[1] << 24) |
                (header[2] << 16) |
                (header[3] << 8) |
                header[4];
            if (payloadLength < 0 || payloadLength > MaxPayloadLength)
            {
                throw new InvalidDataException($"invalid packet payload length: {payloadLength}");
            }

            var payload = new byte[payloadLength];
            if (payloadLength > 0 && !ReadExact(stream, payload, payloadLength))
            {
                return false;
            }

            packet = new NetworkPacket((PacketType)header[0], payload);
            return true;
        }

        public static void Write(Stream stream, PacketType type, byte[] payload)
        {
            if (payload == null)
            {
                throw new ArgumentNullException(nameof(payload));
            }

            var header = new byte[HeaderLength];
            header[0] = (byte)type;
            header[1] = (byte)((payload.Length >> 24) & 0xff);
            header[2] = (byte)((payload.Length >> 16) & 0xff);
            header[3] = (byte)((payload.Length >> 8) & 0xff);
            header[4] = (byte)(payload.Length & 0xff);
            stream.Write(header, 0, header.Length);
            if (payload.Length > 0)
            {
                stream.Write(payload, 0, payload.Length);
            }
        }

        private static bool ReadExact(Stream stream, byte[] buffer, int count)
        {
            var offset = 0;
            while (offset < count)
            {
                var read = stream.Read(buffer, offset, count - offset);
                if (read <= 0)
                {
                    return false;
                }
                offset += read;
            }
            return true;
        }
    }
}
