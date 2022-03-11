// Copyright (c) Xenko contributors (https://xenko.com) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System;
using Xenko.Core;
using Xenko.Core.Mathematics;
using Xenko.Graphics;
using Xenko.Rendering.Rendering;

namespace Xenko.Rendering
{
    // Need to add support for fields in auto data converter
    [DataContract]
    public class MeshDraw
    {
        virtual public PrimitiveType PrimitiveType { get; set; }

        virtual public int DrawCount { get; set; }

        virtual public int StartLocation { get; set; }

        virtual public VertexBufferBinding[] VertexBuffers { get; set; }

        virtual public IndexBufferBinding IndexBuffer { get; set; }

        virtual internal Action<GraphicsDevice, StagedMeshDraw> performStage { get; set; }

        virtual internal Action<BoundingFrustum> updateVerts { get; set; }

        virtual internal Action<CommandList> uploadVerts { get; set; }

        virtual internal StagedMeshDraw getStagedMeshDraw { get; set; }
    }
}
