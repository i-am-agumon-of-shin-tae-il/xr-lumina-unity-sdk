using UnityEngine;
using UnityEngine.XR;
using Keyboard = UnityEngine.InputSystem.Keyboard;
using Mouse = UnityEngine.InputSystem.Mouse;

namespace XRLumina.Sample
{
    /// <summary>PC 키보드·마우스, 모바일 터치와 VR HMD·컨트롤러 이동을 제공하는 샘플 플레이어.</summary>
    [RequireComponent(typeof(CharacterController))]
    public sealed class XRLuminaSamplePlayerController : MonoBehaviour
    {
        [SerializeField] private Camera playerCamera;
        [SerializeField] private float moveSpeed = 5f;
        [SerializeField] private float sprintMultiplier = 2f;
        [SerializeField] private float mouseSensitivity = 2f;
        [SerializeField] private float vrTurnSpeed = 75f;
        [SerializeField] private float respawnHeight = -2f;
        [SerializeField] private float interactionDistance = 6f;

        [SerializeField] private float mobileLookSpeed = 110f;
        [SerializeField] private bool enableMobileControls = true;

        [SerializeField] private XRLuminaSampleMobileControls _mobileControls;
        private bool _mobileActive;
        private readonly System.Collections.Generic.List<UnityEngine.EventSystems.RaycastResult> _uiHits = new();
        private CharacterController _characterController;
        private float _pitch;
        private float _verticalVelocity;
        private bool _vrActive;
        private bool _cursorCaptured;
        private bool _cursorInitialized;
        private bool _wasVrInteractPressed;
        private Vector3 _spawnPosition;
        private Vector3 _initialControllerCenter;

        /// <summary>플레이어 이동에 사용할 CharacterController와 Camera를 준비한다.</summary>
        private void Awake()
        {
            _characterController = GetComponent<CharacterController>();
            _initialControllerCenter = _characterController.center;
            _spawnPosition = transform.position;
            _mobileControls?.SetInteractionHandler(InteractFromScreenButton);
            if (playerCamera == null)
            {
                playerCamera = Camera.main;
            }
        }

        /// <summary>현재 플랫폼과 XR 장치 상태에 따라 VR·모바일·데스크톱 이동을 갱신한다.</summary>
        private void Update()
        {
            InputDevice headDevice = InputDevices.GetDeviceAtXRNode(XRNode.Head);
            // Editor는 HMD 시뮬레이터가 감지되어도 화면 조작을 바로 테스트할 수 있게 한다.
            _vrActive = headDevice.isValid && !Application.isEditor;
            _mobileActive = !_vrActive && enableMobileControls;
            UpdateMobileControls();

            if (_vrActive)
            {
                UpdateVrPose(headDevice);
                UpdateVrCollisionCenter();
                UpdateVrLocomotion();
            }
            else
            {
                if (_characterController.center != _initialControllerCenter)
                {
                    _characterController.center = _initialControllerCenter;
                }
                if (_mobileActive)
                {
                    if (!Application.isMobilePlatform)
                    {
                        UpdateDesktopLocomotion();
                    }
                    UpdateMobileLocomotion();
                }
                else
                {
                    UpdateDesktopLocomotion();
                }
            }

            ApplyGravity();
            RestorePlayerIfFallen();
            UpdateSampleInteraction();
        }

