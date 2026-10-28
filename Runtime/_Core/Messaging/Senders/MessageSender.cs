using System;
using XRLumina.Protocol;
using XRLumina._Core.Infrastructure;

namespace XRLumina._Core.Messaging.Senders
{
    /// <summary>공통 메시지 전송과 측정 데이터의 인코딩·송신 대기열을 관리한다.</summary>
    internal sealed class MessageSender
    {
        private readonly Func<TcpTransport> _resolveTransport;

        /// <summary>현재 선택된 공통 전송 계층을 사용하는 메시지 센더를 생성한다.</summary>
        internal MessageSender(Func<TcpTransport> resolveTransport)
        {
            _resolveTransport = resolveTransport;
        }

        /// <summary>선택한 연결에 생성된 메시지를 공통 Envelope로 전송한다.</summary>
        internal bool SendMessage(MessageUnion message)
        {
            return SendTo(_resolveTransport(), message);
        }

        /// <summary>지정한 연결에 생성된 메시지를 공통 Envelope로 인코딩해 전송한다.</summary>
        internal static bool SendTo(TcpTransport transport, MessageUnion message)
        {
            return transport?.Send(message.Type, FlatBufferCodec.Encode(message)) == true;
        }

        /// <summary>JPEG 미러링 프레임을 스키마의 바이트 벡터로 전송한다.</summary>
        public bool SendMirrorFrame(byte[] payload)
        {
            if (payload == null)
            {
                return false;
            }
            // 이미지 벡터는 객체 API의 List 변환 없이 원본 배열에서 작성한다.
            var builder = new Google.FlatBuffers.FlatBufferBuilder(payload.Length + 128);
            var data = MirrorFrame.CreateDataVectorBlock(builder, payload);
            var format = ImageFormat.Jpeg;
            var frame = MirrorFrame.CreateMirrorFrame(builder, format, data);
            var envelope = Envelope.CreateEnvelope(builder, 1, Message.MirrorFrame, frame.Value);
            Envelope.FinishSizePrefixedEnvelopeBuffer(builder, envelope);
            return _resolveTransport()?.Send(Message.MirrorFrame, builder.SizedByteArray()) == true;
        }

        /// <summary>측정 메시지를 인코딩하고 미연결이면 재전송 큐에 추가한다.</summary>
        internal void SendStream(MessageUnion message)
        {
            var payload = FlatBufferCodec.Encode(message);
            var transport = _resolveTransport();
            if (transport != null && !transport.Send(message.Type, payload))
            {
                transport.Enqueue(message.Type, new ArraySegment<byte>(payload));
            }
        }

        /// <summary>PNG 파일을 추가 청크 메타데이터와 배열 변환 없이 전송한다.</summary>
        internal void SendCapture(string name, byte[] bytes)
        {
            var builder = new Google.FlatBuffers.FlatBufferBuilder(bytes.Length + 128);
            var data = Capture.CreateDataVectorBlock(builder, bytes);
            var fileName = builder.CreateString(name);
            var image = Capture.CreateCapture(builder, fileName, data);
            SendEncodedStream(builder, Message.Capture, image.Value);
        }

        /// <summary>휴리스틱 정보와 PNG 파일을 단일 FlatBuffers 메시지로 전송한다.</summary>
        internal void SendScreenshot(string name, XRLumina._Core.Model.HeuristicScreenshotTrigger trigger, byte[] bytes)
        {
            var builder = new Google.FlatBuffers.FlatBufferBuilder(bytes.Length + 256);
            var data = HeuristicScreenshot.CreateDataVectorBlock(builder, bytes);
            var fileName = builder.CreateString(name);
            var objectName = builder.CreateString(trigger.ObjectName);
            var image = HeuristicScreenshot.CreateHeuristicScreenshot(builder, fileName, trigger.Type,
                trigger.CapturedAt, objectName, trigger.ControllerInput, trigger.Hand, data);
            SendEncodedStream(builder, Message.HeuristicScreenshot, image.Value);
        }

        /// <summary>작성한 파일 메시지를 공통 Envelope로 마무리하고 재전송 정책을 적용한다.</summary>
        private void SendEncodedStream(Google.FlatBuffers.FlatBufferBuilder builder, Message type, int offset)
        {
            var envelope = Envelope.CreateEnvelope(builder, 1, type, offset);
            Envelope.FinishSizePrefixedEnvelopeBuffer(builder, envelope);
            var payload = builder.SizedByteArray();
            if (payload.Length - 4 > FlatBufferCodec.MaxMessageBytes)
            {
                throw new System.IO.InvalidDataException("image exceeds maximum message size");
            }
            var transport = _resolveTransport();
            if (transport != null && !transport.Send(type, payload))
            {
                transport.Enqueue(type, new ArraySegment<byte>(payload));
            }
        }
    }
}
