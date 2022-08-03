// Copyright (c) Xenko contributors (https://xenko.com) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System.Collections.Generic;
using Xenko.Core;
using Xenko.Core.Annotations;
using Xenko.Core.Extensions;
using Xenko.Core.Mathematics;
using Xenko.Core.Threading;
using Xenko.Engine;
using Xenko.Graphics;
using Xenko.Rendering.Materials;
using Xenko.Rendering.Materials.ComputeColors;

namespace Xenko.Rendering
{
    public class ModelRenderProcessor : EntityProcessor<ModelComponent, RenderModel>, IEntityComponentRenderProcessor
    {
        internal static Material fallbackMaterial;

        public List<RenderModel> RenderModels => ComponentDataValues;
        public List<ModelComponent> ModelComponents => ComponentDataKeys;

        public VisibilityGroup VisibilityGroup { get; set; }

        public ModelRenderProcessor() : base(typeof(TransformComponent))
        {
        }

        /// <inheritdoc />
        protected internal override void OnSystemAdd()
        {
            var graphicsDevice = Services.GetSafeServiceAs<IGraphicsDeviceService>().GraphicsDevice;

            fallbackMaterial = Material.New(graphicsDevice, new MaterialDescriptor
            {
                Attributes =
                {
                    Diffuse = new MaterialDiffuseMapFeature(new ComputeTextureColor()),
                    DiffuseModel = new MaterialDiffuseLambertModelFeature(),
                },
            });
        }

        /// <inheritdoc />
        protected override RenderModel GenerateComponentData(Entity entity, ModelComponent component)
        {
            return new RenderModel();
        }

        /// <inheritdoc />
        protected override bool IsAssociatedDataValid(Entity entity, ModelComponent component, RenderModel associatedData)
        {
            return true;
        }

        /// <inheritdoc />
        protected override void OnEntityComponentRemoved(Entity entity, ModelComponent component, RenderModel renderModel)
        {
            // Remove old meshes
            if (renderModel.Meshes != null)
            {
                foreach (var renderMesh in renderModel.Meshes)
                {
                    // Unregister from render system
                    VisibilityGroup.RenderObjects.Remove(renderMesh);
                }
            }
        }

        protected override void OnEntityComponentAdding(Entity entity, [NotNull] ModelComponent component, [NotNull] RenderModel data)
        {
            base.OnEntityComponentAdding(entity, component, data);
            if (component.ForceModelUpdateCount < 1)
                component.ForceModelUpdateCount = 1;
        }

        internal List<int> checkMeshes = new List<int>();
        internal List<int> updateMeshes = new List<int>();

        /// <inheritdoc />
        public override void Draw(RenderContext context)
        {
            checkMeshes.Clear();
            updateMeshes.Clear();
            for (int i=0; i<ComponentDataKeys.Count; i++)
            {
                var modelComponent = ComponentDataKeys[i];
                if (modelComponent == null) continue;
                var renderModel = ComponentDataValues[i];
                if (renderModel == null) continue;

                if (modelComponent.FixedModel == false ||
                    modelComponent.ForceModelUpdateCount > 0 ||
                    modelComponent.Entity.Transform.UpdateImmobilePosition)
                {
                    checkMeshes.Add(i);
                    if (modelComponent.Model != null)
                        updateMeshes.Add(i);
                }
                else if (modelComponent.Entity.Transform.Immobile == IMMOBILITY.FullMotion &&
                         modelComponent.Model != null)
                {
                    updateMeshes.Add(i);
                }
            }

            Dispatcher.For(0, checkMeshes.Count, j =>
            {
                var i = checkMeshes[j];
                var modelComponent = ComponentDataKeys[i];
                var renderModel = ComponentDataValues[i];
                if (modelComponent.ForceModelUpdateCount > 0)
                    modelComponent.ForceModelUpdateCount--;
                CheckMeshes(modelComponent, renderModel);
            });

            Dispatcher.For(0, updateMeshes.Count, j =>
            {
                var i = updateMeshes[j];
                var modelComponent = ComponentDataKeys[i];
                var renderModel = ComponentDataValues[i];
                UpdateRenderModel(modelComponent, renderModel);
            });
        }

        private void UpdateRenderModel(ModelComponent modelComponent, RenderModel renderModel)
        {
            var modelViewHierarchy = modelComponent.Skeleton;
            var nodeTransformations = modelViewHierarchy.NodeTransformations;

            for (int sourceMeshIndex = 0; sourceMeshIndex < renderModel.Materials.Length; sourceMeshIndex++)
            {
                var passes = renderModel.Materials[sourceMeshIndex].MeshCount;
                // Note: indices in RenderModel.Meshes and Model.Meshes are different (due to multipass materials)
                var meshIndex = renderModel.Materials[sourceMeshIndex].MeshStartIndex;

                for (int pass = 0; pass < passes; ++pass, ++meshIndex)
                {
                    var renderMesh = renderModel.Meshes[meshIndex];
                    
                    renderMesh.Enabled = modelComponent.Enabled;
                    renderMesh.RenderGroup = modelComponent.RenderGroup;

                    if (modelComponent.Enabled)
                    {
                        // Copy world matrix
                        var mesh = renderModel.Model.Meshes[sourceMeshIndex];
                        var meshInfo = modelComponent.MeshInfos[sourceMeshIndex];
                        var nodeIndex = mesh.NodeIndex;
                        renderMesh.DistanceSortFudge = modelComponent.DistanceSortFudge;
                        if (modelComponent.SkipCullIfSmall)
                            renderMesh.SmallFactorMultiplier = 0f;
                        else if (renderModel.Model.SmallFactorMultiplierOverride > 0.000001f)
                            renderMesh.SmallFactorMultiplier = renderModel.Model.SmallFactorMultiplierOverride;
                        else if (modelComponent.SmallFactorMultiplier > 0.000001f)
                            renderMesh.SmallFactorMultiplier = modelComponent.SmallFactorMultiplier;
                        else
                            renderMesh.SmallFactorMultiplier = 1f;
                        renderMesh.World = nodeTransformations[nodeIndex].WorldMatrix;
                        renderMesh.IsScalingNegative = nodeTransformations[nodeIndex].IsScalingNegative;
                        renderMesh.BoundingBox.Center = (meshInfo.BoundingBox.Maximum + meshInfo.BoundingBox.Minimum) * 0.5f;
                        renderMesh.BoundingBox.Extent = (meshInfo.BoundingBox.Maximum - meshInfo.BoundingBox.Minimum) * 0.5f;
                        renderMesh.BlendMatrices = meshInfo.BlendMatrices;
                    }
                }
            }
        }

