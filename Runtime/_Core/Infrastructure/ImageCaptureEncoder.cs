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
                                ReadPixelsAndEncode(target, width, height, shiftPixels, completion);
                            }
                            else
                            {
                                Encode(request.GetData<byte>().ToArray(), width, height, shiftPixels, completion);
                            }
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

            try
            {
                ReadPixelsAndEncode(target, width, height, shiftPixels, completion);
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
            finally
            {
                release();
            }
            return completion.Task;
        }

        /// <summary>비동기 GPU 읽기를 사용할 수 없으면 기존 픽셀 읽기로 대체하고 인코딩은 작업 스레드에 맡긴다.</summary>
        private static void ReadPixelsAndEncode(RenderTexture target, int width, int height, int shiftPixels,
            TaskCompletionSource<byte[]> completion)
        {
            var previousActive = RenderTexture.active;
            Texture2D texture = null;
            try
            {
                RenderTexture.active = target;
                texture = new Texture2D(width, height, TextureFormat.RGB24, false);
                texture.ReadPixels(new Rect(0f, 0f, width, height), 0, 0, false);
                Encode(texture.GetRawTextureData<byte>().ToArray(), width, height, shiftPixels, completion);
            }
            finally
            {
                RenderTexture.active = previousActive;
                if (texture != null)
                {
                    UnityEngine.Object.Destroy(texture);
                }
            }
        }

        /// <summary>RGB 행 보정과 스레드 안전 PNG 인코딩을 Unity 프레임 밖에서 처리한다.</summary>
        private static void Encode(byte[] pixels, int width, int height, int shiftPixels,
            TaskCompletionSource<byte[]> completion)
        {
            Task.Run(() =>
            {
                try
                {
                    if (width <= 0 || height <= 0 || (long)width * height * 3 != pixels.Length)
                    {
                        throw new ArgumentException("캡처 크기와 RGB 픽셀 배열 길이가 일치해야 합니다.");
                    }
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
