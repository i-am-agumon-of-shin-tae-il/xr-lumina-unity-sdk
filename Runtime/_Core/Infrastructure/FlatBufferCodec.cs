using System;
using System.IO;
using Google.FlatBuffers;
using XRLumina.Protocol;

namespace XRLumina._Core.Infrastructure
{
    /// <summary>전송 방식과 엔진에 독립적인 FlatBuffers 메시지를 인코딩하고 검증한다.</summary>
    internal static class FlatBufferCodec
    {
        internal const int MaxMessageBytes = 64 * 1024 * 1024;

        /// <summary>생성된 메시지 객체를 표준 길이 접두사가 있는 Envelope로 작성한다.</summary>
        internal static byte[] Encode(MessageUnion message)
        {
            var builder = new FlatBufferBuilder(1024);
            var envelope = new EnvelopeT { ProtocolVersion = 1, Message = message, };
            Envelope.FinishSizePrefixedEnvelopeBuffer(builder, Envelope.Pack(builder, envelope));
            var bytes = builder.SizedByteArray();
            if (bytes.Length - 4 > MaxMessageBytes)
            {
                throw new InvalidDataException("message too large");
            }
            return bytes;
        }

        /// <summary>길이·식별자·테이블 구조·버전을 검증하고 생성된 메시지를 반환한다.</summary>
        internal static Envelope Decode(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 16 || bytes.Length - 4 > MaxMessageBytes)
            {
                throw new InvalidDataException("invalid message size");
            }
            var buffer = new ByteBuffer(bytes);
            if (!new Verifier(buffer).VerifyBuffer("XRLM", true, EnvelopeVerify.Verify))
            {
                throw new InvalidDataException("invalid XRLM FlatBuffer");
            }
            buffer.Position = 4;
            if (!Envelope.EnvelopeBufferHasIdentifier(buffer))
            {
                throw new InvalidDataException("invalid XRLM identifier");
            }
            var envelope = Envelope.GetRootAsEnvelope(buffer);
            if (envelope.ProtocolVersion != 1 || envelope.MessageType <= Message.NONE || envelope.MessageType > Message.DiscoveryResponse)
            {
                throw new InvalidDataException("unsupported protocol version or message");
            }
            return envelope;
        }

    }
}
