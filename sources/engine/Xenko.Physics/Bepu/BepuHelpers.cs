using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using BepuPhysics.Collidables;
using BepuPhysics.Constraints;
using BepuUtilities.Memory;
using Xenko.Core;
using Xenko.Core.Mathematics;
using Xenko.Core.Threading;
using Xenko.Engine;
using Xenko.Games;
using Xenko.Graphics;
using Xenko.Rendering.Rendering;

namespace Xenko.Physics.Bepu
{
    /// <summary>
    /// Useful class with lots of static functions to do common bepu physics things. Can help making collider shapes and all sorts of goodies here.
    /// </summary>
    public class BepuHelpers
    {
        internal static object creationLocker = new object();
        internal static PhysicsSystem physicsSystem;

        /// <summary>
        /// Good to call this at the start of your application. Will automatically get called in some situations, but not be soon enough.
        /// </summary>
        public static void AssureBepuSystemCreated()
        {
            // do we have the physics system already?
            if (physicsSystem == null)
            {
                // do we have a service registry ready to go?
                if (ServiceRegistry.instance == null) return;

                // is it already added somewhere else?
                physicsSystem = ServiceRegistry.instance.GetService<IPhysicsSystem>() as PhysicsSystem;
                if (physicsSystem == null)
                {
                    // ok, appears we need to make it...
                    lock (creationLocker)
                    {
                        // lock and check so we only make it once...
                        if (physicsSystem == null)
                        {
                            physicsSystem = new PhysicsSystem(ServiceRegistry.instance);
                            ServiceRegistry.instance.AddService<IPhysicsSystem>(physicsSystem);
                            var gameSystems = ServiceRegistry.instance.GetSafeServiceAs<IGameSystemCollection>();
                            gameSystems.Add(physicsSystem);
                            ((IReferencable)physicsSystem).AddReference();
                            physicsSystem.Create(null, PhysicsEngineFlags.None, true);
                        }
                    }
                }
                else
                {
                    // make sure we only do this once too
                    lock (creationLocker)
                    {
                        if (physicsSystem.HasSimulation<BepuSimulation>() == false)
                            physicsSystem.Create(null, PhysicsEngineFlags.None, true);
                    }
                }
            }
        }

        private static Vector3 getBounds(Entity e, out Vector3 center, bool updateWorldMatrix)
        {
            if (updateWorldMatrix) e.Transform.UpdateWorldMatrix();
            ModelComponent mc = e.Get<ModelComponent>();
            center = new Vector3();
            if (mc == null || mc.Model == null || mc.Model.Meshes.Count <= 0f)
                throw new ArgumentException("Entity '" + e.Name + "' has no valid mesh to get sizing from!");

            Vector3 biggest = new Vector3(0.05f, 0.05f, 0.05f);
            int count = mc.Model.Meshes.Count;
            for (int i=0; i<count; i++)
            {
                Xenko.Rendering.Mesh m = mc.Model.Meshes[i];
                BoundingBox bb = m.BoundingBox;
                Vector3 extent = bb.Extent;
                if (extent.X > biggest.X) biggest.X = extent.X;
                if (extent.Y > biggest.Y) biggest.Y = extent.Y;
                if (extent.Z > biggest.Z) biggest.Z = extent.Z;
                center += bb.Center;
            }
            center /= count;
            return biggest * e.Transform.WorldScale();
        }

        /// <summary>
        /// Is this an OK shape? Checks for 0 or negative sizes, or compounds with no children etc...
        /// </summary>
        /// <param name="shape">Shape to check</param>
        /// <returns>true is this shape is sane, false if it has problems</returns>
        public static bool SanityCheckShape(IShape shape)
        {
            if (shape is Box box)
                return box.HalfHeight > 0f && box.HalfLength > 0f && box.HalfWidth > 0f;

            if (shape is Sphere sphere)
                return sphere.Radius > 0f;

            if (shape is Cylinder cylinder)
                return cylinder.Radius > 0f && cylinder.HalfLength > 0f;

            if (shape is Capsule capsule)
                return capsule.HalfLength > 0f && capsule.Radius > 0f;

            if (shape is Triangle triangle)
                return triangle.A != triangle.B && triangle.A != triangle.C && triangle.B != triangle.C;

            if (shape is ICompoundShape compound)
                return compound.ChildCount > 0;

            if (shape is Mesh mesh)
                return mesh.ChildCount > 0;

            return shape != null;
        }

