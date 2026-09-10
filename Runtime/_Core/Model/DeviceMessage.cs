using System;

namespace XRLumina._Core.Model
{
    /// <summary>장비 프로토콜로 수신하는 JSON 메시지 필드를 나타낸다.</summary>
    [Serializable]
    internal sealed class DeviceMessage
    {
        public string type;
        public string uuid;
        public int seq;
        public string selectionId;
        public string action;
        public string id;
        public bool ok;
        public string error;
    }
}
