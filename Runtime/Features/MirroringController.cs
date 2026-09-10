using UnityEngine;
using XRLumina._Core.Service;
using XRLumina.Client;

namespace XRLumina.Features
{
    /// <summary>인스펙터의 미러링 설정을 런타임 서비스에 연결하는 SDK 컴포넌트.</summary>
    public sealed class MirroringController : MonoBehaviour
    {
        [SerializeField] private bool streamEnabled = true;
        [SerializeField] private Camera sourceCamera;
        [SerializeField] private int width = 640;
        [SerializeField] private int height = 480;
        [SerializeField] private int framesPerSecond = 20;
        [SerializeField, Range(1, 100)] private int jpegQuality = 50;

        private MirroringService _service;
        private XRLuminaClientService _client;
        private Coroutine _streamCoroutine;

        /// <summary>클라이언트 서비스에 연결하고 미러링 상태 이벤트를 구독한다.</summary>
        private void Start()
        {
            _client = XRLuminaClientController.Instance?.Service;
            if (_client == null)
            {
                Debug.LogError("[XRLumina] Client service is unavailable.", this);
                enabled = false;
                return;
            }
            _service = new MirroringService(
                _client,
                streamEnabled,
                sourceCamera,
                width,
                height,
                framesPerSecond,
                jpegQuality);
            _client.SessionAccepted += StartStreaming;
            _client.MeasurementStarted += StartStreaming;
            _client.MeasurementFinished += StopStreaming;
            _client.Disconnected += StopStreaming;
        }

        /// <summary>미러링 스트림 시작을 서비스에 전달한다.</summary>
        private void StartStreaming()
        {
            if (_streamCoroutine != null || _service?.StartStreaming() != true)
            {
                return;
            }
            _streamCoroutine = StartCoroutine(_service.StreamLoop());
        }

        /// <summary>미러링 스트림 종료를 서비스에 전달한다.</summary>
        private void StopStreaming()
        {
            if (_streamCoroutine != null)
            {
                StopCoroutine(_streamCoroutine);
                _streamCoroutine = null;
            }
            _service?.StopStreaming();
        }

        /// <summary>컴포넌트가 제거될 때 서비스 자원을 정리한다.</summary>
        private void OnDestroy()
        {
            if (_client != null)
            {
                _client.SessionAccepted -= StartStreaming;
                _client.MeasurementStarted -= StartStreaming;
                _client.MeasurementFinished -= StopStreaming;
                _client.Disconnected -= StopStreaming;
            }
            StopStreaming();
        }
    }
}
