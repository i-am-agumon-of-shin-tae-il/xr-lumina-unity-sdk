using System.Collections.Generic;
using UnityEngine;

namespace XRLumina._Core.Model
{
    /// <summary>단일 시점의 시선 추적 결과를 나타내는 SDK 데이터 모델.</summary>
    public readonly struct XRLuminaGazeFrame
    {
        public readonly int Frame;
        public readonly float Time;
        public readonly IReadOnlyList<Vector3> Points;

        /// <summary>프레임 번호, 시간, 시선 충돌 지점으로 시선 프레임을 생성한다.</summary>
        public XRLuminaGazeFrame(int frame, float time, IReadOnlyList<Vector3> points)
        {
            Frame = frame;
            Time = time;
            Points = points;
        }
    }
}