        /// <summary>
        /// Takes a shape and offsets it using compound shapes.
        /// </summary>
        public static IShape OffsetSingleShape(IConvexShape shape, Vector3? offset = null, Quaternion? rotation = null, bool kinematic = false)
        {
            if (offset.HasValue == false && rotation.HasValue == false) return shape;

            if (shape is ICompoundShape) throw new InvalidOperationException("Cannot offset a compound shape. Can't support nested compounds.");

            using (var compoundBuilder = new CompoundBuilder(BepuSimulation.safeBufferPool, BepuSimulation.instance.internalSimulation.Shapes, 1))
            {
                using (BepuSimulation.instance.simulationLocker.WriteLock())
                {
                    if (kinematic)
                    {
                        compoundBuilder.AddForKinematicEasy(shape, new BepuPhysics.RigidPose(ToBepu(offset ?? Vector3.Zero), ToBepu(rotation ?? Quaternion.Identity)), 1f);
                    }
                    else
                    {
                        compoundBuilder.AddEasy(shape, new BepuPhysics.RigidPose(ToBepu(offset ?? Vector3.Zero), ToBepu(rotation ?? Quaternion.Identity)), 1f);
                    }

                    return compoundBuilder.BuildCompleteCompoundShape(BepuSimulation.instance.internalSimulation.Shapes, BepuSimulation.safeBufferPool, kinematic);
                }
            }
        }

        /// <summary>
        /// Generates a box collider shape of an entity. Entity must have a mesh to get sizing from.
        /// </summary>
        public static IShape GenerateBoxOfEntity(Entity e, Vector3? scale = null, bool allowOffsetCompound = true, bool updateWorldMatrix = true)
        {
            Vector3 b = getBounds(e, out Vector3 center, updateWorldMatrix) * 2f;
            if (scale.HasValue) b *= scale.Value;
            var box = new Box(b.X, b.Y, b.Z);
            if (allowOffsetCompound && center.LengthSquared() > 0.01f) return OffsetSingleShape(box, center);
            return box;
        }

        /// <summary>
        /// Generates a physics box shape based on a bounding box
        /// </summary>
        /// <param name="bb">Bounding box</param>
        /// <param name="scale">Should we scale this bounding box by something? Defaults to null (no)</param>
        /// <param name="allowOffsetCompound">Allow us to move the center using a compound shape? Defaults to true (yes)</param>
        /// <returns>A physics box</returns>
        public static IShape GenerateBoxOfBounding(BoundingBox bb, Vector3? scale = null, bool allowOffsetCompound = true)
        {
            if (scale.HasValue)
            {
                bb.Maximum *= scale.Value;
                bb.Minimum *= scale.Value;
            }

            var center = bb.Center;
            var box = new Box(bb.Extent.X * 2f, bb.Extent.Y * 2f, bb.Extent.Z * 2f);
            if (allowOffsetCompound && center.LengthSquared() > 0.01f) return OffsetSingleShape(box, center);
            return box;
        }

        /// <summary>
        /// Generates a sphere collider shape of an entity. Entity must have a mesh to get sizing from.
        /// </summary>
        public static IShape GenerateSphereOfEntity(Entity e, float scale = 1f, bool allowOffsetCompound = true, bool updateWorldMatrix = true) 
        {
            Vector3 b = getBounds(e, out Vector3 center, updateWorldMatrix);
            var box = new Sphere(Math.Max(b.Z, Math.Max(b.X, b.Y)) * scale);
            if (allowOffsetCompound && center.LengthSquared() > 0.01f) return OffsetSingleShape(box, center);
            return box;
        }

        /// <summary>
        /// Generates a capsule collider shape of an entity. Entity must have a mesh to get sizing from.
        /// </summary>
        public static IShape GenerateCapsuleOfEntity(Entity e, Vector3? scale = null, bool XZradius = true, bool allowOffsetCompound = true, bool updateWorldMatrix = true)
        {
            Vector3 b = getBounds(e, out Vector3 center, updateWorldMatrix);
            if (scale.HasValue) b *= scale.Value;
            var box = XZradius ? new Capsule(Math.Max(b.X, b.Z), b.Y * 2f) : new Capsule(b.Y, 2f * Math.Max(b.X, b.Z));
            if (allowOffsetCompound && center.LengthSquared() > 0.01f) return OffsetSingleShape(box, center);
            return box;
        }

