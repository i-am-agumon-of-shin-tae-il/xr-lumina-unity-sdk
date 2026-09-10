using System.Collections;
using UnityEngine;
using XRLumina._Core.Model;
using XRLumina.Features;

namespace XRLumina.Sample
{
    /// <summary>샘플 씬의 타깃 선택을 SDK 인터랙션 이벤트 기록으로 연결한다.</summary>
    [RequireComponent(typeof(Collider))]
    public sealed class XRLuminaSampleInteractionTarget : MonoBehaviour
    {
        [SerializeField] private XRLuminaInteractionEventType interactionType;
        [SerializeField] private Renderer targetRenderer;
        [SerializeField] private float feedbackDuration = 0.1f;

        private Coroutine _feedbackCoroutine;
        private Vector3 _baseScale;
        private TextMesh _label;
        private Camera _labelCamera;

        /// <summary>시각 피드백에 사용할 Renderer를 준비한다.</summary>
        private void Awake()
        {
            _baseScale = transform.localScale;
            if (targetRenderer == null)
            {
                targetRenderer = GetComponent<Renderer>();
            }
            _label = GetComponentInChildren<TextMesh>(true);
        }

        /// <summary>타깃 라벨이 플레이어 카메라를 향하면서 수직 방향을 유지하도록 회전한다.</summary>
        private void LateUpdate()
        {
            if (_label == null)
            {
                return;
            }
            if (_labelCamera == null)
            {
                _labelCamera = Camera.main;
            }
            if (_labelCamera == null)
            {
                return;
            }

            Vector3 direction = _label.transform.position - _labelCamera.transform.position;
            direction.y = 0f;
            if (direction.sqrMagnitude <= 0.0001f)
            {
                return;
            }
            _label.transform.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
        }

        /// <summary>설정된 종류의 SDK 인터랙션 이벤트를 기록하고 타깃을 점멸시킨다.</summary>
        public void Interact()
        {
            InteractionController.Instance?.RecordInteractionEvent(interactionType);
            if (_feedbackCoroutine != null)
            {
                StopCoroutine(_feedbackCoroutine);
            }
            transform.localScale = _baseScale;
            _feedbackCoroutine = StartCoroutine(ShowFeedback());
        }

        /// <summary>선택된 타깃의 크기를 잠시 키워 입력 성공을 표시한다.</summary>
        private IEnumerator ShowFeedback()
        {
            transform.localScale = _baseScale * 1.12f;
            yield return new WaitForSeconds(feedbackDuration);
            transform.localScale = _baseScale;
            _feedbackCoroutine = null;
        }

        /// <summary>타깃이 비활성화될 때 시각 피드백 스케일을 원래 크기로 복원한다.</summary>
        private void OnDisable()
        {
            if (_feedbackCoroutine != null)
            {
                StopCoroutine(_feedbackCoroutine);
                _feedbackCoroutine = null;
            }
            transform.localScale = _baseScale;
        }
    }
}
