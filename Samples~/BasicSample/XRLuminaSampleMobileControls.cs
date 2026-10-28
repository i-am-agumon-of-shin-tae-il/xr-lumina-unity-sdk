using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;

namespace XRLumina.Sample
{
    /// <summary>모바일 샘플의 안전 영역 양쪽에 이동 및 시점용 터치 조이패드를 배치한다.</summary>
    public sealed class XRLuminaSampleMobileControls : MonoBehaviour
    {
        [SerializeField] private GameObject _canvasObject;
        private GameObject _ownedEventSystem;
        private bool _ownsCanvas;
        [SerializeField] private RectTransform _safeArea;
        [SerializeField] private XRLuminaSampleVirtualJoystick _moveStick;
        [SerializeField] private XRLuminaSampleVirtualJoystick _lookStick;
        [SerializeField] private UnityEngine.UI.Button _interactButton;
        private System.Action _interaction;
        private Rect _lastSafeArea;
        private Vector2Int _lastScreenSize;

        public Vector2 MoveInput => _moveStick != null ? _moveStick.Value : Vector2.zero;
        public Vector2 LookInput => _lookStick != null ? _lookStick.Value : Vector2.zero;

        /// <summary>화면 오버레이와 두 조이패드를 생성하고 멀티터치 UI 입력을 준비한다.</summary>
        private void Awake()
        {
            if (_canvasObject != null && _safeArea != null && _moveStick != null && _lookStick != null)
            {
                PrepareInteractionButton();
                EnsureEventSystem();
                UpdateSafeArea();
                return;
            }
            _ownsCanvas = true;
            _canvasObject = new GameObject("MobileControls", typeof(RectTransform), typeof(Canvas),
                typeof(UnityEngine.UI.CanvasScaler), typeof(UnityEngine.UI.GraphicRaycaster));
            Canvas canvas = _canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;
            UnityEngine.UI.CanvasScaler scaler = _canvasObject.GetComponent<UnityEngine.UI.CanvasScaler>();
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280f, 720f);
            scaler.matchWidthOrHeight = 0.5f;

            _safeArea = new GameObject("SafeArea", typeof(RectTransform)).GetComponent<RectTransform>();
            _safeArea.SetParent(_canvasObject.transform, false);
            _moveStick = CreateStick("MoveJoystick", false);
            _lookStick = CreateStick("LookJoystick", true);
            PrepareInteractionButton();
            EnsureEventSystem();
            UpdateSafeArea();
        }

        /// <summary>두 패드 사이의 클릭 버튼을 준비하고 한 번의 누름에 한 번만 액션을 전달한다.</summary>
        private void PrepareInteractionButton()
        {
            if (_interactButton == null)
            {
                GameObject buttonObject = new GameObject("InteractButton", typeof(RectTransform), typeof(CanvasRenderer),
                    typeof(XRLuminaSampleInteractionGraphic), typeof(UnityEngine.UI.Button));
                RectTransform rect = buttonObject.GetComponent<RectTransform>();
                rect.SetParent(_safeArea, false);
                rect.anchorMin = new Vector2(0.5f, 0f);
                rect.anchorMax = rect.anchorMin;
                rect.pivot = new Vector2(0.5f, 0f);
                rect.anchoredPosition = new Vector2(0f, 40f);
                rect.sizeDelta = new Vector2(96f, 96f);
                _interactButton = buttonObject.GetComponent<UnityEngine.UI.Button>();
                _interactButton.targetGraphic = buttonObject.GetComponent<XRLuminaSampleInteractionGraphic>();
            }
            UnityEngine.UI.Navigation navigation = _interactButton.navigation;
            navigation.mode = UnityEngine.UI.Navigation.Mode.None;
            _interactButton.navigation = navigation;
            _interactButton.onClick.AddListener(InvokeInteraction);
        }

        /// <summary>플레이어의 기존 화면 중앙 타깃 실행 기능을 클릭 버튼에 연결한다.</summary>
        public void SetInteractionHandler(System.Action interaction)
        {
            _interaction = interaction;
        }

