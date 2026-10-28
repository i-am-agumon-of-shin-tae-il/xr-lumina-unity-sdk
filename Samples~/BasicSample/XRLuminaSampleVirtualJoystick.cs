using UnityEngine;
using UnityEngine.EventSystems;

namespace XRLumina.Sample
{
    /// <summary>각 손가락을 독립적으로 추적하고 원형 패드의 아날로그 입력을 제공한다.</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class XRLuminaSampleVirtualJoystick : UnityEngine.UI.Graphic,
        IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        private const float Deadzone = 0.12f;
        private const int Segments = 48;
        private int? _pointerId;
        private Vector2 _stick;

        public Vector2 Value { get; private set; }

        /// <summary>다른 손가락이 점유하지 않은 패드의 터치 입력을 시작한다.</summary>
        public void OnPointerDown(PointerEventData eventData)
        {
            if (_pointerId.HasValue || eventData.button != PointerEventData.InputButton.Left)
            {
                return;
            }
            _pointerId = eventData.pointerId;
            UpdateStick(eventData);
        }

        /// <summary>이 패드를 점유한 손가락의 드래그만 이동 입력에 반영한다.</summary>
        public void OnDrag(PointerEventData eventData)
        {
            if (_pointerId == eventData.pointerId)
            {
                UpdateStick(eventData);
            }
        }

        /// <summary>점유한 손가락을 떼면 패드를 중앙으로 되돌리고 입력을 해제한다.</summary>
        public void OnPointerUp(PointerEventData eventData)
        {
            if (_pointerId == eventData.pointerId)
            {
                ResetInput();
            }
        }

        /// <summary>화면 좌표를 원형 범위로 제한하고 데드존 이후의 입력을 연속적으로 보정한다.</summary>
        private void UpdateStick(PointerEventData eventData)
        {
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                rectTransform, eventData.position, eventData.pressEventCamera, out Vector2 local))
            {
                return;
            }
            float travel = Mathf.Min(rectTransform.rect.width, rectTransform.rect.height) * 0.32f;
            if (travel <= 0f)
            {
                ResetInput();
                return;
            }
            _stick = Vector2.ClampMagnitude((local - rectTransform.rect.center) / travel, 1f);
            float magnitude = _stick.magnitude;
            Value = magnitude <= Deadzone
                ? Vector2.zero
                : _stick.normalized * ((magnitude - Deadzone) / (1f - Deadzone));
            SetVerticesDirty();
        }

        /// <summary>터치 취소·비활성화·포커스 변경 시 남은 입력을 제거한다.</summary>
        public void ResetInput()
        {
            _pointerId = null;
            _stick = Vector2.zero;
            Value = Vector2.zero;
            SetVerticesDirty();
        }

        /// <summary>패드가 숨겨질 때 터치 소유권과 이동값을 함께 해제한다.</summary>
        protected override void OnDisable()
        {
            ResetInput();
            base.OnDisable();
        }

        /// <summary>별도 이미지 자산 없이 반투명 원형 받침과 손잡이를 그린다.</summary>
        protected override void OnPopulateMesh(UnityEngine.UI.VertexHelper vertices)
        {
            vertices.Clear();
            Vector2 center = rectTransform.rect.center;
            float radius = Mathf.Min(rectTransform.rect.width, rectTransform.rect.height) * 0.5f;
            AddCircle(vertices, center, radius, new Color(0.08f, 0.12f, 0.18f, 0.55f));
            AddCircle(vertices, center, radius * 0.88f, new Color(0.22f, 0.32f, 0.4f, 0.5f));
            AddCircle(vertices, center + _stick * (radius * 0.64f), radius * 0.32f,
                new Color(0.7f, 0.95f, 1f, _pointerId.HasValue ? 0.95f : 0.72f));
        }

        /// <summary>중심점과 외곽 삼각형으로 조이패드의 원형 그래픽을 구성한다.</summary>
        private static void AddCircle(UnityEngine.UI.VertexHelper vertices, Vector2 center, float radius, Color tint)
        {
            int first = vertices.currentVertCount;
            vertices.AddVert(center, tint, Vector2.zero);
            for (int index = 0; index <= Segments; index++)
            {
                float angle = index * Mathf.PI * 2f / Segments;
                Vector2 position = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
                vertices.AddVert(position, tint, Vector2.zero);
                if (index > 0)
                {
                    vertices.AddTriangle(first, first + index, first + index + 1);
                }
            }
        }
    }
}
