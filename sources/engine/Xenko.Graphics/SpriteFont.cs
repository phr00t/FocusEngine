// Copyright (c) Xenko contributors (https://xenko.com) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

using Xenko.Core;
using Xenko.Core.Diagnostics;
using Xenko.Core.Mathematics;
using Xenko.Core.Serialization;
using Xenko.Core.Serialization.Contents;
using Xenko.Graphics.Font;

using Color = Xenko.Core.Mathematics.Color;
using RectangleF = Xenko.Core.Mathematics.RectangleF;

namespace Xenko.Graphics
{
    /// <summary>
    /// SpriteFont to use with <see cref="SpriteBatch"/>. See <see cref="SpriteFont"/> to learn how to use it.
    /// </summary>
    [DataContract]
    [ReferenceSerializer, DataSerializerGlobal(typeof(ReferenceSerializer<SpriteFont>), Profile = "Content")]
    public class SpriteFont : ComponentBase
    {
        public static readonly Logger Logger = GlobalLogger.GetLogger("SpriteFont");

        // Lookup table indicates which way to move along each axis per SpriteEffects enum value.
        private static readonly Vector2[] AxisDirectionTable =
        {
            new Vector2(-1, -1),
            new Vector2(1, -1),
            new Vector2(-1, 1),
            new Vector2(1, 1),
        };

        // Lookup table indicates which axes are mirrored for each SpriteEffects enum value.
        private static readonly Vector2[] AxisIsMirroredTable =
        {
            new Vector2(0, 0),
            new Vector2(1, 0),
            new Vector2(0, 1),
            new Vector2(1, 1),
        };

        [DataMember(0)]
        internal float BaseOffsetY;

        [DataMember(1)]
        internal float DefaultLineSpacing;

        [DataMember(2)]
        internal Dictionary<int, float> KerningMap;

        /// <summary>
        /// The swizzle mode to use when drawing the sprite font.
        /// </summary>
        protected SwizzleMode swizzle;

        private FontSystem fontSystem;
        private readonly GlyphAction<InternalDrawCommand> internalDrawGlyphAction;
        private readonly GlyphAction<InternalUIDrawCommand> internalUIDrawGlyphAction;
        private readonly GlyphAction<Vector2> measureStringGlyphAction;

        protected internal SpriteFont()
        {
            internalDrawGlyphAction = InternalDrawGlyph;
            internalUIDrawGlyphAction = InternalUIDrawGlyph;
            measureStringGlyphAction = MeasureStringGlyph;
        }

        /// <summary>
        /// Gets the textures containing the font character data.
        /// </summary>
        [DataMemberIgnore]
        public virtual IReadOnlyList<Texture> Textures { get; protected set; }

        /// <summary>
        /// Gets the font size (resp. the default font size) for static fonts (resp. for dynamic fonts) in pixels.
        /// </summary>
        public float Size { get; internal set; }

        /// <summary>
        /// Gets or sets the default character for the font.
        /// </summary>
        public char? DefaultCharacter { get; set; }

        /// <summary>
        /// Completely skips characters that are not in the map.
        /// </summary>
        [DataMemberIgnore]
        public bool IgnoreUnkownCharacters { get; set; }

        /// <summary>
        /// Gets or sets extra spacing (in pixels) between the characters for the current font <see cref="Size"/>. 
        /// This value is scaled during the draw in the case of dynamic fonts. 
        /// Use <see cref="GetExtraSpacing"/> to get the value of the extra spacing for a given font size.
        /// </summary>
        public float ExtraSpacing { get; set; }

        /// <summary>
        /// Gets or sets the extra line spacing (in pixels) to add to the default font line spacing for the current font <see cref="Size"/>.
        /// This value will be scaled during the draw in the case of dynamic fonts.
        /// Use <see cref="GetExtraLineSpacing"/> to get the value of the extra spacing for a given font size.
        /// </summary>
        /// <remarks>Line spacing is the distance between the base lines of two consecutive lines of text (blank space as well as characters' height are thus included).</remarks>
        public float ExtraLineSpacing { get; set; }
        
        /// <summary>
        /// Gets the type indicating if the current font is static, dynamic or SDF.
        /// </summary>
        [DataMemberIgnore]
        public SpriteFontType FontType { get; protected set; }