        /// <summary>
        /// Generates a cylinder collider shape of an entity. Entity must have a mesh to get sizing from.
        /// </summary>
        public static IShape GenerateCylinderOfEntity(Entity e, Vector3? scale = null, bool XZradius = true, bool allowOffsetCompound = true, bool updateWorldMatrix = true)
        {
            Vector3 b = getBounds(e, out Vector3 center, updateWorldMatrix);
            if (scale.HasValue) b *= scale.Value;
            var box = XZradius ? new Cylinder(Math.Max(b.X, b.Z), b.Y * 2f) : new Cylinder(b.Y, 2f * Math.Max(b.X, b.Z));
            if (allowOffsetCompound && center.LengthSquared() > 0.01f) return OffsetSingleShape(box, center);
            return box;
        }

        /// <summary>
        /// Since you can't have non-convex shapes (e.g. mesh's) in a compound object, this helper will generate a bunch of individual static components to attach to an entity, with each shape.
        /// </summary>
        /// <param name="e">Entity to add static components to</param>
        /// <param name="shapes">shapes that will generate a static component for each</param>
        /// <param name="offsets">optional offset for each</param>
        /// <param name="rotations">optional rotation for each</param>
        public static void GenerateStaticComponents(Entity e, List<IShape> shapes, List<Vector3> offsets = null, List<Quaternion> rotations = null,
                                                    CollisionFilterGroups group = CollisionFilterGroups.DefaultFilter, CollisionFilterGroupFlags collidesWith = CollisionFilterGroupFlags.AllFilter,
                                                    float FrictionCoefficient = 0.5f, float MaximumRecoverableVelocity = 2f, SpringSettings? springSettings = null)
        {
            for (int i=0; i<shapes.Count; i++)
            {
                BepuStaticColliderComponent sc = new BepuStaticColliderComponent();
                sc.ColliderShape = shapes[i];
                if (offsets != null && offsets.Count > i) sc.Position = offsets[i];
                if (rotations != null && rotations.Count > i) sc.Rotation = rotations[i];
                sc.CanCollideWith = collidesWith;
                sc.CollisionGroup = group;
                sc.FrictionCoefficient = FrictionCoefficient;
                sc.MaximumRecoveryVelocity = MaximumRecoverableVelocity;
                if (springSettings.HasValue) sc.SpringSettings = springSettings.Value;
                e.Add(sc);
            }
        }

        /// <summary>
        /// Easily makes a Compound shape for you, given a list of individual shapes and how they should be offset.
        /// </summary>
        /// <param name="shapes">List of convex shapes</param>
        /// <param name="offsets">Matching length list of offsets of bodies, can be null if nothing has an offset</param>
        /// <param name="rotations">Matching length list of rotations of bodies, can be null if nothing is rotated</param>
        /// <param name="isDynamic">True if intended to use in a dynamic situation, false if kinematic or static</param>
        /// <param name="bigThreshold">How many compound shapes before we should use a "big" compound object internally? Defaults to 5, which is usually fine.</param>
        /// <returns>Compound shape</returns>
        public static ICompoundShape MakeCompound(List<IConvexShape> shapes, List<Vector3> offsets = null, List<Quaternion> rotations = null, bool isDynamic = true, int bigThreshold = 5)
        {
            using (var compoundBuilder = new CompoundBuilder(BepuSimulation.safeBufferPool, BepuSimulation.instance.internalSimulation.Shapes, shapes.Count))
            {
                bool allConvex = true;

                //All allocations from the buffer pool used for the final compound shape will be disposed when the demo is disposed. Don't have to worry about leaks in these demos.
                for (int i=0; i<shapes.Count; i++)
                {
                    if (shapes[i] is ICompoundShape) throw new InvalidOperationException("Cannot include compounds in another compound shape.");

                    using (BepuSimulation.instance.simulationLocker.WriteLock())
                    {
                        if (isDynamic)
                        {
                            compoundBuilder.AddEasy(shapes[i] as IConvexShape, new BepuPhysics.RigidPose(ToBepu(offsets?[i] ?? Vector3.Zero), ToBepu(rotations?[i] ?? Quaternion.Identity)), 1f);
                        }
                        else
                        {
                            if (shapes[i] is IConvexShape == false) allConvex = false;

                            compoundBuilder.AddForKinematicEasy(shapes[i], new BepuPhysics.RigidPose(ToBepu(offsets?[i] ?? Vector3.Zero), ToBepu(rotations?[i] ?? Quaternion.Identity)), 1f);
                        }
                    }
                }

                using (BepuSimulation.instance.simulationLocker.WriteLock())
                {
                    return compoundBuilder.BuildCompleteCompoundShape(BepuSimulation.instance.internalSimulation.Shapes, BepuSimulation.safeBufferPool, isDynamic, allConvex ? bigThreshold : int.MaxValue);
                }
            }
        }

