using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using XRLumina._Core.Infrastructure;
using XRLumina._Core.Messaging;
using XRLumina._Core.Model;

namespace XRLumina._Core.Service
{
    internal sealed class EyeTrackingService
    {
        private readonly DeviceMessageSender _sender;
        private readonly XRLuminaClientService _client;
        private readonly Camera _sourceCamera;
        private readonly LayerMask _gazeLayerMask;
        private readonly float _maxDistance;
        private readonly int _framesPerSecond;
        private readonly int _stationaryDurationMs;
        private readonly float _movementThresholdMeters;
        private readonly int _panoramaWidth;
        private readonly int _panoramaHeight;
        private readonly int _panoramaCubemapSize;
        private readonly int _captureChunkSize;
        private readonly List<TimedGazeFrame> _recentGaze = new();
        private readonly List<XRLuminaGazeFrame> _requestGaze = new();
        private int _frameIndex;
        private int _segmentIndex;
        private bool _hasStationaryCandidate;
        private bool _isStationaryConfirmed;
        private float _stationaryStartedAt;
        private Vector3 _positionAnchor;
        private byte[] _stationaryPanorama;
        private Quaternion _panoramaRotation;
        private TimedGazeFrame? _segmentStartSample;

        /// <summary>Unity 실행 호스트, SDK 의존성, 시선 기록 설정으로 서비스를 생성한다.</summary>
        internal EyeTrackingService(
            XRLuminaClientService client,
            Camera sourceCamera,
            LayerMask gazeLayerMask,
            float maxDistance,
            int framesPerSecond,
            int stationaryDurationMs,
            float movementThresholdMeters,
            int panoramaWidth,
            int panoramaHeight,
            int panoramaCubemapSize,
            int captureChunkSize)
        {
            _client = client;
            _sender = client.MessageSender;
            _sourceCamera = sourceCamera;
            _gazeLayerMask = gazeLayerMask;
            _maxDistance = maxDistance;
            _framesPerSecond = framesPerSecond;
            _stationaryDurationMs = stationaryDurationMs;
            _movementThresholdMeters = movementThresholdMeters;
            _panoramaWidth = panoramaWidth;
            _panoramaHeight = panoramaHeight;
            _panoramaCubemapSize = panoramaCubemapSize;
            _captureChunkSize = captureChunkSize;
        }

        /// <summary>이전 기록을 초기화하고 연속 시선 기록과 정지 감지를 시작한다.</summary>
        internal IEnumerator StartFeature()
        {
            ResetState();
            return RecordingLoop();
        }

        /// <summary>연속 기록을 종료하고 이미 생성된 구간들의 업로드를 요청한다.</summary>
        internal void FinishFeature()
        {
            _recentGaze.Clear();
            _requestGaze.Clear();
            _hasStationaryCandidate = false;
            _isStationaryConfirmed = false;
            _stationaryPanorama = null;
        }

        /// <summary>설정된 FPS로 시선을 계속 기록하면서 카메라 공간 위치의 정지 구간을 판정한다.</summary>
        internal IEnumerator RecordingLoop()
        {
            var delay = new WaitForSeconds(1f / Mathf.Max(1, _framesPerSecond));
            while (true)
            {
                var camera = ResolveCamera();
                if (camera != null)
                {
                    ProcessSample(camera, Time.realtimeSinceStartup);
                }
                yield return delay;
            }
        }

        /// <summary>현재 시선을 계산해 움직임을 판정한 뒤 카메라 자세와 함께 연속 기록한다.</summary>
        private void ProcessSample(Camera camera, float capturedAt)
        {
            var cameraTransform = camera.transform;
            var gaze = CreateGazeFrame(_frameIndex++, Time.time, cameraTransform);
            if (!_hasStationaryCandidate)
            {
                BeginStationaryCandidate(cameraTransform.position, capturedAt);
            }
            else if (Vector3.Distance(cameraTransform.position, _positionAnchor) >
                     _movementThresholdMeters)
            {
                if (_isStationaryConfirmed)
                {
                    SendConfirmedSegment();
                }
                BeginStationaryCandidate(cameraTransform.position, capturedAt);
            }

            _recentGaze.Add(new TimedGazeFrame(
                capturedAt,
                gaze,
                cameraTransform.position,
                cameraTransform.rotation));
            TrimRecentGaze(capturedAt);

            if (_isStationaryConfirmed)
            {
                _requestGaze.Add(gaze);
            }
            else if (capturedAt - _stationaryStartedAt >= GetStationaryDurationSeconds())
            {
                ConfirmStationarySegment(camera, capturedAt);
            }
        }

