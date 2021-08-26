// Copyright (c) Xenko contributors (https://xenko.com) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Xenko.Core;
using Xenko.Core.Diagnostics;
using Xenko.Core.Mathematics;
using Xenko.Core.Threading;
using Xenko.Engine;
using Xenko.Games;
using Xenko.Graphics;
using Xenko.Input;
using Xenko.UI;
using Xenko.UI.Renderers;

namespace Xenko.Rendering.UI
{
    public partial class UIRenderFeature : RootRenderFeature
    {
        private IGame game;
        private UISystem uiSystem;
        private InputManager input;
        private IGraphicsDeviceService graphicsDeviceService;

        private RendererManager rendererManager;

        private static ConcurrentQueue<UIBatch> batches = new ConcurrentQueue<UIBatch>();

        public override Type SupportedRenderObjectType => typeof(RenderUIElement);

        protected override void InitializeCore()
        {
            base.InitializeCore();

            Name = "UIComponentRenderer";
            game = RenderSystem.Services.GetService<IGame>();
            input = RenderSystem.Services.GetService<InputManager>();
            uiSystem = RenderSystem.Services.GetService<UISystem>();
            graphicsDeviceService = RenderSystem.Services.GetSafeServiceAs<IGraphicsDeviceService>();

            if (uiSystem == null)
            {
                var gameSytems = RenderSystem.Services.GetSafeServiceAs<IGameSystemCollection>();
                uiSystem = new UISystem(RenderSystem.Services);
                RenderSystem.Services.AddService(uiSystem);
                gameSytems.Add(uiSystem);
            }

            rendererManager = new RendererManager(new DefaultRenderersFactory(RenderSystem.Services));


        }

        partial void PickingPrepare(List<PointerEvent> compactedPointerEvents);

        partial void PickingUpdate(RenderUIElement renderUIElement, Viewport viewport, ref Matrix worldViewProj, GameTime drawTime, List<PointerEvent> compactedPointerEvents);

        public override void Draw(RenderDrawContext context, RenderView renderView, RenderViewStage renderViewStage, int startIndex, int endIndex)
        {
            using (context.PushRenderTargetsAndRestore())
            {
                DrawInternal(context, renderView, renderViewStage, startIndex, endIndex);
            }
        }

        private object drawLocker = new object(), pickingLocker = new object();

