using System.Collections;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using XRLumina._Core.Messaging;

namespace XRLumina._Core.Service
{
    internal sealed class MirroringService
    {
        private readonly DeviceMessageSender _sender;
        private readonly bool _streamEnabled;
        private readonly Camera _sourceCamera;
        private readonly int _width;
        private readonly int _height;
        private readonly int _framesPerSecond;
        private readonly int _jpegQuality;
        private RenderTexture _renderTexture;
        private int _bufferWidth;
        private int _bufferHeight;
        private bool _readbackPending;
        private bool _isStreaming;
        private int _streamGeneration;

        /// <summary>Unity 실행 호스트, 송신기, 미러링 설정으로 서비스를 생성한다.</summary>
        internal MirroringService(
            XRLuminaClientService client,
            bool streamEnabled,
            Camera sourceCamera,
            int width,
            int height,
            int framesPerSecond,
            int jpegQuality)
        {
            _sender = client.MessageSender;
            _streamEnabled = streamEnabled;
            _sourceCamera = sourceCamera;
            _width = width;
            _height = height;
            _framesPerSecond = framesPerSecond;
            _jpegQuality = jpegQuality;
        }

        /// <summary>프레임 생산을 시작한다. 이미 실행 중이면 중복 시작하지 않는다.</summary>
        internal bool StartStreaming()
        {
            if (!_streamEnabled || _isStreaming)
            {
                return false;
            }
            _streamGeneration++;
            _isStreaming = true;
            return true;
        }

        /// <summary>프레임 생산을 중단하고 캡처 버퍼를 해제한다.</summary>
        public void StopStreaming()
        {
            _streamGeneration++;
            _isStreaming = false;
            ReleaseBuffers();
            _readbackPending = false;
        }

        /// <summary>설정된 주기에 맞춰 매 프레임 캡처를 요청한다.</summary>
        internal IEnumerator StreamLoop()
        {
            var delay = new WaitForSeconds(1f / Mathf.Max(1, _framesPerSecond));
            while (true)
            {
                yield return new WaitForEndOfFrame();
                CaptureFrame();
                yield return delay;
            }
        }

        /// <summary>메인 스레드를 대기시키지 않고 GPU에서 프레임을 비동기로 읽는다.</summary>
        private void CaptureFrame()
        {
            var camera = ResolveCamera();
            if (!_isStreaming || camera == null || _sender == null || _readbackPending)
            {
                return;
            }

            var targetWidth = Mathf.Max(1, _width);
            var targetHeight = Mathf.Max(1, _height);
            EnsureBuffers(targetWidth, targetHeight);
            var previousTarget = camera.targetTexture;
            try
            {
                camera.targetTexture = _renderTexture;
                camera.Render();
                _readbackPending = true;
                var generation = _streamGeneration;
                AsyncGPUReadback.Request(
                    _renderTexture,
                    0,
                    TextureFormat.RGB24,
                    request => OnReadbackComplete(request, generation)
                );
            }
            finally
            {
                camera.targetTexture = previousTarget;
            }
        }

        /// <summary>GPU 읽기 결과를 이미지로 인코딩해 등록된 소비자에게 전달한다.</summary>
        private void OnReadbackComplete(AsyncGPUReadbackRequest request, int generation)
        {
            if (generation != _streamGeneration)
            {
                return;
            }
            _readbackPending = false;
            if (!_isStreaming || request.hasError || _sender == null)
            {
                return;
            }

            NativeArray<byte> encoded = ImageConversion.EncodeNativeArrayToJPG(
                request.GetData<byte>(),
                GraphicsFormat.R8G8B8_UNorm,
                (uint)_bufferWidth,
                (uint)_bufferHeight,
                0,
                Mathf.Clamp(_jpegQuality, 1, 100)
            );
            try
            {
                _sender.SendMirrorFrame(encoded.ToArray());
            }
            finally
            {
                encoded.Dispose();
            }
        }

        /// <summary>현재 해상도에 맞는 렌더링 및 인코딩 버퍼를 준비한다.</summary>
        private void EnsureBuffers(int targetWidth, int targetHeight)
        {
            if (_renderTexture != null &&
                _bufferWidth == targetWidth && _bufferHeight == targetHeight)
            {
                return;
            }

            ReleaseBuffers();
            _bufferWidth = targetWidth;
            _bufferHeight = targetHeight;
            _renderTexture = new RenderTexture(targetWidth, targetHeight, 24, RenderTextureFormat.ARGB32);
        }

        /// <summary>생성된 렌더링 및 인코딩 버퍼를 해제한다.</summary>
        private void ReleaseBuffers()
        {
            if (_renderTexture != null)
            {
                _renderTexture.Release();
                UnityEngine.Object.Destroy(_renderTexture);
                _renderTexture = null;
            }
            _bufferWidth = 0;
            _bufferHeight = 0;
        }

        /// <summary>지정 카메라, 메인 카메라, 활성 카메라 순서로 캡처 대상을 찾는다.</summary>
        private Camera ResolveCamera()
        {
            if (_sourceCamera != null && _sourceCamera.isActiveAndEnabled)
            {
                return _sourceCamera;
            }
            if (Camera.main != null && Camera.main.isActiveAndEnabled)
            {
                return Camera.main;
            }
            var cameras = UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsSortMode.None);
            foreach (var camera in cameras)
            {
                if (camera != null && camera.isActiveAndEnabled)
                {
                    return camera;
                }
            }
            return null;
        }
    }
}
