using System;
using System.Threading.Tasks;
using FFmpeg.AutoGen;

namespace FramePlayer.Engines.FFmpeg
{
    internal static unsafe class FfmpegHardwareDeviceCache
    {
        private static readonly object VulkanDeviceSync = new object();
        private static AVBufferRef* _sharedVulkanDeviceContext;
        private static Task _vulkanWarmupTask;

        public static void StartVulkanWarmup()
        {
            if (!IsRuntimeConfigured())
            {
                return;
            }

            lock (VulkanDeviceSync)
            {
                if (_sharedVulkanDeviceContext != null || _vulkanWarmupTask != null)
                {
                    return;
                }

                _vulkanWarmupTask = Task.Run((Action)WarmVulkanDeviceCore);
            }
        }

        public static bool TryAcquireVulkanDevice(out AVBufferRef* deviceContext, out string errorMessage)
        {
            deviceContext = null;
            errorMessage = string.Empty;

            if (!IsRuntimeConfigured())
            {
                errorMessage = "FFmpeg runtime path is not configured.";
                return false;
            }

            Task warmupTask = null;
            lock (VulkanDeviceSync)
            {
                if (_sharedVulkanDeviceContext != null)
                {
                    return TryCloneSharedVulkanDeviceLocked(out deviceContext, out errorMessage);
                }

                warmupTask = _vulkanWarmupTask;
            }

            if (warmupTask != null)
            {
                try
                {
                    warmupTask.Wait();
                }
                catch (AggregateException)
                {
                }

                lock (VulkanDeviceSync)
                {
                    if (_sharedVulkanDeviceContext != null)
                    {
                        return TryCloneSharedVulkanDeviceLocked(out deviceContext, out errorMessage);
                    }
                }
            }

            if (!EnsureSharedVulkanDevice(out errorMessage))
            {
                return false;
            }

            lock (VulkanDeviceSync)
            {
                return TryCloneSharedVulkanDeviceLocked(out deviceContext, out errorMessage);
            }
        }

        private static void WarmVulkanDeviceCore()
        {
            try
            {
                string ignored;
                EnsureSharedVulkanDevice(out ignored);
            }
            finally
            {
                lock (VulkanDeviceSync)
                {
                    _vulkanWarmupTask = null;
                }
            }
        }

        private static bool EnsureSharedVulkanDevice(out string errorMessage)
        {
            lock (VulkanDeviceSync)
            {
                if (_sharedVulkanDeviceContext != null)
                {
                    errorMessage = string.Empty;
                    return true;
                }

                AVBufferRef* createdDeviceContext = null;
                var createDeviceResult = ffmpeg.av_hwdevice_ctx_create(
                    &createdDeviceContext,
                    AVHWDeviceType.AV_HWDEVICE_TYPE_VULKAN,
                    null,
                    null,
                    0);
                if (createDeviceResult < 0)
                {
                    if (createdDeviceContext != null)
                    {
                        ffmpeg.av_buffer_unref(&createdDeviceContext);
                    }

                    errorMessage = FfmpegNativeHelpers.GetErrorMessage(createDeviceResult);
                    return false;
                }

                _sharedVulkanDeviceContext = createdDeviceContext;
                errorMessage = string.Empty;
                return true;
            }
        }

        private static bool TryCloneSharedVulkanDeviceLocked(out AVBufferRef* deviceContext, out string errorMessage)
        {
            deviceContext = ffmpeg.av_buffer_ref(_sharedVulkanDeviceContext);
            if (deviceContext == null)
            {
                errorMessage = "Could not reference the shared Vulkan FFmpeg device.";
                return false;
            }

            errorMessage = string.Empty;
            return true;
        }

        private static bool IsRuntimeConfigured()
        {
            return !string.IsNullOrWhiteSpace(ffmpeg.RootPath);
        }
    }
}
