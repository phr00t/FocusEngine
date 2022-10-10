// Copyright (c) Xenko contributors (https://xenko.com) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.
#if XENKO_GRAPHICS_API_VULKAN
using System;
using System.Collections.Generic;
using Xenko.Core.Threading;
using Vortice.Vulkan;
using static Vortice.Vulkan.Vulkan;
using Xenko.Core;
using System.Runtime.ExceptionServices;
using Xenko.Core.Serialization.Contents;

namespace Xenko.Graphics
{
    public partial class Buffer
    {
        internal static object BufferLocker = new object();
        internal VkBuffer NativeBuffer;
        internal VkBufferView NativeBufferView;
        internal VkAccessFlags NativeAccessMask;

        /// <summary>
        /// Initializes a new instance of the <see cref="Buffer" /> class.
        /// </summary>
        /// <param name="description">The description.</param>
        /// <param name="viewFlags">Type of the buffer.</param>
        /// <param name="viewFormat">The view format.</param>
        /// <param name="dataPointer">The data pointer.</param>
        protected Buffer InitializeFromImpl(BufferDescription description, BufferFlags viewFlags, PixelFormat viewFormat, IntPtr dataPointer)
        {
            bufferDescription = description;
            //nativeDescription = ConvertToNativeDescription(Description);
            ViewFlags = viewFlags;
            InitCountAndViewFormat(out this.elementCount, ref viewFormat);
            ViewFormat = viewFormat;
            Recreate(dataPointer);

            if (GraphicsDevice != null)
            {
                GraphicsDevice.RegisterBufferMemoryUsage(SizeInBytes);
            }

            Ready = true;

            return this;
        }

        public unsafe void DestroyNow()
        {
            GraphicsDevice.RegisterBufferMemoryUsage(-SizeInBytes);

            if (NativeBufferView != VkBufferView.Null)
            {
                vkDestroyBufferView(GraphicsDevice.NativeDevice, NativeBufferView, null);
                NativeBufferView = VkBufferView.Null;
            }

            if (NativeBuffer != VkBuffer.Null)
            {
                vkDestroyBuffer(GraphicsDevice.NativeDevice, NativeBuffer, null);            
                NativeBuffer = VkBuffer.Null;
            }

            if (NativeMemory != VkDeviceMemory.Null)
            {
                if (NativeMemoryOffset.HasValue)
                    VulkanMemoryPool.Free(this, NativeMemoryOffset.Value);
                else
                    vkFreeMemory(GraphicsDevice.NativeDevice, NativeMemory, null);
                NativeMemory = VkDeviceMemory.Null;
            }

            base.OnDestroyed();
        }

        /// <inheritdoc/>
        protected internal override void OnDestroyed()
        {
            Ready = false;

            GraphicsDevice.RegisterBufferMemoryUsage(-SizeInBytes);

            if (NativeBufferView != VkBufferView.Null)
            {
                GraphicsDevice.Collect(NativeBufferView);
                NativeBufferView = VkBufferView.Null;
            }

            if (NativeBuffer != VkBuffer.Null)
            {
                GraphicsDevice.Collect(NativeBuffer);
                NativeBuffer = VkBuffer.Null;
            }

            if (NativeMemory != VkDeviceMemory.Null)
            {
                if (NativeMemoryOffset.HasValue)
                    VulkanMemoryPool.Free(this, NativeMemoryOffset.Value);
                else
                    GraphicsDevice.Collect(NativeMemory);
                NativeMemory = VkDeviceMemory.Null;
            }

            base.OnDestroyed();
        }

        /// <inheritdoc/>
        protected internal override bool OnRecreate()
        {
            base.OnRecreate();

            if (Description.Usage == GraphicsResourceUsage.Immutable
                || Description.Usage == GraphicsResourceUsage.Default)
                return false;

            Recreate(IntPtr.Zero);

            return true;
        }

