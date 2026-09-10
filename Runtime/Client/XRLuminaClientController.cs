using System;
using System.Collections;
using UnityEngine;
using XRLumina._Core.Model;
using XRLumina._Core.Service;

namespace XRLumina.Client
{
    /// <summary>인스펙터 설정과 Unity 생명주기를 SDK 런타임 서비스에 연결하는 메인 컴포넌트.</summary>
    public sealed class XRLuminaClientController : MonoBehaviour
    {
        public static XRLuminaClientController Instance { get; private set; }

        public event Action<string> OnCommand;
        public event Action SessionReady;

        [SerializeField] private int discoveryTimeoutMs = 800;
        [SerializeField] private int retryDelayMs = 1500;

        private readonly string _host = "";
        private readonly int _port = 9999;
        private readonly int _discoveryPort = 9998;
        private XRLuminaClientService _service;

        public XRLuminaSession Session => _service?.Session;
        public bool IsAuthenticated => _service?.IsAuthenticated ?? false;
        internal XRLuminaClientService Service => _service;

        /// <summary>인스펙터 연결 설정으로 클라이언트 서비스를 생성한다.</summary>
        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;

            DontDestroyOnLoad(gameObject);

            _service = new XRLuminaClientService(_host, _port, _discoveryPort, discoveryTimeoutMs, retryDelayMs);
            _service.CommandReceived += HandleCommand;
            _service.SessionReady += HandleSessionReady;
        }

        /// <summary>SDK 연결 시작을 서비스에 전달한다.</summary>
        private IEnumerator Start()
        {
            yield return null;
            _service?.Start();
        }

        /// <summary>Unity 프레임 갱신을 서비스에 전달한다.</summary>
        private void Update()
        {
            _service?.Tick();
        }

        /// <summary>Electron 호스트에 테스트 시작을 요청한다.</summary>
        public bool RequestStart()
        {
            return _service?.RequestStart() ?? false;
        }

        /// <summary>Electron 호스트에 테스트 종료를 요청한다.</summary>
        public bool RequestFinish()
        {
            return _service?.RequestFinish() ?? false;
        }

        /// <summary>서비스 명령 이벤트를 공개 컴포넌트 이벤트로 전달한다.</summary>
        private void HandleCommand(string action)
        {
            OnCommand?.Invoke(action);
        }

        /// <summary>서비스 세션 이벤트를 공개 컴포넌트 이벤트로 전달한다.</summary>
        private void HandleSessionReady()
        {
            SessionReady?.Invoke();
        }

        /// <summary>컴포넌트가 제거될 때 서비스 구독과 자원을 정리한다.</summary>
        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
            if (_service == null)
            {
                return;
            }
            _service.CommandReceived -= HandleCommand;
            _service.SessionReady -= HandleSessionReady;
            _service.Dispose();
            _service = null;
        }
    }
}
