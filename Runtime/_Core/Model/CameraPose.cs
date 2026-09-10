using System;

namespace XRLumina._Core.Model
{
    /// <summary>파노라마 캡처 시점의 카메라 자세 JSON 모델.</summary>
    [Serializable]
    internal sealed class CameraPose
    {
        public float px;
        public float py;
        public float pz;
        public float rx;
        public float ry;
        public float rz;
        public float rw;
    }
}
