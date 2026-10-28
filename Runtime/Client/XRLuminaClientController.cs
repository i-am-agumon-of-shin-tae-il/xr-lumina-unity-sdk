using System;
using XRLumina.Protocol;
using System.Collections;
using UnityEngine;
using XRLumina._Core.Model;

namespace XRLumina.Client
{
    /// <summary>인스펙터 설정과 Unity 생명주기를 SDK 런타임 서비스에 연결하는 메인 컴포넌트.</summary>
    public sealed class XRLuminaClientController : MonoBehaviour
    {
        public static XRLuminaClientController Instance { get; private set; }

        public event Action<TestAction> OnCommand;
        public event Action SessionReady;

        [SerializeField] private Transform head;
        [SerializeField] private Transform body;
        [SerializeField] private int discoveryTimeoutMs = 800;
        [SerializeField] private int retryDelayMs = 1500;
        [SerializeField] private bool usbEnabled = true;
        [SerializeField] private int usbPort = 10000;
#if UNITY_ANDROID && !UNITY_EDITOR
        private AndroidJavaObject _usbBridge;
#endif

        private readonly string _host = "";
        private readonly int _port = 9999;
        private readonly int _discoveryPort = 9998;
        private XRLuminaClient _service;

        public Transform Head => head;
        public Transform Body => body;
        public Camera HeadCamera => head != null ? head.GetComponent<Camera>() : null;

        public XRLuminaSession Session => _service?.Session;
        public bool IsAuthenticated => _service?.IsAuthenticated ?? false;
        public bool IsMeasuring => _service?.IsMeasuring ?? false;
        public XRLuminaClient Client => _service;
        internal XRLuminaClient Service => _service;

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

            _service = new XRLuminaClient(new XRLuminaClientOptions
            {
                ClientId = SystemInfo.deviceUniqueIdentifier,
                Model = SystemInfo.deviceModel,
                Name = SystemInfo.deviceName,
                Host = _host, Port = _port, DiscoveryPort = _discoveryPort,
                DiscoveryTimeoutMs = discoveryTimeoutMs, RetryDelayMs = retryDelayMs,
                UsbEnabled = usbEnabled, UsbPort = usbPort,
            });
            _service.CommandReceived += HandleCommand;
            _service.SessionReady += HandleSessionReady;
            _service.Faulted += HandleFault;
        }

        /// <summary>SDK 연결 시작을 서비스에 전달한다.</summary>
        private IEnumerator Start()
        {
            yield return null;
            _service?.Start();
#if UNITY_ANDROID && !UNITY_EDITOR
            if (usbEnabled)
            {
                try
                {
                    using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                    using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                    {
                        _usbBridge = new AndroidJavaObject("com.ninefive.xrlumina.UsbAccessoryBridge", activity, usbPort);
                        _usbBridge.Call("start");
                    }
                }
                catch (Exception exception)
                {
                    Debug.LogError($"[XRLumina.Usb] USB 초기화 실패: {exception.Message}");
                }
            }
#endif
        }

        /// <summary>Unity 프레임 갱신을 서비스에 전달한다.</summary>
        private void Update()
        {
            _service?.Tick(Time.unscaledDeltaTime);
        }

        /// <summary>SDK의 시선·캡처·머리 추적에 사용할 HMD Transform을 설정한다.</summary>
        public void SetHead(Transform target)
        {
            head = target;
        }

        /// <summary>SDK의 이동 판정·몸 추적에 사용할 플레이어 루트를 설정한다.</summary>
        public void SetBody(Transform target)
        {
            body = target;
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
        private void HandleCommand(TestAction action)
        {
            OnCommand?.Invoke(action);
        }

        /// <summary>서비스 세션 이벤트를 공개 컴포넌트 이벤트로 전달한다.</summary>
        private void HandleSessionReady()
        {
            SessionReady?.Invoke();
        }

        /// <summary>코어가 Tick에서 전달한 통신 오류를 Unity 로그에 기록한다.</summary>
        private void HandleFault(Exception exception)
        {
            Debug.LogWarning($"[XRLumina] 통신 오류: {exception.Message}");
        }

        /// <summary>엔진 독립적인 Flush 콜백을 Unity 코루틴의 완료 대기로 연결한다.</summary>
        internal static IEnumerator Flush(XRLuminaClient client, FlushGroup group,
            float frameIntervalSeconds, float timeoutSeconds, Action<bool, string> onResult)
        {
            bool completed = false;
            client.Flush(group, frameIntervalSeconds, (ok, error) =>
            {
                completed = true;
                onResult?.Invoke(ok, error);
            }, timeoutSeconds);
            while (!completed)
            {
                yield return null;
            }
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
            _service.Faulted -= HandleFault;
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                _usbBridge?.Call("stop");
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[XRLumina.Usb] USB 종료 오류: {exception.Message}");
            }
            finally
            {
                _usbBridge?.Dispose();
                _usbBridge = null;
            }
#endif
            _service.Dispose();
            _service = null;
        }
    }
}