        /// <summary>
        /// Goes through the whole scene and adds bepu physics objects to the simulation. Only will add if AllowHelperToAdd is true (which is set to true by default)
        /// and if the body isn't added already.
        /// </summary>
        /// <param name="rootScene"></param>
        public static void SetBodiesInSimulation(Scene rootScene, bool add = true)
        {
            foreach (Entity e in rootScene.Entities)
                SetBodiesInSimulation(e, add);
        }

        /// <summary>
        /// Goes through the entity and children and adds/removes bepu physics objects to the simulation. Only will add/remove if AllowHelperToManage is true (which is set to true by default)
        /// and if the body isn't added already.
        /// </summary>
        /// <param name="rootEntity"></param>
        public static void SetBodiesInSimulation(Entity rootEntity, bool add = true)
        {
            foreach (BepuPhysicsComponent pc in rootEntity.GetAll<BepuPhysicsComponent>())
                if (pc.AutomaticAdd && pc.ColliderShape != null) pc.AddedToScene = add;
            foreach (Entity e in rootEntity.GetChildren())
                SetBodiesInSimulation(e, add);
        }

        /// <summary>
        /// Shortcut to clearing the simulation of all bodies. Optionally clears all the buffers too (e.g. mesh colliders), which is enabled by default
        /// </summary>
        public static void ClearSimulation(bool clearBuffers = true)
        {
            BepuSimulation.instance.Clear(clearBuffers);
        }

        private static unsafe bool getMeshOutputs(Xenko.Rendering.Mesh modelMesh, out List<Vector3> positions, out List<int> indicies)
        {
            if (modelMesh.Draw is StagedMeshDraw)
            {
                StagedMeshDraw smd = modelMesh.Draw as StagedMeshDraw;

                object verts = smd.Verticies;

                if (verts is VertexPositionNormalColor[])
                {
                    VertexPositionNormalColor[] vpnc = verts as VertexPositionNormalColor[];
                    positions = new List<Vector3>(vpnc.Length);
                    for (int k = 0; k < vpnc.Length; k++)
                        positions.Add(vpnc[k].Position);
                }
                else if (verts is VertexPositionNormalTexture[])
                {
                    VertexPositionNormalTexture[] vpnc = verts as VertexPositionNormalTexture[];
                    positions = new List<Vector3>(vpnc.Length);
                    for (int k = 0; k < vpnc.Length; k++)
                        positions.Add(vpnc[k].Position);
                }
                else if (verts is VertexPositionNormalTextureTangent[])
                {
                    VertexPositionNormalTextureTangent[] vpnc = verts as VertexPositionNormalTextureTangent[];
                    positions = new List<Vector3>(vpnc.Length);
                    for (int k = 0; k < vpnc.Length; k++)
                        positions.Add(vpnc[k].Position);
                }
                else
                {
                    throw new ArgumentException("Couldn't get StageMeshDraw mesh, unknown vert type for " + modelMesh.Name);
                }

                // take care of indicies
                indicies = new List<int>(smd.Indicies.Length);
                for (int i = 0; i < smd.Indicies.Length; i++)
                    indicies.Add((int)smd.Indicies[i]);
            }
            else
            {
                Xenko.Graphics.Buffer buf = modelMesh.Draw?.VertexBuffers[0].Buffer;
                Xenko.Graphics.Buffer ibuf = modelMesh.Draw?.IndexBuffer.Buffer;
                if (buf == null || buf.VertIndexData == null ||
                    ibuf == null || ibuf.VertIndexData == null)
                {
                    throw new ArgumentException("Couldn't get mesh for " + modelMesh.Name + ", buffer wasn't stored probably. Try Xenko.Graphics.Buffer.CaptureAllModelBuffers to true.");
                }

                if (ModelBatcher.UnpackRawVertData(buf.VertIndexData, modelMesh.Draw.VertexBuffers[0].Declaration,
                                                   out Vector3[] arraypositions, out Core.Mathematics.Vector3[] normals, out Core.Mathematics.Vector2[] uvs,
                                                   out Color4[] colors, out Vector4[] tangents, modelMesh.Draw.VertexBuffers[0].Offset) == false)
                {
                    throw new ArgumentException("Couldn't unpack mesh for " + modelMesh.Name + ", buffer wasn't stored or data packed weird.");
                }

                // indicies
                fixed (byte* pdst = ibuf.VertIndexData)
                {
                    int numIndices = modelMesh.Draw.IndexBuffer.Count;
                    indicies = new List<int>(numIndices);
                    if (modelMesh.Draw.IndexBuffer.Is32Bit)
                    {
                        var dst = (uint*)(pdst + modelMesh.Draw.IndexBuffer.Offset);
                        for (var k = 0; k < numIndices; k++)
                            indicies.Add((int)dst[k]);
                    }
                    else
                    {
                        var dst = (ushort*)(pdst + modelMesh.Draw.IndexBuffer.Offset);
                        for (var k = 0; k < numIndices; k++)
                            indicies.Add(dst[k]);
                    }
                }

                // take care of positions
                positions = new List<Vector3>(arraypositions);
            }

            return true;
        }

