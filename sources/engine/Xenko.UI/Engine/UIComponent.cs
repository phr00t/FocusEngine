// Copyright (c) Xenko contributors (https://xenko.com) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using Xenko.Core;
using Xenko.Core.Mathematics;
using Xenko.Engine.Design;
using Xenko.Rendering;
using Xenko.Rendering.Sprites;
using Xenko.Rendering.UI;

namespace Xenko.Engine
{
    /// <summary>
    /// Add an <see cref="UIPage"/> to an <see cref="Entity"/>.
    /// </summary>
    [DataContract("UIComponent")]
    [Display("UI", Expand = ExpandRule.Once)]
    [DefaultEntityComponentRenderer(typeof(UIRenderProcessor))]
    [ComponentOrder(9800)]
    [ComponentCategory("UI")]
    public sealed class UIComponent : ActivableEntityComponent
    {
        public enum BILLBOARD_TYPE
        {
            VIEW,
            POSITION
        }

        public static readonly float DefaultDepth = 128f;
        public static readonly float DefaultHeight = 720f;
        public static readonly float DefaultWidth = 1280f;

        public UIComponent()
        {
            Resolution = new Vector3(DefaultWidth, DefaultHeight, DefaultDepth);
        }

        /// <summary>
        /// Gets or sets the UI page.
        /// </summary>
        /// <userdoc>The UI page.</userdoc>
        [DataMember(10)]
        [Display("Page")]
        public UIPage Page { get; set; }

        /// <summary>
        /// Gets or sets the value indicating whether the UI should be full screen.
        /// </summary>
        /// <userdoc>Check this checkbox to display UI of this component on full screen. Uncheck it to display UI using standard camera.</userdoc>
        [DataMember(20)]
        [Display("Full Screen")]
        [DefaultValue(true)]
        public bool IsFullScreen { get; set; } = true;

        private Vector3 internalResolution;

        /// <summary>
        /// Gets or sets the virtual resolution of the UI in virtual pixels.
        /// </summary>
        /// <userdoc>The value in pixels of the resolution of the UI</userdoc>
        [DataMember(30)]
        [Display("Resolution")]
        public Vector3 Resolution
        {
            get => internalResolution;
            set
            {
                internalResolution.Z = 128f;
                internalResolution.X = value.X;
                internalResolution.Y = value.Y;
            }
        }

        /// <summary>
        /// Gets the virtual resolution used after any resizing due to resolution.
        /// </summary>
        [DataMemberIgnore]
        public Vector3 RenderedResolution { get; internal set; }

        /// <summary>
        /// Gets or sets the camera.
        /// </summary>
        /// <value>The camera.</value>
        /// <userdoc>Indicate how the virtual resolution value should be interpreted</userdoc>
        [DataMember(40)]
        [Display("Resolution Stretch")]
        [DefaultValue(ResolutionStretch.AutoFit)]
        public ResolutionStretch ResolutionStretch { get; set; } = ResolutionStretch.AutoFit;

        /// <summary>
        /// Gets or sets the value indicating whether the UI should be displayed as billboard.
        /// </summary>
        /// <userdoc>If checked, the UI is displayed as a billboard. That is, it is automatically rotated parallel to the screen.</userdoc>
        [DataMember(50)]
        [Display("Billboard")]
        [DefaultValue(true)]
        public bool IsBillboard { get; set; } = true;

        /// <summary>
        /// How do we calculate the billboard? Position works better for VR, but perhaps not for non-VR.
        /// </summary>
        [DataMember(55)]
        [DefaultValue(BILLBOARD_TYPE.VIEW)]
        public BILLBOARD_TYPE BillboardType { get; set; } = BILLBOARD_TYPE.VIEW; 

