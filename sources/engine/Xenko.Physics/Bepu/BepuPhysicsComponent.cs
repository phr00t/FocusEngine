// Copyright (c) Xenko contributors (https://xenko.com) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using BepuPhysics;
using BepuPhysics.Collidables;
using BepuPhysics.Constraints;
using Xenko.Core;
using Xenko.Core.Annotations;
using Xenko.Core.Collections;
using Xenko.Core.Diagnostics;
using Xenko.Core.Mathematics;
using Xenko.Core.MicroThreading;
using Xenko.Engine.Design;
using Xenko.Extensions;
using Xenko.Games;
using Xenko.Graphics.GeometricPrimitives;
using Xenko.Physics;
using Xenko.Physics.Bepu;
using Xenko.Physics.Engine;
using Xenko.Rendering;
using Xenko.Rendering.Materials;
using Xenko.Rendering.Materials.ComputeColors;

namespace Xenko.Engine
{
    [DataContract("BepuPhysicsComponent", Inherited = true)]
    [Display("BepuPhysics", Expand = ExpandRule.Once)]
    [AllowMultipleComponents]
    [ComponentOrder(3100)]
    public abstract class BepuPhysicsComponent : EntityComponent
    {
        protected static Logger logger = GlobalLogger.GetLogger("BepuPhysicsComponent");

        private static int GlobalStoredShapeIndex;
        private static ConcurrentDictionary<int, IShape> StoredShapes;

        /// <summary>
        /// Allow the physics system to automatically add, based on changes to the entity in the scene?
        /// </summary>
        [DataMember]
        public bool AutomaticAdd { get; set; } = true;

        [DataMemberIgnore]
        public virtual bool AddedToScene { get; set; }

        public virtual int HandleIndex { get => -1; }

        // are we safe to make changes to bodies (e.g. not simulating)
        internal static volatile bool safeRun;

        public BepuPhysicsComponent()
        {
            BepuHelpers.AssureBepuSystemCreated();
        }

        /// <summary>
        /// Gets or sets the collision group.
        /// </summary>
        /// <value>
        /// The collision group.
        /// </value>
        /// <userdoc>
        /// Which collision group the component belongs to. This can't be changed at runtime. The default is DefaultFilter. 
        /// </userdoc>
        /// <remarks>
        /// The collider will still produce events, to allow non trigger rigidbodies or static colliders to act as a trigger if required for certain filtering groups.
        /// </remarks>
        [DataMember(30)]
        [Display("Collision group")]
        [DefaultValue(CollisionFilterGroups.DefaultFilter)]
        public CollisionFilterGroups CollisionGroup { get; set; } = CollisionFilterGroups.DefaultFilter;

        /// <summary>
        /// Gets or sets the can collide with.
        /// </summary>
        /// <value>
        /// The can collide with.
        /// </value>
        /// <userdoc>
        /// Which collider groups this component collides with. With nothing selected, it collides with all groups. This can't be changed at runtime.
        /// </userdoc>
        /// /// <remarks>
        /// The collider will still produce events, to allow non trigger rigidbodies or static colliders to act as a trigger if required for certain filtering groups.
        /// </remarks>
        [DataMember(40)]
        [Display("Collides with...")]
        [DefaultValue(CollisionFilterGroupFlags.AllFilter)]
        public CollisionFilterGroupFlags CanCollideWith { get; set; } = CollisionFilterGroupFlags.AllFilter;

        /// <summary>
        /// Gets or sets the tag.
        /// </summary>
        /// <value>
        /// The tag.
        /// </value>
        [DataMember]
        public string Tag { get; set; }

        /// <summary>
        /// Store what index we are using for BepuHelpers.StoredShapes
        /// </summary>
        [DataMember]
        public int StoredShapeIndex { get; internal set; } = -1;

        /// <summary>
        /// Object useful for storing more information regarding this physics body
        /// </summary>
        [DataMemberIgnore]
        public object UserObject { get; set; }

        [DataMember]
        public float FrictionCoefficient = 0.5f;

        [DataMember]
        public float MaximumRecoveryVelocity = 2f;

        [DataMember]
        public SpringSettings SpringSettings = new SpringSettings(30f, 20f);

        [DataMemberIgnore]
        private IShape internalShape;

        [DataMemberIgnore]
        virtual public IShape ColliderShape
        {
            get
            {
                if (internalShape != null)
                    return internalShape;

                if (StoredShapeIndex >= 0 && StoredShapes.TryGetValue(StoredShapeIndex, out internalShape))
                    return internalShape;

                return null;
            }
            set
            {
                internalShape = value;
            }
        }

        [DataMemberIgnore]
        virtual public TypedIndex ShapeIndex { get; }

        /// <summary>
        /// Stores my current ColliderShape, so if I get cloned, I will use this shape again by default
        /// </summary>
        public void StoreMyShapeForCloning()
        {
            if (ColliderShape == null)
                throw new Exception("This physics component has no ColliderShape to store!");

            if (StoredShapes == null) StoredShapes = new ConcurrentDictionary<int, IShape>();
            int myindex = Interlocked.Increment(ref GlobalStoredShapeIndex);
            StoredShapes[myindex] = ColliderShape;
            StoredShapeIndex = myindex;
        }

        /// <summary>
        /// Removes the shared stored shape from storage
        /// </summary>
        public IShape RemoveStoredShape()
        {
            if (StoredShapeIndex == -1) return null;
            int oldIndex = StoredShapeIndex;
            StoredShapeIndex = -1;
            StoredShapes.TryRemove(oldIndex, out var oldShape);
            return oldShape;
        }

