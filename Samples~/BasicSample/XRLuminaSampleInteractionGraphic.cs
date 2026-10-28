using UnityEngine;

namespace XRLumina.Sample
{
    /// <summary>두 조이패드 사이의 상호작용 버튼을 과녁 아이콘으로 표시한다.</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class XRLuminaSampleInteractionGraphic : UnityEngine.UI.Graphic
    {
        /// <summary>버튼 받침과 과녁 모양을 별도 이미지 없이 그린다.</summary>
        protected override void OnPopulateMesh(UnityEngine.UI.VertexHelper vertices)
        {
            vertices.Clear();
            Vector2 center = rectTransform.rect.center;
            float radius = Mathf.Min(rectTransform.rect.width, rectTransform.rect.height) * 0.5f;
            AddCircle(vertices, center, radius, new Color(0.1f, 0.18f, 0.23f, 0.85f) * color);
            AddCircle(vertices, center, radius * 0.56f, new Color(0.7f, 0.95f, 1f, 1f) * color);
            AddCircle(vertices, center, radius * 0.43f, new Color(0.1f, 0.18f, 0.23f, 1f) * color);
            AddCircle(vertices, center, radius * 0.16f, new Color(0.7f, 0.95f, 1f, 1f) * color);
        }

        /// <summary>과녁을 구성하는 원의 삼각형 메시를 생성한다.</summary>
        private static void AddCircle(UnityEngine.UI.VertexHelper vertices, Vector2 center, float radius, Color tint)
        {
            const int segments = 48;
            int first = vertices.currentVertCount;
            vertices.AddVert(center, tint, Vector2.zero);
            for (int index = 0; index <= segments; index++)
            {
                float angle = index * Mathf.PI * 2f / segments;
                vertices.AddVert(center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius, tint, Vector2.zero);
                if (index > 0)
                {
                    vertices.AddTriangle(first, first + index, first + index + 1);
                }
            }
        }
    }
}