        /// <summary>
        /// The <see cref="Xenko.Graphics.Font.FontSystem"/> that is managing this sprite font.
        /// </summary>
        [DataMemberIgnore]
        internal virtual FontSystem FontSystem
        {
            get { return fontSystem; }
            set
            {
                if (fontSystem == value)
                    return;

                // unregister itself from the previous font system
                fontSystem?.AllocatedSpriteFonts.Remove(this);

                fontSystem = value;

                // register itself to the new managing font system
                fontSystem?.AllocatedSpriteFonts.Add(this);
            }
        }

        protected override void Destroy()
        {
            base.Destroy();

            // unregister itself from its managing system
            FontSystem.AllocatedSpriteFonts.Remove(this);
        }

        public interface IFontManager
        {
            void New();
        }
        
        /// <summary>
        /// Get the value of the extra line spacing for the given font size.
        /// </summary>
        /// <param name="fontSize">The font size in pixels</param>
        /// <returns>The value of the character spacing</returns>
        public virtual float GetExtraSpacing(float fontSize)
        {
            if (Size == 0f) return ExtraSpacing == 0f ? 1f : ExtraSpacing;
            return fontSize / Size * ExtraSpacing;
        }

        /// <summary>
        /// Get the value of the extra character spacing for the given font size.
        /// </summary>
        /// <param name="fontSize">The font size in pixels</param>
        /// <returns>The value of the character spacing</returns>
        public virtual float GetExtraLineSpacing(float fontSize)
        {
            if (Size == 0f) return ExtraLineSpacing == 0f ? 1f : ExtraLineSpacing;
            return fontSize / Size * ExtraLineSpacing;
        }

        /// <summary>
        /// Get the value of the font default line spacing for the given font size.
        /// </summary>
        /// <param name="fontSize">The font size in pixels</param>
        /// <returns>The value of the default line spacing</returns>
        public virtual float GetFontDefaultLineSpacing(float fontSize)
        {
            if (Size == 0f) return DefaultLineSpacing == 0f ? 1f : DefaultLineSpacing;
            return fontSize / Size * DefaultLineSpacing;
        }

        /// <summary>
        /// Get the value of the base offset for the given font size.
        /// </summary>
        /// <param name="fontSize">The font size in pixels</param>
        /// <returns>The value of the base offset</returns>
        protected virtual float GetBaseOffsetY(float fontSize)
        {
            if (Size == 0f) return BaseOffsetY;
            return fontSize / Size * BaseOffsetY;
        }

        /// <summary>
        /// Gets the value of the total line spacing (font default + user defined) in pixels for a given font size. 
        /// </summary>
        /// <remarks>Line spacing is the distance between the base lines of two consecutive lines of text (blank space as well as characters' height are thus included).</remarks>
        public float GetTotalLineSpacing(float fontSize)
        {
            return GetExtraLineSpacing(fontSize) + GetFontDefaultLineSpacing(fontSize);
        }
        
        internal void InternalDraw(CommandList commandList, ref StringProxy text, ref InternalDrawCommand drawCommand, TextAlignment alignment, TextVerticalAlignment vert_alignment, float lineAdjustment)
        {
            // If the text is mirrored, offset the start position accordingly.
            if (drawCommand.SpriteEffects != SpriteEffects.None)
            {
                drawCommand.Origin -= MeasureString(ref text, ref drawCommand.FontSize, lineAdjustment) * AxisIsMirroredTable[(int)drawCommand.SpriteEffects & 3];
            }

            // Draw each character in turn.
            ForEachGlyph(commandList, ref text, ref drawCommand.FontSize, internalDrawGlyphAction, ref drawCommand, alignment, vert_alignment, true, null, lineAdjustment);
        }        
        
        /// <summary>
        /// Pre-generate synchronously the glyphs of the character needed to render the provided text at the provided size.
        /// </summary>
        /// <param name="text">The text containing the characters to pre-generate</param>
        /// <param name="size">The size of the font</param>
        public void PreGenerateGlyphs(string text, Vector2 size)
        {
            var proxyText = new StringProxy(text);
            PreGenerateGlyphs(ref proxyText, ref size);
        }

        internal virtual void PreGenerateGlyphs(ref StringProxy text, ref Vector2 size)
        {
        }