        /// <summary>화면 조이패드를 표시하고 모바일에서만 커서를 해제한다.</summary>
        private void UpdateMobileControls()
        {
            if (_mobileActive && _mobileControls == null)
            {
                _mobileControls = Object.FindFirstObjectByType<XRLuminaSampleMobileControls>(FindObjectsInactive.Include);
                if (_mobileControls == null)
                {
                    _mobileControls = gameObject.AddComponent<XRLuminaSampleMobileControls>();
                }
                _mobileControls.SetInteractionHandler(InteractFromScreenButton);
            }
            _mobileControls?.SetVisible(_mobileActive);
            if (_mobileActive && Application.isMobilePlatform && (_cursorCaptured || Cursor.lockState != CursorLockMode.None))
            {
                _cursorCaptured = false;
                _cursorInitialized = false;
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
        }

        /// <summary>왼쪽 패드로 앞뒤·좌우 이동하고 오른쪽 패드로 좌우·상하 시점을 회전한다.</summary>
        private void UpdateMobileLocomotion()
        {
            MovePlayer(_mobileControls.MoveInput, moveSpeed);
            Vector2 look = _mobileControls.LookInput * (mobileLookSpeed * Time.deltaTime);
            if (look.sqrMagnitude <= 0f)
            {
                return;
            }
            transform.Rotate(0f, look.x, 0f);
            _pitch = Mathf.Clamp(_pitch - look.y, -80f, 80f);
            if (playerCamera != null)
            {
                playerCamera.transform.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
            }
        }

        /// <summary>플레이어 조작을 끄면 남은 터치 입력과 조이패드 표시를 정리한다.</summary>
        private void OnDisable()
        {
            _mobileControls?.SetVisible(false);
        }

        /// <summary>HMD의 로컬 위치와 회전을 플레이어 Camera에 적용한다.</summary>
        private void UpdateVrPose(InputDevice headDevice)
        {
            if (playerCamera == null)
            {
                return;
            }

            if (headDevice.TryGetFeatureValue(CommonUsages.centerEyePosition, out Vector3 headPosition))
            {
                playerCamera.transform.localPosition = headPosition;
            }
            else if (headDevice.TryGetFeatureValue(CommonUsages.devicePosition, out Vector3 devicePosition))
            {
                playerCamera.transform.localPosition = devicePosition;
            }

            if (headDevice.TryGetFeatureValue(CommonUsages.centerEyeRotation, out Quaternion headRotation))
            {
                playerCamera.transform.localRotation = headRotation;
            }
            else if (headDevice.TryGetFeatureValue(CommonUsages.deviceRotation, out Quaternion deviceRotation))
            {
                playerCamera.transform.localRotation = deviceRotation;
            }
        }

        /// <summary>몸의 충돌체 중심을 현재 HMD의 수평 위치에 맞춘다.</summary>
        private void UpdateVrCollisionCenter()
        {
            if (playerCamera == null)
            {
                return;
            }

            Vector3 headPosition = transform.InverseTransformPoint(playerCamera.transform.position);
            Vector3 center = new Vector3(headPosition.x, _initialControllerCenter.y, headPosition.z);
            if (_characterController.center != center)
            {
                _characterController.center = center;
            }
        }

        /// <summary>현재 머리 위치를 유지하면서 플레이어와 충돌체의 방향을 회전한다.</summary>
        private void RotateVrPlayer(float angle)
        {
            if (playerCamera == null || Mathf.Approximately(angle, 0f))
            {
                return;
            }

            transform.RotateAround(playerCamera.transform.position, Vector3.up, angle);
            Physics.SyncTransforms();
        }

        /// <summary>VR 컨트롤러의 왼쪽 스틱 이동과 머리 중심의 오른쪽 스틱 회전을 적용한다.</summary>
        private void UpdateVrLocomotion()
        {
            InputDevice leftController = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
            InputDevice rightController = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);

            Vector2 moveInput = Vector2.zero;
            Vector2 turnInput = Vector2.zero;
            leftController.TryGetFeatureValue(CommonUsages.primary2DAxis, out moveInput);
            rightController.TryGetFeatureValue(CommonUsages.primary2DAxis, out turnInput);

            MovePlayer(moveInput, moveSpeed);
            RotateVrPlayer(turnInput.x * vrTurnSpeed * Time.deltaTime);
        }