        /// <summary>
        /// Computes the physics transformation from the TransformComponent values
        /// </summary>
        /// <returns></returns>
        internal void DerivePhysicsTransform(ref Matrix fromMatrix, out Matrix outMatrix)
        {
            fromMatrix.Decompose(out Vector3 scale, out Matrix rotation, out Vector3 translation);
            DerivePhysicsTransform(translation, rotation, scale, out outMatrix);
        }

        internal void DerivePhysicsTransform(Vector3? worldPosition, Matrix? worldRotation, Vector3? worldScale, out Matrix outMatrix)
        {
            Vector3 translation = worldPosition ?? Entity.Transform.WorldPosition(), scale;
            Matrix rotation;

            if( worldScale.HasValue ) {
                scale = worldScale.Value;
            } else {
                Entity.Transform.WorldMatrix.GetScale(out scale);
            }

            if (worldRotation.HasValue) {
                rotation = worldRotation.Value;
            } else {
                Entity.Transform.WorldMatrix.GetRotationMatrix(out rotation);
            }

            var translationMatrix = Matrix.Translation(translation);
            Matrix.Multiply(ref rotation, ref translationMatrix, out outMatrix);
        }

        [DataMemberIgnore]
        public virtual Vector3 Position { get; set; }

        [DataMemberIgnore]
        public virtual Quaternion Rotation { get; set; }

        /// <summary>
        /// transfer from one entity to another, preserving transform information
        /// </summary>
        internal override void PrepareForTransfer(Entity toEntity)
        {
            Position = Entity.Transform.WorldPosition(true) - toEntity.Transform.WorldPosition(true);
            Core.Mathematics.Quaternion inverted = toEntity.Transform.WorldRotation();
            inverted.Invert();
            Rotation = Entity.Transform.WorldRotation() * inverted;
        }

        /// <summary>
        /// Is this a "ghost"? Useful for triggers that detect collisions, but don't cause them
        /// </summary>
        [DataMember]
        public bool GhostBody { get; set; }

        /// <summary>
        /// How much ahead-of-time to look for contacts? Defaults to 0.1
        /// </summary>
        [DataMember]
        virtual public float SpeculativeMargin { get; set; } = 0.1f;

        private static Material[] debugShapeMaterial;
        private static Xenko.Rendering.Mesh cubeMesh;

        /// <summary>
        /// Helpful function to quickly generate a colored, visible bounding box over this physics component. This component must be attached to an Entity already if attaching as a child (default behavior).
        /// </summary>
        /// <returns>Child entity generated</returns>
        public Entity GenerateDebugBoundingBox(bool attachAsChild = true)
        {
            System.Numerics.Vector3 min, max;
            if (ColliderShape is IConvexShape ics)
            {
                ics.ComputeBounds(BepuHelpers.ToBepu(Quaternion.Identity), out min, out max);
            }
            else if (ColliderShape is BepuPhysics.Collidables.Mesh cm)
            {
                cm.ComputeBounds(BepuHelpers.ToBepu(Quaternion.Identity), out min, out max);
            }
            else return null;

            Vector3 centerOffset = BepuHelpers.ToXenko(max + min) * 0.5f;

            Game g = ServiceRegistry.instance.GetService<IGame>() as Game;

            if (debugShapeMaterial == null)
            {
                debugShapeMaterial = new Material[3];

                for (int i=0; i<debugShapeMaterial.Length; i++) {
                    var materialDescription = new MaterialDescriptor
                    {
                        Attributes = {
                            DiffuseModel = new MaterialDiffuseLambertModelFeature(),
                            Diffuse = new MaterialDiffuseMapFeature(new ComputeColor { Key = MaterialKeys.DiffuseValue })
                        }
                    };

                    debugShapeMaterial[i] = Material.New(g.GraphicsDevice, materialDescription);
                }

                debugShapeMaterial[0].Passes[0].Parameters.Set(MaterialKeys.DiffuseValue, Color.OrangeRed);
                debugShapeMaterial[1].Passes[0].Parameters.Set(MaterialKeys.DiffuseValue, Color.Blue);
                debugShapeMaterial[2].Passes[0].Parameters.Set(MaterialKeys.DiffuseValue, Color.Pink);

                var meshDraw = GeometricPrimitive.Cube.New(g.GraphicsDevice, Vector3.One).ToMeshDraw();

                cubeMesh = new Rendering.Mesh { Draw = meshDraw };
            }

            Entity e = new Entity((Entity?.Name ?? "unattached") + "-physicsBB");

            Model m = new Model();

            // pick material based on rigidbody type
            if (this is BepuStaticColliderComponent)
                m.Add(debugShapeMaterial[0]);
            else if (this is BepuRigidbodyComponent brc)
                m.Add(debugShapeMaterial[brc.RigidBodyType == RigidBodyTypes.Dynamic ? 1 : 2]);

            m.Meshes.Add(cubeMesh);

            ModelComponent mc = e.GetOrCreate<ModelComponent>();
            mc.Model = m;

            if (attachAsChild)
            {
                e.Transform.Scale = new Vector3(max.X - min.X, max.Y - min.Y, max.Z - min.Z) / Entity.Transform.WorldScale();
                e.Transform.Position = centerOffset / Entity.Transform.WorldScale();
                if (this is BepuRigidbodyComponent rb && rb.LocalPhysicsOffset.HasValue)
                    e.Transform.Position -= rb.LocalPhysicsOffset.Value;
                e.Transform.Parent = Entity.Transform;
            }
            else
            {
                e.Transform.Scale = new Vector3(max.X - min.X, max.Y - min.Y, max.Z - min.Z);
                e.Transform.Position = Position + centerOffset;
                e.Transform.Rotation = Rotation;
            }

            return e;
        }
    }
}