        private void initUIElementStates(RenderDrawContext context, RenderView renderView,
                                         RenderViewStage renderViewStage, ref UIElementState[] uiElementStates,
                                         int index, int storeIndex, GameTime drawTime, List<PointerEvent> events, ulong posecount)
        {
            var renderNodeReference = renderViewStage.SortedRenderNodes[index].RenderNode;
            var renderNode = GetRenderNode(renderNodeReference);
            var renderElement = (RenderUIElement)renderNode.RenderObject;

            var uiElementState = new UIElementState(renderElement);
            uiElementStates[storeIndex] = uiElementState;

            var renderObject = uiElementState.RenderObject;
            var rootElement = renderObject.Page?.RootElement;

            if (rootElement != null)
            {
                UIBatch batch = getFreeBatch(context);

                var virtualResolution = renderObject.Resolution;
                var updatableRootElement = (IUIElementUpdate)rootElement;

                // update the UI element disposition
                rootElement.Measure(virtualResolution);
                rootElement.Arrange(virtualResolution, false);

                // calculate an estimate of the UI real size by projecting the element virtual resolution on the screen
                var virtualOrigin = uiElementState.WorldViewProjectionMatrix.Row4;
                var virtualWidth = new Vector4(virtualResolution.X / 2, 0, 0, 1);
                var virtualHeight = new Vector4(0, virtualResolution.Y / 2, 0, 1);
                var transformedVirtualWidth = Vector4.Zero;
                var transformedVirtualHeight = Vector4.Zero;
                for (var i = 0; i < 4; i++)
                {
                    transformedVirtualWidth[i] = virtualWidth[0] * uiElementState.WorldViewProjectionMatrix[0 + i] + uiElementState.WorldViewProjectionMatrix[12 + i];
                    transformedVirtualHeight[i] = virtualHeight[1] * uiElementState.WorldViewProjectionMatrix[4 + i] + uiElementState.WorldViewProjectionMatrix[12 + i];
                }

                var viewportSize = context.CommandList.Viewport.Size;
                var projectedOrigin = virtualOrigin.XY() / virtualOrigin.W;
                var projectedVirtualWidth = viewportSize * (transformedVirtualWidth.XY() / transformedVirtualWidth.W - projectedOrigin);
                var projectedVirtualHeight = viewportSize * (transformedVirtualHeight.XY() / transformedVirtualHeight.W - projectedOrigin);

                // Set default services
                rootElement.UIElementServices = new UIElementServices { Services = RenderSystem.Services };

                // perform the time-based updates of the UI element
                updatableRootElement.Update(drawTime);

                // update the UI element hierarchical properties
                var rootMatrix = Matrix.Translation(-virtualResolution / 2); // UI world is translated by a half resolution compared to its quad, which is centered around the origin
                updatableRootElement.UpdateWorldMatrix(ref rootMatrix, rootMatrix != uiElementState.RenderObject.LastRootMatrix);
                updatableRootElement.UpdateElementState(0);
                uiElementState.RenderObject.LastRootMatrix = rootMatrix;
                // set default resource dictionary

                // update layouting context.
                var layoutingContext = batch.layoutingContext as LayoutingContext;
                layoutingContext.VirtualResolution = virtualResolution;
                layoutingContext.RealResolution = viewportSize;
                layoutingContext.RealVirtualResolutionRatio = new Vector2(projectedVirtualWidth.Length() / virtualResolution.X, projectedVirtualHeight.Length() / virtualResolution.Y);
                rootElement.LayoutingContext = layoutingContext;

                if (renderObject.IsFullScreen)
                {
                    //var targetSize = viewportSize;
                    var targetSize = new Vector2(context.CommandList.RenderTargets[0].Width, context.CommandList.RenderTargets[0].Height);

                    // update the virtual resolution of the renderer
                    switch (renderObject.ResolutionStretch)
                    {
                        case ResolutionStretch.FixedWidthAdaptableHeight:
                            virtualResolution.Y = virtualResolution.X * targetSize.Y / targetSize.X;
                            break;
                        case ResolutionStretch.FixedHeightAdaptableWidth:
                            virtualResolution.X = virtualResolution.Y * targetSize.X / targetSize.Y;
                            break;
                        case ResolutionStretch.AutoFit:
                            float aspect = targetSize.X / targetSize.Y;
                            float virtAspect = virtualResolution.X / virtualResolution.Y;
                            if (aspect >= virtAspect)
                                goto case ResolutionStretch.FixedHeightAdaptableWidth;
                            goto case ResolutionStretch.FixedWidthAdaptableHeight;
                        case ResolutionStretch.AutoShrink:
                            if (targetSize.X < virtualResolution.X ||
                                targetSize.Y < virtualResolution.Y)
                                goto case ResolutionStretch.AutoFit;
                            else
                            {
                                virtualResolution.X = targetSize.X * targetSize.X / virtualResolution.X;
                                virtualResolution.Y = targetSize.Y * targetSize.Y / virtualResolution.Y;
                            }
                            break;
                    }

                    uiElementState.Update(renderObject, virtualResolution);
                }
                else
                {
                    CameraComponent cameraComponent = renderView.Camera as CameraComponent;
                    if (cameraComponent != null)
                        uiElementState.Update(renderObject, cameraComponent.VerticalFieldOfView,
                                              ref renderView.View, ref renderView.Projection);
                }

                if (renderObject.Source is UIComponent uic)
                {
                    uic.RenderedResolution = virtualResolution;
                    if (posecount != uic.VRPoseUpdate)
                    {
                        PickingUpdate(uiElementState.RenderObject, context.CommandList.Viewport, ref uiElementState.WorldViewProjectionMatrix, drawTime, events);
                        uic.VRPoseUpdate = posecount;
                    }
                }

                ReturnBatch(batch);
            }
        }