        internal void InternalDrawGlyph(ref InternalDrawCommand parameters, ref Vector2 fontSize, ref Glyph glyph, float x, float y, float nextx, ref Vector2 auxiliaryScaling)
        {
            if (char.IsWhiteSpace((char)glyph.Character) || glyph.Subrect.Width == 0 || glyph.Subrect.Height == 0)
                return;

            var spriteEffects = parameters.SpriteEffects;

            var offset = new Vector2(x, y + GetBaseOffsetY(fontSize.Y) + glyph.Offset.Y);
            Vector2.Modulate(ref offset, ref AxisDirectionTable[(int)spriteEffects & 3], out offset);
            Vector2.Add(ref offset, ref parameters.Origin, out offset);
            offset.X = (float)Math.Round(offset.X);
            offset.Y = (float)Math.Round(offset.Y);

            if (spriteEffects != SpriteEffects.None)
            {
                // For mirrored characters, specify bottom and/or right instead of top left.
                var glyphRect = new Vector2(glyph.Subrect.Right - glyph.Subrect.Left, glyph.Subrect.Top - glyph.Subrect.Bottom);
                Vector2.Modulate(ref glyphRect, ref AxisIsMirroredTable[(int)spriteEffects & 3], out offset);
            }
            var destination = new RectangleF(parameters.Position.X, parameters.Position.Y, parameters.Scale.X, parameters.Scale.Y);
            RectangleF? sourceRectangle = glyph.Subrect;
            parameters.SpriteBatch.DrawSprite(Textures[glyph.BitmapIndex], ref destination, true, ref sourceRectangle, parameters.Color, new Color4(0, 0, 0, 0),  parameters.Rotation, ref offset, spriteEffects, ImageOrientation.AsIs, parameters.Depth, swizzle, true);            
        }

        internal void InternalUIDraw(CommandList commandList, ref StringProxy text, ref InternalUIDrawCommand drawCommand)
        {
            // We don't want to have letters with non uniform ratio
            var requestedFontSize = new Vector2(drawCommand.RequestedFontSize * drawCommand.RealVirtualResolutionRatio.Y);

            var textBoxSize = drawCommand.TextBoxSize * drawCommand.RealVirtualResolutionRatio;
            ForEachGlyph(commandList, ref text, ref requestedFontSize, internalUIDrawGlyphAction,
                         ref drawCommand, drawCommand.Alignment, drawCommand.VertAlignment, true, textBoxSize, drawCommand.LineSpacingAdjustment);
        }

        internal void InternalUIDrawGlyph(ref InternalUIDrawCommand parameters, ref Vector2 requestedFontSize, ref Glyph glyph, float x, float y, float nextx, ref Vector2 auxiliaryScaling)
        {
            if (char.IsWhiteSpace((char)glyph.Character))
                return;

            var realVirtualResolutionRatio = requestedFontSize / parameters.RequestedFontSize;

            // Skip items with null size
            var elementSize = new Vector2(
                auxiliaryScaling.X * glyph.Subrect.Width / realVirtualResolutionRatio.X,
                auxiliaryScaling.Y * glyph.Subrect.Height / realVirtualResolutionRatio.Y);
            if (elementSize.LengthSquared() < MathUtil.ZeroTolerance) 
                return;

            var xShift = x;
            var yShift = y + (GetBaseOffsetY(requestedFontSize.Y) + glyph.Offset.Y * auxiliaryScaling.Y);
            if (parameters.SnapText)
            {
                xShift = (float)Math.Round(xShift);
                yShift = (float)Math.Round(yShift);
            }
            var xScaledShift = xShift / realVirtualResolutionRatio.X;
            var yScaledShift = yShift / realVirtualResolutionRatio.Y;

            var worldMatrix = parameters.Matrix;

            worldMatrix.M41 += worldMatrix.M11 * xScaledShift + worldMatrix.M21 * yScaledShift;
            worldMatrix.M42 += worldMatrix.M12 * xScaledShift + worldMatrix.M22 * yScaledShift;
            worldMatrix.M43 += worldMatrix.M13 * xScaledShift + worldMatrix.M23 * yScaledShift;
            worldMatrix.M44 += worldMatrix.M14 * xScaledShift + worldMatrix.M24 * yScaledShift;
            
            worldMatrix.M11 *= elementSize.X;
            worldMatrix.M12 *= elementSize.X;
            worldMatrix.M13 *= elementSize.X;
            worldMatrix.M14 *= elementSize.X;
            worldMatrix.M21 *= elementSize.Y;
            worldMatrix.M22 *= elementSize.Y;
            worldMatrix.M23 *= elementSize.Y;
            worldMatrix.M24 *= elementSize.Y;

            RectangleF sourceRectangle = glyph.Subrect;
            parameters.Batch.DrawCharacter(Textures[glyph.BitmapIndex], ref worldMatrix, ref sourceRectangle, ref parameters.Color, parameters.DepthBias, swizzle);
        }

