using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.XR;
using XRLumina._Core.Infrastructure;
using XRLumina._Core.Messaging;
using XRLumina._Core.Model;

namespace XRLumina._Core.Service
{
    internal sealed class InteractionService
    {
        private readonly DeviceMessageSender _sender;
        private readonly XRLuminaClientService _client;
        private Transform _head;
        private Transform _body;
        private readonly int _framesPerSecond;
        private readonly float _joystickDeadzone;
        private bool _isTracking;
        private bool _wasControllerPressed;
        private bool _wasLeftJoystickPushed;
        private bool _wasRightJoystickPushed;

        /// <summary>Unity 실행 호스트, SDK 의존성, 인터랙션 설정으로 서비스를 생성한다.</summary>
        internal InteractionService(
            XRLuminaClientService client,
            Transform head,
            Transform body,
            int framesPerSecond,
            float joystickDeadzone)
        {
            _client = client;
            _sender = client.MessageSender;
            _head = head;
            _body = body;
            _framesPerSecond = framesPerSecond;
            _joystickDeadzone = joystickDeadzone;
        }

        /// <summary>포즈 추적과 XR 입력 이벤트 기록을 시작한다.</summary>
        internal IEnumerator StartFeature()
        {
            ResetInputState();
            RecordEvent(XRLuminaInteractionEventType.Task);
            return CreateTrackingLoop(_framesPerSecond);
        }

        /// <summary>히트맵과 상호작용 위치에 사용할 머리 Transform을 교체한다.</summary>
        internal void SetHead(Transform target)
        {
            _head = target;
        }

        /// <summary>이동·회전 분석에 사용할 몸 Transform을 교체한다.</summary>
        internal void SetBody(Transform target)
        {
            _body = target;
        }

        /// <summary>추적을 종료하고 NavMesh와 누적 데이터를 Electron으로 플러시한다.</summary>
        internal void FinishFeature()
        {
            RecordEvent(XRLuminaInteractionEventType.Task);
            CaptureAndSendNavMesh();
        }

        /// <summary>측정 중 XR 컨트롤러 버튼과 조이스틱 입력의 시작 시점을 기록한다.</summary>
        internal void Tick()
        {
            if (!_isTracking)
            {
                return;
            }

            var left = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
            var right = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
            var controllerPressed = IsControllerPressed(left) || IsControllerPressed(right);
            if (controllerPressed && !_wasControllerPressed)
            {
                RecordEvent(XRLuminaInteractionEventType.Controller);
            }
            _wasControllerPressed = controllerPressed;

            UpdateJoystickEvent(left, ref _wasLeftJoystickPushed);
            UpdateJoystickEvent(right, ref _wasRightJoystickPushed);
        }

        /// <summary>현재 머리 위치를 기준으로 지정 종류의 상호작용 이벤트를 기록한다.</summary>
        internal void RecordEvent(XRLuminaInteractionEventType type)
        {
            var source = _head != null ? _head : Camera.main?.transform;
            SendEvent(
                GetEventTypeName(type),
                "ReportCount",
                0,
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"),
                source != null ? source.position : Vector3.zero);
        }

        /// <summary>조이스틱이 데드존 밖으로 처음 이동한 순간을 제스처 이벤트로 기록한다.</summary>
        private void UpdateJoystickEvent(InputDevice device, ref bool wasPushed)
        {
            var isPushed = device.TryGetFeatureValue(CommonUsages.primary2DAxis, out var axis)
                && axis.magnitude > _joystickDeadzone;
            if (isPushed && !wasPushed)
            {
                RecordEvent(XRLuminaInteractionEventType.Gesture);
            }
            wasPushed = isPushed;
        }

        /// <summary>장치의 주요 버튼·트리거·그립 중 하나가 눌렸는지 확인한다.</summary>
        private static bool IsControllerPressed(InputDevice device)
        {
            return IsPressed(device, CommonUsages.primaryButton)
                || IsPressed(device, CommonUsages.secondaryButton)
                || IsPressed(device, CommonUsages.triggerButton)
                || IsPressed(device, CommonUsages.gripButton);
        }

        /// <summary>XR 장치에서 지정된 불리언 입력 값을 읽는다.</summary>
        private static bool IsPressed(InputDevice device, InputFeatureUsage<bool> usage)
        {
            return device.TryGetFeatureValue(usage, out var pressed) && pressed;
        }

        /// <summary>새 측정 시작 시 입력의 이전 프레임 상태를 초기화한다.</summary>
        private void ResetInputState()
        {
            _wasControllerPressed = false;
            _wasLeftJoystickPushed = false;
            _wasRightJoystickPushed = false;
        }