        /// <summary>
        /// Generate a mesh collider from a given mesh. The mesh must have a readable buffer behind it to generate veriticies from
        /// </summary>
        /// <returns>Returns false if no mesh could be made</returns>
        public static unsafe bool GenerateMeshShape(Xenko.Rendering.Mesh modelMesh, out BepuPhysics.Collidables.Mesh outMesh, out BepuUtilities.Memory.BufferPool poolUsed, Vector3? scale = null)
        {
            getMeshOutputs(modelMesh, out var positions, out var indicies);
            return GenerateMeshShape(positions, indicies, out outMesh, out poolUsed, scale);
        }

        /// <summary>
        /// Generate a mesh collider from a given mesh. The mesh must have a readable buffer behind it to generate veriticies from
        /// </summary>
        /// <returns>Returns false if no mesh could be made</returns>
        public static unsafe bool GenerateMeshShape(Xenko.Rendering.Model model, out BepuPhysics.Collidables.Mesh outMesh, out BepuUtilities.Memory.BufferPool poolUsed, Vector3? scale = null)
        {
            List<Vector3> allPositions = new List<Vector3>();
            List<int> allIndicies = new List<int>();
            for (int i = 0; i < model.Meshes.Count; i++)
            {
                getMeshOutputs(model.Meshes[i], out var pos, out var indicies);
                for (int j = 0; j < indicies.Count; j++)
                    allIndicies.Add(indicies[j] + allPositions.Count);
                allPositions.AddRange(pos);
            }

            if (allIndicies.Count == 0 || allPositions.Count == 0)
            {
                outMesh = default;
                poolUsed = null;
                return false;
            }

            return GenerateMeshShape(allPositions, allIndicies, out outMesh, out poolUsed, scale);
        }

        /// <summary>
        /// Generate a mesh collider from all meshes in an entity. The meshes must have a readable buffer behind it to generate veriticies from.
        /// </summary>
        /// <returns>Returns false if no mesh could be made</returns>
        public static unsafe bool GenerateMeshShape(Entity e, out BepuPhysics.Collidables.Mesh outMesh, out BepuUtilities.Memory.BufferPool poolUsed, bool skipAlreadyHasCollider = false)
        {
            // strike out my transform so it isn't included in shape
            Vector3 originalPosition = e.Transform.Position;
            Quaternion originalRotation = e.Transform.Rotation;
            Vector3 originalScale = e.Transform.Scale;
            e.Transform.Position = Vector3.Zero;
            e.Transform.Scale = Vector3.One;
            e.Transform.Rotation = Quaternion.Identity;
            // get all meshes
            List<MeshTransformed> meshes = new List<MeshTransformed>();
            CollectMeshes(e, meshes, skipAlreadyHasCollider);
            // restore e transform
            e.Transform.Position = originalPosition;
            e.Transform.Scale = originalScale;
            e.Transform.Rotation = originalRotation;
            e.Transform.UpdateWorldMatrix(true, false);
            List<Vector3> allPositions = new List<Vector3>();
            List<int> allIndicies = new List<int>();
            for (int i = 0; i < meshes.Count; i++)
            {
                MeshTransformed mt = meshes[i];
                getMeshOutputs(mt.mesh, out var pos, out var indicies);
                for (int j = 0; j < indicies.Count; j++)
                    allIndicies.Add(indicies[j] + allPositions.Count);
                if (mt.matrix.Equals(Matrix.Identity))
                    allPositions.AddRange(pos);
                else
                {
                    for (int j = 0; j < pos.Count; j++)
                        allPositions.Add((Vector3)Vector3.Transform(pos[j], mt.matrix));
                }
            }

            if (allIndicies.Count == 0 || allPositions.Count == 0)
            {
                outMesh = default;
                poolUsed = null;
                return false;
            }

            return GenerateMeshShape(allPositions, allIndicies, out outMesh, out poolUsed, e.Transform.WorldScale());
        }