        /// <summary>
        /// Explicitly recreate buffer with given data. Usually called after a <see cref="GraphicsDevice"/> reset.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="dataPointer"></param>
        public unsafe void Recreate(IntPtr dataPointer)
        {
            // capture vertex information for possible later easy batching or physics mesh generation
            if (dataPointer != IntPtr.Zero &&
                (ViewFlags == BufferFlags.VertexBuffer && (ContentManager._captureThisModel || CaptureAllModelBuffers || bufferDescription.SizeInBytes <= CaptureVertexBuffersOfSize) ||
                 ViewFlags == BufferFlags.IndexBuffer && (ContentManager._captureThisModel || CaptureAllModelBuffers || bufferDescription.SizeInBytes <= CaptureIndexBuffersOfSize)))
            {
                VertIndexData = new byte[Description.SizeInBytes];
                fixed (byte* vid = &VertIndexData[0])
                {
                    Utilities.CopyMemory((IntPtr)vid, dataPointer, VertIndexData.Length);
                }
            } else VertIndexData = null;

            var createInfo = new VkBufferCreateInfo
            {
                sType = VkStructureType.BufferCreateInfo,
                size = (ulong)bufferDescription.SizeInBytes,
                flags = VkBufferCreateFlags.None,
            };

            createInfo.usage |= VkBufferUsageFlags.TransferSrc;
            createInfo.usage |= VkBufferUsageFlags.TransferDst;

            if (Usage == GraphicsResourceUsage.Staging)
            {
                NativeAccessMask = VkAccessFlags.HostRead | VkAccessFlags.HostWrite;
                NativePipelineStageMask |= VkPipelineStageFlags.Host;
            }
            else
            {
                if ((ViewFlags & BufferFlags.VertexBuffer) != 0)
                {
                    createInfo.usage |= VkBufferUsageFlags.VertexBuffer;
                    NativeAccessMask |= VkAccessFlags.VertexAttributeRead;
                    NativePipelineStageMask |= VkPipelineStageFlags.VertexInput;
                }

                if ((ViewFlags & BufferFlags.IndexBuffer) != 0)
                {
                    createInfo.usage |= VkBufferUsageFlags.IndexBuffer;
                    NativeAccessMask |= VkAccessFlags.IndexRead;
                    NativePipelineStageMask |= VkPipelineStageFlags.VertexInput;
                }

                if ((ViewFlags & BufferFlags.ConstantBuffer) != 0)
                {
                    createInfo.usage |= VkBufferUsageFlags.UniformBuffer;
                    NativeAccessMask |= VkAccessFlags.UniformRead;
                    NativePipelineStageMask |= VkPipelineStageFlags.VertexShader | VkPipelineStageFlags.FragmentShader;
                }

                if ((ViewFlags & BufferFlags.ShaderResource) != 0)
                {
                    createInfo.usage |= VkBufferUsageFlags.UniformTexelBuffer;
                    NativeAccessMask |= VkAccessFlags.ShaderRead;
                    NativePipelineStageMask |= VkPipelineStageFlags.VertexShader | VkPipelineStageFlags.FragmentShader;

                    if ((ViewFlags & BufferFlags.UnorderedAccess) != 0)
                    {
                        createInfo.usage |= VkBufferUsageFlags.StorageTexelBuffer;
                        NativeAccessMask |= VkAccessFlags.ShaderWrite;
                    }
                }
            }

            // Create buffer
            vkCreateBuffer(GraphicsDevice.NativeDevice, &createInfo, null, out NativeBuffer);

            // Allocate memory
            var memoryProperties = VkMemoryPropertyFlags.DeviceLocal;
            if (bufferDescription.Usage == GraphicsResourceUsage.Staging || Usage == GraphicsResourceUsage.Dynamic)
            { 
                memoryProperties = VkMemoryPropertyFlags.HostVisible | VkMemoryPropertyFlags.HostCoherent;
            }

            vkGetBufferMemoryRequirements(GraphicsDevice.NativeDevice, NativeBuffer, out var memoryRequirements);

            if (bufferDescription.Usage == GraphicsResourceUsage.Staging || Usage == GraphicsResourceUsage.Dynamic)
            {
                AllocateMemory(memoryProperties, memoryRequirements); // special case that doesn't use a pool
                NativeMemoryOffset = null;
            }
            else if (VulkanMemoryPool.TryAllocateMemoryForBuffer(createInfo.size, GraphicsDevice.NativeDevice, GraphicsDevice.NativePhysicalDevice, ref memoryRequirements, ref memoryProperties, out var memOffset, out var devmem))
            {
                NativeMemory = devmem;
                NativeMemoryOffset = memOffset;
            }
            else
            {
                AllocateMemory(memoryProperties, memoryRequirements); // last resort, allocate its own VkDeviceMemory
                NativeMemoryOffset = null;
            }

            if (NativeMemory != VkDeviceMemory.Null)
            {
                vkBindBufferMemory(GraphicsDevice.NativeDevice, NativeBuffer, NativeMemory, NativeMemoryOffset ?? 0);

                if (SizeInBytes > 0)
                {
                    // Begin copy command buffer
                    var commandBufferAllocateInfo = new VkCommandBufferAllocateInfo
                    {
                        sType = VkStructureType.CommandBufferAllocateInfo,
                        commandPool = GraphicsDevice.NativeCopyCommandPool,
                        commandBufferCount = 1,
                        level = VkCommandBufferLevel.Primary
                    };
                    VkCommandBuffer commandBuffer;

                    // stuff we can prepare before the lock
                    var fenceCreateInfo = new VkFenceCreateInfo { sType = VkStructureType.FenceCreateInfo };
                    vkCreateFence(GraphicsDevice.NativeDevice, &fenceCreateInfo, null, out var fence);

                    var bufferMemoryBarrier = new VkBufferMemoryBarrier(NativeBuffer, VkAccessFlags.TransferWrite, NativeAccessMask);

                    var submitInfo = new VkSubmitInfo
                    {
                        sType = VkStructureType.SubmitInfo,
                        commandBufferCount = 1,
                        pCommandBuffers = &commandBuffer,
                    };

                    var beginInfo = new VkCommandBufferBeginInfo { sType = VkStructureType.CommandBufferBeginInfo, flags = VkCommandBufferUsageFlags.OneTimeSubmit };

                    lock (BufferLocker)
                    {
                        vkAllocateCommandBuffers(GraphicsDevice.NativeDevice, &commandBufferAllocateInfo, &commandBuffer);

                        vkBeginCommandBuffer(commandBuffer, &beginInfo);

                        // Copy to upload buffer
                        if (dataPointer != IntPtr.Zero)
                        {
                            if (Usage == GraphicsResourceUsage.Dynamic)
                            {
                                void* uploadMemory;
                                vkMapMemory(GraphicsDevice.NativeDevice, NativeMemory, 0, (ulong)SizeInBytes, VkMemoryMapFlags.None, &uploadMemory);
                                Utilities.CopyMemory((IntPtr)uploadMemory, dataPointer, SizeInBytes);
                                GraphicsDevice.DelayedUnmaps.Enqueue(NativeMemory);
                            }
                            else
                            {
                                var sizeInBytes = bufferDescription.SizeInBytes;
                                var uploadMemory = GraphicsDevice.AllocateUploadBuffer(sizeInBytes, out var uploadBuffer, out int uploadOffset);
                                Utilities.CopyMemory(uploadMemory, dataPointer, sizeInBytes);

                                // Barrier
                                var memoryBarrier = new VkBufferMemoryBarrier(uploadBuffer, VkAccessFlags.HostWrite, VkAccessFlags.TransferRead, (ulong)uploadOffset, (ulong)sizeInBytes);
                                vkCmdPipelineBarrier(commandBuffer, VkPipelineStageFlags.Host, VkPipelineStageFlags.Transfer, VkDependencyFlags.None, 0, null, 1, &memoryBarrier, 0, null);

                                // Copy
                                var bufferCopy = new VkBufferCopy
                                {
                                    srcOffset = (uint)uploadOffset,
                                    dstOffset = 0,
                                    size = (uint)sizeInBytes
                                };

                                vkCmdCopyBuffer(commandBuffer, uploadBuffer, NativeBuffer, 1, &bufferCopy);
                            }
                        }
                        else
                        {
                            vkCmdFillBuffer(commandBuffer, NativeBuffer, 0, (uint)bufferDescription.SizeInBytes, 0);
                        }

                        // Barrier
                        vkCmdPipelineBarrier(commandBuffer, VkPipelineStageFlags.Transfer, VkPipelineStageFlags.AllCommands, VkDependencyFlags.None, 0, null, 1, &bufferMemoryBarrier, 0, null);

                        // Close and submit
                        vkEndCommandBuffer(commandBuffer);
                        vkQueueSubmit(GraphicsDevice.NativeCommandQueue, 1, &submitInfo, fence);
                        vkWaitForFences(GraphicsDevice.NativeDevice, 1, &fence, Vortice.Vulkan.VkBool32.True, ulong.MaxValue);

                        vkFreeCommandBuffers(GraphicsDevice.NativeDevice, GraphicsDevice.NativeCopyCommandPool, 1, &commandBuffer);
                    }

                    vkDestroyFence(GraphicsDevice.NativeDevice, fence, null);

                    InitializeViews();
                }
            }
        }

