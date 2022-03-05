// Copyright (c) Xenko contributors (https://xenko.com) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using Xenko.Core;
using Xenko.Core.Mathematics;
using Xenko.Graphics;
using Xenko.UI.Controls;

namespace Xenko.UI.Renderers
{
    /// <summary>
    /// The default renderer for <see cref="Button"/>.
    /// </summary>
    internal class DefaultButtonRenderer : ElementRenderer
    {
        public DefaultButtonRenderer(IServiceRegistry services)
            : base(services)
        {
        }

        public override void RenderColor(UIElement element, UIRenderingContext context, UIBatch Batch)
        {
            base.RenderColor(element, context, Batch);

            var button = (Button)element;
            var sprite = button.ButtonImage;
            if (sprite?.Texture == null)
                return;

            var color = new Color4(button.ButtonTint.R, button.ButtonTint.G, button.ButtonTint.B, button.RenderOpacity);
            Batch.DrawImage(sprite.Texture, ref element.WorldMatrixInternal, ref sprite.RegionInternal, ref element.RenderSizeInternal, ref sprite.BordersInternal, ref color, context.DepthBias, sprite.Orientation);
        }
    }
}
