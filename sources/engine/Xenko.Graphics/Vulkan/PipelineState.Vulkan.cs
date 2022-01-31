// Copyright (c) Xenko contributors (https://xenko.com) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.
#if XENKO_GRAPHICS_API_VULKAN
using System;
using System.Collections.Generic;
using System.Linq;
using Vortice.Vulkan;
using static Vortice.Vulkan.Vulkan;
using System.Runtime.ExceptionServices;
using System.Security;
using Xenko.Core;
using Xenko.Core.Threading;
using Xenko.Core.Collections;
using Xenko.Core.Serialization;
using Xenko.Shaders;
using System.Threading;
using Encoding = System.Text.Encoding;

namespace Xenko.Graphics
{
    public partial class PipelineState
    {
        internal VkDescriptorSetLayout NativeDescriptorSetLayout;
        internal uint[] DescriptorTypeCounts;
        internal DescriptorSetLayout DescriptorSetLayout;

        internal bool errorDuringCreate;
        internal VkPipelineLayout NativeLayout;
        internal VkPipeline NativePipeline = VkPipeline.Null;
        internal VkRenderPass NativeRenderPass;
        internal int[] ResourceGroupMapping;
        internal int ResourceGroupCount;
        internal PipelineStateDescription Description;

        // GLSL converter always outputs entry point main()
        private static readonly byte[] defaultEntryPoint = Encoding.UTF8.GetBytes("main\0");

        // State exposed by the CommandList
        private static readonly VkDynamicState[] dynamicStates =
        {
            VkDynamicState.Viewport,
            VkDynamicState.Scissor,
            VkDynamicState.BlendConstants,
            VkDynamicState.StencilReference,
        };

        public PIPELINE_STATE CurrentState() {
            if (errorDuringCreate) return PIPELINE_STATE.ERROR;
            return NativePipeline != VkPipeline.Null ? PIPELINE_STATE.READY : PIPELINE_STATE.LOADING;
        }

        internal PipelineState(GraphicsDevice graphicsDevice) : base(graphicsDevice) {
            // just return a memory address to Prepare later
        }

        internal void Prepare(PipelineStateDescription pipelineStateDescription)
        {

            Description = pipelineStateDescription.Clone();
            Recreate();
        }

