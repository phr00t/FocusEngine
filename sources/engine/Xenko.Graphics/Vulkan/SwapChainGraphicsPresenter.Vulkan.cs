// Copyright (c) Xenko contributors (https://xenko.com) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.
#if XENKO_GRAPHICS_API_VULKAN
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Runtime.InteropServices;
using Vortice.Vulkan;
using static Vortice.Vulkan.Vulkan;
using Xenko.Core;
using Xenko.Core.Threading;
using System.Runtime.ExceptionServices;

namespace Xenko.Graphics
{
    /// <summary>
    /// Graphics presenter for SwapChain.
    /// </summary>
    public class SwapChainGraphicsPresenter : GraphicsPresenter
    {
        private VkSwapchainKHR swapChain = VkSwapchainKHR.Null;
        private VkSurfaceKHR surface;

        private Texture backbuffer;
        private SwapChainImageInfo[] swapchainImages;
        private uint currentBufferIndex;
        private VkFence presentFence;

        private struct SwapChainImageInfo
        {
            public VkImage NativeImage;
            public VkImageView NativeColorAttachmentView;
        }

        public SwapChainGraphicsPresenter(GraphicsDevice device, PresentationParameters presentationParameters)
            : base(device, presentationParameters)
        {
            PresentInterval = presentationParameters.PresentationInterval;

            backbuffer = new Texture(device);

            CreateSurface();

            // Initialize the swap chain
            CreateSwapChain();
        }

        public override Texture BackBuffer
        {
            get
            {
                return backbuffer;
            }
        }

        public override object NativePresenter
        {
            get
            {
                return swapChain;
            }
        }

        public override bool InternalFullscreen { get; set; }

        [HandleProcessCorruptedStateExceptionsAttribute]
        public override unsafe void Present()
        {
            // remember which frame we need to present (for presenting thread)
            VkSwapchainKHR swapChainCopy = swapChain;
            uint currentBufferIndexCopy = currentBufferIndex;
            VkPresentInfoKHR presentInfo = new VkPresentInfoKHR {
                sType = VkStructureType.PresentInfoKHR,
                swapchainCount = 1,
                pSwapchains = &swapChainCopy,
                pImageIndices = &currentBufferIndexCopy,
            };

            VkResult result = VkResult.Success;

            try {
                result = vkAcquireNextImageKHR(GraphicsDevice.NativeDevice, swapChain, (ulong)0, VkSemaphore.Null, VkFence.Null, out currentBufferIndex);
                vkQueuePresentKHR(GraphicsDevice.NativeCommandQueue, &presentInfo);
            } catch (AccessViolationException ave) {
                Xenko.Graphics.SDL.Window.GeneratePresentError();                
            }

            if ((int)result < 0)
                Xenko.Graphics.SDL.Window.GenerateSwapchainError("vkAcquireNextImageKHR result: " + (int)result);

            // did we get another image?
            while ((int)result > 0)
            {
                result = vkAcquireNextImageKHR(GraphicsDevice.NativeDevice, swapChain, (ulong)0, VkSemaphore.Null, VkFence.Null, out currentBufferIndex);
                Thread.Sleep(1);
            }

            // Flip render targets
            backbuffer.SetNativeHandles(swapchainImages[currentBufferIndex].NativeImage, swapchainImages[currentBufferIndex].NativeColorAttachmentView);
        }

        public override void BeginDraw(CommandList commandList)
        {   
            // Backbuffer needs to be cleared
            backbuffer.IsInitialized = false;
        }

        public override void EndDraw(CommandList commandList, bool present)
        {
        }

        protected override void OnNameChanged()
        {
            base.OnNameChanged();
        }

        /// <inheritdoc/>
        protected internal override unsafe void OnDestroyed()
        {
            DestroySwapchain();

            vkDestroySurfaceKHR(GraphicsDevice.NativeInstance, surface, null);
            surface = VkSurfaceKHR.Null;

            base.OnDestroyed();
        }

        /// <inheritdoc/>
        public override void OnRecreated()
        {
            base.OnRecreated();

            // not supported
        }

