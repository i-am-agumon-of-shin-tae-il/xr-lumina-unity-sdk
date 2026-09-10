using UnityEngine;
using UnityEngine.XR;
using Keyboard = UnityEngine.InputSystem.Keyboard;
using Mouse = UnityEngine.InputSystem.Mouse;

namespace XRLumina.Sample
{
    /// <summary>Editor 키보드 이동과 VR HMD·컨트롤러 이동을 함께 제공하는 샘플 플레이어.</summary>
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

        private CharacterController _characterController;
        private float _pitch;
        private float _verticalVelocity;
        private bool _vrActive;
        private bool _cursorCaptured;
        private bool _cursorInitialized;
        private bool _wasVrInteractPressed;
        private Vector3 _spawnPosition;

        /// <summary>플레이어 이동에 사용할 CharacterController와 Camera를 준비한다.</summary>
        private void Awake()
        {
            _characterController = GetComponent<CharacterController>();
            _spawnPosition = transform.position;
            if (playerCamera == null)
            {
                playerCamera = Camera.main;
            }
        }

        /// <summary>현재 XR 장치 상태에 따라 VR 또는 데스크톱 이동을 갱신한다.</summary>
        private void Update()
        {
            InputDevice headDevice = InputDevices.GetDeviceAtXRNode(XRNode.Head);
            _vrActive = headDevice.isValid;

            if (_vrActive)
            {
                UpdateVrPose(headDevice);
                UpdateVrLocomotion();
            }
            else
            {
                UpdateDesktopLocomotion();
            }

            ApplyGravity();
            RestorePlayerIfFallen();
            UpdateSampleInteraction();
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

        /// <summary>VR 컨트롤러의 왼쪽 스틱 이동과 오른쪽 스틱 회전을 적용한다.</summary>
        private void UpdateVrLocomotion()
        {
            InputDevice leftController = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
            InputDevice rightController = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);

            Vector2 moveInput = Vector2.zero;
            Vector2 turnInput = Vector2.zero;
            leftController.TryGetFeatureValue(CommonUsages.primary2DAxis, out moveInput);
            rightController.TryGetFeatureValue(CommonUsages.primary2DAxis, out turnInput);

            MovePlayer(moveInput, moveSpeed);
            transform.Rotate(0f, turnInput.x * vrTurnSpeed * Time.deltaTime, 0f);
        }

        /// <summary>방향키·WASD 이동과 우클릭 마우스 시점 회전을 적용한다.</summary>
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
            if (_cursorCaptured && mouse != null)
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
            else if (mouse != null && mouse.leftButton.wasPressedThisFrame)
            {
                CaptureCursor();
            }
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
            bool mouseInteraction = _cursorCaptured && mouse != null && mouse.leftButton.wasPressedThisFrame;
            if (keyboardInteraction || mouseInteraction)
            {
                TryInteractWithTarget();
            }
        }

        /// <summary>화면 중앙의 샘플 타깃을 찾아 해당 SDK 인터랙션 이벤트를 기록한다.</summary>
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

            XRLuminaSampleInteractionTarget target = hit.collider.GetComponent<XRLuminaSampleInteractionTarget>();
            target?.Interact();
        }
    }
}
