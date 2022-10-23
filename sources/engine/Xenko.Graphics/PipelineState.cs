// Copyright (c) Xenko contributors (https://xenko.com) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using Xenko.Core.ReferenceCounting;
using System.Threading;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Xenko.Graphics
{
    public partial class PipelineState : GraphicsResourceBase
    {
        public enum PIPELINE_STATE {
            LOADING = 0,
            READY = 1,
            ERROR = 2
        };

        public int InputBindingCount { get; private set; }
        internal long storedHash;

        public static PipelineState New(GraphicsDevice graphicsDevice, PipelineStateDescription pipelineStateDescription, PipelineState existingState, bool forcewait = false)
        {
            // Hash the current state
            long hashedState = pipelineStateDescription.GetLongHashCode();

            // do we even need to check the cache? We already have this?
            if (existingState != null && existingState.storedHash == hashedState)
                return existingState;

            PipelineState pipelineState = null;

            // check if it is in the cache, or being worked on...
            bool foundInCache = false;

            lock (graphicsDevice.CachedPipelineStates) {
                foundInCache = graphicsDevice.CachedPipelineStates.TryGetValue(hashedState, out pipelineState);
                if (!foundInCache) {
                    pipelineState = new PipelineState(graphicsDevice); // mark we will work on this pipeline (which is just blank right now)
                    pipelineState.storedHash = hashedState;
                    graphicsDevice.CachedPipelineStates[hashedState] = pipelineState;
                }
            }

            // if we have this cached, wait until it is ready to return
            if (foundInCache) {
                pipelineState.AddReferenceInternal();
                return pipelineState;
            }

            if (GraphicsDevice.Platform == GraphicsPlatform.Vulkan) {
                // if we are in vulkan, do it in a task to not hold up the rendering thread
                var cloned = pipelineStateDescription.Clone();

                if (forcewait)
                {
                    pipelineState.Prepare(cloned);
                    return pipelineState;
                }

                // do it as a task for later
                Xenko.Core.Threading.ThreadPool.Instance.QueueWorkItem(() =>
                {
                    pipelineState.Prepare(cloned);
                });

                return pipelineState;
            }
            
            // D3D seems to have quite bad concurrency when using CreateSampler while rendering
            lock (graphicsDevice.CachedPipelineStates) {
                pipelineState.Prepare(pipelineStateDescription);
            }

            return pipelineState;
        }
    }
}
