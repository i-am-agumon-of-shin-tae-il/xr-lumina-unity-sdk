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

        [SerializeField] private Transform head;
        [SerializeField] private Transform body;
        [SerializeField] private int framesPerSecond = 5;
        [SerializeField] private float joystickDeadzone = 0.5f;

        private InteractionService _service;
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
            _client = XRLuminaClientController.Instance?.Service;
            if (_client == null)
            {
                Debug.LogError("[XRLumina] Client service is unavailable.", this);
                enabled = false;
                return;
            }
            _service = new InteractionService(
                _client,
                head,
                body,
                framesPerSecond,
                joystickDeadzone);
            _client.MeasurementStarted += StartFeature;
            _client.MeasurementFinished += FinishFeature;
        }

        /// <summary>상호작용 측정 시작을 서비스에 전달한다.</summary>
        private void StartFeature()
        {
            StopTracking();
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
        }

        /// <summary>Unity 프레임 갱신을 서비스에 전달한다.</summary>
        private void Update()
        {
            _service?.Tick();
        }

        /// <summary>지정 종류의 상호작용 이벤트 기록을 서비스에 전달한다.</summary>
        public void RecordInteractionEvent(XRLuminaInteractionEventType type)
        {
            _service?.RecordEvent(type);
        }

        /// <summary>히트맵과 상호작용 위치에 사용할 머리 Transform을 설정한다.</summary>
        public void SetHead(Transform target)
        {
            head = target;
            _service?.SetHead(target);
        }

        /// <summary>이동·회전 분석에 사용할 몸 Transform을 설정한다.</summary>
        public void SetBody(Transform target)
        {
            body = target;
            _service?.SetBody(target);
        }

        /// <summary>포즈 추적 종료를 서비스에 전달한다.</summary>
        private void StopTracking()
        {
            if (_trackingCoroutine != null)
            {
                StopCoroutine(_trackingCoroutine);
                _trackingCoroutine = null;
            }
            _service?.StopTracking();
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
