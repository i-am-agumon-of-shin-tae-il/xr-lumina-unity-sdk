using System;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace XRLumina._Core.Infrastructure
{
    /// <summary>GPU 읽기와 이미지 인코딩을 분리하고 캡처 자원의 수명을 관리한다.</summary>
    internal static class ImageCaptureEncoder
    {
        /// <summary>GPU 읽기 완료 후 작업 스레드에서 PNG를 생성하고 읽기 자원을 해제한다.</summary>
        internal static Task<byte[]> EncodePng(RenderTexture target, int shiftPixels, Action release)
        {
            var width = target.width;
            var height = target.height;
            var completion = new TaskCompletionSource<byte[]>();
            if (SystemInfo.supportsAsyncGPUReadback)
            {
                try
                {
                    AsyncGPUReadback.Request(target, 0, TextureFormat.RGB24, request =>
                    {
                        try
                        {
                            if (request.hasError)
                            {
                                throw new InvalidOperationException("GPU capture readback failed.");
                            }
                            Encode(request.GetData<byte>().ToArray(), width, height, shiftPixels, completion);
                        }
                        catch (Exception exception)
                        {
                            completion.TrySetException(exception);
                        }
                        finally
                        {
                            release();
                        }
                    });
                }
                catch (Exception exception)
                {
                    release();
                    completion.TrySetException(exception);
                }
                return completion.Task;
            }

            var previousActive = RenderTexture.active;
            Texture2D texture = null;
            try
            {
                RenderTexture.active = target;
                texture = new Texture2D(width, height, TextureFormat.RGB24, false);
                texture.ReadPixels(new Rect(0f, 0f, width, height), 0, 0, false);
                Encode(texture.GetRawTextureData<byte>().ToArray(), width, height, shiftPixels, completion);
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
            finally
            {
                RenderTexture.active = previousActive;
                if (texture != null)
                {
                    UnityEngine.Object.Destroy(texture);
                }
                release();
            }
            return completion.Task;
        }

        /// <summary>RGB 행 보정과 스레드 안전 PNG 인코딩을 Unity 프레임 밖에서 처리한다.</summary>
        private static void Encode(byte[] pixels, int width, int height, int shiftPixels,
            TaskCompletionSource<byte[]> completion)
        {
            Task.Run(() =>
            {
                try
                {
                    var shift = ((shiftPixels % width) + width) % width;
                    if (shift != 0)
                    {
                        var row = new byte[width * 3];
                        for (var y = 0; y < height; y++)
                        {
                            var offset = y * row.Length;
                            Buffer.BlockCopy(pixels, offset + shift * 3, row, 0, row.Length - shift * 3);
                            Buffer.BlockCopy(pixels, offset, row, row.Length - shift * 3, shift * 3);
                            Buffer.BlockCopy(row, 0, pixels, offset, row.Length);
                        }
                    }
                    completion.TrySetResult(ImageConversion.EncodeArrayToPNG(
                        pixels, GraphicsFormat.R8G8B8_UNorm, (uint)width, (uint)height));
                }
                catch (Exception exception)
                {
                    completion.TrySetException(exception);
                }
            });
        }
    }
}
