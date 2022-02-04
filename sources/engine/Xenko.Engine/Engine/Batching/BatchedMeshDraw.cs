using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using Xenko.Core.Mathematics;
using Xenko.Core.Threading;
using Xenko.Graphics;
using Xenko.Rendering;
using Xenko.Rendering.Rendering;

namespace Xenko.Engine
{
    /// <summary>
    /// This is like GPU instancing, but handled on the CPU-side to make it easier to implement and use.
    /// It tracks the mesh and moves parts only as needed.
    /// Can be linked with other TransformComponents for easy updating "instanced" copies via each TransformComponent's BatchedMeshDrawReference
    /// Needs one Entity with a ModelComponent to do drawing of all "instances", and you can use GenerateDynamicBatchedModel to provide the Model for dynamic batching
    /// </summary>
    public class BatchedMeshDraw : MeshDraw
    {
        /// <summary>
        /// Set an instance at a certain transform matrix
        /// </summary>
        /// <param name="index">Which one to use?</param>
        /// <param name="transform">WorldTransform</param>
        public void SetTransform(int index, Matrix transform)
        {
            if (internalTransforms[index] != transform)
            {
                internalTransforms[index] = transform;
                MarkUpdateNeeded(index);
            }
        }

        /// <summary>
        /// You can give individual "instances" a UV offset, so copied models use different sections of a texture
        /// </summary>
        /// <param name="index">Which index to offset UVs by?</param>
        /// <param name="offset">How much to offset the UVs?</param>
        public void SetUVOffset(int index, Vector2 offset)
        {
            if (uvOffsets == null) uvOffsets = new Vector2[internalTransforms.Length];

            if (uvOffsets[index] != offset)
            {
                uvOffsets[index] = offset;
                MarkUpdateNeeded(index);
            }
        }

        /// <summary>
        /// You can give individual "instances" colors if the shader is using vertex colors instead of textures
        /// </summary>
        /// <param name="index">Which index to set a custom color?</param>
        /// <param name="offset">What should the custom color be?</param>
        public void SetColor(int index, Color4 color)
        {
            if (colors == null) colors = new Color4[internalTransforms.Length];

            if (colors[index] != color)
            {
                colors[index] = color;
                MarkUpdateNeeded(index);
            }
        }

        /// <summary>
        /// Uses a built-in "zero" matrix to hide this index instance
        /// </summary>
        /// <param name="index">Which one to hide</param>
        public void HideIndex(int index)
        {
            if (internalTransforms[index] != dontdraw)
            {
                internalTransforms[index] = dontdraw;
                MarkUpdateNeeded(index);
            }
        }

        /// <summary>
        /// Reference used on TransformComponent to automatically update as needed
        /// </summary>
        public class BatchedMeshDrawReference
        {
            public BatchedMeshDraw batchedMeshDraw { get; private set; }
            public int index { get; private set; }

            public BatchedMeshDrawReference(BatchedMeshDraw batchedMeshDraw, int index)
            {
                this.index = index;
                this.batchedMeshDraw = batchedMeshDraw;
            }
        }

        /// <summary>
        /// Easy function for making dynamic models. Make sure to do this during initialization and not runtime, as it is a bit slow.
        /// </summary>
        /// <param name="baseModel">What is the model we will be copying?</param>
        /// <param name="capacity">How many do we want to be able to draw at once?</param>
        /// <param name="batchManager">Output used for updating transforms of each copy</param>
        /// <returns></returns>
        public static Model GenerateDynamicBatchedModel(Model baseModel, int capacity, out BatchedMeshDraw batchManager)
        {
            batchManager = new BatchedMeshDraw(baseModel, capacity);
            Model mod = new Xenko.Rendering.Model();
            Xenko.Rendering.Mesh m = new Xenko.Rendering.Mesh(batchManager, new ParameterCollection());
            mod.Add(m);
            mod.Add(baseModel.Materials[0]);
            return mod;
        }