        /// <summary>플레이어가 활성화한 화면 버튼의 클릭을 기존 상호작용에 전달한다.</summary>
        private void InvokeInteraction()
        {
            _interaction?.Invoke();
        }

        /// <summary>기존 UI 입력 시스템을 재사용하고 없을 때만 Input System 기반 입력기를 만든다.</summary>
        private void EnsureEventSystem()
        {
            if (Object.FindFirstObjectByType<EventSystem>() != null)
            {
                return;
            }
            _ownedEventSystem = new GameObject("MobileControlsEventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            _ownedEventSystem.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
        }

        /// <summary>안전 영역의 좌하단 또는 우하단에 고정 크기 조이패드를 배치한다.</summary>
        private XRLuminaSampleVirtualJoystick CreateStick(string objectName, bool right)
        {
            RectTransform rect = new GameObject(objectName, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(_safeArea, false);
            Vector2 anchor = new Vector2(right ? 1f : 0f, 0f);
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = anchor;
            rect.anchoredPosition = new Vector2(right ? -24f : 24f, 24f);
            rect.sizeDelta = new Vector2(180f, 180f);
            XRLuminaSampleVirtualJoystick stick = rect.gameObject.AddComponent<XRLuminaSampleVirtualJoystick>();
            stick.raycastTarget = true;
            return stick;
        }

        /// <summary>화면 회전과 노치·홈 인디케이터 영역의 변경을 반영한다.</summary>
        private void LateUpdate()
        {
            if (_lastSafeArea != Screen.safeArea || _lastScreenSize != new Vector2Int(Screen.width, Screen.height))
            {
                UpdateSafeArea();
            }
        }

        /// <summary>화면 안전 영역을 Canvas의 정규화 앵커 좌표로 변환한다.</summary>
        private void UpdateSafeArea()
        {
            if (Screen.width <= 0 || Screen.height <= 0)
            {
                return;
            }
            Rect safeArea = Screen.safeArea;
            if (safeArea.width <= 0f || safeArea.height <= 0f)
            {
                safeArea = new Rect(0f, 0f, Screen.width, Screen.height);
            }
            _safeArea.anchorMin = new Vector2(safeArea.xMin / Screen.width, safeArea.yMin / Screen.height);
            _safeArea.anchorMax = new Vector2(safeArea.xMax / Screen.width, safeArea.yMax / Screen.height);
            _safeArea.offsetMin = Vector2.zero;
            _safeArea.offsetMax = Vector2.zero;
            ResetInput();
            _lastSafeArea = Screen.safeArea;
            _lastScreenSize = new Vector2Int(Screen.width, Screen.height);
        }

        /// <summary>모바일 조작 여부에 따라 패드 표시를 전환하고 숨길 때 입력을 초기화한다.</summary>
        public void SetVisible(bool visible)
        {
            if (_canvasObject != null && _canvasObject.activeSelf != visible)
            {
                _canvasObject.SetActive(visible);
                ResetInput();
            }
        }

        /// <summary>양쪽 손가락 입력을 동시에 초기화한다.</summary>
        public void ResetInput()
        {
            _moveStick?.ResetInput();
            _lookStick?.ResetInput();
        }

        /// <summary>앱 포커스를 잃으면 터치 해제 누락으로 인한 이동을 중지한다.</summary>
        private void OnApplicationFocus(bool focused)
        {
            if (!focused)
            {
                ResetInput();
            }
        }

        /// <summary>앱이 일시 정지되면 양쪽 조이패드의 입력을 해제한다.</summary>
        private void OnApplicationPause(bool paused)
        {
            if (paused)
            {
                ResetInput();
            }
        }

        /// <summary>플레이어가 비활성화되면 화면 조작 UI도 숨긴다.</summary>
        private void OnDisable()
        {
            SetVisible(false);
        }

        /// <summary>플레이어 종료 시 생성한 Canvas와 전용 입력 시스템만 정리한다.</summary>
        private void OnDestroy()
        {
            if (_ownsCanvas && _canvasObject != null)
            {
                Destroy(_canvasObject);
            }
            if (_ownedEventSystem != null)
            {
                Destroy(_ownedEventSystem);
            }
        }
    }
}