        private void DrawInternal(RenderDrawContext context, RenderView renderView, RenderViewStage renderViewStage, int startIndex, int endIndex)
        {
            base.Draw(context, renderView, renderViewStage, startIndex, endIndex);

            var uiProcessor = SceneInstance.GetCurrent(context.RenderContext).GetProcessor<UIRenderProcessor>();
            if (uiProcessor == null)
                return;

            // evaluate the current draw time (game instance is null for thumbnails)
            var drawTime = game != null ? game.DrawTime : new GameTime();

            // Prepare content required for Picking and MouseOver events
            var events = new List<PointerEvent>();
            PickingPrepare(events);

            ulong poseCount = VirtualReality.VRDeviceSystem.GetSystem?.Device?.PoseCount ?? 0;

            // build the list of the UI elements to render
            UIElementState[] uiElementStates = new UIElementState[endIndex - startIndex];
            if (uiElementStates.Length > 1 && GraphicsDevice.Platform == GraphicsPlatform.Vulkan)
            {
                Xenko.Core.Threading.Dispatcher.For(startIndex, endIndex, (index) =>
                {
                    initUIElementStates(context, renderView, renderViewStage, ref uiElementStates, index, index - startIndex, drawTime, events, poseCount);
                });
            } 
            else
            {
                for(int i=startIndex; i<endIndex; i++)
                {
                    initUIElementStates(context, renderView, renderViewStage, ref uiElementStates, i, i - startIndex, drawTime, events, poseCount);
                }
            }

            events?.Clear();

            lock (drawLocker)
            {
                UIBatch batch = getFreeBatch(context);

                var renderingContext = batch.renderingContext as UIRenderingContext;

                // update the rendering context
                renderingContext.GraphicsContext = context.GraphicsContext;
                renderingContext.Time = drawTime;

                DepthStencilStateDescription stencilState = uiSystem.KeepStencilValueState;

                // actually draw stuff
                for (int j = 0; j < uiElementStates.Length; j++)
                {
                    var uiElementState = uiElementStates[j];

                    renderingContext.RenderObject = uiElementState.RenderObject;
                    var rootElement = renderingContext.RenderObject.Page?.RootElement;
                    if (rootElement == null) continue;

                    // update the rendering context values specific to this element
                    renderingContext.ViewProjectionMatrix = uiElementState.WorldViewProjectionMatrix;
                    
                    switch (renderingContext.RenderObject.depthMode)
                    {
                        case Sprites.RenderSprite.SpriteDepthMode.Ignore:
                            stencilState.DepthBufferWriteEnable = false;
                            stencilState.DepthBufferEnable = false;
                            break;
                        case Sprites.RenderSprite.SpriteDepthMode.ReadOnly:
                            stencilState.DepthBufferWriteEnable = false;
                            stencilState.DepthBufferEnable = true;
                            break;
                        default:
                            stencilState.DepthBufferWriteEnable = true;
                            stencilState.DepthBufferEnable = true;
                            break;
                        case Sprites.RenderSprite.SpriteDepthMode.WriteOnly:
                            stencilState.DepthBufferWriteEnable = true;
                            stencilState.DepthBufferEnable = true;
                            stencilState.DepthBufferFunction = CompareFunction.Always;
                            break;
                    }

                    SamplerState samplerState;
                    switch (renderingContext.RenderObject.Sampler)
                    {
                        default:
                            samplerState = context.GraphicsDevice.SamplerStates.LinearClamp;
                            break;
                        case UIElementSampler.PointClamp:
                            samplerState = context.GraphicsDevice.SamplerStates.PointClamp;
                            break;
                        case UIElementSampler.AnisotropicClamp:
                            samplerState = context.GraphicsDevice.SamplerStates.AnisotropicClamp;
                            break;
                    }

                    // start the image draw session
                    renderingContext.StencilTestReferenceValue = 0;
                    batch.Begin(context.GraphicsContext, ref uiElementState.WorldViewProjectionMatrix, BlendStates.AlphaBlend, samplerState, null, stencilState, renderingContext.StencilTestReferenceValue);

                    // Render the UI elements in the final render target
                    RecursiveDrawWithClipping(context, rootElement, ref uiElementState.WorldViewProjectionMatrix, batch, ref stencilState, samplerState);

                    batch.End();
                }

                ReturnBatch(batch);
            }
        }