        //[HandleProcessCorruptedStateExceptionsAttribute, SecurityCriticalAttribute]
        private unsafe void Recreate()
        {
            errorDuringCreate = false;

            if (Description.RootSignature == null)
                return;

            VkPipelineShaderStageCreateInfo[] stages;

            // create render pass
            bool hasDepthStencilAttachment = Description.Output.DepthStencilFormat != PixelFormat.None;

            var renderTargetCount = Description.Output.RenderTargetCount;

            var attachmentCount = renderTargetCount;
            if (hasDepthStencilAttachment)
                attachmentCount++;

            var attachments = new VkAttachmentDescription[attachmentCount];
            var colorAttachmentReferences = new VkAttachmentReference[renderTargetCount];

            fixed (PixelFormat* renderTargetFormat = &Description.Output.RenderTargetFormat0)
            fixed (BlendStateRenderTargetDescription* blendDescription = &Description.BlendState.RenderTarget0)
            {
                for (int i = 0; i < renderTargetCount; i++)
                {
                    var currentBlendDesc = Description.BlendState.IndependentBlendEnable ? (blendDescription + i) : blendDescription;

                    attachments[i] = new VkAttachmentDescription
                    {
                        format = VulkanConvertExtensions.ConvertPixelFormat(*(renderTargetFormat + i)),
                        samples = VkSampleCountFlags.Count1,
                        loadOp = currentBlendDesc->BlendEnable ? VkAttachmentLoadOp.Load : VkAttachmentLoadOp.DontCare, // TODO VULKAN: Only if any destination blend?
                        storeOp = VkAttachmentStoreOp.Store,
                        stencilLoadOp = VkAttachmentLoadOp.DontCare,
                        stencilStoreOp = VkAttachmentStoreOp.DontCare,
                        initialLayout = VkImageLayout.ColorAttachmentOptimal,
                        finalLayout = VkImageLayout.ColorAttachmentOptimal,
                    };

                    colorAttachmentReferences[i] = new VkAttachmentReference
                    {
                        attachment = (uint)i,
                        layout = VkImageLayout.ColorAttachmentOptimal,
                    };
                }
            }

            if (hasDepthStencilAttachment)
            {
                attachments[attachmentCount - 1] = new VkAttachmentDescription
                {
                    format = Texture.GetFallbackDepthStencilFormat(GraphicsDevice, VulkanConvertExtensions.ConvertPixelFormat(Description.Output.DepthStencilFormat)),
                    samples = VkSampleCountFlags.Count1,
                    loadOp = VkAttachmentLoadOp.Load, // TODO VULKAN: Only if depth read enabled?
                    storeOp = VkAttachmentStoreOp.Store, // TODO VULKAN: Only if depth write enabled?
                    stencilLoadOp = VkAttachmentLoadOp.DontCare, // TODO VULKAN: Handle stencil
                    stencilStoreOp = VkAttachmentStoreOp.DontCare,
                    initialLayout = VkImageLayout.DepthStencilAttachmentOptimal,
                    finalLayout = VkImageLayout.DepthStencilAttachmentOptimal,
                };
            }

            var depthAttachmentReference = new VkAttachmentReference
            {
                attachment = (uint)attachments.Length - 1,
                layout = VkImageLayout.DepthStencilAttachmentOptimal,
            };

            var subpass = new VkSubpassDescription
            {
                pipelineBindPoint = VkPipelineBindPoint.Graphics,
                colorAttachmentCount = (uint)renderTargetCount,
                pColorAttachments = colorAttachmentReferences.Length > 0 ? (VkAttachmentReference*)Core.Interop.Fixed(colorAttachmentReferences) : null,
                pDepthStencilAttachment = hasDepthStencilAttachment ? &depthAttachmentReference : null,
            };

            var renderPassCreateInfo = new VkRenderPassCreateInfo
            {
                sType = VkStructureType.RenderPassCreateInfo,
                attachmentCount = (uint)attachmentCount,
                pAttachments = attachments.Length > 0 ? (VkAttachmentDescription*)Core.Interop.Fixed(attachments) : null,
                subpassCount = 1,
                pSubpasses = &subpass,
            };

            // create pipeline layout
            // Remap descriptor set indices to those in the shader. This ordering generated by the ShaderCompiler
            var resourceGroups = Description.EffectBytecode.Reflection.ResourceBindings.Select(x => x.ResourceGroup ?? "Globals").Distinct().ToList();
            ResourceGroupCount = resourceGroups.Count;

            var layouts = Description.RootSignature.EffectDescriptorSetReflection.Layouts;
            
            // Get binding indices used by the shader
            var destinationBindings = Description.EffectBytecode.Stages
                .SelectMany(x => BinarySerialization.Read<ShaderInputBytecode>(x.Data).ResourceBindings)
                .GroupBy(x => x.Key, x => x.Value)
                .ToDictionary(x => x.Key, x => x.First());

            var maxBindingIndex = destinationBindings.Max(x => x.Value);
            var destinationEntries = new DescriptorSetLayoutBuilder.Entry[maxBindingIndex + 1];

            DescriptorBindingMapping = new List<DescriptorSetInfo>();

            for (int i = 0; i < resourceGroups.Count; i++)
            {
                var resourceGroupName = resourceGroups[i] == "Globals" ? Description.RootSignature.EffectDescriptorSetReflection.DefaultSetSlot : resourceGroups[i];
                var layoutIndex = resourceGroups[i] == null ? 0 : layouts.FindIndex(x => x.Name == resourceGroupName);

                // Check if the resource group is used by the shader
                if (layoutIndex == -1)
                    continue;

                var sourceEntries = layouts[layoutIndex].Layout.Entries;

                for (int sourceBinding = 0; sourceBinding < sourceEntries.Count; sourceBinding++)
                {
                    var sourceEntry = sourceEntries[sourceBinding];

                    int destinationBinding;
                    if (destinationBindings.TryGetValue(sourceEntry.Key.Name, out destinationBinding))
                    {
                        destinationEntries[destinationBinding] = sourceEntry;

                        // No need to umpdate immutable samplers
                        if (sourceEntry.Class == EffectParameterClass.Sampler && sourceEntry.ImmutableSampler != null)
                        {
                            continue;
                        }

                        DescriptorBindingMapping.Add(new DescriptorSetInfo
                        {
                            SourceSet = layoutIndex,
                            SourceBinding = sourceBinding,
                            DestinationBinding = destinationBinding,
                            DescriptorType = VulkanConvertExtensions.ConvertDescriptorType(sourceEntry.Class, sourceEntry.Type)
                        });
                    }
                }
            }

            // Create default sampler, used by texture and buffer loads
            destinationEntries[0] = new DescriptorSetLayoutBuilder.Entry
            {
                Class = EffectParameterClass.Sampler,
                Type = EffectParameterType.Sampler,
                ImmutableSampler = GraphicsDevice.SamplerStates.PointWrap,
                ArraySize = 1,
            };

            // Create descriptor set layout
            NativeDescriptorSetLayout = DescriptorSetLayout.CreateNativeDescriptorSetLayout(GraphicsDevice, destinationEntries, out DescriptorTypeCounts);

            // Create pipeline layout
            var nativeDescriptorSetLayout = NativeDescriptorSetLayout;
            var pipelineLayoutCreateInfo = new VkPipelineLayoutCreateInfo
            {
                sType = VkStructureType.PipelineLayoutCreateInfo,
                setLayoutCount = 1,
                pSetLayouts = &nativeDescriptorSetLayout,
            };

            // Create shader stages
            Dictionary<int, string> inputAttributeNames;

            // Note: important to pin this so that stages[x].Name is valid during this whole function
            void* defaultEntryPointData = Core.Interop.Fixed(defaultEntryPoint);
            stages = CreateShaderStages(Description, out inputAttributeNames);

            var inputAttributes = new VkVertexInputAttributeDescription[Description.InputElements.Length];
            int inputAttributeCount = 0;
            var inputBindings = new VkVertexInputBindingDescription[inputAttributes.Length];
            int inputBindingCount = 0;

            for (int inputElementIndex = 0; inputElementIndex < inputAttributes.Length; inputElementIndex++)
            {
                var inputElement = Description.InputElements[inputElementIndex];
                var slotIndex = inputElement.InputSlot;

                if (inputElement.InstanceDataStepRate > 1)
                {
                    throw new NotImplementedException();
                }

                VkFormat format;
                int size;
                bool isCompressed;
                VulkanConvertExtensions.ConvertPixelFormat(inputElement.Format, out format, out size, out isCompressed);

                var location = inputAttributeNames.FirstOrDefault(x => x.Value == inputElement.SemanticName && inputElement.SemanticIndex == 0 || x.Value == inputElement.SemanticName + inputElement.SemanticIndex);
                if (location.Value != null)
                {
                    inputAttributes[inputAttributeCount++] = new VkVertexInputAttributeDescription
                    {
                        format = format,
                        offset = (uint)inputElement.AlignedByteOffset,
                        binding = (uint)inputElement.InputSlot,
                        location = (uint)location.Key
                    };
                }

                inputBindings[slotIndex].binding = (uint)slotIndex;
                inputBindings[slotIndex].inputRate = inputElement.InputSlotClass == InputClassification.Vertex ? VkVertexInputRate.Vertex : VkVertexInputRate.Instance;

                // TODO VULKAN: This is currently an argument to Draw() overloads.
                if (inputBindings[slotIndex].stride < inputElement.AlignedByteOffset + size)
                    inputBindings[slotIndex].stride = (uint)(inputElement.AlignedByteOffset + size);

                if (inputElement.InputSlot >= inputBindingCount)
                    inputBindingCount = inputElement.InputSlot + 1;
            }

            var inputAssemblyState = new VkPipelineInputAssemblyStateCreateInfo
            {
                sType = VkStructureType.PipelineInputAssemblyStateCreateInfo,
                topology = VulkanConvertExtensions.ConvertPrimitiveType(Description.PrimitiveType),
                primitiveRestartEnable = VulkanConvertExtensions.ConvertPrimitiveRestart(Description.PrimitiveType) ? (uint)1 : (uint)0,
            };

            // TODO VULKAN: Tessellation and multisampling
            var multisampleState = new VkPipelineMultisampleStateCreateInfo
            {
                sType = VkStructureType.PipelineMultisampleStateCreateInfo,
                rasterizationSamples = VkSampleCountFlags.Count1,
            };

            var rasterizationState = new VkPipelineRasterizationStateCreateInfo
            {
                sType = VkStructureType.PipelineRasterizationStateCreateInfo,
                cullMode = VulkanConvertExtensions.ConvertCullMode(Description.RasterizerState.CullMode),
                frontFace = Description.RasterizerState.FrontFaceCounterClockwise ? VkFrontFace.CounterClockwise : VkFrontFace.Clockwise,
                polygonMode = VulkanConvertExtensions.ConvertFillMode(Description.RasterizerState.FillMode),
                depthBiasEnable = 1, // TODO VULKAN
                depthBiasConstantFactor = Description.RasterizerState.DepthBias,
                depthBiasSlopeFactor = Description.RasterizerState.SlopeScaleDepthBias,
                depthBiasClamp = Description.RasterizerState.DepthBiasClamp,
                lineWidth = 1.0f,
                depthClampEnable = Description.RasterizerState.DepthClipEnable ? (uint)0 : (uint)1,
                rasterizerDiscardEnable = 0,
            };

            var depthStencilState = new VkPipelineDepthStencilStateCreateInfo
            {
                sType = VkStructureType.PipelineDepthStencilStateCreateInfo,
                depthTestEnable = Description.DepthStencilState.DepthBufferEnable ? (uint)1 : (uint)0,
                stencilTestEnable = Description.DepthStencilState.StencilEnable ? (uint)1 : (uint)0,
                depthWriteEnable = Description.DepthStencilState.DepthBufferWriteEnable ? (uint)1 : (uint)0,

                minDepthBounds = 0.0f,
                maxDepthBounds = 1.0f,
                depthCompareOp = VulkanConvertExtensions.ConvertComparisonFunction(Description.DepthStencilState.DepthBufferFunction),
                front =
                {
                    compareOp = VulkanConvertExtensions.ConvertComparisonFunction(Description.DepthStencilState.FrontFace.StencilFunction),
                    depthFailOp = VulkanConvertExtensions.ConvertStencilOperation(Description.DepthStencilState.FrontFace.StencilDepthBufferFail),
                    failOp = VulkanConvertExtensions.ConvertStencilOperation(Description.DepthStencilState.FrontFace.StencilFail),
                    passOp = VulkanConvertExtensions.ConvertStencilOperation(Description.DepthStencilState.FrontFace.StencilPass),
                    compareMask = Description.DepthStencilState.StencilMask,
                    writeMask = Description.DepthStencilState.StencilWriteMask
                },
                back =
                {
                    compareOp = VulkanConvertExtensions.ConvertComparisonFunction(Description.DepthStencilState.BackFace.StencilFunction),
                    depthFailOp = VulkanConvertExtensions.ConvertStencilOperation(Description.DepthStencilState.BackFace.StencilDepthBufferFail),
                    failOp = VulkanConvertExtensions.ConvertStencilOperation(Description.DepthStencilState.BackFace.StencilFail),
                    passOp = VulkanConvertExtensions.ConvertStencilOperation(Description.DepthStencilState.BackFace.StencilPass),
                    compareMask = Description.DepthStencilState.StencilMask,
                    writeMask = Description.DepthStencilState.StencilWriteMask
                }
            };

            var description = Description.BlendState;

            var colorBlendAttachments = new VkPipelineColorBlendAttachmentState[renderTargetCount];

            var renderTargetBlendState = &description.RenderTarget0;
            for (int i = 0; i < renderTargetCount; i++)
            {
                colorBlendAttachments[i] = new VkPipelineColorBlendAttachmentState
                {
                    blendEnable = renderTargetBlendState->BlendEnable ? (uint)1 : (uint)0,
                    alphaBlendOp = VulkanConvertExtensions.ConvertBlendFunction(renderTargetBlendState->AlphaBlendFunction),
                    colorBlendOp = VulkanConvertExtensions.ConvertBlendFunction(renderTargetBlendState->ColorBlendFunction),
                    dstAlphaBlendFactor = VulkanConvertExtensions.ConvertBlend(renderTargetBlendState->AlphaDestinationBlend),
                    dstColorBlendFactor = VulkanConvertExtensions.ConvertBlend(renderTargetBlendState->ColorDestinationBlend),
                    srcAlphaBlendFactor = VulkanConvertExtensions.ConvertBlend(renderTargetBlendState->AlphaSourceBlend),
                    srcColorBlendFactor = VulkanConvertExtensions.ConvertBlend(renderTargetBlendState->ColorSourceBlend),
                    colorWriteMask = VulkanConvertExtensions.ConvertColorWriteChannels(renderTargetBlendState->ColorWriteChannels),
                };

                if (description.IndependentBlendEnable)
                    renderTargetBlendState++;
            }

            var viewportState = new VkPipelineViewportStateCreateInfo
            {
                sType = VkStructureType.PipelineViewportStateCreateInfo,
                scissorCount = 1,
                viewportCount = 1,
            };

            fixed (void* dynamicStatesPointer = dynamicStates.Length == 0 ? null : dynamicStates,
                         inputAttributesPointer = inputAttributes.Length == 0 ? null : inputAttributes,
                         inputBindingsPointer = inputBindings.Length == 0 ? null : inputBindings,
                         colorBlendAttachmentsPointer = colorBlendAttachments.Length == 0 ? null : colorBlendAttachments,
                         stagesPointer = stages.Length == 0 ? null : stages)
            {
                var vertexInputState = new VkPipelineVertexInputStateCreateInfo
                {
                    sType = VkStructureType.PipelineVertexInputStateCreateInfo,
                    vertexAttributeDescriptionCount = (uint)inputAttributeCount,
                    pVertexAttributeDescriptions = (Vortice.Vulkan.VkVertexInputAttributeDescription*)inputAttributesPointer,
                    vertexBindingDescriptionCount = (uint)inputBindingCount,
                    pVertexBindingDescriptions = (Vortice.Vulkan.VkVertexInputBindingDescription*)inputBindingsPointer,
                };

                var colorBlendState = new VkPipelineColorBlendStateCreateInfo
                {
                    sType = VkStructureType.PipelineColorBlendStateCreateInfo,
                    attachmentCount = (uint)renderTargetCount,
                    pAttachments = (Vortice.Vulkan.VkPipelineColorBlendAttachmentState*)colorBlendAttachmentsPointer,
                };

                var dynamicState = new VkPipelineDynamicStateCreateInfo
                {
                    sType = VkStructureType.PipelineDynamicStateCreateInfo,
                    dynamicStateCount = (uint)dynamicStates.Length,
                    pDynamicStates = (Vortice.Vulkan.VkDynamicState*)dynamicStatesPointer,
                };
                
                var createInfo = new VkGraphicsPipelineCreateInfo
                {
                    sType = VkStructureType.GraphicsPipelineCreateInfo,
                    layout = NativeLayout,
                    stageCount = (uint)stages.Length,
                    pVertexInputState = &vertexInputState,
                    pInputAssemblyState = &inputAssemblyState,
                    pRasterizationState = &rasterizationState,
                    pMultisampleState = &multisampleState,
                    pDepthStencilState = &depthStencilState,
                    pColorBlendState = &colorBlendState,
                    pDynamicState = &dynamicState,
                    pStages = (Vortice.Vulkan.VkPipelineShaderStageCreateInfo*)stagesPointer,
                    pViewportState = &viewportState,
                    renderPass = NativeRenderPass,
                    subpass = 0,
                };

                using (GraphicsDevice.QueueLock.ReadLock())
                {
                    vkCreateRenderPass(GraphicsDevice.NativeDevice, &renderPassCreateInfo, null, out NativeRenderPass);
                    vkCreatePipelineLayout(GraphicsDevice.NativeDevice, &pipelineLayoutCreateInfo, null, out NativeLayout);

                    createInfo.layout = NativeLayout;
                    createInfo.renderPass = NativeRenderPass;

                    try {
                        fixed (VkPipeline* nativePipelinePtr = &NativePipeline)
                            vkCreateGraphicsPipelines(GraphicsDevice.NativeDevice, VkPipelineCache.Null, 1, &createInfo, null, nativePipelinePtr);
                    } catch (Exception e) {
                        errorDuringCreate = true;
                        NativePipeline = VkPipeline.Null;
                    }
                }
            }

            // Cleanup shader modules
            for (int i=0; i<stages.Length; i++)
            {
                vkDestroyShaderModule(GraphicsDevice.NativeDevice, stages[i].module, null);
            }
        }