        /// <summary>이동이 끝난 현재 카메라 위치와 시각을 다음 정지 후보의 기준으로 저장한다.</summary>
        private void BeginStationaryCandidate(Vector3 position, float capturedAt)
        {
            _hasStationaryCandidate = true;
            _isStationaryConfirmed = false;
            _stationaryStartedAt = capturedAt;
            _positionAnchor = position;
            _stationaryPanorama = null;
            _panoramaRotation = Quaternion.identity;
            _segmentStartSample = null;
            _requestGaze.Clear();
        }

        /// <summary>정지 판정 순간 n ms 전부터의 시선을 요청 버퍼에 넣고 보정된 파노라마를 촬영한다.</summary>
        private void ConfirmStationarySegment(Camera camera, float confirmedAt)
        {
            var segmentStartedAt = confirmedAt - GetStationaryDurationSeconds();
            var startSample = FindClosestRecentSample(segmentStartedAt);
            if (!startSample.HasValue)
            {
                return;
            }

            var panoramaRotation = GetCorrectedPanoramaRotation(
                camera.transform.rotation,
                startSample.Value.Rotation);
            var panorama = CapturePanorama(camera, panoramaRotation);
            if (panorama == null)
            {
                return;
            }

            _stationaryPanorama = panorama;
            _panoramaRotation = panoramaRotation;
            _segmentStartSample = startSample;
            _requestGaze.Clear();
            foreach (var sample in _recentGaze)
            {
                if (sample.CapturedAt < segmentStartedAt)
                {
                    continue;
                }
                _requestGaze.Add(sample.Frame);
            }
            _isStationaryConfirmed = _requestGaze.Count > 0;
        }

        /// <summary>n ms 전 시각에 가장 가까운 시선·카메라 자세 샘플을 반환한다.</summary>
        private TimedGazeFrame? FindClosestRecentSample(float capturedAt)
        {
            if (_recentGaze.Count == 0)
            {
                return null;
            }

            var closest = _recentGaze[0];
            var closestDistance = Mathf.Abs(closest.CapturedAt - capturedAt);
            for (var index = 1; index < _recentGaze.Count; index++)
            {
                var candidate = _recentGaze[index];
                var distance = Mathf.Abs(candidate.CapturedAt - capturedAt);
                if (distance >= closestDistance)
                {
                    continue;
                }
                closest = candidate;
                closestDistance = distance;
            }
            return closest;
        }

        /// <summary>현재 yaw와 n ms 전 yaw의 차이를 역보정하고 pitch·roll을 제거한다.</summary>
        private static Quaternion GetCorrectedPanoramaRotation(
            Quaternion currentRotation,
            Quaternion previousRotation)
        {
            var currentYaw = GetLevelRotation(currentRotation);
            var previousYaw = GetLevelRotation(previousRotation);
            var inverseGazeDelta = previousYaw * Quaternion.Inverse(currentYaw);
            return inverseGazeDelta * currentYaw;
        }

        /// <summary>회전에서 좌우 방향만 남겨 수평 정면 회전으로 변환한다.</summary>
        private static Quaternion GetLevelRotation(Quaternion rotation)
        {
            var forward = rotation * Vector3.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f)
            {
                forward = Vector3.forward;
            }
            return Quaternion.LookRotation(forward.normalized, Vector3.up);
        }

