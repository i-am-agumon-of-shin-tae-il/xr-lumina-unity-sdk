using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;
using XRLumina._Core.Infrastructure;
using XRLumina._Core.Messaging;
using XRLumina._Core.Model;

namespace XRLumina._Core.Service
{
    internal sealed class HeuristicScreenshotService
    {
        private readonly struct PositionSample
        {
            internal float Time { get; }
            internal Vector2 Position { get; }

            internal PositionSample(float time, Vector2 position)
            {
                Time = time;
                Position = position;
            }
        }

        private readonly DeviceMessageSender _sender;
        private readonly XRLuminaClientService _client;
        private readonly System.Func<Camera> _resolveCamera;
        private readonly Func<Transform> _resolveHead;
        private readonly float _stationaryDurationSeconds;
        private readonly float _stationaryMovementThreshold;
        private readonly float _yawWindowSeconds;
        private readonly int _yawRequiredReversals;
        private readonly float _yawMinimumSwingDegrees;
        private readonly float _interactionWindowSeconds;
        private readonly int _interactionRequiredCount;
        private readonly LayerMask _interactionLayerMask;
        private readonly float _interactionMaxDistance;
        private readonly int _captureWidth;
        private readonly int _captureHeight;
        private readonly int _captureChunkSize;
        private readonly Queue<PositionSample> _positionSamples = new();
        private readonly Queue<float> _yawReversalTimes = new();
        private readonly Dictionary<string, Queue<float>> _interactionTimes = new();
        private readonly Dictionary<string, bool> _inputStates = new();
        private float _stationaryDistance;
        private bool _stationaryCaptured;
        private bool _hasYaw;
        private float _lastYaw;
        private int _yawDirection;
        private float _yawSwingDegrees;
        private bool _captureInProgress;
        private int _captureIndex;

        /// <summary>휴리스틱 판정 기준과 캡처 설정으로 서비스를 생성한다.</summary>
        internal HeuristicScreenshotService(
            XRLuminaClientService client,
            System.Func<Camera> resolveCamera,
            Func<Transform> resolveHead,
            float stationaryDurationSeconds,
            float stationaryMovementThreshold,
            float yawWindowSeconds,
            int yawRequiredReversals,
            float yawMinimumSwingDegrees,
            float interactionWindowSeconds,
            int interactionRequiredCount,
            LayerMask interactionLayerMask,
            float interactionMaxDistance,
            int captureWidth,
            int captureHeight,
            int captureChunkSize)
        {
            _client = client;
            _sender = client.MessageSender;
            _resolveCamera = resolveCamera;
            _resolveHead = resolveHead;
            _stationaryDurationSeconds = stationaryDurationSeconds;
            _stationaryMovementThreshold = stationaryMovementThreshold;
            _yawWindowSeconds = yawWindowSeconds;
            _yawRequiredReversals = yawRequiredReversals;
            _yawMinimumSwingDegrees = yawMinimumSwingDegrees;
            _interactionWindowSeconds = interactionWindowSeconds;
            _interactionRequiredCount = interactionRequiredCount;
            _interactionLayerMask = interactionLayerMask;
            _interactionMaxDistance = interactionMaxDistance;
            _captureWidth = captureWidth;
            _captureHeight = captureHeight;
            _captureChunkSize = captureChunkSize;
        }

        /// <summary>누적 판정 상태를 초기화하고 휴리스틱 추적을 시작한다.</summary>
        internal void StartFeature()
        {
            ResetState();
        }

        /// <summary>현재 HMD 포즈와 컨트롤러 입력을 판정한다.</summary>
        internal HeuristicScreenshotTrigger? Tick()
        {
            if (!_client.IsMeasuring || _captureInProgress)
            {
                return null;
            }

            var source = ResolveHead();
            if (source == null)
            {
                return null;
            }

            var now = Time.realtimeSinceStartup;
            var stationary = UpdateStationary(source.position, now);
            if (stationary.HasValue)
            {
                return stationary;
            }

            var yaw = UpdateYaw(source.eulerAngles.y, now);
            if (yaw.HasValue)
            {
                return yaw;
            }

            return UpdateControllerInputs(now);
        }

        /// <summary>게임에서 확정된 오브젝트·입력 조합을 반복 인터랙션 판정에 추가한다.</summary>
        internal HeuristicScreenshotTrigger? RecordInteraction(
            GameObject target,
            string controllerInput,
            float capturedAt)
        {
            if (!_client.IsMeasuring || _captureInProgress || target == null || string.IsNullOrWhiteSpace(controllerInput))
            {
                return null;
            }

            var key = $"{target.GetInstanceID()}:{controllerInput}";
            if (!_interactionTimes.TryGetValue(key, out var timestamps))
            {
                timestamps = new Queue<float>();
                _interactionTimes[key] = timestamps;
            }
            timestamps.Enqueue(capturedAt);
            while (timestamps.Count > 0 && capturedAt - timestamps.Peek() > _interactionWindowSeconds)
            {
                timestamps.Dequeue();
            }
            if (timestamps.Count < _interactionRequiredCount)
            {
                return null;
            }

            timestamps.Clear();
            return new HeuristicScreenshotTrigger(
                "repeated_interaction",
                capturedAt,
                target.name,
                controllerInput);
        }