        /// <summary>
        /// Raw creation of a BatchedMeshDraw object. I recommend using GenerateDynamicBatchedModel instead for ease.
        /// Make sure to do this during initialization and not runtime, as it is a bit slow.
        /// </summary>
        /// <param name="m">Base model that will be copied</param>
        /// <param name="capacity">How mnay to prepare and be able to draw at once?</param>
        public BatchedMeshDraw(Model m, int capacity)
        {
            if (m.Materials.Count != 1)
                throw new ArgumentException("Model needs exactly 1 material for proper batching.");

            // calculate what a single mesh looks like and store its original verts
            Model singleMesh = ModelBatcher.BatchModel(m);
            StagedMeshDraw singleDraw = singleMesh.Meshes[0].Draw as StagedMeshDraw;
            if (singleDraw.Verticies is VertexPositionNormalTextureTangent[] vppntt)
                origVerts0 = vppntt;
            else if (singleDraw.Verticies is VertexPositionNormalColor[] vpnc)
                origVerts1 = vpnc;

            internalTransforms = new Matrix[capacity];
            for (int i = 0; i < capacity; i++)
                internalTransforms[i] = dontdraw;

            avoidDuplicates = new ConcurrentHashSet<int>(Xenko.Core.Threading.Dispatcher.MaxDegreeOfParallelism, capacity);
            indexUpdatesRequired = new int[capacity];
            Model batched = ModelBatcher.GenerateBatch(singleMesh, new List<Matrix>(internalTransforms));
            smd = (StagedMeshDraw)batched.Meshes[0].Draw;
            updateVerts = UpdateVertexBuffer;

            singleDraw.Dispose();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void MarkUpdateNeeded(int index)
        {
            if (avoidDuplicates.Add(index))
            {
                tCount = Interlocked.Increment(ref tCount);
                indexUpdatesRequired[tCount] = index;
            }
        }

        internal void UpdateVertexBuffer(CommandList commandList)
        {
            if (smd == null || (smd.VertexBuffers?[0].Buffer?.Ready ?? false) == false) return; // not ready yet
            int tUpdateCount = Interlocked.Exchange(ref tCount, 0);
            avoidDuplicates.Clear();
            if (tUpdateCount == 0) return;
            if (tUpdateCount > indexUpdatesRequired.Length) tUpdateCount = indexUpdatesRequired.Length;
            
            object origVerts = origVerts0 == null ? origVerts1 : origVerts0;
            int len = origVerts0 == null ? origVerts1.Length : origVerts0.Length;

            Xenko.Core.Threading.Dispatcher.For(0, tUpdateCount, (i) =>
            {
                int index = indexUpdatesRequired[i];
                Matrix tMatrix = internalTransforms[index];
                Vector2? uvOffset = uvOffsets == null ? null : uvOffsets[index];
                tMatrix.GetScale(out var tMatrixScale);
                int vPos = index * len;
                int vertsPerThread = (int)Math.Ceiling((float)len / (float)Xenko.Core.Threading.Dispatcher.MaxDegreeOfParallelism);
                if (origVerts is VertexPositionNormalTextureTangent[] ov)
                {
                    Xenko.Core.Threading.Dispatcher.For(0, Xenko.Core.Threading.Dispatcher.MaxDegreeOfParallelism, (t) =>
                    {
                        int start = vPos + t * vertsPerThread;
                        for (int j=start; j<start + vertsPerThread; j++)
                        {
                            if (j >= vPos + len) return;
                            ref VertexPositionNormalTextureTangent ovr = ref ov[j - vPos];
                            Vector3 origPos = ovr.Position;
                            Vector3 origNom = ovr.Normal;
                            ((VertexPositionNormalTextureTangent[])smd.Verticies)[j] = new VertexPositionNormalTextureTangent()
                            {
                                Position = new Vector3((origPos.X * tMatrix.M11) + (origPos.Y * tMatrix.M21) + (origPos.Z * tMatrix.M31) + tMatrix.M41,
                                                       (origPos.X * tMatrix.M12) + (origPos.Y * tMatrix.M22) + (origPos.Z * tMatrix.M32) + tMatrix.M42,
                                                       (origPos.X * tMatrix.M13) + (origPos.Y * tMatrix.M23) + (origPos.Z * tMatrix.M33) + tMatrix.M43),
                                Normal = new Vector3(((origNom.X * tMatrix.M11) + (origNom.Y * tMatrix.M21) + (origNom.Z * tMatrix.M31)) / tMatrixScale.X,
                                                     ((origNom.X * tMatrix.M12) + (origNom.Y * tMatrix.M22) + (origNom.Z * tMatrix.M32)) / tMatrixScale.Y,
                                                     ((origNom.X * tMatrix.M13) + (origNom.Y * tMatrix.M23) + (origNom.Z * tMatrix.M33)) / tMatrixScale.Z),
                                Tangent = ovr.Tangent,
                                TextureCoordinate = uvOffset.HasValue ? ovr.TextureCoordinate + uvOffset.Value : ovr.TextureCoordinate
                            };
                        }
                    });
                }
                else
                {
                    var opnc = origVerts as VertexPositionNormalColor[];
                    Xenko.Core.Threading.Dispatcher.For(0, Xenko.Core.Threading.Dispatcher.MaxDegreeOfParallelism, (t) => {
                        int start = vPos + t * vertsPerThread;
                        for (int j = start; j < start + vertsPerThread; j++) {
                            if (j >= vPos + len) return;
                            ref VertexPositionNormalColor ovr = ref opnc[j - vPos];
                            Vector3 origPos = ovr.Position;
                            Vector3 origNom = ovr.Normal;
                            ((VertexPositionNormalColor[])smd.Verticies)[j] = new VertexPositionNormalColor() {
                                Position = new Vector3((origPos.X * tMatrix.M11) + (origPos.Y * tMatrix.M21) + (origPos.Z * tMatrix.M31) + tMatrix.M41,
                                                       (origPos.X * tMatrix.M12) + (origPos.Y * tMatrix.M22) + (origPos.Z * tMatrix.M32) + tMatrix.M42,
                                                       (origPos.X * tMatrix.M13) + (origPos.Y * tMatrix.M23) + (origPos.Z * tMatrix.M33) + tMatrix.M43),
                                Normal = new Vector3(((origNom.X * tMatrix.M11) + (origNom.Y * tMatrix.M21) + (origNom.Z * tMatrix.M31)) / tMatrixScale.X,
                                                     ((origNom.X * tMatrix.M12) + (origNom.Y * tMatrix.M22) + (origNom.Z * tMatrix.M32)) / tMatrixScale.Y,
                                                     ((origNom.X * tMatrix.M13) + (origNom.Y * tMatrix.M23) + (origNom.Z * tMatrix.M33)) / tMatrixScale.Z),
                                Color = ovr.Color,
                            };
                        }
                    });
                }
            });

            if (origVerts is VertexPositionNormalTextureTangent[])
                smd.VertexBuffers[0].Buffer.SetData<VertexPositionNormalTextureTangent>(commandList, (VertexPositionNormalTextureTangent[])smd.Verticies);
            else
                smd.VertexBuffers[0].Buffer.SetData<VertexPositionNormalColor>(commandList, (VertexPositionNormalColor[])smd.Verticies);
        }

        private static Matrix dontdraw = Matrix.Transformation(Vector3.Zero, Quaternion.Identity, Vector3.Zero);

        public override PrimitiveType PrimitiveType => smd.PrimitiveType;
        public override int DrawCount => smd.DrawCount;
        public override int StartLocation => smd.StartLocation;
        public override VertexBufferBinding[] VertexBuffers => smd.VertexBuffers;
        public override IndexBufferBinding IndexBuffer => smd.IndexBuffer;
        internal override Action<GraphicsDevice, StagedMeshDraw> performStage => smd.performStage;
        internal override StagedMeshDraw getStagedMeshDraw => smd;

        private Matrix[] internalTransforms;
        private StagedMeshDraw smd;
        private int tCount;
        private int[] indexUpdatesRequired;
        private ConcurrentHashSet<int> avoidDuplicates;
        private Vector2[] uvOffsets;
        private Color4[] colors;

        VertexPositionNormalTextureTangent[] origVerts0;
        VertexPositionNormalColor[] origVerts1;
    }
}