        /// <summary>Electron에 상호작용 데이터 조합·업로드를 요청하고 응답을 기다린다.</summary>
        internal IEnumerator Flush(
            int framesPerSecond,
            float timeoutSec,
            Action<bool, string> onResult)
        {
            var session = _client.Session;
            if (session == null)
            {
                onResult?.Invoke(false, "session unavailable");
                yield break;
            }

            var frameIntervalSeconds = 1f / Mathf.Max(1, framesPerSecond);
            var requestId = _client.RequestFlush("interaction", session.seq, frameIntervalSeconds);
            if (requestId == null)
            {
                onResult?.Invoke(false, "host disconnected");
                yield break;
            }

            yield return _client.WaitForFlushResponse(requestId, timeoutSec, onResult);
        }

        /// <summary>지정 머리·몸 Transform을 설정된 간격으로 전송하기 시작한다.</summary>
        private IEnumerator CreateTrackingLoop(int framesPerSecond)
        {
            _isTracking = true;
            return TrackingLoop(Mathf.Max(1, framesPerSecond));
        }

        /// <summary>실행 중인 포즈 추적 코루틴을 중지한다.</summary>
        internal void StopTracking()
        {
            _isTracking = false;
        }

        /// <summary>현재 씬의 NavMesh 삼각형 데이터를 계산해 전송한다.</summary>
        private void CaptureAndSendNavMesh()
        {
            var triangulation = NavMesh.CalculateTriangulation();
            SendMesh(triangulation.vertices, triangulation.indices);
        }

        /// <summary>지정된 간격마다 머리와 몸의 위치·회전을 전송한다.</summary>
        private IEnumerator TrackingLoop(int framesPerSecond)
        {
            var delay = new WaitForSeconds(1f / framesPerSecond);
            while (true)
            {
                var fallback = Camera.main != null ? Camera.main.transform : null;
                var currentHead = _head != null ? _head : fallback;
                var currentBody = _body != null ? _body : fallback;
                SendTracking(
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"),
                    currentHead,
                    currentBody);
                yield return delay;
            }
        }

        /// <summary>단일 시점의 머리·몸 Transform을 상호작용 추적 패킷으로 전송한다.</summary>
        private void SendTracking(string timestamp, Transform head, Transform body)
        {
            if (head == null || body == null)
            {
                return;
            }

            Send(PacketType.InteractionTracking, writer =>
            {
                BinaryPayload.WriteString(writer, timestamp);
                WriteTransform(writer, head);
                WriteTransform(writer, body);
            });
        }

        /// <summary>상호작용 종류와 위치를 이벤트 패킷으로 전송한다.</summary>
        private void SendEvent(
            string type,
            string operationName,
            int count,
            string timestamp,
            Vector3 position,
            bool includeY = true)
        {
            Send(PacketType.InteractionEvent, writer =>
            {
                BinaryPayload.WriteString(writer, type);
                BinaryPayload.WriteString(writer, operationName);
                writer.Write(count);
                BinaryPayload.WriteString(writer, timestamp);
                writer.Write(position.x);
                writer.Write(includeY);
                if (includeY)
                {
                    writer.Write(position.y);
                }
                writer.Write(position.z);
            });
        }

        /// <summary>지원 이벤트 enum을 플랫폼 전송 규격의 이름으로 변환한다.</summary>
        private static string GetEventTypeName(XRLuminaInteractionEventType type)
        {
            switch (type)
            {
                case XRLuminaInteractionEventType.Task:
                    return "task";
                case XRLuminaInteractionEventType.Npc:
                    return "npc";
                case XRLuminaInteractionEventType.Gesture:
                    return "gesture";
                case XRLuminaInteractionEventType.Controller:
                    return "controller";
                case XRLuminaInteractionEventType.Object:
                    return "object";
                default:
                    throw new ArgumentOutOfRangeException(nameof(type), type, null);
            }
        }

        /// <summary>NavMesh 정점과 인덱스를 메시 패킷으로 전송한다.</summary>
        private void SendMesh(IReadOnlyList<Vector3> vertices, IReadOnlyList<int> indices)
        {
            if (vertices == null || indices == null)
            {
                return;
            }

            Send(PacketType.InteractionMesh, writer =>
            {
                writer.Write(vertices.Count);
                foreach (var vertex in vertices)
                {
                    writer.Write(vertex.x);
                    writer.Write(vertex.y);
                    writer.Write(vertex.z);
                }
                writer.Write(indices.Count);
                foreach (var index in indices)
                {
                    writer.Write(index);
                }
            });
        }

        /// <summary>작성된 바이너리 페이로드를 지정 패킷 종류로 전송한다.</summary>
        private void Send(PacketType type, System.Action<System.IO.BinaryWriter> write)
        {
            _sender?.SendStream(type, BinaryPayload.Create(write));
        }

        /// <summary>Transform의 위치와 오일러 회전을 바이너리 스트림에 기록한다.</summary>
        private static void WriteTransform(System.IO.BinaryWriter writer, Transform target)
        {
            var position = target.position;
            var rotation = target.eulerAngles;
            writer.Write(position.x);
            writer.Write(position.y);
            writer.Write(position.z);
            writer.Write(rotation.x);
            writer.Write(rotation.y);
            writer.Write(rotation.z);
        }
    }
}