        /// <summary>판정된 시점의 HMD 카메라 화면을 PNG로 생성해 Electron으로 전송한다.</summary>
        internal IEnumerator CaptureAndSend(HeuristicScreenshotTrigger trigger)
        {
            _captureInProgress = true;
            yield return new WaitForEndOfFrame();
            try
            {
                var camera = ResolveCamera();
                if (camera != null)
                {
                    var png = CaptureCamera(camera);
                    if (png != null && png.Length > 0)
                    {
                        SendCapture(trigger, png);
                        Debug.Log(
                            $"[HeuristicScreenshot] 캡처 전송 완료(type={trigger.Type}, bytes={png.Length}, " +
                            $"object={trigger.ObjectName}, input={trigger.ControllerInput})");
                    }
                }
            }
            finally
            {
                _captureInProgress = false;
            }
        }

        /// <summary>진행 중인 캡처가 끝난 뒤 Electron에 휴리스틱 이미지 업로드를 요청한다.</summary>
        internal IEnumerator FinishAndFlush(float timeoutSec, Action<bool, string> onResult)
        {
            while (_captureInProgress)
            {
                yield return null;
            }

            var session = _client.Session;
            if (session == null)
            {
                onResult?.Invoke(false, "session unavailable");
                yield break;
            }
            var requestId = _client.RequestFlush("heuristics", session.seq, 0f);
            if (requestId == null)
            {
                onResult?.Invoke(false, "host disconnected");
                yield break;
            }
            yield return _client.WaitForFlushResponse(requestId, timeoutSec, onResult);
        }

        /// <summary>XZ 누적 이동 거리가 기준 이하로 유지된 정지 구간을 판정한다.</summary>
        private HeuristicScreenshotTrigger? UpdateStationary(Vector3 position, float now)
        {
            var current = new PositionSample(now, new Vector2(position.x, position.z));
            _positionSamples.Enqueue(current);

            while (_positionSamples.Count > 1)
            {
                var samples = _positionSamples.ToArray();
                if (now - samples[1].Time < _stationaryDurationSeconds)
                {
                    break;
                }
                var removed = _positionSamples.Dequeue();
                if (_positionSamples.Count > 0)
                {
                    _stationaryDistance -= Vector2.Distance(removed.Position, _positionSamples.Peek().Position);
                }
            }

            _stationaryDistance = Mathf.Max(0f, CalculateCumulativeDistance(_positionSamples));
            if (_stationaryDistance > _stationaryMovementThreshold)
            {
                _stationaryCaptured = false;
                return null;
            }
            if (
                _stationaryCaptured ||
                _positionSamples.Count < 2 ||
                now - _positionSamples.Peek().Time < _stationaryDurationSeconds)
            {
                return null;
            }

            _stationaryCaptured = true;
            return new HeuristicScreenshotTrigger("stationary", now);
        }

        /// <summary>최근 Yaw 방향 반전 중 30도 이상 회전한 구간 수를 판정한다.</summary>
        private HeuristicScreenshotTrigger? UpdateYaw(float yaw, float now)
        {
            if (!_hasYaw)
            {
                _hasYaw = true;
                _lastYaw = yaw;
                return null;
            }

            var delta = Mathf.DeltaAngle(_lastYaw, yaw);
            _lastYaw = yaw;
            if (Mathf.Abs(delta) < 0.5f)
            {
                return null;
            }

            var direction = delta > 0f ? 1 : -1;
            if (_yawDirection == 0 || direction == _yawDirection)
            {
                _yawDirection = direction;
                _yawSwingDegrees += Mathf.Abs(delta);
                return null;
            }

            if (_yawSwingDegrees >= _yawMinimumSwingDegrees)
            {
                _yawReversalTimes.Enqueue(now);
            }
            _yawDirection = direction;
            _yawSwingDegrees = Mathf.Abs(delta);
            while (_yawReversalTimes.Count > 0 && now - _yawReversalTimes.Peek() > _yawWindowSeconds)
            {
                _yawReversalTimes.Dequeue();
            }
            if (_yawReversalTimes.Count < _yawRequiredReversals)
            {
                return null;
            }

            _yawReversalTimes.Clear();
            return new HeuristicScreenshotTrigger("head_direction_change", now);
        }

        /// <summary>좌우 컨트롤러의 버튼 상승 에지를 동일 오브젝트 반복 입력으로 판정한다.</summary>
        private HeuristicScreenshotTrigger? UpdateControllerInputs(float now)
        {
            var left = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
            var right = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
            return UpdateInput(left, "left", CommonUsages.primaryButton, "primary", now)
                ?? UpdateInput(left, "left", CommonUsages.secondaryButton, "secondary", now)
                ?? UpdateInput(left, "left", CommonUsages.triggerButton, "trigger", now)
                ?? UpdateInput(left, "left", CommonUsages.gripButton, "grip", now)
                ?? UpdateInput(right, "right", CommonUsages.primaryButton, "primary", now)
                ?? UpdateInput(right, "right", CommonUsages.secondaryButton, "secondary", now)
                ?? UpdateInput(right, "right", CommonUsages.triggerButton, "trigger", now)
                ?? UpdateInput(right, "right", CommonUsages.gripButton, "grip", now);
        }