        /// <summary>
        /// Returns the width and height of the provided text for the current font size <see cref="Size"/>
        /// </summary>
        /// <param name="text">The string to measure.</param>
        /// <returns>Vector2.</returns>
        public Vector2 MeasureString(string text)
        {
            var fontSize = new Vector2(Size, Size);
            return MeasureString(text, fontSize, text.Length);
        }

        /// <summary>
        /// Returns the width and height of the provided text for the current font size <see cref="Size"/>
        /// </summary>
        /// <param name="text">The string to measure.</param>
        /// <returns>Vector2.</returns>
        public Vector2 MeasureString(StringBuilder text)
        {
            var fontSize = new Vector2(Size, Size);
            return MeasureString(text, fontSize, text.Length);
        }

        /// <summary>
        /// Returns the width and height of the provided text for a given font size
        /// </summary>
        /// <param name="text">The string to measure.</param>
        /// <param name="fontSize">The size of the font (ignored in the case of static fonts)</param>
        /// <returns>Vector2.</returns>
        public Vector2 MeasureString(string text, float fontSize)
        {
            return MeasureString(text, new Vector2(fontSize, fontSize), text.Length);
        }

        /// <summary>
        /// Returns the width and height of the provided text for a given font size
        /// </summary>
        /// <param name="text">The string to measure.</param>
        /// <param name="fontSize">The size of the font (ignored in the case of static fonts)</param>
        /// <returns>Vector2.</returns>
        public Vector2 MeasureString(StringBuilder text, float fontSize)
        {
            return MeasureString(text, new Vector2(fontSize, fontSize), text.Length);
        }

        /// <summary>
        /// Returns the width and height of the provided text for a given font size
        /// </summary>
        /// <param name="text">The string to measure.</param>
        /// <param name="fontSize">The size of the font (ignored in the case of static fonts)</param>
        /// <returns>Vector2.</returns>
        public Vector2 MeasureString(string text, Vector2 fontSize)
        {
            return MeasureString(text, ref fontSize, text.Length);
        }

        /// <summary>
        /// Returns the width and height of the provided text for a given font size
        /// </summary>
        /// <param name="text">The string to measure.</param>
        /// <param name="fontSize">The size of the font (ignored in the case of static fonts)</param>
        /// <returns>Vector2.</returns>
        public Vector2 MeasureString(StringBuilder text, Vector2 fontSize)
        {
            return MeasureString(text, ref fontSize, text.Length);
        }

        /// <summary>
        /// Returns the width and height of the provided text for a given font size
        /// </summary>
        /// <param name="text">The string to measure.</param>
        /// <param name="fontSize">The size of the font (ignored in the case of static fonts)</param>
        /// <returns>Vector2.</returns>
        public Vector2 MeasureString(string text, ref Vector2 fontSize)
        {
            return MeasureString(text, ref fontSize, text.Length);
        }

        /// <summary>
        /// Returns the width and height of the provided text for a given font size
        /// </summary>
        /// <param name="text">The string to measure.</param>
        /// <param name="fontSize">The size of the font (ignored in the case of static fonts)</param>
        /// <returns>Vector2.</returns>
        public Vector2 MeasureString(StringBuilder text, ref Vector2 fontSize)
        {
            return MeasureString(text, ref fontSize, text.Length);
        }

        /// <summary>
        /// Returns the width and height of the provided text for a given font size
        /// </summary>
        /// <param name="text">The string to measure.</param>
        /// <param name="fontSize">The size of the font (ignored in the case of static fonts)</param>
        /// <param name="length">The length of the string to measure</param>
        /// <returns>Vector2.</returns>
        public Vector2 MeasureString(string text, Vector2 fontSize, int length)
        {
            return MeasureString(text, ref fontSize, length);
        }

        /// <summary>
        /// Returns the width and height of the provided text for a given font size
        /// </summary>
        /// <param name="text">The string to measure.</param>
        /// <param name="fontSize">The size of the font (ignored in the case of static fonts)</param>
        /// <param name="length">The length of the string to measure</param>
        /// <returns>Vector2.</returns>
        public Vector2 MeasureString(StringBuilder text, Vector2 fontSize, int length)
        {
            return MeasureString(text, ref fontSize, length);
        }

