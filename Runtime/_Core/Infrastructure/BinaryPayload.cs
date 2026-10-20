using System;
using System.IO;

namespace XRLumina._Core.Infrastructure
{
    /// <summary>바이너리 페이로드를 공통 규격으로 작성한다.</summary>
    internal static class BinaryPayload
    {
        /// <summary>작성 버퍼의 유효 구간을 반환하여 페이로드의 추가 배열 복사를 방지한다.</summary>
        public static ArraySegment<byte> Create(Action<BinaryWriter> write, int capacity = 256)
        {
            using (var stream = new MemoryStream(capacity))
            using (var writer = new BinaryWriter(stream))
            {
                write(writer);
                writer.Flush();
                return new ArraySegment<byte>(stream.GetBuffer(), 0, checked((int)stream.Length));
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
