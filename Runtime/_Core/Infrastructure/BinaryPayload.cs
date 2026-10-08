using System;
using System.IO;

namespace XRLumina._Core.Infrastructure
{
    /// <summary>바이너리 페이로드를 공통 규격으로 작성한다.</summary>
    internal static class BinaryPayload
    {
        /// <summary>예상 크기의 버퍼를 준비하여 확장 복사를 줄인 바이너리 페이로드를 생성한다.</summary>
        public static byte[] Create(Action<BinaryWriter> write, int capacity = 256)
        {
            using (var stream = new MemoryStream(capacity))
            using (var writer = new BinaryWriter(stream))
            {
                write(writer);
                writer.Flush();
                return stream.ToArray();
            }
        }

        /// <summary>UTF-8 문자열을 길이 접두사와 함께 기록한다.</summary>
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
