using System;
using System.IO;

namespace XRLumina._Core.Infrastructure
{
    /// <summary>바이너리 페이로드를 공통 규격으로 작성한다.</summary>
    internal static class BinaryPayload
    {
        public static byte[] Create(Action<BinaryWriter> write)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                write(writer);
                writer.Flush();
                return stream.ToArray();
            }
        }

        public static void WriteString(BinaryWriter writer, string value)
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(value ?? string.Empty);
            if (bytes.Length > ushort.MaxValue)
            {
                throw new InvalidDataException($"string payload too long: {bytes.Length}");
            }
            writer.Write((ushort)bytes.Length);
            writer.Write(bytes);
        }
    }
}