        /// <summary>
        /// Returns the width and height of the provided text for a given font size
        /// </summary>
        /// <param name="text">The string to measure.</param>
        /// <param name="fontSize">The size of the font (ignored in the case of static fonts)</param>
        /// <param name="length">The length of the string to measure</param>
        /// <returns>Vector2.</returns>
        public Vector2 MeasureString(string text, ref Vector2 fontSize, int length)
        {
            if (text == null)
                throw new ArgumentNullException(nameof(text));

            var proxy = new StringProxy(text, length);
            return MeasureString(ref proxy, ref fontSize);
        }

        /// <summary>
        /// Returns the width and height of the provided text for a given font size
        /// </summary>
        /// <param name="text">The string to measure.</param>
        /// <param name="fontSize">The size of the font (ignored in the case of static fonts)</param>
        /// <param name="length">The length of the string to measure</param>
        /// <returns>Vector2.</returns>
        public Vector2 MeasureString(StringBuilder text, ref Vector2 fontSize, int length)
        {
            if (text == null)
                throw new ArgumentNullException(nameof(text));

            var proxy = new StringProxy(text, length);
            return MeasureString(ref proxy, ref fontSize);
        }

        internal Vector2 MeasureString(ref StringProxy text, ref Vector2 size, float lineSpaceAdjustment = 0f)
        {
            var result = Vector2.Zero;
            ForEachGlyph(null, ref text, ref size, measureStringGlyphAction, ref result, TextAlignment.Left, TextVerticalAlignment.Top, false, null, GetTotalLineSpacing(size.Y) + lineSpaceAdjustment); // text size is independent from the text alignment
            return result;
        }

        /// <summary>
        /// Checks whether the provided character is present in the character map of the current <see cref="SpriteFont"/>.
        /// </summary>
        /// <param name="c">The character to check.</param>
        /// <returns>true if the <paramref name="c"/> is present in the character map, false - otherwise.</returns>
        public virtual bool IsCharPresent(char c)
        {
            return false;
        }

        /// <summary>
        /// Return the glyph associated to provided character at the given size.
        /// </summary>
        /// <param name="commandList">The command list in case we upload gpu resources</param>
        /// <param name="character">The character we want the glyph of</param>
        /// <param name="fontSize">The font size in pixel</param>
        /// <param name="uploadGpuResources">Indicate if the GPU resource should be uploaded or not.</param>
        /// <param name="auxiliaryScaling">If the requested font size isn't available, the closest one is chosen and an auxiliary scaling is returned</param>
        /// <returns>The glyph corresponding to the request or null if not existing</returns>
        protected virtual Glyph GetGlyph(CommandList commandList, char character, ref Vector2 fontSize, bool uploadGpuResources, out Vector2 auxiliaryScaling)
        {
            auxiliaryScaling = Vector2.One;
            return null;
        }
        
        private void MeasureStringGlyph(ref Vector2 result, ref Vector2 fontSize, ref Glyph glyph, float x, float y, float nextx, ref Vector2 auxiliaryScaling)
        {
            // TODO Do we need auxiliaryScaling
            var h = y + GetTotalLineSpacing(fontSize.Y);
            if (nextx > result.X)
            {
                result.X = nextx;
            }
            if (h > result.Y)
            {
                result.Y = h;
            }
        }

        private delegate void GlyphAction<T>(ref T parameters, ref Vector2 fontSize, ref Glyph glyph, float x, float y, float nextx, ref Vector2 auxiliaryScaling);

        private static int FindCariageReturn(ref StringProxy text, int startIndex)
        {
            var index = startIndex;

            while (index < text.Length && text[index] != '\n')
                ++index;

            return index;
        }

