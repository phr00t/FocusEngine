// Copyright (c) Xenko contributors (https://xenko.com) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using Xenko.Core.Mathematics;
using Xenko.Rendering.Materials;

namespace Xenko.Rendering
{
    /// <summary>
    /// How should this mesh use depth testing?
    /// </summary>
    public enum MESH_DEPTH_MODE
    {
        /// <summary>
        /// Default depth testing and writing (not seen behind walls etc.)
        /// </summary>
        Default,

        /// <summary>
        /// Default depth testing and always writing to the depth buffer, even if transparent
        /// </summary>
        DefaultForceWrite,

        /// <summary>
        /// Always draw this, regardless of depth
        /// </summary>
        NoDepthTest,

        /// <summary>
        /// Draw this only behind walls (inverse of default)
        /// </summary>
        ReverseDepthTest
    }

    /// <summary>
    /// Used by <see cref="MeshRenderFeature"/> to render a <see cref="Rendering.Mesh"/>.
    /// </summary>
    public class RenderMesh : RenderObject
    {
        public MeshDraw ActiveMeshDraw;

        public RenderModel RenderModel;

        /// <summary>
        /// Underlying mesh, can be accessed only during <see cref="RenderFeature.Extract"/> phase.
        /// </summary>
        public Mesh Mesh;

        // Material
        private MaterialPass _mpass;

        public MaterialPass MaterialPass
        {
            set
            {
                _mpass = value;
            }
            get
            {
                return _mpass ?? MaterialRenderFeature.fallbackMaterial.Passes[0];
            }
        }

        // TODO GRAPHICS REFACTOR store that in RenderData (StaticObjectNode?)
        internal MaterialRenderFeature.MaterialInfo MaterialInfo;

        public bool IsShadowCaster;

        public bool IsScalingNegative;

        public MESH_DEPTH_MODE DepthMode;

        public bool IsPreviousScalingNegative;

        public Matrix World = Matrix.Identity;

        public Matrix[] BlendMatrices;

        public bool lastTransparency;
    }
}