        /// <summary>
        /// Gets or sets the value indicating of the UI texts should be snapped to closest pixel.
        /// </summary>
        /// <userdoc>If checked, all the text of the UI is snapped to the closest pixel (pixel perfect).</userdoc>
        [DataMember(60)]
        [Display("Snap Text")]
        [DefaultValue(true)]
        public bool SnapText { get; set; } = true;

        /// <summary>
        /// Gets or sets the value indicating whether the UI should be always a fixed size on the screen.
        /// </summary>
        /// <userdoc>
        /// Gets or sets the value indicating whether the UI should be always a fixed size on the screen.
        /// A fixed size component with a height of 1 unit will be 0.1 of the screen size.
        /// </userdoc>
        [DataMember(70)]
        [Display("Fixed Size")]
        [DefaultValue(false)]
        public bool IsFixedSize { get; set; } = false;

        /// <summary>
        /// The render group for this component.
        /// </summary>
        [DataMember(80)]
        [Display("Render group")]
        [DefaultValue(RenderGroup.Group0)]
        public RenderGroup RenderGroup { get; set; }

        /// <summary>
        /// Specifies the sampling method to be used for this component
        /// </summary>
        /// <userdoc>Specifies the sampling method to be used for this component</userdoc>
        [DataMember(15)]
        [DefaultValue(UIElementSampler.LinearClamp)]
        [Display("Sampler")]
        public UIElementSampler Sampler { get; set; } = UIElementSampler.LinearClamp;

        /// <summary>
        /// Tweak the sorting distance. Can help with transparent sorting.
        /// </summary>
        [DataMember(90)]
        [Display("Distance Sort Fudge")]
        [DefaultValue(0f)]
        public float DistanceSortFudge { get; set; }

        [DataMember(100)]
        [DefaultValue(RenderSprite.SpriteDepthMode.ReadOnly)]
        public RenderSprite.SpriteDepthMode DepthMode { get; set; } = RenderSprite.SpriteDepthMode.ReadOnly;

        [DataMember(110)]
        [DefaultValue(1f)]
        public float SmallFactorMultiplier { get; set; } = 1f;

        /// <summary>
        /// Track where the cursor is on this component? Works for mouse and VR pointer
        /// </summary>
        [DataMember(120)]
        [DefaultValue(false)]
        public bool AlwaysTrackPointer
        {
            get
            {
                return AveragedPositions != null;
            }
            set
            {
                if (!value)
                    AveragedPositions = null;
                else if (AveragedPositions == null)
                    AveragedPositions = new Vector2[1];
            }
        }

        /// <summary>
        /// How many frames to smooth out pointer tracking? Can be useful in VR to handle pointer shake
        /// </summary>
        [DataMember(130)]
        [DefaultValue(1)]
        public int FramesPointerSmoothing
        {
            get
            {
                return AveragedPositions?.Length ?? 1;
            }
            set
            {
                int minSize = value < 1 ? 1 : value;
                
                if (minSize != FramesPointerSmoothing && AveragedPositions != null)
                    Array.Resize<Vector2>(ref AveragedPositions, minSize);
            }
        }

        /// <summary>
        /// If we are always tracking the pointer on this canvas, how much area should we expand the tracked space?
        /// </summary>
        [DataMember(140)]
        public Vector2 TrackedCanvasScale { get; set; } = Vector2.One;

        /// <summary>
        /// Where is the cursor relative to this UI component?
        /// </summary>
        [DataMemberIgnore]
        public Vector2 TrackedPointerPosition
        {
            get
            {
                if (AveragedPositions == null || AveragedPositions.Length == 0)
                    return Vector2.Zero;

                if (AveragedPositions.Length == 1)
                    return AveragedPositions[0];

                Vector2 result = Vector2.Zero;
                for (int i=0; i<AveragedPositions.Length; i++)
                {
                    result += AveragedPositions[i];
                }

                return result / AveragedPositions.Length;
            }
        }

        internal Vector2[] AveragedPositions;
        internal int AveragePositionIndex;
        internal ulong VRPoseUpdate;
    }
}