        private UIBatch directXBatch;
        private UIBatch getFreeBatch(RenderDrawContext context)
        {
            if (GraphicsDevice.Platform == GraphicsPlatform.Vulkan)
            {
                if (batches.TryDequeue(out UIBatch batch) == false)
                    return new UIBatch(context.GraphicsDevice, new UIRenderingContext(), new LayoutingContext());
                return batch;
            }
            else
            {
                if (directXBatch == null) directXBatch = new UIBatch(context.GraphicsDevice, new UIRenderingContext(), new LayoutingContext());
                return directXBatch;
            }
        }

        private void ReturnBatch(UIBatch batch)
        {
            if (batch != directXBatch)
                batches.Enqueue(batch);
        }

        private void RecursiveDrawWithClipping(RenderDrawContext context, UIElement element, ref Matrix worldViewProj, UIBatch batch, ref DepthStencilStateDescription dstate, SamplerState samplerState)
        {
            // if the element is not visible, we also remove all its children
            if (!element.IsVisible)
                return;

            var renderingContext = batch.renderingContext as UIRenderingContext;
            var layoutingContext = batch.layoutingContext as LayoutingContext;

            var renderer = rendererManager.GetRenderer(element);
            renderingContext.DepthBias = element.DepthBias;

            // render the clipping region of the element
            if (element.ClipToBounds)
            {
                // flush current elements
                batch.End();

                // render the clipping region
                batch.Begin(context.GraphicsContext, ref worldViewProj, BlendStates.ColorDisabled, samplerState, null, uiSystem.IncreaseStencilValueState, renderingContext.StencilTestReferenceValue);
                renderer.RenderClipping(element, renderingContext, batch);
                batch.End();

                // update context and restart the batch
                renderingContext.StencilTestReferenceValue += 1;
                batch.Begin(context.GraphicsContext, ref worldViewProj, BlendStates.AlphaBlend, samplerState, null, dstate, renderingContext.StencilTestReferenceValue);
            }

            // render the design of the element
            renderer.RenderColor(element, renderingContext, batch);

            // render the children
            foreach (var child in element.VisualChildrenCollection)
                RecursiveDrawWithClipping(context, child, ref worldViewProj, batch, ref dstate, samplerState);

            // clear the element clipping region from the stencil buffer
            if (element.ClipToBounds)
            {
                // flush current elements
                batch.End();

                renderingContext.DepthBias = element.MaxChildrenDepthBias;

                // render the clipping region
                batch.Begin(context.GraphicsContext, ref worldViewProj, BlendStates.ColorDisabled, samplerState, null, uiSystem.DecreaseStencilValueState, renderingContext.StencilTestReferenceValue);
                renderer.RenderClipping(element, renderingContext, batch);
                batch.End();

                // update context and restart the batch
                renderingContext.StencilTestReferenceValue -= 1;
                batch.Begin(context.GraphicsContext, ref worldViewProj, BlendStates.AlphaBlend, samplerState, null, dstate, renderingContext.StencilTestReferenceValue);
            }
        }

        public ElementRenderer GetRenderer(UIElement element)
        {
            return rendererManager.GetRenderer(element);
        }

        public void RegisterRendererFactory(Type uiElementType, IElementRendererFactory factory)
        {
            rendererManager.RegisterRendererFactory(uiElementType, factory);
        }

        public void RegisterRenderer(UIElement element, ElementRenderer renderer)
        {
            rendererManager.RegisterRenderer(element, renderer);
        }

        private static Matrix ReallyCloseUI = Matrix.Transformation(new Vector3(0.505f), Quaternion.Identity, new Vector3(0f, 0f, 63.5f));

        private class UIElementState
        {
            public readonly RenderUIElement RenderObject;
            public Matrix WorldViewProjectionMatrix;

            public UIElementState(RenderUIElement renderObject)
            {
                RenderObject = renderObject;
                WorldViewProjectionMatrix = Matrix.Identity;
            }