        private class MeshTransformed
        {
            public Xenko.Rendering.Mesh mesh;
            public Matrix matrix;
        }

        private static void CollectMeshes(Entity e, List<MeshTransformed> meshes, bool skipAlreadyDone)
        {
            // skip stuff that already has a component (so we don't double it up)
            if (skipAlreadyDone && e.Get<BepuStaticColliderComponent>() != null) return;

            // make sure worldmatrix is updated
            e.Transform.UpdateWorldMatrix(true, false);

            foreach(ModelComponent mc in e.GetAll<ModelComponent>())
            {
                if (mc.Model == null) continue;
                Matrix wm = e.Transform.WorldMatrix;
                for (int i=0;i<mc.Model.Meshes.Count; i++)
                {
                    var m = mc.Model.Meshes[i];
                    if (m != null) meshes.Add(new MeshTransformed() { matrix = wm, mesh = m });
                }
            }
            foreach (Entity child in e.GetChildren())
                CollectMeshes(child, meshes, skipAlreadyDone);
        }

        /// <summary>
        /// Scans all BepuStaticColliderComponents in entity (and children) and repositions them to the Entity's transform world position.
        /// </summary>
        /// <param name="e"></param>
        public static void RepositionAllStatics(Entity e)
        {
            foreach (BepuStaticColliderComponent scc in e.GetAll<BepuStaticColliderComponent>())
                scc.UpdatePhysicalTransform();
            foreach (Entity child in e.GetChildren())
                RepositionAllStatics(child);
        }

        /// <summary>
        /// Frees up memory used by mesh colliders in entity
        /// </summary>
        /// <param name="e"></param>
        public static void DisposeAllMeshes(Entity e)
        {
            foreach (BepuStaticColliderComponent scc in e.GetAll<BepuStaticColliderComponent>())
                scc.DisposeMesh();
            foreach (Entity child in e.GetChildren())
                DisposeAllMeshes(child);
        }

        /// <summary>
        /// Adds or removes all colliders in all entities from e
        /// </summary>
        public static void SetAllColliders(Entity e, bool enabled)
        {
            foreach (BepuPhysicsComponent pc in e.GetAll<BepuPhysicsComponent>())
                pc.AddedToScene = enabled;
            foreach (Entity child in e.GetChildren())
                SetAllColliders(child, enabled);
        }

        /// <summary>
        /// Generate a mesh collider from all meshes in an entity. The meshes must have a readable buffer behind it to generate veriticies from
        /// </summary>
        /// <returns>Returns false if no mesh could be made</returns>
        public static unsafe bool GenerateBigMeshStaticColliders(Entity e, CollisionFilterGroups group = CollisionFilterGroups.DefaultFilter, CollisionFilterGroupFlags collidesWith = CollisionFilterGroupFlags.AllFilter,
                                                                 float friction = 0.5f, float maximumRecoverableVelocity = 1f, SpringSettings? springSettings = null, bool disposeOnDetach = false, bool skipAlreadyHasCollider = true)
        {
            // strike out my transform so it isn't included in shape
            Vector3 originalPosition = e.Transform.Position;
            Quaternion originalRotation = e.Transform.Rotation;
            Vector3 originalScale = e.Transform.Scale;
            e.Transform.Position = Vector3.Zero;
            e.Transform.Scale = Vector3.One;
            e.Transform.Rotation = Quaternion.Identity;
            // get all meshes
            List<MeshTransformed> meshes = new List<MeshTransformed>();
            CollectMeshes(e, meshes, skipAlreadyHasCollider);
            // restore e transform
            e.Transform.Position = originalPosition;
            e.Transform.Scale = originalScale;
            e.Transform.Rotation = originalRotation;
            e.Transform.UpdateWorldMatrix(true, false);
            List<Vector3> allPositions = new List<Vector3>();
            List<int> allIndicies = new List<int>();
            for (int i=0; i<meshes.Count; i++)
            {
                MeshTransformed mt = meshes[i];
                getMeshOutputs(mt.mesh, out var pos, out var indicies);
                for (int j=0; j<indicies.Count; j++)
                    allIndicies.Add(indicies[j] + allPositions.Count);
                if (mt.matrix.Equals(Matrix.Identity))
                    allPositions.AddRange(pos);
                else
                {
                    for (int j = 0; j < pos.Count; j++)
                        allPositions.Add((Vector3)Vector3.Transform(pos[j], mt.matrix));
                }
            }

            if (allIndicies.Count == 0 || allPositions.Count == 0) return false;

            GenerateBigMeshStaticColliders(e, allPositions, allIndicies, e.Transform.WorldScale(), group, collidesWith, friction, maximumRecoverableVelocity, springSettings, disposeOnDetach);

            return true;
        }