        /// <inheritdoc/>
        protected internal override bool OnRecreate()
        {
            Recreate();

            return true;
        }

        /// <inheritdoc/>
        protected internal override unsafe void OnDestroyed()
        {
            if (NativePipeline != VkPipeline.Null)
            {
                vkDestroyRenderPass(GraphicsDevice.NativeDevice, NativeRenderPass, null);
                vkDestroyPipeline(GraphicsDevice.NativeDevice, NativePipeline, null);
                vkDestroyPipelineLayout(GraphicsDevice.NativeDevice, NativeLayout, null);

                vkDestroyDescriptorSetLayout(GraphicsDevice.NativeDevice, NativeDescriptorSetLayout, null);

                NativePipeline = VkPipeline.Null;
            }

            base.OnDestroyed();
        }

        internal struct DescriptorSetInfo
        {
            public int SourceSet;
            public int SourceBinding;
            public int DestinationBinding;
            public VkDescriptorType DescriptorType;
        }

        internal List<DescriptorSetInfo> DescriptorBindingMapping;

        private unsafe VkPipelineShaderStageCreateInfo[] CreateShaderStages(PipelineStateDescription pipelineStateDescription, out Dictionary<int, string> inputAttributeNames)
        {
            var stages = pipelineStateDescription.EffectBytecode.Stages;
            var nativeStages = new VkPipelineShaderStageCreateInfo[stages.Length];

            inputAttributeNames = null;

            for (int i = 0; i < stages.Length; i++)
            {
                var shaderBytecode = BinarySerialization.Read<ShaderInputBytecode>(stages[i].Data);
                if (stages[i].Stage == ShaderStage.Vertex)
                    inputAttributeNames = shaderBytecode.InputAttributeNames;

                fixed (byte* entryPointPointer = &defaultEntryPoint[0])
                {
                    // Create stage
                    nativeStages[i] = new VkPipelineShaderStageCreateInfo
                    {
                        sType = VkStructureType.PipelineShaderStageCreateInfo,
                        stage = VulkanConvertExtensions.Convert(stages[i].Stage),
                        pName = entryPointPointer,
                    };
                    vkCreateShaderModule(GraphicsDevice.NativeDevice, shaderBytecode.Data, null, out nativeStages[i].module);
                }
            };

            return nativeStages;
        }
    }
}

#endif