        /// <summary>정지 판정 순간 n ms 전부터 움직이기 직전까지 확정된 한 세트를 전송한다.</summary>
        private void SendConfirmedSegment()
        {
            if (_requestGaze.Count == 0 ||
                _stationaryPanorama == null ||
                !_segmentStartSample.HasValue)
            {
                return;
            }

            var suffix = _segmentIndex.ToString("D3");
            SendGaze($"gaze_frames_{suffix}.json", _requestGaze);
            SendCapture($"capture_image_{suffix}.png", _stationaryPanorama, _captureChunkSize);

            var pose = _segmentStartSample.Value;
            var poseJson = JsonUtility.ToJson(new CameraPose
            {
                px = pose.Position.x,
                py = pose.Position.y,
                pz = pose.Position.z,
                rx = _panoramaRotation.x,
                ry = _panoramaRotation.y,
                rz = _panoramaRotation.z,
                rw = _panoramaRotation.w,
            });
            SendCameraPose(
                $"camera_pose_{suffix}.json",
                System.Text.Encoding.UTF8.GetBytes(poseJson));
            _segmentIndex++;
            _requestGaze.Clear();
        }

        /// <summary>현재 카메라 위치와 정면 방향을 기준으로 360° 파노라마 PNG를 캡처한다.</summary>
        private byte[] CapturePanorama(Camera source, Quaternion correctedRotation)
        {
            if (source == null)
            {
                return null;
            }

            var cubemapSize = Mathf.Max(1, _panoramaCubemapSize);
            var width = Mathf.Max(1, _panoramaWidth);
            var height = Mathf.Max(1, _panoramaHeight);
            var cubemap = new RenderTexture(
                cubemapSize,
                cubemapSize,
                24,
                RenderTextureFormat.ARGB32)
            {
                dimension = UnityEngine.Rendering.TextureDimension.Cube,
            };
            var panorama = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32);
            var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
            var captureObject = new GameObject("XRLumina Panorama Camera")
            {
                hideFlags = HideFlags.HideAndDontSave,
            };
            var captureCamera = captureObject.AddComponent<Camera>();
            captureCamera.CopyFrom(source);
            captureCamera.enabled = false;
            captureCamera.stereoTargetEye = StereoTargetEyeMask.None;
            captureCamera.targetTexture = null;
            captureObject.transform.SetPositionAndRotation(
                source.transform.position,
                correctedRotation);
            var previousActive = RenderTexture.active;
            try
            {
                cubemap.Create();
                panorama.Create();
                if (!captureCamera.RenderToCubemap(cubemap))
                {
                    return null;
                }
                cubemap.ConvertToEquirect(
                    panorama,
                    Camera.MonoOrStereoscopicEye.Mono);
                RenderTexture.active = panorama;
                texture.ReadPixels(new Rect(0f, 0f, width, height), 0, 0);
                texture.Apply(false, false);
                return texture.EncodeToPNG();
            }
            finally
            {
                RenderTexture.active = previousActive;
                cubemap.Release();
                panorama.Release();
                UnityEngine.Object.Destroy(cubemap);
                UnityEngine.Object.Destroy(panorama);
                UnityEngine.Object.Destroy(texture);
                UnityEngine.Object.Destroy(captureObject);
            }
        }

        /// <summary>정지 판정 순간 n ms 전의 시선과 자세를 복원할 최근 기록만 유지한다.</summary>
        private void TrimRecentGaze(float capturedAt)
        {
            var oldestAllowed = capturedAt - GetStationaryDurationSeconds() -
                                1f / Mathf.Max(1, _framesPerSecond);
            var removeCount = 0;
            while (removeCount < _recentGaze.Count &&
                   _recentGaze[removeCount].CapturedAt < oldestAllowed)
            {
                removeCount++;
            }
            if (removeCount > 0)
            {
                _recentGaze.RemoveRange(0, removeCount);
            }
        }

        /// <summary>밀리초 설정값을 정지 판정에 사용할 초 단위 값으로 변환한다.</summary>
        private float GetStationaryDurationSeconds()
        {
            return Mathf.Max(0, _stationaryDurationMs) / 1000f;
        }

        /// <summary>시선 레이캐스트 결과를 전송 가능한 프레임으로 생성한다.</summary>
        private XRLuminaGazeFrame CreateGazeFrame(int frame, float time, Transform source)
        {
            var origin = source.position;
            var direction = source.forward;
            var hitPosition = Physics.Raycast(
                origin,
                direction,
                out var hit,
                _maxDistance,
                _gazeLayerMask)
                ? hit.point
                : origin + direction * _maxDistance;

            return new XRLuminaGazeFrame(frame, time, new[] { hitPosition });
        }

        /// <summary>지정 카메라 또는 현재 메인 카메라를 시선 기록 대상으로 반환한다.</summary>
        private Camera ResolveCamera()
        {
            return _sourceCamera != null ? _sourceCamera : Camera.main;
        }

        /// <summary>새 세션 기록을 위해 런타임 상태와 버퍼를 초기화한다.</summary>
        private void ResetState()
        {
            _recentGaze.Clear();
            _requestGaze.Clear();
            _frameIndex = 0;
            _segmentIndex = 0;
            _hasStationaryCandidate = false;
            _isStationaryConfirmed = false;
            _stationaryPanorama = null;
            _panoramaRotation = Quaternion.identity;
            _segmentStartSample = null;
        }

        /// <summary>Electron에 아이트래킹 데이터 조합·업로드를 요청하고 응답을 기다린다.</summary>
        internal IEnumerator Flush(float timeoutSec, Action<bool, string> onResult)
        {
            var session = _client.Session;
            if (session == null)
            {
                onResult?.Invoke(false, "session unavailable");
                yield break;
            }

            var requestId = _client.RequestFlush("eyetracking", session.seq, 0f);
            if (requestId == null)
            {
                onResult?.Invoke(false, "host disconnected");
                yield break;
            }

            yield return _client.WaitForFlushResponse(requestId, timeoutSec, onResult);
        }

        /// <summary>시선 프레임 목록을 아이트래킹 시선 패킷으로 전송한다.</summary>
        private void SendGaze(string fileName, IReadOnlyList<XRLuminaGazeFrame> frames)
        {
            if (frames == null)
            {
                return;
            }

            Send(PacketType.EyeTrackingGaze, writer =>
            {
                BinaryPayload.WriteString(writer, fileName);
                writer.Write(frames.Count);
                foreach (var frame in frames)
                {
                    writer.Write(frame.Frame);
                    writer.Write(frame.Time);
                    var points = frame.Points;
                    writer.Write(points?.Count ?? 0);
                    if (points == null)
                    {
                        continue;
                    }
                    foreach (var point in points)
                    {
                        writer.Write(point.x);
                        writer.Write(point.y);
                        writer.Write(point.z);
                    }
                }
            });
        }

        /// <summary>캡처 이미지를 지정 크기로 분할해 청크 패킷으로 전송한다.</summary>
        private void SendCapture(
            string fileName,
            byte[] bytes,
            int chunkSize)
        {
            if (bytes == null || chunkSize <= 0)
            {
                return;
            }

            var totalChunks = Mathf.CeilToInt(bytes.Length / (float)chunkSize);
            var uploadId = $"capture-{Guid.NewGuid():N}";
            for (var index = 0; index < totalChunks; index++)
            {
                var offset = index * chunkSize;
                var count = Math.Min(chunkSize, bytes.Length - offset);
                Send(PacketType.EyeTrackingCapture, writer =>
                {
                    BinaryPayload.WriteString(writer, fileName);
                    BinaryPayload.WriteString(writer, uploadId);
                    writer.Write(index);
                    writer.Write(totalChunks);
                    writer.Write(bytes.Length);
                    writer.Write(bytes, offset, count);
                });
            }
        }

        /// <summary>카메라 자세 JSON 바이트를 단일 패킷으로 전송한다.</summary>
        private void SendCameraPose(string fileName, byte[] bytes)
        {
            if (bytes == null)
            {
                return;
            }

            Send(PacketType.EyeTrackingCameraPose, writer =>
            {
                BinaryPayload.WriteString(writer, fileName);
                writer.Write(bytes);
            });
        }

        /// <summary>작성된 바이너리 페이로드를 지정 패킷 종류로 전송한다.</summary>
        private void Send(PacketType type, Action<System.IO.BinaryWriter> write)
        {
            _sender?.SendStream(type, BinaryPayload.Create(write));
        }

    }
}
