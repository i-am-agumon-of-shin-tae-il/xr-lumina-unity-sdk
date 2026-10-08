using UnityEngine;
using XRLumina._Core.Service;
using XRLumina.Client;

namespace XRLumina.Features
{
    /// <summary>인스펙터의 아이트래킹 설정을 런타임 서비스에 연결하는 SDK 컴포넌트.</summary>
    public sealed class EyeTrackingController : MonoBehaviour
    {
        [SerializeField] private LayerMask gazeLayerMask = ~0;
        [SerializeField] private float maxDistance = 100f;
        [SerializeField] private int framesPerSecond = 5;
        [SerializeField] private int stationaryDurationMs = 1000;
        [SerializeField] private float movementThresholdMeters = 0.05f;
        [SerializeField] private int panoramaWidth = 2048;
        [SerializeField] private int panoramaHeight = 1024;
        [SerializeField] private int panoramaCubemapSize = 512;
        [SerializeField] private int captureChunkSize = 64 * 1024;

        private XRLuminaClientController _rig;
        private bool _warnedMissingMovementSource;
        private EyeTrackingService _service;
        private XRLuminaClientService _client;
        private Coroutine _recordingCoroutine;

        /// <summary>클라이언트 서비스에 연결하고 측정 상태 이벤트를 구독한다.</summary>
        private void Start()
        {
            _rig = XRLuminaClientController.Instance;
            _client = _rig?.Service;
            if (_client == null)
            {
                Debug.LogError("[XRLumina] Client service is unavailable.", this);
                enabled = false;
                return;
            }
            _service = new EyeTrackingService(
                _client,
                () => _rig != null ? _rig.HeadCamera : null,
                ResolveMovementSource,
                gazeLayerMask,
                maxDistance,
                framesPerSecond,
                stationaryDurationMs,
                movementThresholdMeters,
                panoramaWidth,
                panoramaHeight,
                panoramaCubemapSize,
                captureChunkSize);
            _client.MeasurementStarted += StartFeature;
            _client.MeasurementFinished += FinishFeature;
            _client.FlushRequested += RetryFlush;
        }

        /// <summary>아이트래킹 이동 판정에 사용할 플레이어 Transform을 반환한다.</summary>
        private Transform ResolveMovementSource()
        {
            var source = _rig != null ? _rig.Body : null;
            if (source == null && !_warnedMissingMovementSource)
            {
                Debug.LogWarning("[XRLumina] 아이트래킹 이동 판정용 플레이어 루트가 없습니다. XRLuminaClientController의 Body를 연결하세요.", this);
                _warnedMissingMovementSource = true;
            }
            else if (source != null)
            {
                _warnedMissingMovementSource = false;
            }
            return source;
        }

        /// <summary>아이트래킹 측정 시작을 서비스에 전달한다.</summary>
        private void StartFeature()
        {
            StopRecording();
            var routine = _service?.StartFeature();
            if (routine != null)
            {
                _recordingCoroutine = StartCoroutine(routine);
            }
        }

        /// <summary>아이트래킹 측정 종료를 서비스에 전달한다.</summary>
        private  void FinishFeature()
        {
            StopRecording();
            _service?.FinishFeature();
            RetryFlush();
        }

        /// <summary>완료된 구간을 재생성하지 않고 아이트래킹 종료 요청만 재전송한다.</summary>
        private void RetryFlush()
        {
            if (_service != null)
            {
                StartCoroutine(_service.Flush(180f, LogFlushResult));
            }
        }

        /// <summary>캡처·업로드 응답 실패를 Unity 로그에 전달한다.</summary>
        private void LogFlushResult(bool ok, string error)
        {
            if (!ok)
            {
                Debug.LogError($"[XRLumina] 아이트래킹 종료 실패: {error}", this);
            }
        }

        /// <summary>컴포넌트가 제거될 때 서비스 자원을 정리한다.</summary>
        private void OnDestroy()
        {
            if (_client != null)
            {
                _client.MeasurementStarted -= StartFeature;
                _client.MeasurementFinished -= FinishFeature;
                _client.FlushRequested -= RetryFlush;
            }
            StopRecording();
        }

        /// <summary>실행 중인 아이트래킹 기록 코루틴을 중지한다.</summary>
        private void StopRecording()
        {
            if (_recordingCoroutine == null)
            {
                return;
            }
            StopCoroutine(_recordingCoroutine);
            _recordingCoroutine = null;
        }
    }
}