        /// <summary>
        /// Generate a big mesh collider from a given mesh using multithreading. The mesh must have a readable buffer behind it to generate veriticies from
        /// </summary>
        /// <returns>Returns false if no mesh could be made</returns>
        public static unsafe bool GenerateBigMeshStaticColliders(Entity e, Xenko.Rendering.Mesh modelMesh, Vector3? scale = null,
                                                                 CollisionFilterGroups group = CollisionFilterGroups.DefaultFilter, CollisionFilterGroupFlags collidesWith = CollisionFilterGroupFlags.AllFilter,
                                                                 float friction = 0.5f, float maximumRecoverableVelocity = 1f, SpringSettings? springSettings = null, bool disposeOnDetach = false)
        {
            getMeshOutputs(modelMesh, out var positions, out var indicies);
            GenerateBigMeshStaticColliders(e, positions, indicies, scale, group, collidesWith, friction, maximumRecoverableVelocity, springSettings, disposeOnDetach);
            return true;
        }

        /// <summary>
        /// Generate a mesh collider from a given mesh. The mesh must have a readable buffer behind it to generate veriticies from
        /// </summary>
        /// <returns>Returns false if no mesh could be made</returns>
        public static unsafe bool GenerateMeshShape(List<Vector3> positions, List<int> indicies, out BepuPhysics.Collidables.Mesh outMesh, out BepuUtilities.Memory.BufferPool poolUsed, Vector3? scale = null)
        {
            poolUsed = BepuSimulation.safeBufferPool;

            // ok, should have what we need to make triangles
            int triangleCount = indicies.Count / 3;

            BepuUtilities.Memory.Buffer<Triangle> triangles;
            lock (poolUsed)
            {
                poolUsed.Take<Triangle>(triangleCount, out triangles);
            }

            Xenko.Core.Threading.Dispatcher.For(0, triangleCount, (i) =>
            {
                int shiftedi = i * 3;
                triangles[i].A = ToBepu(positions[indicies[shiftedi]]);
                triangles[i].B = ToBepu(positions[indicies[shiftedi + 1]]);
                triangles[i].C = ToBepu(positions[indicies[shiftedi + 2]]);
            });

            lock (poolUsed)
            {
                outMesh = new Mesh(triangles, new System.Numerics.Vector3(scale?.X ?? 1f, scale?.Y ?? 1f, scale?.Z ?? 1f), poolUsed);
            }

            return true;
        }