        /// <summary>단일 컨트롤러 입력의 상승 에지에서 정면 오브젝트를 찾아 반복 횟수를 누적한다.</summary>
        private HeuristicScreenshotTrigger? UpdateInput(
            InputDevice device,
            string hand,
            InputFeatureUsage<bool> usage,
            string input,
            float now)
        {
            var stateKey = $"{hand}:{input}";
            var pressed = device.TryGetFeatureValue(usage, out var value) && value;
            var wasPressed = _inputStates.TryGetValue(stateKey, out var previous) && previous;
            _inputStates[stateKey] = pressed;
            if (!pressed || wasPressed)
            {
                return null;
            }

            var camera = ResolveCamera();
            if (camera == null || !Physics.Raycast(
                    camera.transform.position,
                    camera.transform.forward,
                    out var hit,
                    _interactionMaxDistance,
                    _interactionLayerMask))
            {
                return null;
            }
            return RecordInteraction(hit.collider.gameObject, stateKey, now);
        }

        /// <summary>현재 카메라 영상을 지정 해상도의 PNG로 생성한다.</summary>
        private byte[] CaptureCamera(Camera source)
        {
            var width = Mathf.Max(1, _captureWidth);
            var height = Mathf.Max(1, _captureHeight);
            var target = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32);
            var previousTarget = source.targetTexture;
            var previousActive = RenderTexture.active;
            Texture2D texture = null;
            try
            {
                source.targetTexture = target;
                source.Render();
                RenderTexture.active = target;
                texture = new Texture2D(width, height, TextureFormat.RGB24, false);
                texture.ReadPixels(new Rect(0f, 0f, width, height), 0, 0);
                texture.Apply();
                return texture.EncodeToPNG();
            }
            finally
            {
                source.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                RenderTexture.ReleaseTemporary(target);
                if (texture != null)
                {
                    UnityEngine.Object.Destroy(texture);
                }
            }
        }

        /// <summary>휴리스틱 이미지와 원인 정보를 지정 크기의 바이너리 청크로 전송한다.</summary>
        private void SendCapture(HeuristicScreenshotTrigger trigger, byte[] bytes)
        {
            var chunkSize = Mathf.Max(1024, _captureChunkSize);
            var totalChunks = Mathf.CeilToInt(bytes.Length / (float)chunkSize);
            var uploadId = $"heuristic-{Guid.NewGuid():N}";
            var fileName = $"{_captureIndex++:D3}-{trigger.Type}-{trigger.CapturedAt:F3}.png";
            for (var index = 0; index < totalChunks; index++)
            {
                var offset = index * chunkSize;
                var count = Math.Min(chunkSize, bytes.Length - offset);
                _sender?.SendStream(PacketType.HeuristicScreenshot, BinaryPayload.Create(writer =>
                {
                    BinaryPayload.WriteString(writer, fileName);
                    BinaryPayload.WriteString(writer, uploadId);
                    BinaryPayload.WriteString(writer, trigger.Type);
                    writer.Write(trigger.CapturedAt);
                    BinaryPayload.WriteString(writer, trigger.ObjectName);
                    BinaryPayload.WriteString(writer, trigger.ControllerInput);
                    writer.Write(index);
                    writer.Write(totalChunks);
                    writer.Write(bytes.Length);
                    writer.Write(bytes, offset, count);
                }));
            }
        }

        /// <summary>위치 샘플 사이의 누적 이동거리를 계산한다.</summary>
        private static float CalculateCumulativeDistance(IEnumerable<PositionSample> samples)
        {
            var hasPrevious = false;
            var previous = Vector2.zero;
            var total = 0f;
            foreach (var sample in samples)
            {
                if (hasPrevious)
                {
                    total += Vector2.Distance(previous, sample.Position);
                }
                previous = sample.Position;
                hasPrevious = true;
            }
            return total;
        }

        /// <summary>캡처와 정면 Raycast에 사용할 카메라를 반환한다.</summary>
        private Camera ResolveCamera()
        {
            return _resolveCamera?.Invoke();
        }

        /// <summary>위치·Yaw 추적에 사용할 HMD Transform을 반환한다.</summary>
        private Transform ResolveHead()
        {
            return _resolveHead?.Invoke();
        }

        /// <summary>새 측정을 위해 모든 누적 판정 상태를 초기화한다.</summary>
        private void ResetState()
        {
            _positionSamples.Clear();
            _yawReversalTimes.Clear();
            _interactionTimes.Clear();
            _inputStates.Clear();
            _stationaryDistance = 0f;
            _stationaryCaptured = false;
            _hasYaw = false;
            _lastYaw = 0f;
            _yawDirection = 0;
            _yawSwingDegrees = 0f;
            _captureInProgress = false;
            _captureIndex = 0;
        }
    }
}
