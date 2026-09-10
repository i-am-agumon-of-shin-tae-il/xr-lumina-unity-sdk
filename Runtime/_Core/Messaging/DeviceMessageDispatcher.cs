using System;
using System.Collections.Generic;
using XRLumina._Core.Model;

namespace XRLumina._Core.Messaging
{
    /// <summary>수신 메시지를 타입에 해당하는 수신 처리기로 전달한다.</summary>
    internal sealed class DeviceMessageDispatcher
    {
        private readonly Dictionary<string, Action<DeviceMessage>> _handlers;

        /// <summary>수신 처리기의 메시지 핸들러를 등록한다.</summary>
        public DeviceMessageDispatcher(DeviceMessageReceiver receiver)
        {
            _handlers = new Dictionary<string, Action<DeviceMessage>>
            {
                ["session"] = receiver.ReceiveSession,
                ["command"] = receiver.ReceiveCommand,
                ["api:res"] = receiver.ReceiveApiResponse,
            };
        }

        /// <summary>메시지 타입에 등록된 수신 처리기를 실행한다.</summary>
        public void Dispatch(DeviceMessage message)
        {
            if (message.type != null && _handlers.TryGetValue(message.type, out var handler))
            {
                handler(message);
            }
        }
    }
}
