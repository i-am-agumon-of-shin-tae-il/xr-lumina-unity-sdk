using System.Collections;
using UnityEngine;
using UnityEngine.Events;

namespace XRLumina.Sample
{
    /// <summary>샘플 타겟 입력 시 지정된 액션을 호출하고 크기 피드백을 표시한다.</summary>
    public sealed class XRLuminaSampleContactAction : MonoBehaviour
    {
        [SerializeField] private UnityEvent onContact = new UnityEvent();
        [SerializeField] private float feedbackDuration = 0.1f;

        private Vector3 _baseScale;
        private Coroutine _feedbackCoroutine;

        /// <summary>사용자가 설정한 박스 크기를 피드백 복원 기준으로 저장한다.</summary>
        private void Awake()
        {
            _baseScale = transform.localScale;
        }

        /// <summary>지정된 액션을 호출하고 박스를 잠시 확대한다.</summary>
        public void Invoke()
        {
            if (_feedbackCoroutine != null)
            {
                StopCoroutine(_feedbackCoroutine);
            }
            else
            {
                _baseScale = transform.localScale;
            }
            _feedbackCoroutine = StartCoroutine(ShowFeedback());
            onContact.Invoke();
        }

        /// <summary>기존 샘플 타겟과 같은 비율로 확대했다가 원래 크기로 복원한다.</summary>
        private IEnumerator ShowFeedback()
        {
            transform.localScale = _baseScale * 1.12f;
            yield return new WaitForSeconds(feedbackDuration);
            transform.localScale = _baseScale;
            _feedbackCoroutine = null;
        }

        /// <summary>비활성화될 때 진행 중인 피드백을 중단하고 원래 크기를 복원한다.</summary>
        private void OnDisable()
        {
            if (_feedbackCoroutine != null)
            {
                StopCoroutine(_feedbackCoroutine);
                _feedbackCoroutine = null;
                transform.localScale = _baseScale;
            }
        }
    }
}
