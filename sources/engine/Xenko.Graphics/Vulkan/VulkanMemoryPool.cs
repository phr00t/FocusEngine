#if XENKO_GRAPHICS_API_VULKAN

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using Vortice.Vulkan;

namespace Xenko.Graphics
{
    public class VulkanMemoryPool
    {
        internal static VkDeviceMemory BigMemoryPool = VkDeviceMemory.Null, SmallMemoryPool = VkDeviceMemory.Null;
        internal static ConcurrentQueue<ulong> FreeBig = new ConcurrentQueue<ulong>(), FreeSmall = new ConcurrentQueue<ulong>();

        internal static unsafe void AllocatePool(out VkDeviceMemory NativeMemory, ref VkDevice device, ref VkPhysicalDevice pDevice, ref VkMemoryPropertyFlags memoryProperties, ref VkMemoryRequirements memoryRequirements, ulong size)
        {
            var allocateInfo = new VkMemoryAllocateInfo
            {
                sType = VkStructureType.MemoryAllocateInfo,
                allocationSize = size,
            };

            Vortice.Vulkan.Vulkan.vkGetPhysicalDeviceMemoryProperties(pDevice, out var physicalDeviceMemoryProperties);

            var typeBits = memoryRequirements.memoryTypeBits;
            for (int i = 0; i < physicalDeviceMemoryProperties.memoryTypeCount; i++)
            {
                if ((typeBits & 1) == 1)
                {
                    // Type is available, does it match user properties?
                    var memoryType = physicalDeviceMemoryProperties.memoryTypes[i];
                    if ((memoryType.propertyFlags & memoryProperties) == memoryProperties)
                    {
                        allocateInfo.memoryTypeIndex = (uint)i;
                        break;
                    }
                }
                typeBits >>= 1;
            }

            NativeMemory = new VkDeviceMemory();

            Vortice.Vulkan.Vulkan.vkAllocateMemory(device, &allocateInfo, null, out NativeMemory);
        }

        public static bool Free(Buffer buf, ulong offset)
        {
            if ((ulong)buf.SizeInBytes >= Buffer.SmallPooledBufferSize)
            {
                if (BigMemoryPool != VkDeviceMemory.Null)
                {
                    FreeBig.Enqueue(offset);
                    Buffer.CurrentFreeBigPool = FreeBig.Count;
                    return true;
                }
            }
            else if (SmallMemoryPool != VkDeviceMemory.Null)
            {
                FreeSmall.Enqueue(offset);
                Buffer.CurrentFreeSmallPool = FreeSmall.Count;
                return true;
            }
            
            return false;
        }

        public static bool TryAllocateMemoryForBuffer(ulong size, VkDevice device, VkPhysicalDevice pDevice, ref VkMemoryRequirements memoryRequirements, ref VkMemoryPropertyFlags memoryProperties, out ulong offset, out VkDeviceMemory mem)
        {
            if (size > Buffer.LargestBufferSize) Buffer.LargestBufferSize = size;

            if (size >= Buffer.LargePooledBufferSize)
            {
                offset = 0;
                mem = VkDeviceMemory.Null;
                Buffer.PoolMissesDueToSize++;
                return false;
            }
            else if (size >= Buffer.SmallPooledBufferSize) 
            {
                if (BigMemoryPool == VkDeviceMemory.Null)
                {
                    AllocatePool(out BigMemoryPool, ref device, ref pDevice, ref memoryProperties, ref memoryRequirements, Buffer.LargePooledBufferCount * Buffer.LargePooledBufferSize);
                    for (ulong i = 0; i < Buffer.LargePooledBufferCount; i++)
                        FreeBig.Enqueue(i * Buffer.LargePooledBufferSize);
                }

                if (FreeBig.TryDequeue(out offset))
                {
                    Buffer.CurrentFreeBigPool = FreeBig.Count;
                    mem = BigMemoryPool;
                    return true;
                }
            }
            else
            {
                if (SmallMemoryPool == VkDeviceMemory.Null)
                {
                    AllocatePool(out SmallMemoryPool, ref device, ref pDevice, ref memoryProperties, ref memoryRequirements, Buffer.SmallPooledBufferCount * Buffer.SmallPooledBufferSize);
                    for (ulong i = 0; i < Buffer.SmallPooledBufferCount; i++)
                        FreeSmall.Enqueue(i * Buffer.SmallPooledBufferSize);
                }

                if (FreeSmall.TryDequeue(out offset))
                {
                    Buffer.CurrentFreeSmallPool = FreeSmall.Count;
                    mem = SmallMemoryPool;
                    return true;
                }
            }

            offset = 0;
            mem = VkDeviceMemory.Null;
            Buffer.PoolMissesDueToExaustion++;
            return false;
        }
    }
}

#endif
