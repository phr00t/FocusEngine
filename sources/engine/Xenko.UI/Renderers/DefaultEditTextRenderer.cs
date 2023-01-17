// Copyright (c) Xenko contributors (https://xenko.com) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using Xenko.Core.Mathematics;
using Xenko.Graphics;
using Xenko.Graphics.Font;
using Xenko.UI.Controls;
using IServiceRegistry = Xenko.Core.IServiceRegistry;
using Vector3 = Xenko.Core.Mathematics.Vector3;

namespace Xenko.UI.Renderers
{
    /// <summary>
    /// The default renderer for <see cref="EditText"/>.
    /// </summary>
    internal class DefaultEditTextRenderer : ElementRenderer
    {
        public DefaultEditTextRenderer(IServiceRegistry services)
            : base(services)
        {
        }

        private void RenderSelection(EditText editText, UIRenderingContext context, int start, int length, Color color, out float offsetTextStart, out float offsetAlignment, out float selectionSize, UIBatch Batch)
        {
            // calculate the size of the text region by removing padding
            var textRegionSize = new Vector2(editText.ActualWidth - editText.Padding.Left - editText.Padding.Right,
                                                editText.ActualHeight - editText.Padding.Top - editText.Padding.Bottom);

            var font = editText.Font;

            // determine the image to draw in background of the edit text
            var fontScale = editText.LayoutingContext.RealVirtualResolutionRatio;
            var provider = editText.IsSelectionActive ? editText.ActiveImage : editText.MouseOverState == MouseOverState.MouseOverElement ? editText.MouseOverImage : editText.InactiveImage;
            var image = provider?.GetSprite();

            var fontSize = new Vector2(fontScale.Y * editText.ActualTextSize);
            offsetTextStart = font.MeasureString(editText.TextToDisplay, ref fontSize, start).X;
            selectionSize = font.MeasureString(editText.TextToDisplay, ref fontSize, start + length).X - offsetTextStart;
            var lineSpacing = font.GetTotalLineSpacing(editText.ActualTextSize);
            if (font.FontType == SpriteFontType.Dynamic)
            {
                offsetTextStart /= fontScale.X;
                selectionSize /= fontScale.X;
            }

            var scaleRatio = editText.ActualTextSize / font.Size;
            if (font.FontType != SpriteFontType.Dynamic)
            {
                offsetTextStart *= scaleRatio;
                selectionSize *= scaleRatio;
                lineSpacing *= editText.ActualTextSize / font.Size;
            }


            offsetAlignment = -textRegionSize.X / 2f;
            if (editText.TextAlignment != TextAlignment.Left)
            {
                var textWidth = font.MeasureString(editText.TextToDisplay, ref fontSize).X;
                if (font.FontType == SpriteFontType.Dynamic)
                    textWidth /= fontScale.X;
                else textWidth *= scaleRatio;

                offsetAlignment = editText.TextAlignment == TextAlignment.Center ? -textWidth / 2 : -textRegionSize.X / 2f + (textRegionSize.X - textWidth);
            }

            var selectionWorldMatrix = editText.WorldMatrixInternal;
            selectionWorldMatrix.M42 += editText.TextOffset.Y;
            selectionWorldMatrix.M41 += offsetTextStart + selectionSize / 2 + offsetAlignment + editText.TextOffset.X;
            var selectionScaleVector = new Vector3(selectionSize, editText.LineCount * lineSpacing, 0);
            Batch.DrawRectangle(ref selectionWorldMatrix, ref selectionScaleVector, ref color, context.DepthBias + 1);
        }

