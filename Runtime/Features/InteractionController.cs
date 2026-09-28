using UnityEngine;
using XRLumina._Core.Model;
using XRLumina._Core.Service;
using XRLumina.Client;

namespace XRLumina.Features
{
    /// <summary>인스펙터의 상호작용 설정을 런타임 서비스에 연결하는 SDK 컴포넌트.</summary>
    public sealed class InteractionController : MonoBehaviour
    {
        public static InteractionController Instance { get; private set; }

        [SerializeField] private int framesPerSecond = 5;
        [SerializeField] private float joystickDeadzone = 0.5f;
        [SerializeField] private float stationaryDurationSeconds = 3f;
        [SerializeField] private float stationaryMovementThreshold = 0.15f;
        [SerializeField] private float yawWindowSeconds = 5f;
        [SerializeField] private int yawRequiredReversals = 3;
        [SerializeField] private float yawMinimumSwingDegrees = 30f;
        [SerializeField] private float interactionWindowSeconds = 5f;
        [SerializeField] private int interactionRequiredCount = 3;
        [SerializeField] private LayerMask interactionLayerMask = ~0;
        [SerializeField] private float interactionMaxDistance = 100f;
        [SerializeField] private int heuristicCaptureWidth = 1280;
        [SerializeField] private int heuristicCaptureHeight = 720;
        [SerializeField] private int heuristicCaptureChunkSize = 64 * 1024;

        private InteractionService _service;
        private HeuristicScreenshotService _heuristicScreenshotService;
        private XRLuminaClientService _client;
        private Coroutine _trackingCoroutine;

        /// <summary>현재 상호작용 컴포넌트를 SDK 접근점으로 등록한다.</summary>
        private void Awake()
        {
            if (Instance == null)
            {
                Instance = this;
            }
        }

        /// <summary>클라이언트 서비스에 연결하고 측정 상태 이벤트를 구독한다.</summary>
        private void Start()
        {
            var client = XRLuminaClientController.Instance;
            _client = client?.Service;
            if (_client == null)
            {
                Debug.LogError("[XRLumina] Client service is unavailable.", this);
                enabled = false;
                return;
            }
            _service = new InteractionService(
                _client,
                () => client.Head,
                () => client.Body,
                framesPerSecond,
                joystickDeadzone);
            _heuristicScreenshotService = new HeuristicScreenshotService(
                _client,
                () => client.HeadCamera,
                () => client.Head,
                stationaryDurationSeconds,
                stationaryMovementThreshold,
                yawWindowSeconds,
                yawRequiredReversals,
                yawMinimumSwingDegrees,
                interactionWindowSeconds,
                interactionRequiredCount,
                interactionLayerMask,
                interactionMaxDistance,
                heuristicCaptureWidth,
                heuristicCaptureHeight,
                heuristicCaptureChunkSize);
            _client.MeasurementStarted += StartFeature;
            _client.MeasurementFinished += FinishFeature;
        }

        /// <summary>상호작용 측정 시작을 서비스에 전달한다.</summary>
        private void StartFeature()
        {
            StopTracking();
            _heuristicScreenshotService?.StartFeature();
            var routine = _service?.StartFeature();
            if (routine != null)
            {
                _trackingCoroutine = StartCoroutine(routine);
            }
        }

        /// <summary>상호작용 측정 종료를 서비스에 전달한다.</summary>
        private void FinishFeature()
        {
            _service?.FinishFeature();
            StopTracking();
            if (_service != null)
            {
                StartCoroutine(_service.Flush(framesPerSecond, 60f, null));
            }
            if (_heuristicScreenshotService != null)
            {
                StartCoroutine(_heuristicScreenshotService.FinishAndFlush(60f, null));
            }
        }

        /// <summary>Unity 프레임 갱신을 서비스에 전달한다.</summary>
        private void Update()
        {
            _service?.Tick();
            var trigger = _heuristicScreenshotService?.Tick();
            if (trigger.HasValue)
            {
                StartCoroutine(_heuristicScreenshotService.CaptureAndSend(trigger.Value));
            }
        }

        /// <summary>지정 종류의 상호작용 이벤트 기록을 서비스에 전달한다.</summary>
        public void RecordInteractionEvent(XRLuminaInteractionEventType type)
        {
            if (_client?.IsMeasuring != true)
            {
                return;
            }

            _service?.RecordEvent(type);
        }

        /// <summary>대상 오브젝트와 Controller 입력을 포함한 상호작용 이벤트를 기록한다.</summary>
        public void RecordInteractionEvent(
            XRLuminaInteractionEventType type,
            GameObject target,
            string controllerInput)
        {
            if (_client?.IsMeasuring != true)
            {
                return;
            }

            _service?.RecordEvent(type);
            var trigger = _heuristicScreenshotService?.RecordInteraction(
                target,
                controllerInput,
                Time.realtimeSinceStartup);
            if (trigger.HasValue)
            {
                StartCoroutine(_heuristicScreenshotService.CaptureAndSend(trigger.Value));
            }
        }

        /// <summary>포즈 추적 종료를 서비스에 전달한다.</summary>
        private void StopTracking()
        {
            if (_trackingCoroutine != null)
            {
                StopCoroutine(_trackingCoroutine);
                _trackingCoroutine = null;
            }
        }

        /// <summary>컴포넌트가 제거될 때 서비스 자원을 정리한다.</summary>
        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
            if (_client != null)
            {
                _client.MeasurementStarted -= StartFeature;
                _client.MeasurementFinished -= FinishFeature;
            }
            StopTracking();
        }
    }
}
