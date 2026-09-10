using UnityEngine;

namespace XRLumina._Core.Model
{
    /// <summary>시각, 시선, 카메라 자세를 묶은 내부 기록 모델.</summary>
    internal readonly struct TimedGazeFrame
    {
        public readonly float CapturedAt;
        public readonly XRLuminaGazeFrame Frame;
        public readonly Vector3 Position;
        public readonly Quaternion Rotation;

        public TimedGazeFrame(
            float capturedAt,
            XRLuminaGazeFrame frame,
            Vector3 position,
            Quaternion rotation)
        {
            CapturedAt = capturedAt;
            Frame = frame;
            Position = position;
            Rotation = rotation;
        }
    }
}