        /// <summary>
        /// Initializes the views.
        /// </summary>
        private void InitializeViews()
        {
            var viewFormat = ViewFormat;

            if ((ViewFlags & BufferFlags.RawBuffer) != 0)
            {
                viewFormat = PixelFormat.R32_Typeless;
            }

            if ((ViewFlags & (BufferFlags.ShaderResource | BufferFlags.UnorderedAccess)) != 0)
            {
                NativeBufferView = GetShaderResourceView(viewFormat);
            }
        }

        internal unsafe VkBufferView GetShaderResourceView(PixelFormat viewFormat)
        {
            var createInfo = new VkBufferViewCreateInfo
            {
                sType = VkStructureType.BufferViewCreateInfo,
                buffer = NativeBuffer,
                format = viewFormat == PixelFormat.None ? VkFormat.Undefined : VulkanConvertExtensions.ConvertPixelFormat(viewFormat),
                range = (ulong)SizeInBytes, // this.ElementCount
                //view = (Description.BufferFlags & BufferFlags.RawBuffer) != 0 ? VkBufferViewType.Raw : VkBufferViewType.Formatted,
            };

            vkCreateBufferView(GraphicsDevice.NativeDevice, &createInfo, null, out var bufferView);
            return bufferView;
        }

        private void InitCountAndViewFormat(out int count, ref PixelFormat viewFormat)
        {
            if (Description.StructureByteStride == 0)
            {
                // TODO: The way to calculate the count is not always correct depending on the ViewFlags...etc.
                if ((ViewFlags & BufferFlags.RawBuffer) != 0)
                {
                    count = Description.SizeInBytes / sizeof(int);
                }
                else if ((ViewFlags & BufferFlags.ShaderResource) != 0)
                {
                    count = Description.SizeInBytes / viewFormat.SizeInBytes();
                }
                else
                {
                    count = 0;
                }
            }
            else
            {
                // For structured buffer
                count = Description.SizeInBytes / Description.StructureByteStride;
                viewFormat = PixelFormat.None;
            }
        }
    }
} 
#endif 