        /// <summary>
        /// Generate a big mesh collider from a given mesh using multithreading. The mesh must have a readable buffer behind it to generate veriticies from
        /// </summary>
        /// <returns>Returns false if no mesh could be made</returns>
        public static unsafe void GenerateBigMeshStaticColliders(Entity e, List<Vector3> positions, List<int> indicies, Vector3? scale = null,
                                                                 CollisionFilterGroups group = CollisionFilterGroups.DefaultFilter, CollisionFilterGroupFlags collidesWith = CollisionFilterGroupFlags.AllFilter,
                                                                 float friction = 0.5f, float maximumRecoverableVelocity = 1f, SpringSettings? springSettings = null, bool disposeOnDetach = false)
        {
            // ok, should have what we need to make triangles
            int triangleCount = indicies.Count / 3;

            if (triangleCount < Xenko.Core.Threading.Dispatcher.MaxDegreeOfParallelism * 2f)
            {
                // if we have a really small mesh, doesn't make sense to split it up a bunch
                var scc = new BepuStaticColliderComponent();
                scc.CanCollideWith = collidesWith;
                scc.CollisionGroup = group;
                scc.FrictionCoefficient = friction;
                scc.DisposeMeshOnDetach = disposeOnDetach;
                scc.MaximumRecoveryVelocity = maximumRecoverableVelocity;
                if (springSettings.HasValue) scc.SpringSettings = springSettings.Value;
                GenerateMeshShape(positions, indicies, out var outMesh, out scc.PoolUsedForMesh, scale);
                scc.ColliderShape = outMesh;
                e.Add(scc);
            }
            else
            {
                int trianglesPerThread = 1 + (triangleCount / Xenko.Core.Threading.Dispatcher.MaxDegreeOfParallelism);
                BepuStaticColliderComponent[] scs = new BepuStaticColliderComponent[Xenko.Core.Threading.Dispatcher.MaxDegreeOfParallelism];
                var scalev3 = new System.Numerics.Vector3(scale?.X ?? 1f, scale?.Y ?? 1f, scale?.Z ?? 1f);
                Xenko.Core.Threading.Dispatcher.For(0, scs.Length, (index) =>
                {
                    var pool = BepuSimulation.safeBufferPool;
                    int triangleStart = index * trianglesPerThread;
                    int triangleEnd = Math.Min(triangleStart + trianglesPerThread, triangleCount);
                    int triangleLen = triangleEnd - triangleStart;
                    if (triangleLen <= 0) return;
                    BepuUtilities.Memory.Buffer<Triangle> buf;
                    lock (pool)
                    {
                        pool.Take<Triangle>(triangleLen, out buf);
                    }
                    int pos = 0;
                    for (int i=triangleStart; i<triangleEnd; i++)
                    {
                        int shiftedi = i * 3;
                        buf[pos].A = ToBepu(positions[indicies[shiftedi]]);
                        buf[pos].B = ToBepu(positions[indicies[shiftedi + 1]]);
                        buf[pos].C = ToBepu(positions[indicies[shiftedi + 2]]);
                        pos++;
                    }
                    scs[index] = new BepuStaticColliderComponent();
                    ref var sc = ref scs[index];
                    sc.PoolUsedForMesh = pool;
                    sc.CanCollideWith = collidesWith;
                    sc.CollisionGroup = group;
                    sc.FrictionCoefficient = friction;
                    sc.DisposeMeshOnDetach = disposeOnDetach;
                    sc.MaximumRecoveryVelocity = maximumRecoverableVelocity;
                    if (springSettings.HasValue) sc.SpringSettings = springSettings.Value;

                    lock (sc.PoolUsedForMesh)
                    {
                        sc.ColliderShape = new Mesh(buf, scalev3, sc.PoolUsedForMesh);
                    }
                });

                for (int i = 0; i < scs.Length; i++)
                    if (scs[i] != null) e.Add(scs[i]);
            }
        }

        /// <summary>
        /// Helper function to convert from Focus Engine's Vector3 to Bepu Vector3
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe System.Numerics.Vector3 ToBepu(Xenko.Core.Mathematics.Vector3 v)
        {
            return *((System.Numerics.Vector3*)(void*)&v);
        }

        /// <summary>
        /// Helper function to convert from Bepu Vector3 to Focus Engine's Vector3
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe Xenko.Core.Mathematics.Vector3 ToXenko(System.Numerics.Vector3 v)
        {
            return *((Xenko.Core.Mathematics.Vector3*)(void*)&v);
        }

        /// <summary>
        /// Helper function to convert from Bepu Vector3 to Focus Engine's Quaternion
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe Xenko.Core.Mathematics.Quaternion ToXenko(System.Numerics.Quaternion q)
        {
            return *((Xenko.Core.Mathematics.Quaternion*)(void*)&q);
        }

        /// <summary>
        /// Helper function to convert from Focus Engine's Vector3 to Bepu Quaternion
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe System.Numerics.Quaternion ToBepu(Xenko.Core.Mathematics.Quaternion q)
        {
            return *((System.Numerics.Quaternion*)(void*)&q);
        }
    }
}