        private void UpdateMaterial(RenderMesh renderMesh, MaterialPass materialPass, ModelComponent modelComponent)
        {
            renderMesh.MaterialPass = materialPass;

            renderMesh.IsShadowCaster = modelComponent.IsShadowCaster;
            renderMesh.TransparentWriteDepth = modelComponent.AlwaysDepthWrite;
        }

        private void CheckMeshes(ModelComponent modelComponent, RenderModel renderModel)
        {
            // Check if model changed
            var model = modelComponent.Model;
            if (renderModel.Model == model)
            {
                // Check if any material pass count changed
                if (model != null)
                {
                    // Number of meshes changed in the model?
                    if (model.Meshes.Count != renderModel.UniqueMeshCount)
                        goto RegenerateMeshes;

                    if (modelComponent.Enabled)
                    {
                        // Check materials
                        for (int sourceMeshIndex = 0; sourceMeshIndex < model.Meshes.Count; sourceMeshIndex++)
                        {
                            ref var material = ref renderModel.Materials[sourceMeshIndex];
                            var materialIndex = model.Meshes[sourceMeshIndex].MaterialIndex;

                            var newMaterial = modelComponent.GetMaterial(materialIndex);

                            // If material changed or its number of pass changed, trigger a full regeneration of RenderMeshes (note: we could do partial later)
                            if ((newMaterial?.Passes.Count ?? 1) != material.MeshCount)
                                goto RegenerateMeshes;

                            // Update materials
                            material.Material = newMaterial;
                            int meshIndex = material.MeshStartIndex;
                            for (int pass = 0; pass < material.MeshCount; ++pass, ++meshIndex)
                            {
                                UpdateMaterial(renderModel.Meshes[meshIndex], newMaterial?.Passes[pass], modelComponent);
                            }
                        }
                    }
                }

                return;
            }

        RegenerateMeshes:
            renderModel.Model = model;

            // Remove old meshes
            if (renderModel.Meshes != null)
            {
                lock (VisibilityGroup.RenderObjects)
                {
                    foreach (var renderMesh in renderModel.Meshes)
                    {
                        // Unregister from render system
                        VisibilityGroup.RenderObjects.Remove(renderMesh);
                    }
                }
            }

            if (model == null)
                return;

            // Count meshes
            var materialMeshCount = 0;
            renderModel.Materials = new RenderModel.MaterialInfo[model.Meshes.Count];
            for (int sourceMeshIndex = 0; sourceMeshIndex < model.Meshes.Count; sourceMeshIndex++)
            {
                var materialIndex = model.Meshes[sourceMeshIndex].MaterialIndex;
                var material = modelComponent.GetMaterial(materialIndex);
                var meshCount = material?.Passes.Count ?? 1;
                renderModel.Materials[sourceMeshIndex] = new RenderModel.MaterialInfo { Material = material, MeshStartIndex = materialMeshCount, MeshCount = meshCount };
                materialMeshCount += meshCount;
            }

            // Create render meshes
            var renderMeshes = new RenderMesh[materialMeshCount];
            for (int sourceMeshIndex = 0; sourceMeshIndex < model.Meshes.Count; sourceMeshIndex++)
            {
                var mesh = model.Meshes[sourceMeshIndex];
                ref var material = ref renderModel.Materials[sourceMeshIndex];
                int meshIndex = material.MeshStartIndex;

                for (int pass = 0; pass < material.MeshCount; ++pass, ++meshIndex)
                {
                    // TODO: Somehow, if material changed we might need to remove/add object in render system again (to evaluate new render stage subscription)
                    var materialIndex = mesh.MaterialIndex;
                    renderMeshes[meshIndex] = new RenderMesh
                    {
                        Source = modelComponent,
                        RenderModel = renderModel,
                        Mesh = mesh,
                    };

                    // Update material
                    UpdateMaterial(renderMeshes[meshIndex], material.Material?.Passes[pass], modelComponent);
                }
            }

            renderModel.Meshes = renderMeshes;
            renderModel.UniqueMeshCount = model.Meshes.Count;

            // Update before first add so that RenderGroup is properly set
            UpdateRenderModel(modelComponent, renderModel);

            // Update and register with render system
            lock (VisibilityGroup.RenderObjects)
            {
                foreach (var renderMesh in renderMeshes)
                {
                    VisibilityGroup.RenderObjects.Add(renderMesh);
                }
            }
        }
    }
}
