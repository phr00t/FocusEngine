// Copyright (c) Xenko contributors (https://xenko.com) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System;
using System.ComponentModel;
using Xenko.Core;
using Xenko.Data;

namespace Xenko.Graphics
{
    //Workaround needed for now, since we don't support orientation changes during game play
    public enum RequiredDisplayOrientation
    {
        /// <summary>
        /// The default value for the orientation.
        /// </summary>
        Default = DisplayOrientation.Default,

        /// <summary>
        /// Displays in landscape mode to the left.
        /// </summary>
        [Display("Landscape Left")]
        LandscapeLeft = DisplayOrientation.LandscapeLeft,

        /// <summary>
        /// Displays in landscape mode to the right.
        /// </summary>
        [Display("Landscape Right")]
        LandscapeRight = DisplayOrientation.LandscapeRight,

        /// <summary>
        /// Displays in portrait mode.
        /// </summary>
        Portrait = DisplayOrientation.Portrait,
    }

    public enum PreferredGraphicsPlatform
    {
        Default,

        /// <summary>
        /// Direct3D11.
        /// </summary>
        Direct3D11,

        /// <summary>
        /// Direct3D12.
        /// </summary>
        Direct3D12,

        /// <summary>
        /// OpenGL.
        /// </summary>
        OpenGL,

        /// <summary>
        /// OpenGL ES.
        /// </summary>
        [Display("OpenGL ES")]
        OpenGLES,

        /// <summary>
        /// Vulkan
        /// </summary>
        Vulkan,
    }

    [DataContract]
    [Display("Core/Rendering")]
    public class RenderingSettings : Configuration
    {
        /// <summary>
        /// Gets or sets the width of the back buffer.
        /// </summary>
        /// <userdoc>
        /// The desired back buffer width.
        /// Might be overriden depending on actual device resolution and/or ratio.
        /// On Windows, it will be the window size. On Android/iOS, it will be the off-screen target resolution.
        /// </userdoc>
        [DataMember(0)]
        public int DefaultBackBufferWidth = 1280;

        /// <summary>
        /// Gets or sets the height of the back buffer.
        /// </summary>
        /// <userdoc>
        /// The desired back buffer height.
        /// Might be overriden depending on actual device resolution and/or ratio.
        /// On Windows, it will be the window size. On Android/iOS, it will be the off-screen target resolution.
        /// </userdoc>
        [DataMember(10)]
        public int DefaultBackBufferHeight = 720;

        /// <summary>
        /// Gets or sets a value that if true will make sure that the aspect ratio of screen is kept.
        /// </summary>
        /// <userdoc>
        /// If true, adapt the ratio of the back buffer so that it fits the screen ratio.
        /// </userdoc>
        [DataMember(15)]
        public bool AdaptBackBufferToScreen = false;

        /// <summary>
        /// Gets or sets the default graphics profile.
        /// </summary>
        /// <userdoc>The graphics feature level this game require.</userdoc>
        [DataMember(20)]
        public GraphicsProfile DefaultGraphicsProfile => GraphicsProfile.Level_11_2;

        /// <summary>
        /// Gets or sets the colorspace.
        /// </summary>
        /// <value>The colorspace.</value>
        /// <userdoc>The colorspace (Gamma or Linear) used for rendering. This value affects both the runtime and editor.</userdoc>
        [DataMember(30)]
        public ColorSpace ColorSpace = ColorSpace.Linear;

        /// <summary>
        /// Gets or sets the display orientation.
        /// </summary>
        /// <userdoc>The display orientations this game support.</userdoc>
        [DataMember(40)]
        public RequiredDisplayOrientation DisplayOrientation = RequiredDisplayOrientation.Default;

        /// <summary>
        /// Are mesh models by default shadow casters? Used for initialization of value only.
        /// </summary>
        [DataMember(50)]
        [DefaultValue(true)]
        public bool DefaultShadowCasters { get; set; } = true;

        /// <summary>
        /// Store vertex buffers for later use (like batching) that are less than this size. Used for initialization of value only.
        /// </summary>
        [DataMember(60)]
        [DefaultValue(32768)]
        public int CaptureVertexBufferOfSize { get; set; } = 32768;

        /// <summary>
        /// Store index buffers for later use (like batching) that are less than this size. Used for initialization of value only.
        /// </summary>
        [DataMember(70)]
        [DefaultValue(8192)]
        public int CaptureIndexBufferOfSize { get; set; } = 8192;

        /// <summary>
        /// How big should the Vulkan upload buffer be in megabytes? It is ring recycled. Bigger buffers may be needed for very frequent large uploads. Defaults to 128.
        /// </summary>
        [DataMember(75)]
        [DefaultValue(128)]
        public int UploadBufferSizeInMB { get; set; } = 128;

        /// <summary>
        /// How big should the Vulkan "Large" StagedMeshBuffer be
        /// </summary>
        [DataMember(76)]
        [DefaultValue(32 * 4096)]
        public int LargePooledBufferSize { get; set; } = 48 * 4096;

        /// <summary>
        /// How big should the Vulkan upload buffer be in megabytes? It is ring recycled. Bigger buffers may be needed for very frequent large uploads. Defaults to 128.
        /// </summary>
        [DataMember(77)]
        [DefaultValue(1024)]
        public int LargedPooledBufferCount { get; set; } = 400;

        /// <summary>
        /// How big should the Vulkan upload buffer be in megabytes? It is ring recycled. Bigger buffers may be needed for very frequent large uploads. Defaults to 128.
        /// </summary>
        [DataMember(78)]
        [DefaultValue(2 * 4096)]
        public int SmallPooledBufferSize { get; set; } = 12 * 4096;

        /// <summary>
        /// How big should the Vulkan upload buffer be in megabytes? It is ring recycled. Bigger buffers may be needed for very frequent large uploads. Defaults to 128.
        /// </summary>
        [DataMember(79)]
        [DefaultValue(4096)]
        public int SmallPooledBufferCount { get; set; } = 5000;

        /// <summary>
        /// Disable generating error log files in My Documents (default location)? Can be configured via ErrorFileLogger class
        /// </summary>
        [DataMember(80)]
        [DefaultValue(false)]
        public bool DisableErrorFileLog { get; set; } = false;

        /// <summary>
        /// Cull small things? 0f (the default) disables this feature. Higher numbers cull more things from being rendered.
        /// </summary>
        [DataMember(90)]
        [DefaultValue(0.002f)]
        public float SmallCullFactor { get; set; } = 0.002f;

        /// <summary>
        /// Cull small things from casting shadows? 0f disables this feature. Higher numbers cull more things from being shadowed.
        /// </summary>
        [DataMember(100)]
        [DefaultValue(0.2f)]
        public float SmallShadowCullFactor { get; set; } = 0.2f;
    }
}