        /// <summary>방향키·WASD 이동과 커서 캡처 또는 우클릭 드래그 시점 회전을 적용한다.</summary>
        private void UpdateDesktopLocomotion()
        {
            UpdateCursorCapture();

            Keyboard keyboard = Keyboard.current;
            Vector2 moveInput = Vector2.zero;
            if (keyboard != null)
            {
                if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed)
                {
                    moveInput.x -= 1f;
                }
                if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed)
                {
                    moveInput.x += 1f;
                }
                if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed)
                {
                    moveInput.y -= 1f;
                }
                if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed)
                {
                    moveInput.y += 1f;
                }
            }

            bool isSprinting = keyboard != null && keyboard.leftShiftKey.isPressed;
            float speed = isSprinting ? moveSpeed * sprintMultiplier : moveSpeed;
            MovePlayer(moveInput, speed);

            Mouse mouse = Mouse.current;
            if (mouse != null && (_cursorCaptured || (_mobileActive && mouse.rightButton.isPressed && !IsMouseOverUi())))
            {
                Vector2 lookInput = mouse.delta.ReadValue() * (mouseSensitivity * 0.05f);
                transform.Rotate(0f, lookInput.x, 0f);
                _pitch = Mathf.Clamp(_pitch - lookInput.y, -80f, 80f);

                if (playerCamera != null)
                {
                    playerCamera.transform.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
                }
            }
        }

        /// <summary>데스크톱 플레이 중 마우스 시점 조작을 위한 커서 캡처 상태를 관리한다.</summary>
        private void UpdateCursorCapture()
        {
            if (!_cursorInitialized)
            {
                CaptureCursor();
                _cursorInitialized = true;
            }
            Keyboard keyboard = Keyboard.current;
            Mouse mouse = Mouse.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            {
                _cursorCaptured = false;
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            else if (mouse != null && mouse.leftButton.wasPressedThisFrame && !IsMouseOverUi())
            {
                CaptureCursor();
            }
        }

        /// <summary>현재 마우스 위치의 UI를 검사하여 조이패드·버튼 클릭이 커서 잠금이나 타깃 실행으로 이어지지 않게 한다.</summary>
        private bool IsMouseOverUi()
        {
            var eventSystem = UnityEngine.EventSystems.EventSystem.current;
            Mouse mouse = Mouse.current;
            if (eventSystem == null || mouse == null)
            {
                return false;
            }
            var pointer = new UnityEngine.EventSystems.PointerEventData(eventSystem)
            {
                position = mouse.position.ReadValue(),
            };
            _uiHits.Clear();
            eventSystem.RaycastAll(pointer, _uiHits);
            return _uiHits.Count > 0;
        }

        /// <summary>마우스 이동값이 시점 회전에 전달되도록 커서를 Game View에 고정한다.</summary>
        private void CaptureCursor()
        {
            _cursorCaptured = true;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        /// <summary>Camera의 수평 방향을 기준으로 플레이어를 이동한다.</summary>
        private void MovePlayer(Vector2 input, float speed)
        {
            if (_characterController == null || playerCamera == null || input.sqrMagnitude <= 0.0001f)
            {
                return;
            }

            Vector3 forward = Vector3.ProjectOnPlane(playerCamera.transform.forward, Vector3.up).normalized;
            Vector3 right = Vector3.ProjectOnPlane(playerCamera.transform.right, Vector3.up).normalized;
            Vector3 movement = forward * input.y + right * input.x;
            if (movement.sqrMagnitude > 1f)
            {
                movement.Normalize();
            }

            _characterController.Move(movement * speed * Time.deltaTime);
        }

        /// <summary>플레이어가 지면을 따라 이동하도록 중력을 적용한다.</summary>
        private void ApplyGravity()
        {
            if (_characterController == null)
            {
                return;
            }

            if (_characterController.isGrounded && _verticalVelocity < 0f)
            {
                _verticalVelocity = -1f;
            }
            else
            {
                _verticalVelocity += Physics.gravity.y * Time.deltaTime;
            }

            _characterController.Move(Vector3.up * (_verticalVelocity * Time.deltaTime));
        }

        /// <summary>안전 바닥 아래로 내려간 플레이어를 최초 시작 위치로 복구한다.</summary>
        private void RestorePlayerIfFallen()
        {
            if (_characterController == null || transform.position.y >= respawnHeight)
            {
                return;
            }

            _characterController.enabled = false;
            transform.position = _spawnPosition;
            _characterController.enabled = true;
            _verticalVelocity = 0f;
        }

        /// <summary>데스크톱과 VR 입력으로 샘플 타깃 및 입력 이벤트를 기록한다.</summary>
        private void UpdateSampleInteraction()
        {
            if (_mobileActive && Application.isMobilePlatform)
            {
                return;
            }
            if (_vrActive)
            {
                InputDevice rightController = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
                bool isPressed = rightController.TryGetFeatureValue(CommonUsages.triggerButton, out bool triggerPressed)
                    && triggerPressed;
                if (isPressed && !_wasVrInteractPressed)
                {
                    TryInteractWithTarget();
                }
                _wasVrInteractPressed = isPressed;
                return;
            }

            Keyboard keyboard = Keyboard.current;
            Mouse mouse = Mouse.current;
            bool keyboardInteraction = keyboard != null && keyboard.eKey.wasPressedThisFrame;
            bool mouseInteraction = _cursorCaptured && mouse != null && mouse.leftButton.wasPressedThisFrame && !IsMouseOverUi();
            if (keyboardInteraction || mouseInteraction)
            {
                TryInteractWithTarget();
            }
        }

        /// <summary>화면 클릭 버튼으로 기존 마우스·E키와 동일한 타깃 상호작용을 실행한다.</summary>
        public void InteractFromScreenButton()
        {
            if (isActiveAndEnabled && _mobileActive)
            {
                TryInteractWithTarget();
            }
        }

        /// <summary>화면 중앙의 대상에 연결된 액션이나 샘플 인터랙션을 호출한다.</summary>
        private void TryInteractWithTarget()
        {
            if (playerCamera == null)
            {
                return;
            }

            Ray ray = new Ray(playerCamera.transform.position, playerCamera.transform.forward);
            if (!Physics.Raycast(ray, out RaycastHit hit, interactionDistance))
            {
                return;
            }

            hit.collider.GetComponent<XRLuminaSampleContactAction>()?.Invoke();
            XRLuminaSampleInteractionTarget target = hit.collider.GetComponent<XRLuminaSampleInteractionTarget>();
            target?.Interact();
        }
    }
}
