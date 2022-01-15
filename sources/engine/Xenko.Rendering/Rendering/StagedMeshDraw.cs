using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Xenko.Core;
using Xenko.Games;
using Xenko.Graphics;

namespace Xenko.Rendering.Rendering {
    public class StagedMeshDraw : MeshDraw, IDisposable {

        public Action<GraphicsDevice, StagedMeshDraw> performStage;

        public uint[] Indicies { get; private set; }
        public object Verticies { get; private set; }

        public static uint Created { get; private set; }
        public static uint Disposed { get; private set; }

        private StagedMeshDraw() { }

        internal Xenko.Graphics.Buffer _vertexBuffer, _indexBuffer;
        internal static GraphicsDevice internalDevice;
        internal static ConcurrentQueue<Graphics.Buffer> StagedBufferTrashBin = new ConcurrentQueue<Graphics.Buffer>();

        ~StagedMeshDraw()
        {
            Dispose();
        }

        public void Dispose()
        {
            performStage = null;

            if (_vertexBuffer != null)
                StagedBufferTrashBin.Enqueue(_vertexBuffer);

            if (_indexBuffer != null)
                StagedBufferTrashBin.Enqueue(_indexBuffer);

            _vertexBuffer = null;
            _indexBuffer = null;
        }

        internal static void FlushTrash()
        {
            while (StagedBufferTrashBin.TryDequeue(out var buf))
            {
                buf.DestroyNow();
                buf.Dispose();
                Disposed++;
            }
        }

        /// <summary>
        /// Frees memory related to StagedMeshDraws in model
        /// </summary>
        /// <param name="model"></param>
        /// <returns>number of StagedMeshDraws disposed</returns>
        public static int Dispose(Model model)
        {
            int disposed = 0;
            for (int i=0; i<model.Meshes.Count; i++)
            {
                Mesh m = model.Meshes[i];
                if (m.Draw is StagedMeshDraw smd)
                {
                    smd.Dispose();
                    m.Draw = null;
                    disposed++;
                }
            }

            return disposed;
        }

        /// <summary>
        /// Gets a MeshDraw that will be prepared when needed with the given index buffer & vertex buffer.
        /// </summary>
        /// <typeparam name="T">Type of vertex buffer used</typeparam>
        /// <param name="indexBuffer">Array of vertex indicies</param>
        /// <param name="vertexBuffer">Vertex buffer</param>
        /// <returns></returns>
        public static StagedMeshDraw MakeStagedMeshDraw<T>(ref uint[] indexBuffer, ref T[] vertexBuffer, VertexDeclaration vertexBufferLayout) where T : struct {

            // sanity checks
            if (indexBuffer.Length == 0 || vertexBuffer.Length == 0)
                throw new ArgumentException("Trying to make a StagedMeshDraw with empty index or vertex buffer!");

            StagedMeshDraw smd = new StagedMeshDraw();
            smd.PrimitiveType = PrimitiveType.TriangleList;
            smd.DrawCount = indexBuffer.Length;
            smd.Indicies = indexBuffer;
            smd.Verticies = vertexBuffer;
            smd.performStage = (GraphicsDevice graphicsDevice, StagedMeshDraw _smd) => {
                lock (_smd)
                {
                    if (_smd.VertexBuffers == null)
                    {
                        _smd._vertexBuffer = Xenko.Graphics.Buffer.Vertex.New<T>(
                            graphicsDevice,
                            (T[])_smd.Verticies,
                            GraphicsResourceUsage.Default
                        );
                        _smd._indexBuffer = Xenko.Graphics.Buffer.Index.New<uint>(
                            graphicsDevice,
                            _smd.Indicies,
                            GraphicsResourceUsage.Default
                        );
                        Created++;
                        VertexBufferBinding[] vbb = new[] {
                            new VertexBufferBinding(_smd._vertexBuffer, vertexBufferLayout, _smd.DrawCount)
                        };
                        IndexBufferBinding ibb = new IndexBufferBinding(_smd._indexBuffer, true, _smd.DrawCount);
                        _smd.VertexBuffers = vbb;
                        _smd.IndexBuffer = ibb;
                        _smd.performStage = null;
                    }
                }
            };
            return smd;
        }
    }
}
