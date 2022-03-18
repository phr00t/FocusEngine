// Copyright (c) Xenko contributors (https://xenko.com) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System;
using System.ComponentModel;
using Xenko.Core;
using Xenko.Core.Mathematics;
using Xenko.Graphics;

namespace Xenko.Rendering.Images {
    /// <summary>
    /// A fog filter.
    /// </summary>
    [DataContract("VRFOV")]
    [Display("VR FOV Reduction")]
    public class VRFOV : ImageEffect {
        private readonly ImageEffectShader vrfovFilter;

        /// <summary>
        /// Initializes a new instance of the <see cref="VRFOV"/> class.
        /// </summary>
        public VRFOV()
            : this("VRFOVEffect") {
            Enabled = false;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="VRFOV"/> class.
        /// </summary>
        /// <param name="brightPassShaderName">Name of the bright pass shader.</param>
        public VRFOV(string ShaderName) : base(ShaderName) {
            if (ShaderName == null) throw new ArgumentNullException("vrfovFilterName");
            vrfovFilter = new ImageEffectShader(ShaderName);
        }

        [DataMember(10)]
        public float Intensity { get; set; } = 1f;

        [DataMember(20)]
        public Color3 Color { get; set; } = new Color3(0f, 0f, 0f);

        [DataMember(30)]
        public float Radius { get; set; } = 0.5f;

        [DataMember(40)]
        public float VerticalScale { get; set; } = 1f;

        protected override void InitializeCore() {
            base.InitializeCore();
            ToLoadAndUnload(vrfovFilter);
        }

        /// <summary>
        /// Provides a color buffer and a depth buffer to apply the fog to.
        /// </summary>
        /// <param name="colorBuffer">A color buffer to process.</param>
        /// <param name="depthBuffer">The depth buffer corresponding to the color buffer provided.</param>
        public void SetColorInput(Texture colorBuffer) {
            SetInput(0, colorBuffer);
        }

        protected override void SetDefaultParameters() {
            Color = new Color3(0f, 0f, 0f);
            Intensity = 1f;
            Radius = 0.5f;
            base.SetDefaultParameters();
        }

        protected override void DrawCore(RenderDrawContext context) {
            Texture color = GetInput(0);
            Texture output = GetOutput(0);
            if (color == null || output == null) {
                return;
            }

            vrfovFilter.Parameters.Set(VRFOVEffectKeys.Color, Color);

            // scale these to more useful numbers
            vrfovFilter.Parameters.Set(VRFOVEffectKeys.Radius, Radius * 0.5f);
            vrfovFilter.Parameters.Set(VRFOVEffectKeys.Intensity, Intensity * 100f);
            vrfovFilter.Parameters.Set(VRFOVEffectKeys.VerticalScale, VerticalScale * 0.5f);

            vrfovFilter.SetInput(0, color);
            vrfovFilter.SetOutput(output);
            ((RendererBase)vrfovFilter).Draw(context);
        }
    }
}