        private void ForEachGlyph<T>(CommandList commandList, ref StringProxy text, ref Vector2 requestedFontSize, GlyphAction<T> action, ref T parameters,
                                     TextAlignment scanOrder, TextVerticalAlignment vertAlign, bool updateGpuResources, Vector2? textBoxSize = null, float lineSpaceAdjustment = 0f)
        {
            float rawYSpacing = GetTotalLineSpacing(requestedFontSize.Y);
            float yStart, ySpacing = rawYSpacing + lineSpaceAdjustment;
            if (textBoxSize.HasValue && vertAlign != TextVerticalAlignment.Top) {
                float lineHeight = requestedFontSize.Y + (text.LineCount - 1) * ySpacing;
                switch (vertAlign) {
                    default:
                    case TextVerticalAlignment.Center:
                        yStart = textBoxSize.Value.Y * 0.5f - (lineHeight * 0.5f);
                        break;
                    case TextVerticalAlignment.Bottom:
                        yStart = textBoxSize.Value.Y - lineHeight;
                        break;
                }
            } else yStart = 0f;

            // prepare color tag stack, if needed
            Stack<Color4> colorstack = null;

            if (scanOrder == TextAlignment.Left)
            {
                // scan the whole text only one time following the text letter order
                ForGlyph(commandList, ref text, ref requestedFontSize, action, ref parameters, 0, text.Length, updateGpuResources, ref colorstack, 0f, yStart, vertAlign, ySpacing);
            }
            else
            {
                // scan the text line by line incrementing y start position

                // measure the whole string in order to be able to determine xStart
                var wholeSize = textBoxSize ?? MeasureString(ref text, ref requestedFontSize);

                // scan the text line by line
                var startIndex = 0;
                var endIndex = FindCariageReturn(ref text, 0);
                while (startIndex < text.Length)
                {
                    // measure the size of the current line
                    var lineSize = Vector2.Zero;
                    ForGlyph(commandList, ref text, ref requestedFontSize, MeasureStringGlyph, ref lineSize, startIndex, endIndex, updateGpuResources, ref colorstack, 0f, 0f, vertAlign, ySpacing, true);

                    // Determine the start position of the line along the x axis
                    // We round this value to the closest integer to force alignment of all characters to the same pixels
                    // Otherwise the starting offset can fall just in between two pixels and due to float imprecision 
                    // some characters can be aligned to the pixel before and others to the pixel after, resulting in gaps and character overlapping
                    var xStart = (scanOrder == TextAlignment.Center) ? (wholeSize.X - lineSize.X) / 2 : wholeSize.X - lineSize.X;
                    xStart = (float)Math.Round(xStart);

                    // scan the line
                    ForGlyph(commandList, ref text, ref requestedFontSize, action, ref parameters, startIndex, endIndex, updateGpuResources, ref colorstack, xStart, yStart, vertAlign, ySpacing);
                    
                    // update variable before going to next line
                    yStart += ySpacing;
                    startIndex = endIndex + 1;
                    endIndex = FindCariageReturn(ref text, startIndex);
                }
            }
        }

        private void ForGlyph<T>(CommandList commandList, ref StringProxy text, ref Vector2 fontSize, GlyphAction<T> action, 
                                 ref T parameters, int forStart, int forEnd, bool updateGpuResources, ref Stack<Color4> colorStack, float startX = 0, float startY = 0,
                                 TextVerticalAlignment vertAlign = TextVerticalAlignment.Top, float fontSizeY = 0f, bool skipTags = false)
        {
            var key = 0;
            var x = startX;
            var y = startY;

            // tag management
            var escaping = false;
            for (var i = forStart; i < forEnd; i++)
            {
                var character = text[i];

                if (!escaping && character == '<') {
                    // check tags?
                    if(CheckAndProcessColorTag(ref text, ref i, out Color4 color)) {
                        if (skipTags == false)
                        {
                            if (colorStack == null) colorStack = new Stack<Color4>();
                            colorStack.Push(color);
                        }
                    } else if(EndsTag("</color>", ref text, ref i)) {
                        if (skipTags == false && colorStack != null && colorStack.Count > 0)
                            colorStack.Pop();
                    }
                }
                else
                {
                    switch (character)
                    {
                        case '\r':
                            // Skip carriage returns.
                            key |= character;
                            continue;

                        case '\n':
                            // New line.
                            x = 0;
                            y += fontSizeY;
                            key |= character;
                            break;

                        case '\\':
                            // Skip next escapable character.
                            if (escaping) goto default;
                            escaping = true;
                            key |= character; //? what is this for
                            break;

                        default:
                            // Output this character.
                            escaping = false;
                            Vector2 auxiliaryScaling;
                            var glyph = GetGlyph(commandList, character, ref fontSize, updateGpuResources, out auxiliaryScaling);
                            if (glyph == null && !IgnoreUnkownCharacters && DefaultCharacter.HasValue)
                                glyph = GetGlyph(commandList, DefaultCharacter.Value, ref fontSize, updateGpuResources, out auxiliaryScaling);
                            if (glyph == null)
                                continue;

                            key |= character;

                            var dx = glyph.Offset.X;

                            float kerningOffset;
                            if (KerningMap != null && KerningMap.TryGetValue(key, out kerningOffset))
                                dx += kerningOffset;

                            float nextX = x + (glyph.XAdvance + GetExtraSpacing(fontSize.X)) * auxiliaryScaling.X;

                            // process color we will use, if any
                            if (colorStack != null && colorStack.Count > 0) {
                                // we have colors, but what kind of command do we have?
                                if (parameters is InternalDrawCommand) {
                                    var idc = (InternalDrawCommand)(object)parameters;
                                    idc.Color = colorStack.Peek();
                                    var idcT = (T)(object)idc;
                                    action(ref idcT, ref fontSize, ref glyph, x + dx * auxiliaryScaling.X, y, nextX, ref auxiliaryScaling);
                                } else if (parameters is InternalUIDrawCommand) {
                                    var idc = (InternalUIDrawCommand)(object)parameters;
                                    idc.Color = Color.FromRgba(colorStack.Peek().ToRgba());
                                    var idcT = (T)(object)idc;
                                    action(ref idcT, ref fontSize, ref glyph, x + dx * auxiliaryScaling.X, y, nextX, ref auxiliaryScaling);
                                } else action(ref parameters, ref fontSize, ref glyph, x + dx * auxiliaryScaling.X, y, nextX, ref auxiliaryScaling);
                            } else action(ref parameters, ref fontSize, ref glyph, x + dx * auxiliaryScaling.X, y, nextX, ref auxiliaryScaling);

                            x = nextX;
                            break;
                    }

                    // Shift the kerning key
                    key = (key << 16);
                }
            }
        }

