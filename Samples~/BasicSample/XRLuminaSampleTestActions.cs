using UnityEngine;
using XRLumina.Client;

namespace XRLumina.Sample
{
    /// <summary>샘플에서 호출할 테스트 제어 액션을 제공한다.</summary>
    public sealed class XRLuminaSampleTestActions : MonoBehaviour
    {
        /// <summary>데스크톱에 테스트 시작을 요청한다.</summary>
        public void StartTest()
        {
            XRLuminaClientController.Instance?.RequestStart();
        }

        /// <summary>데스크톱에 테스트 종료를 요청한다.</summary>
        public void FinishTest()
        {
            XRLuminaClientController.Instance?.RequestFinish();
        }
    }
}
