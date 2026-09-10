using UnityEngine;
using XRLumina._Core.Service;
using XRLumina.Client;

namespace XRLumina.Features
{
    /// <summary>인스펙터의 아이트래킹 설정을 런타임 서비스에 연결하는 SDK 컴포넌트.</summary>
    public sealed class EyeTrackingController : MonoBehaviour
    {
        [SerializeField] private Camera sourceCamera;
        [SerializeField] private LayerMask gazeLayerMask = ~0;
        [SerializeField] private float maxDistance = 100f;
        [SerializeField] private int framesPerSecond = 5;
        [SerializeField] private int stationaryDurationMs = 1000;
        [SerializeField] private float movementThresholdMeters = 0.05f;
        [SerializeField] private int panoramaWidth = 2048;
        [SerializeField] private int panoramaHeight = 1024;
        [SerializeField] private int panoramaCubemapSize = 512;
        [SerializeField] private int captureChunkSize = 64 * 1024;

        private EyeTrackingService _service;
        private XRLuminaClientService _client;
        private Coroutine _recordingCoroutine;

        /// <summary>클라이언트 서비스에 연결하고 측정 상태 이벤트를 구독한다.</summary>
        private void Start()
        {
            _client = XRLuminaClientController.Instance?.Service;
            if (_client == null)
            {
                Debug.LogError("[XRLumina] Client service is unavailable.", this);
                enabled = false;
                return;
            }
            _service = new EyeTrackingService(
                _client,
                sourceCamera,
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
            if (_service != null)
            {
                StartCoroutine(_service.Flush(60f, null));
            }
        }

        /// <summary>컴포넌트가 제거될 때 서비스 자원을 정리한다.</summary>
        private void OnDestroy()
        {
            if (_client != null)
            {
                _client.MeasurementStarted -= StartFeature;
                _client.MeasurementFinished -= FinishFeature;
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