        private bool HasTag(string tag, ref StringProxy text, ref int pos) {
            if (text.Length - pos <= tag.Length) return false;
            for (int i=0; i<tag.Length;i++) {
                if (text[i + pos + 1] != tag[i]) return false;
            }
            pos += tag.Length + 2;
            return true;
        }

        private bool EndsTag(string tagEnd, ref StringProxy text, ref int pos) {
            if (text.Length - pos < tagEnd.Length) return false;
            for (int i = 0; i < tagEnd.Length; i++) {
                if (text[i + pos] != tagEnd[i]) {
                    return false;
                }
            }
            pos += tagEnd.Length - 1;
            return true;
        }

        private bool CheckAndProcessColorTag(ref StringProxy text, ref int pos, out Color4 color)
        {
            color = Color4.Black;
            if (HasTag("color", ref text, ref pos) == false) return false;
            StringBuilder colorValue = new StringBuilder();
            for (int i = pos; i < text.Length; i++)
            {
                var curChar = text[i];
                if (curChar != '>')
                {
                    colorValue.Append(curChar);
                }
                else
                {
                    if (colorValue.Length > 0)
                    {
                        if (colorValue[0] == '#')
                        {
                            // read as hex
                            if (colorValue.Length == 7)
                            {
                                // rgb
                                if (uint.TryParse(colorValue.Substring(1, 6), NumberStyles.HexNumber, null, out uint rgb))
                                {
                                    color.R = (rgb >> 16 & 0xff) / 255f;
                                    color.G = (rgb >>  8 & 0xff) / 255f;
                                    color.B = (rgb >>  0 & 0xff) / 255f;
                                }
                                else
                                {
                                    return false;
                                }
                            }
                            else if (colorValue.Length == 9)
                            {
                                // rgba
                                if (uint.TryParse(colorValue.Substring(1, 8), NumberStyles.HexNumber, null, out uint rgba))
                                {
                                    color.R = (rgba >> 24 & 0xff) / 255f;
                                    color.G = (rgba >> 16 & 0xff) / 255f;
                                    color.B = (rgba >>  8 & 0xff) / 255f;
                                    color.A = (rgba >>  0 & 0xff) / 255f;
                                }
                                else
                                {
                                    return false;
                                }
                            }
                            else
                            {
                                return false;
                            }
                        }
                        else
                        {
                            // read as comma separated color ints
                            string[] colorValues = colorValue.ToString().Split(',');
                            if (colorValues.Length == 3)
                            {
                                // rgb
                                if (byte.TryParse(colorValues[0], out byte r) &&
                                    byte.TryParse(colorValues[1], out byte g) &&
                                    byte.TryParse(colorValues[2], out byte b))
                                {
                                    color.R = r / 255f;
                                    color.G = g / 255f;
                                    color.B = b / 255f;
                                    color.A = 1f;
                                }
                                else
                                {
                                    return false;
                                }
                            }
                            else if (colorValues.Length == 4)
                            {
                                // rgba
                                if (byte.TryParse(colorValues[0], out byte r) &&
                                    byte.TryParse(colorValues[1], out byte g) &&
                                    byte.TryParse(colorValues[2], out byte b) &&
                                    byte.TryParse(colorValues[3], out byte a))
                                {
                                    color.R = r / 255f;
                                    color.G = g / 255f;
                                    color.B = b / 255f;
                                    color.A = a / 255f;
                                }
                                else
                                {
                                    return false;
                                }
                            }
                            else
                            {
                                return false;
                            }
                        }
                        // TODO: add color names? (what identifier for comma separated colors?)
                        pos = i;
                        return true;
                    }
                    else
                    {
                        return false;
                    }
                }
            }
            return false;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct StringProxy
        {
            private readonly string textString;
            private readonly StringBuilder textBuilder;
            public readonly int Length;
            private int linecount;

            public int LineCount {
                get {
                    if (linecount == -1 && textString != null)
                    {
                        linecount = textString.Length > 0 ? 1 : 0;
                        for (int i = 0; i < textString.Length; i++)
                            if (textString[i] == '\n') linecount++;
                    }
                    return linecount;
                }
            }

            public StringProxy(string text)
            {
                linecount = -1;
                textString = text?.Replace("<br>", "\n");
                textBuilder = null;
                Length = textString.Length;
            }

            public StringProxy(StringBuilder text)
            {
                linecount = -1;
                textBuilder = text?.Replace("<br>", "\n");
                textString = null;
                Length = textBuilder.Length;
            }
            
            public StringProxy(string text, int length)
            {
                linecount = -1;
                textString = text?.Replace("<br>", "\n");
                textBuilder = null;
                Length = Math.Max(0, Math.Min(length, textString.Length));
            }

            public StringProxy(StringBuilder text, int length)
            {
                linecount = -1;
                textBuilder = text?.Replace("<br>", "\n");
                textString = null;
                Length = Math.Max(0, Math.Min(length, textBuilder.Length));
            }

            public bool IsNull => textString == null && textBuilder == null;

            public char this[int index] => textString?[index] ?? textBuilder[index];
        }

        /// <summary>
        /// Structure InternalDrawCommand used to pass parameters to InternalDrawGlyph
        /// </summary>
        internal struct InternalDrawCommand
        {
            public InternalDrawCommand(SpriteBatch spriteBatch, ref Vector2 fontSize, ref Vector2 position, ref Color4 color, float rotation, ref Vector2 origin, ref Vector2 scale, SpriteEffects spriteEffects, float depth)
            {
                SpriteBatch = spriteBatch;
                Position = position;
                Color = color;
                Rotation = rotation;
                Origin = origin;
                Scale = scale;
                SpriteEffects = spriteEffects;
                Depth = depth;
                FontSize = fontSize;
            }

            public Vector2 FontSize;

            public SpriteBatch SpriteBatch;

            public Vector2 Position;

            public Color4 Color;

            public float Rotation;

            public Vector2 Origin;

            public Vector2 Scale;

            public SpriteEffects SpriteEffects;

            public float Depth;
        }

        /// <summary>
        /// Structure InternalDrawCommand used to pass parameters to InternalDrawGlyph
        /// </summary>
        internal struct InternalUIDrawCommand
        {
            /// <summary>
            /// Font size to be used for the draw command, as requested when the command was issued
            /// </summary>
            public float RequestedFontSize;

            /// <summary>
            /// The ratio between the real and virtual resolution (=real/virtual), inherited from the layouting context
            /// </summary>
            public Vector2 RealVirtualResolutionRatio;

            public UIBatch Batch;

            public Matrix Matrix;

            /// <summary>
            /// The size of the rectangle containing the text
            /// </summary>
            public Vector2 TextBoxSize;

            public float LineSpacingAdjustment;

            public Color Color;

            public TextAlignment Alignment;

            public TextVerticalAlignment VertAlignment;

            public int DepthBias;

            public bool SnapText;
        }
    }
}