        public override void RenderColor(UIElement element, UIRenderingContext context, UIBatch Batch)
        {
            base.RenderColor(element, context, Batch);

            var editText = (EditText)element;

            if (editText.Font == null)
                return;
            
            // determine the image to draw in background of the edit text
            var fontScale = element.LayoutingContext.RealVirtualResolutionRatio;
            var color = new Color4(editText.RenderOpacity);
            var provider = editText.IsSelectionActive ? editText.ActiveImage : editText.MouseOverState == MouseOverState.MouseOverElement ? editText.MouseOverImage : editText.InactiveImage;
            var image = provider?.GetSprite();

            if (image?.Texture != null)
            {
                Batch.DrawImage(image.Texture, ref editText.WorldMatrixInternal, ref image.RegionInternal, ref editText.RenderSizeInternal, ref image.BordersInternal, ref color, context.DepthBias, image.Orientation);
            }
            
            // calculate the size of the text region by removing padding
            var textRegionSize = new Vector2(editText.ActualWidth - editText.Padding.Left - editText.Padding.Right,
                                                editText.ActualHeight - editText.Padding.Top - editText.Padding.Bottom);

            var font = editText.Font;
            var caretColor = editText.RenderOpacity * editText.CaretColor;

            var offsetTextStart = 0f;
            var offsetAlignment = 0f;
            var selectionSize = 0f;

            // Draw the composition selection
            if (editText.Composition.Length > 0)
            {
                var imeSelectionColor = editText.RenderOpacity * editText.IMESelectionColor;
                RenderSelection(editText, context, editText.SelectionStart, editText.Composition.Length, imeSelectionColor, out offsetTextStart, out offsetAlignment, out selectionSize, Batch);
            }
            // Draw the regular selection
            else if (editText.IsSelectionActive)
            {
                var selectionColor = editText.RenderOpacity * editText.SelectionColor;
                RenderSelection(editText, context, editText.SelectionStart, editText.SelectionLength, selectionColor, out offsetTextStart, out offsetAlignment, out selectionSize, Batch);
            }

            // create the text draw command
            var drawCommand = new SpriteFont.InternalUIDrawCommand
            {
                Color = editText.RenderOpacity * editText.TextColor,
                DepthBias = context.DepthBias + 2,
                RealVirtualResolutionRatio = fontScale,
                RequestedFontSize = editText.ActualTextSize,
                Batch = Batch,
                SnapText = context.ShouldSnapText && !editText.DoNotSnapText,
                Matrix = editText.WorldMatrixInternal,
                Alignment = editText.TextAlignment,
                TextBoxSize = textRegionSize
            };

            if (editText.Font.FontType == SpriteFontType.SDF)
            {
                Batch.End();
                Batch.BeginCustom(context.GraphicsContext, 1);
            }

            drawCommand.Matrix.M42 += editText.TextOffset.Y;
            drawCommand.Matrix.M41 += editText.TextOffset.X;

            // Draw the text
            Batch.DrawString(font, editText.TextToDisplay, ref drawCommand);

            if (editText.Font.FontType == SpriteFontType.SDF)
            {
                Batch.End();
                Batch.BeginCustom(context.GraphicsContext, 0);
            }

            // Draw the cursor
            if (editText.IsCaretVisible)
            {
                var lineSpacing = editText.Font.GetTotalLineSpacing(editText.ActualTextSize);
                if (editText.Font.FontType != SpriteFontType.Dynamic)
                    lineSpacing *= editText.ActualTextSize / font.Size;

                var caretWorldMatrix = element.WorldMatrixInternal;
                var linespace = editText.LineCount * lineSpacing;
                caretWorldMatrix.M42 += editText.TextOffset.Y + (1f - editText.CaretScale.Y) * linespace * 0.5f;
                caretWorldMatrix.M41 += offsetTextStart + offsetAlignment + (editText.CaretPosition > editText.SelectionStart? selectionSize: 0) + editText.TextOffset.X;
                var caretScaleVector = new Vector3(editText.CaretScale.X, editText.CaretScale.Y * linespace, 0f);
                Batch.DrawRectangle(ref caretWorldMatrix, ref caretScaleVector, ref caretColor, context.DepthBias + 3);
            }
        }
    }
}