            public void Update(RenderUIElement renderObject, float vFoV, ref Matrix viewMatrix, ref Matrix projMatrix)
            {
                var worldMatrix = renderObject.WorldMatrix;

                // rotate the UI element perpendicular to the camera view vector, if billboard is activated
                if (renderObject.IsFullScreen)
                {
                    Matrix.Scaling(ref renderObject.Component.Entity.Transform.Scale, out Matrix scalar);
                    worldMatrix = ReallyCloseUI * scalar;
                }
                else
                {
                    if (renderObject.IsBillboard || renderObject.IsFullScreen)
                    {
                        Matrix viewInverse;
                        Matrix.Invert(ref viewMatrix, out viewInverse);
                        var diff = worldMatrix.TranslationVector - viewInverse.TranslationVector;

                        if (renderObject.IsBillboard)
                        {
                            switch (renderObject.Component.BillboardType)
                            {
                                case UIComponent.BILLBOARD_TYPE.VIEW:
                                    var viewInverseRow1 = viewInverse.Row1;
                                    var viewInverseRow2 = viewInverse.Row2;

                                    // remove scale of the camera
                                    viewInverseRow1 /= viewInverseRow1.XYZ().Length();
                                    viewInverseRow2 /= viewInverseRow2.XYZ().Length();

                                    // set the scale of the object
                                    viewInverseRow1 *= worldMatrix.Row1.XYZ().Length();
                                    viewInverseRow2 *= worldMatrix.Row2.XYZ().Length();

                                    // set the adjusted world matrix
                                    worldMatrix.Row1 = viewInverseRow1;
                                    worldMatrix.Row2 = viewInverseRow2;
                                    worldMatrix.Row3 = viewInverse.Row3;
                                    break;
                                case UIComponent.BILLBOARD_TYPE.POSITION:
                                    // generate a new matrix that looks at the camera's position
                                    Quaternion look = new Quaternion();
                                    Quaternion.LookAt(ref look, diff);
                                    TransformComponent original = renderObject.Component.Entity.Transform;
                                    worldMatrix = Matrix.Transformation(original.WorldScale(), look, original.WorldPosition());
                                    break;
                            }
                        }

                        if (renderObject.IsFixedSize)
                        {
                            var distVec = diff.Length();

                            worldMatrix.Row1 *= distVec;
                            worldMatrix.Row2 *= distVec;
                            worldMatrix.Row3 *= distVec;
                        }
                    }

                    // If the UI component is not drawn fullscreen it should be drawn as a quad with world sizes corresponding to its actual size
                    worldMatrix = Matrix.Scaling(1f / renderObject.Resolution) * worldMatrix;
                }

                // capture 3D world matrix for picking against things in 3D space
                renderObject.WorldMatrix3D = worldMatrix;

                // Rotation of Pi along 0x to go from UI space to world space
                worldMatrix.Row2 = -worldMatrix.Row2;
                worldMatrix.Row3 = -worldMatrix.Row3;

                Matrix worldViewMatrix;
                Matrix.Multiply(ref worldMatrix, ref viewMatrix, out worldViewMatrix);
                Matrix.Multiply(ref worldViewMatrix, ref projMatrix, out WorldViewProjectionMatrix);
            }

            public void Update(RenderUIElement renderObject, Vector3 virtualResolution)
            {
                var nearPlane = virtualResolution.Z / 2;
                var farPlane = nearPlane + virtualResolution.Z;
                var zOffset = nearPlane + virtualResolution.Z / 2;
                var aspectRatio = virtualResolution.X / virtualResolution.Y;
                var verticalFov = (float)Math.Atan2(virtualResolution.Y / 2, zOffset) * 2;

                Matrix vm = Matrix.LookAtRH(new Vector3(0, 0, zOffset), Vector3.Zero, Vector3.UnitY);
                Matrix pm = Matrix.PerspectiveFovRH(verticalFov, aspectRatio, nearPlane, farPlane);

                Update(renderObject, MathUtil.RadiansToDegrees(verticalFov), ref vm, ref pm);
            }
        }
    }
}