        protected unsafe override void ResizeBackBuffer(int width, int height, PixelFormat format)
        {
            // not supported
        }

        protected override void ResizeDepthStencilBuffer(int width, int height, PixelFormat format)
        {
            // not supported
        }

        private unsafe void DestroySwapchain()
        {
            if (swapChain == VkSwapchainKHR.Null)
                return;
    
            vkQueueWaitIdle(GraphicsDevice.NativeCommandQueue);

            backbuffer.OnDestroyed();

            foreach (var swapchainImage in swapchainImages)
            {
                vkDestroyImageView(GraphicsDevice.NativeDevice, swapchainImage.NativeColorAttachmentView, null);
            }
            swapchainImages = null;

            vkDestroySwapchainKHR(GraphicsDevice.NativeDevice, swapChain, null);
            swapChain = VkSwapchainKHR.Null;
        }

        private unsafe void CreateSwapChain()
        {
            // we are destroying the swap chain now, because it causes lots of other things to be reset too (like all commandbufferpools)
            // normally we pass the old swapchain to the create new swapchain Vulkan call... but I haven't figured out a stable way of
            // preserving the old swap chain to be passed during the new swapchain creation, and then destroying just the old swapchain parts.
            // might have to reset the command buffers and pipeline stuff after swapchain handoff... for another day e.g. TODO
            DestroySwapchain();

            var formats = new[] { PixelFormat.B8G8R8A8_UNorm_SRgb, PixelFormat.R8G8B8A8_UNorm_SRgb, PixelFormat.B8G8R8A8_UNorm, PixelFormat.R8G8B8A8_UNorm };

            foreach (var format in formats)
            {
                var nativeFromat = VulkanConvertExtensions.ConvertPixelFormat(format);

                vkGetPhysicalDeviceFormatProperties(GraphicsDevice.NativePhysicalDevice, nativeFromat, out var formatProperties);

                if ((formatProperties.optimalTilingFeatures & VkFormatFeatureFlags.ColorAttachment) != 0)
                {
                    Description.BackBufferFormat = format;
                    break;
                }
            }

            // Queue
            // TODO VULKAN: Queue family is needed when creating the Device, so here we can just do a sanity check?
            var queueNodeIndex = vkGetPhysicalDeviceQueueFamilyProperties(GraphicsDevice.NativePhysicalDevice).ToArray().
                Where((properties, index) => (properties.queueFlags & VkQueueFlags.Graphics) != 0 && vkGetPhysicalDeviceSurfaceSupportKHR(GraphicsDevice.NativePhysicalDevice, (uint)index, surface, out var supported) == VkResult.Success && supported == Vortice.Vulkan.VkBool32.True).
                Select((properties, index) => index).First();

            // Surface format
            var backBufferFormat = VulkanConvertExtensions.ConvertPixelFormat(Description.BackBufferFormat);

            var surfaceFormats = vkGetPhysicalDeviceSurfaceFormatsKHR(GraphicsDevice.NativePhysicalDevice, surface).ToArray();
            if ((surfaceFormats.Length != 1 || surfaceFormats[0].format != VkFormat.Undefined) &&
                !surfaceFormats.Any(x => x.format == backBufferFormat))
            {
                backBufferFormat = surfaceFormats[0].format;
            }

            // Create swapchain
            vkGetPhysicalDeviceSurfaceCapabilitiesKHR(GraphicsDevice.NativePhysicalDevice, surface, out var surfaceCapabilities);

            // Buffer count
            uint desiredImageCount = Math.Max(surfaceCapabilities.minImageCount, 6);
            if (surfaceCapabilities.maxImageCount > 0 && desiredImageCount > surfaceCapabilities.maxImageCount)
            {
                desiredImageCount = surfaceCapabilities.maxImageCount;
            }

            // Transform
            VkSurfaceTransformFlagsKHR preTransform;
            if ((surfaceCapabilities.supportedTransforms & VkSurfaceTransformFlagsKHR.Identity) != 0)
            {
                preTransform = VkSurfaceTransformFlagsKHR.Identity;
            }
            else
            {
                preTransform = surfaceCapabilities.currentTransform;
            }

            // Find present mode
            var swapChainPresentMode = VkPresentModeKHR.Fifo; // Always supported, but slow
            if (Description.PresentationInterval == PresentInterval.Immediate) {
                var presentModes = vkGetPhysicalDeviceSurfacePresentModesKHR(GraphicsDevice.NativePhysicalDevice, surface);
                foreach (var pm in presentModes)
                {
                    if (pm == VkPresentModeKHR.Mailbox)
                    {
                        swapChainPresentMode = VkPresentModeKHR.Mailbox;
                        break;
                    }
                }
            }

            // Create swapchain
            var swapchainCreateInfo = new VkSwapchainCreateInfoKHR
            {
                sType = VkStructureType.SwapchainCreateInfoKHR,
                surface = surface,
                imageArrayLayers = 1,
                imageSharingMode = VkSharingMode.Exclusive,
                imageExtent = new Vortice.Vulkan.VkExtent2D(Description.BackBufferWidth, Description.BackBufferHeight),
                imageFormat = backBufferFormat,
                imageColorSpace = Description.ColorSpace == ColorSpace.Gamma ? VkColorSpaceKHR.SrgbNonLinear : 0,
                imageUsage = VkImageUsageFlags.ColorAttachment | VkImageUsageFlags.TransferDst | (surfaceCapabilities.supportedUsageFlags & VkImageUsageFlags.TransferSrc), // TODO VULKAN: Use off-screen buffer to emulate
                presentMode = swapChainPresentMode,
                compositeAlpha = VkCompositeAlphaFlagsKHR.Opaque,
                minImageCount = desiredImageCount,
                preTransform = preTransform,
                oldSwapchain = swapChain,
                clipped = Vortice.Vulkan.VkBool32.True
            };

            vkCreateSwapchainKHR(GraphicsDevice.NativeDevice, &swapchainCreateInfo, null, out swapChain);

            CreateBackBuffers();

            // resize/create stencil buffers
            var newTextureDescription = DepthStencilBuffer.Description;
            newTextureDescription.Width = Description.BackBufferWidth;
            newTextureDescription.Height = Description.BackBufferHeight;

            // Manually update the texture
            DepthStencilBuffer.OnDestroyed();

            // Put it in our back buffer texture
            DepthStencilBuffer.InitializeFrom(newTextureDescription);
        }

