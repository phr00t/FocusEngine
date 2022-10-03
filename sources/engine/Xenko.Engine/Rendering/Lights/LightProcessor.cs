// Copyright (c) Xenko contributors (https://xenko.com) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using Xenko.Core;
using Xenko.Core.Mathematics;
using Xenko.Engine;

namespace Xenko.Rendering.Lights
{
    /// <summary>
    /// Process <see cref="LightComponent"/> stored in an <see cref="EntityManager"/> by providing grouped lights per types/shadows.
    /// </summary>
    public class LightProcessor : EntityProcessor<LightComponent, RenderLight>, IEntityComponentRenderProcessor
    {
        /// <summary>
        /// The default direction of a light vector is (x,y,z) = (0,0,-1)
        /// </summary>
        public static readonly Vector3 DefaultDirection = new Vector3(0, 0, -1);

        private const int DefaultLightCapacityCount = 512;

        /// <summary>
        /// Initializes a new instance of the <see cref="LightProcessor"/> class.
        /// </summary>
        public LightProcessor()
        {
        }

        /// <inheritdoc/>
        public VisibilityGroup VisibilityGroup { get; set; }

        public RenderLight GetRenderLight(LightComponent lightComponent)
        {
            return KeyIndex.TryGetValue(lightComponent, out int index) ? ComponentDataValues[index] : null;
        }

        protected override RenderLight GenerateComponentData(Entity entity, LightComponent component) => new RenderLight();

        protected override bool IsAssociatedDataValid(Entity entity, LightComponent component, RenderLight associatedData) => true;

        protected internal override void OnSystemAdd()
        {
            base.OnSystemAdd();
        }

        protected internal override void OnSystemRemove()
        {
            base.OnSystemRemove();
        }

        public override void Draw(RenderContext context)
        {
            // 1) Clear the cache of current lights (without destroying collections but keeping previously allocated ones)
            if (context.Lights == null)
                context.Lights = new RenderLightCollection(DefaultLightCapacityCount);
            else
                context.Lights.Clear();

            var colorSpace = context.GraphicsDevice.ColorSpace;

            // 2) Prepare lights to be dispatched to the correct light group
            for (int i=0; i<ComponentDataKeys.Count; i++)
            {
                var lightComponent = ComponentDataKeys[i];
                var renderLight = ComponentDataValues[i];

                if (lightComponent.Type == null || !lightComponent.Enabled)
                {
                    continue;
                }

                renderLight.Type = lightComponent.Type;
                renderLight.Intensity = lightComponent.Intensity;
                renderLight.WorldMatrix = lightComponent.Entity.Transform.WorldMatrix;

                // Update info specific to this light type
                if (!renderLight.Type.Update(renderLight))
                {
                    continue;
                }

                // Compute light direction and position
                Vector3 lightDirection;
                var lightDir = DefaultDirection;
                Vector3.TransformNormal(ref lightDir, ref renderLight.WorldMatrix, out lightDirection);
                lightDirection.Normalize();

                renderLight.Position = renderLight.WorldMatrix.TranslationVector;
                renderLight.Direction = lightDirection;

                // Color
                var colorLight = renderLight.Type as IColorLight;
                renderLight.Color = colorLight?.ComputeColor(colorSpace, renderLight.Intensity) ?? new Color3();

                // Compute bounding boxes
                renderLight.UpdateBoundingBox();

                context.Lights.Add(renderLight);
            }
        }
    }
}
