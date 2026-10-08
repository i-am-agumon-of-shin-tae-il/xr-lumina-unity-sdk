using System.Collections;
using System.Threading.Tasks;
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
        private readonly System.Func<Camera> _resolveCamera;
        private readonly int _width;
        private readonly int _height;
        private readonly int _framesPerSecond;
        private readonly int _jpegQuality;
        private RenderTexture _renderTexture;
        private int _bufferWidth;
        private int _bufferHeight;
        private bool _readbackPending;
        private Task<byte[]> _encodingTask;
        private bool _isStreaming;
        private int _streamGeneration;

        /// <summary>Unity 실행 호스트, 송신기, 미러링 설정으로 서비스를 생성한다.</summary>
        internal MirroringService(
            XRLuminaClientService client,
            bool streamEnabled,
            System.Func<Camera> resolveCamera,
            int width,
            int height,
            int framesPerSecond,
            int jpegQuality)
        {
            _sender = client.MessageSender;
            _streamEnabled = streamEnabled;
            _resolveCamera = resolveCamera;
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
            if (_encodingTask != null)
            {
                // 중단된 세대의 작업 오류도 관찰하고 결과 프레임은 폐기한다.
                _encodingTask.ContinueWith(task => { var error = task.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
                _encodingTask = null;
            }
            ReleaseBuffers();
            _readbackPending = false;
        }

        /// <summary>설정된 주기에 맞춰 매 프레임 캡처를 요청한다.</summary>
        internal IEnumerator StreamLoop()
        {
            var endOfFrame = new WaitForEndOfFrame();
            var interval = 1f / Mathf.Max(1, _framesPerSecond);
            var nextCaptureAt = Time.realtimeSinceStartup;
            while (true)
            {
                yield return endOfFrame;
                CompleteEncoding();
                var now = Time.realtimeSinceStartup;
                if (now >= nextCaptureAt)
                {
                    CaptureFrame();
                    nextCaptureAt = now + interval;
                }
            }
        }

        /// <summary>메인 스레드를 대기시키지 않고 GPU에서 프레임을 비동기로 읽는다.</summary>
        private void CaptureFrame()
        {
            var camera = ResolveCamera();
            if (!_isStreaming || camera == null || _sender == null || _readbackPending || _encodingTask != null)
            {
                return;
            }

            var targetWidth = Mathf.Max(1, _width);
            var targetHeight = Mathf.Max(1, _height);
            EnsureBuffers(targetWidth, targetHeight);
            var target = _renderTexture;
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
                    request =>
                    {
                        try
                        {
                            OnReadbackComplete(request, generation);
                        }
                        finally
                        {
                            if (generation != _streamGeneration || target != _renderTexture)
                            {
                                ReleaseTexture(target);
                            }
                        }
                    }
                );
            }
            catch (System.Exception exception)
            {
                _readbackPending = false;
                Debug.LogWarning($"[Mirroring] GPU 캡처 실패: {exception.Message}");
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

            var pixels = request.GetData<byte>().ToArray();
            var width = (uint)_bufferWidth;
            var height = (uint)_bufferHeight;
            var quality = Mathf.Clamp(_jpegQuality, 1, 100);
            _encodingTask = Task.Run(() => ImageConversion.EncodeArrayToJPG(
                pixels, GraphicsFormat.R8G8B8_UNorm, width, height, 0, quality));
        }

        /// <summary>인코딩이 끝난 최신 프레임을 전송하고 작업 오류를 Unity 로그로 전달한다.</summary>
        private void CompleteEncoding()
        {
            if (_encodingTask == null || !_encodingTask.IsCompleted)
            {
                return;
            }
            var task = _encodingTask;
            _encodingTask = null;
            if (task.IsFaulted)
            {
                Debug.LogWarning($"[Mirroring] JPEG 인코딩 실패: {task.Exception?.GetBaseException().Message}");
                return;
            }
            if (_isStreaming)
            {
                _sender.SendMirrorFrame(task.Result);
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

        /// <summary>읽기 중인 렌더링 버퍼는 완료 콜백에서 해제하고 현재 버퍼 참조를 초기화한다.</summary>
        private void ReleaseBuffers()
        {
            if (_renderTexture != null)
            {
                if (!_readbackPending)
                {
                    ReleaseTexture(_renderTexture);
                }
                _renderTexture = null;
            }
            _bufferWidth = 0;
            _bufferHeight = 0;
        }

        /// <summary>GPU 읽기가 완료된 렌더링 버퍼의 네이티브 자원을 해제한다.</summary>
        private static void ReleaseTexture(RenderTexture target)
        {
            if (target != null)
            {
                target.Release();
                UnityEngine.Object.Destroy(target);
            }
        }

        /// <summary>클라이언트에 연결된 활성 HMD 카메라를 캡처 대상으로 반환한다.</summary>
        private Camera ResolveCamera()
        {
            var camera = _resolveCamera?.Invoke();
            return camera != null && camera.isActiveAndEnabled ? camera : null;
        }
    }
}