        private unsafe void CreateSurface()
        {
            // Check for Window Handle parameter
            if (Description.DeviceWindowHandle == null)
            {
                throw new ArgumentException("DeviceWindowHandle cannot be null");
            }
            // Create surface
#if XENKO_UI_SDL
            var control = Description.DeviceWindowHandle.NativeWindow as SDL.Window;

            if (SDL2.SDL.SDL_Vulkan_CreateSurface(control.SdlHandle, GraphicsDevice.NativeInstance.Handle, out ulong surfacePtr) == SDL2.SDL.SDL_bool.SDL_FALSE)
                Xenko.Graphics.SDL.Window.GenerateCreationError();

            surface = new VkSurfaceKHR(surfacePtr);
#elif XENKO_PLATFORM_WINDOWS
            var controlHandle = Description.DeviceWindowHandle.Handle;
            if (controlHandle == IntPtr.Zero)
            {
                throw new NotSupportedException($"Form of type [{Description.DeviceWindowHandle.GetType().Name}] is not supported. Only System.Windows.Control are supported");
            }

            var surfaceCreateInfo = new VkWin32SurfaceCreateInfoKHR
            {
                sType = VkStructureType.Win32SurfaceCreateInfoKHR,
                instanceHandle = Process.GetCurrentProcess().Handle,
                windowHandle = controlHandle,
            };
            surface = GraphicsDevice.NativeInstance.CreateWin32Surface(surfaceCreateInfo);
#elif XENKO_PLATFORM_ANDROID
            throw new NotImplementedException();
#elif XENKO_PLATFORM_LINUX
            throw new NotSupportedException("Only SDL is supported for the time being on Linux");
#else
            throw new NotSupportedException();
#endif
        }

