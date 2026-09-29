using UnityEngine;
using XRLumina.Client;

namespace XRLumina.Sample
{
    /// <summary>샘플 라벨이 플레이어를 향하면서 수직 방향을 유지하도록 한다.</summary>
    public sealed class XRLuminaSampleFacingLabel : MonoBehaviour
    {
        /// <summary>SDK에 지정된 머리 위치를 기준으로 라벨의 수평 회전을 갱신한다.</summary>
        private void LateUpdate()
        {
            var head = XRLuminaClientController.Instance?.Head;
            if (head == null)
            {
                return;
            }
            Vector3 direction = transform.position - head.position;
            direction.y = 0f;
            if (direction.sqrMagnitude > 0.0001f)
            {
                transform.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
            }
        }
    }
}