        private unsafe void CreateBackBuffers()
        {
            backbuffer.OnDestroyed();

            // Create the texture object
            var backBufferDescription = new TextureDescription
            {
                ArraySize = 1,
                Dimension = TextureDimension.Texture2D,
                Height = Description.BackBufferHeight,
                Width = Description.BackBufferWidth,
                Depth = 1,
                Flags = TextureFlags.RenderTarget,
                Format = Description.BackBufferFormat,
                MipLevels = 1,
                MultisampleCount = MultisampleCount.None,
                Usage = GraphicsResourceUsage.Default
            };
            backbuffer.InitializeWithoutResources(backBufferDescription);

            var createInfo = new VkImageViewCreateInfo
            {
                sType = VkStructureType.ImageViewCreateInfo,
                subresourceRange = new VkImageSubresourceRange(VkImageAspectFlags.Color, 0, 1, 0, 1),
                format = backbuffer.NativeFormat,
                viewType = VkImageViewType.Image2D,
            };

            // We initialize swapchain images to PresentSource, since we swap them out while in this layout.
            backbuffer.NativeAccessMask = VkAccessFlags.MemoryRead;
            backbuffer.NativeLayout = VkImageLayout.PresentSrcKHR;

            var imageMemoryBarrier = new VkImageMemoryBarrier
            {
                sType = VkStructureType.ImageMemoryBarrier,
                subresourceRange = new VkImageSubresourceRange(VkImageAspectFlags.Color, 0, 1, 0, 1),
                oldLayout = VkImageLayout.Undefined,
                newLayout = VkImageLayout.PresentSrcKHR,
                srcAccessMask = VkAccessFlags.None,
                dstAccessMask = VkAccessFlags.MemoryRead
            };

            var commandBuffer = GraphicsDevice.NativeCopyCommandBuffer;
            var beginInfo = new VkCommandBufferBeginInfo { sType = VkStructureType.CommandBufferBeginInfo };
            vkBeginCommandBuffer(commandBuffer, &beginInfo);

            var buffers = vkGetSwapchainImagesKHR(GraphicsDevice.NativeDevice, swapChain);
            swapchainImages = new SwapChainImageInfo[buffers.Length];

            for (int i = 0; i < buffers.Length; i++)
            {
                // Create image views
                swapchainImages[i].NativeImage = createInfo.image = buffers[i];
                vkCreateImageView(GraphicsDevice.NativeDevice, &createInfo, null, out swapchainImages[i].NativeColorAttachmentView);

                // Transition to default layout
                imageMemoryBarrier.image = buffers[i];
                vkCmdPipelineBarrier(commandBuffer, VkPipelineStageFlags.AllCommands, VkPipelineStageFlags.AllCommands, VkDependencyFlags.None, 0, null, 0, null, 1, &imageMemoryBarrier);
            }

            // Close and submit
            vkEndCommandBuffer(commandBuffer);

            var submitInfo = new VkSubmitInfo
            {
                sType = VkStructureType.SubmitInfo,
                commandBufferCount = 1,
                pCommandBuffers = &commandBuffer,
            };
            vkQueueSubmit(GraphicsDevice.NativeCommandQueue, 1, &submitInfo, VkFence.Null);
            vkQueueWaitIdle(GraphicsDevice.NativeCommandQueue);
            vkResetCommandBuffer(commandBuffer, VkCommandBufferResetFlags.None);

            vkAcquireNextImageKHR(GraphicsDevice.NativeDevice, swapChain, ulong.MaxValue, VkSemaphore.Null, VkFence.Null, out currentBufferIndex);

            // Apply the first swap chain image to the texture
            backbuffer.SetNativeHandles(swapchainImages[currentBufferIndex].NativeImage, swapchainImages[currentBufferIndex].NativeColorAttachmentView);
        }
    }
}
#endif
