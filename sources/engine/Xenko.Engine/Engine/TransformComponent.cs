// Copyright (c) Xenko contributors (https://xenko.com) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System;
using System.Collections.Specialized;
using System.ComponentModel;
using Xenko.Core;
using Xenko.Core.Collections;
using Xenko.Core.Mathematics;
using Xenko.Core.Serialization;
using Xenko.Engine.Design;
using Xenko.Engine.Processors;
using Xenko.VirtualReality;

namespace Xenko.Engine
{
    public enum IMMOBILITY
    {
        FullMotion = 0,
        EverythingImmobile = 1,
        JustMeImmobile = 2
    }

    public enum HIERARCHY_MODE
    {
        Normal = 0,
        IgnoreFinalRotation = 1,
        IgnoreAllRotation = 2
    }

    /// <summary>
    /// Defines Position, Rotation and Scale of its <see cref="Entity"/>.
    /// </summary>
    [DataContract("TransformComponent")]
    [DataSerializerGlobal(null, typeof(FastCollection<TransformComponent>))]
    [DefaultEntityComponentProcessor(typeof(TransformProcessor))]
    [Display("Transform", Expand = ExpandRule.Once)]
    [ComponentOrder(0)]
    public sealed class TransformComponent : EntityComponent //, IEnumerable<TransformComponent> Check why this is not working
    {
        private static readonly TransformOperation[] EmptyTransformOperations = new TransformOperation[0];

        // When false, transformation should be computed in TransformProcessor (no dependencies).
        // When true, transformation is computed later by another system.
        // This is useful for scenario such as binding a node to a bone, where it first need to run TransformProcessor for the hierarchy,
        // run MeshProcessor to update ModelViewHierarchy, copy Node/Bone transformation to another Entity with special root and then update its children transformations.
        private bool useTRS = true;
        private TransformComponent parent;

        private readonly TransformChildrenCollection children;

        internal bool IsMovingInsideRootScene;

        /// <summary>
        /// This is where we can register some custom work to be done after world matrix has been computed, such as updating model node hierarchy or physics for local node.
        /// </summary>
        [DataMemberIgnore]
        public FastListStruct<TransformOperation> PostOperations = new FastListStruct<TransformOperation>(EmptyTransformOperations);

        /// <summary>
        /// The world matrix.
        /// Its value is automatically recomputed at each frame from the local and the parent matrices.
        /// One can use <see cref="UpdateWorldMatrix"/> to force the update to happen before next frame.
        /// </summary>
        /// <remarks>The setter should not be used and is accessible only for performance purposes.</remarks>
        [DataMemberIgnore]
        public Matrix WorldMatrix = Matrix.Identity;

        /// <summary>
        /// The local matrix.
        /// Its value is automatically recomputed at each frame from the position, rotation and scale.
        /// One can use <see cref="UpdateLocalMatrix"/> to force the update to happen before next frame.
        /// </summary>
        /// <remarks>The setter should not be used and is accessible only for performance purposes.</remarks>
        [DataMemberIgnore]
        public Matrix LocalMatrix = Matrix.Identity;

        /// <summary>
        /// The translation relative to the parent transformation.
        /// </summary>
        /// <userdoc>The translation of the entity with regard to its parent</userdoc>
        [DataMember(10)]
        public Vector3 Position;

        /// <summary>
        /// The rotation relative to the parent transformation.
        /// </summary>
        /// <userdoc>The rotation of the entity with regard to its parent</userdoc>
        [DataMember(20)]
        public Quaternion Rotation;

        /// <summary>
        /// The scaling relative to the parent transformation.
        /// </summary>
        /// <userdoc>The scale of the entity with regard to its parent</userdoc>
        [DataMember(30)]
        public Vector3 Scale;

        /// <summary>
        /// Should this entity track with a VR hand?
        /// </summary>
        [DataMember(40)]
        public VirtualReality.TouchControllerHand TrackVRHand = TouchControllerHand.None;

        /// <summary>
        /// Should this TransformComponent automatically update a batched model index?
        /// </summary>
        [DataMemberIgnore]
        public BatchedMeshDraw.BatchedMeshDrawReference AutoUpdateBatchedMesh;

        /// <summary>
        /// If in VR, do we want to override the normally tracked TransformComponent to point at UI elements?
        /// This is useful if we want to adjust our pointer with a child TransformComponent.
        /// </summary>
        static public TransformComponent OverrideLeftHandUIPointer, OverrideRightHandUIPointer;

        /// <summary>
        /// Last left VR hand tracked. Useful for quick access to left hand and internal UI picking
        /// </summary>
        static public TransformComponent LastLeftHandTracked { get; private set; }

        /// <summary>
        /// Last right VR hand tracked. Useful for quick access to right hand and internal UI picking
        /// </summary>
        static public TransformComponent LastRightHandTracked { get; private set; }

        /// <summary>
        /// Does this transform component (and its children) not move? If so, we can do some performance improvements
        /// </summary>
        [DataMember(50)]
        [DefaultValue(IMMOBILITY.FullMotion)]
        public IMMOBILITY Immobile { get; set; } = IMMOBILITY.FullMotion;

        /// <summary>
        /// Used for advanced transform positioning. Can ignore the heirarchy rotations and rotational positioning.
        /// </summary>
        [DataMember(60)]
        [DefaultValue(HIERARCHY_MODE.Normal)]
        public HIERARCHY_MODE HierarchyMode { get; set; } = HIERARCHY_MODE.Normal;

        /// <summary>
        /// If we are immobile, do one transform update to set initial (or just moved) position
        /// </summary>
        [DataMemberIgnore]
        [DefaultValue(true)]
        public bool UpdateImmobilePosition { get; set; } = true;

        /// <summary>
        /// Recursively go through transform hierarchy, marking everything as needing an update to its immobile position.
        /// </summary>
        public void RecursiveUpdateImmobilePosition()
        {
            _updateImmobilePos(this);
        }

        internal static void _updateImmobilePos(TransformComponent root)
        {
            root.UpdateImmobilePosition = true;

            for (int i = 0; i < root.children.Count; i++)
                _updateImmobilePos(root.children[i]);
        }

        [DataMemberIgnore]
        public TransformLink TransformLink;

        /// <summary>
        /// Initializes a new instance of the <see cref="TransformComponent" /> class.
        /// </summary>
        public TransformComponent()
        {
            children = new TransformChildrenCollection(this);

            UseTRS = true;
            Scale = Vector3.One;
            Rotation = Quaternion.Identity;
        }

        /// <summary>
        /// Gets or sets a value indicating whether to use the Translation/Rotation/Scale.
        /// </summary>
        /// <value><c>true</c> if [use TRS]; otherwise, <c>false</c>.</value>
        [DataMemberIgnore]
        [Display(Browsable = false)]
        [DefaultValue(true)]
        public bool UseTRS
        {
            get { return useTRS; }
            set { useTRS = value; }
        }

        /// <summary>
        /// Gets the children of this <see cref="TransformComponent"/>.
        /// </summary>
        public FastCollection<TransformComponent> Children => children;

        /// <summary>
        /// Gets or sets the euler rotation, with XYZ order.
        /// Not stable: setting value and getting it again might return different value as it is internally encoded as a <see cref="Quaternion"/> in <see cref="Rotation"/>.
        /// </summary>
        /// <value>
        /// The euler rotation.
        /// </value>
        [DataMemberIgnore]
        public Vector3 RotationEulerXYZ
        {
            // Unfortunately it is not possible to factorize the following code with Quaternion.RotationYawPitchRoll because Z axis direction is inversed
            get
            {
                var rotation = Rotation;
                Vector3 rotationEuler;

                // Equivalent to:
                //  Matrix rotationMatrix;
                //  Matrix.Rotation(ref cachedRotation, out rotationMatrix);
                //  rotationMatrix.DecomposeXYZ(out rotationEuler);

                float xx = rotation.X * rotation.X;
                float yy = rotation.Y * rotation.Y;
                float zz = rotation.Z * rotation.Z;
                float xy = rotation.X * rotation.Y;
                float zw = rotation.Z * rotation.W;
                float zx = rotation.Z * rotation.X;
                float yw = rotation.Y * rotation.W;
                float yz = rotation.Y * rotation.Z;
                float xw = rotation.X * rotation.W;

                rotationEuler.Y = (float)Math.Asin(2.0f * (yw - zx));
                double test = Math.Cos(rotationEuler.Y);
                if (test > 1e-6f)
                {
                    rotationEuler.Z = (float)Math.Atan2(2.0f * (xy + zw), 1.0f - (2.0f * (yy + zz)));
                    rotationEuler.X = (float)Math.Atan2(2.0f * (yz + xw), 1.0f - (2.0f * (yy + xx)));
                }
                else
                {
                    rotationEuler.Z = (float)Math.Atan2(2.0f * (zw - xy), 2.0f * (zx + yw));
                    rotationEuler.X = 0.0f;
                }
                return rotationEuler;
            }
            set
            {
                // Equilvalent to:
                //  Quaternion quatX, quatY, quatZ;
                //  
                //  Quaternion.RotationX(value.X, out quatX);
                //  Quaternion.RotationY(value.Y, out quatY);
                //  Quaternion.RotationZ(value.Z, out quatZ);
                //  
                //  rotation = quatX * quatY * quatZ;

                var halfAngles = value * 0.5f;

                var fSinX = (float)Math.Sin(halfAngles.X);
                var fCosX = (float)Math.Cos(halfAngles.X);
                var fSinY = (float)Math.Sin(halfAngles.Y);
                var fCosY = (float)Math.Cos(halfAngles.Y);
                var fSinZ = (float)Math.Sin(halfAngles.Z);
                var fCosZ = (float)Math.Cos(halfAngles.Z);

                var fCosXY = fCosX * fCosY;
                var fSinXY = fSinX * fSinY;

                Rotation.X = fSinX * fCosY * fCosZ - fSinZ * fSinY * fCosX;
                Rotation.Y = fSinY * fCosX * fCosZ + fSinZ * fSinX * fCosY;
                Rotation.Z = fSinZ * fCosXY - fSinXY * fCosZ;
                Rotation.W = fCosZ * fCosXY + fSinXY * fSinZ;
            }
        }

        /// <summary>
        /// Gets or sets the parent of this <see cref="TransformComponent"/>.
        /// </summary>
        /// <value>
        /// The parent.
        /// </value>
        [DataMemberIgnore]
        public TransformComponent Parent
        {
            get { return parent; }
            set
            {
                TransformComponent oldParent = Parent;
                if (oldParent == value)
                    return;

                Scene entityScene = Entity?.Scene;
                Scene newParentScene = value == null ? entityScene : value.Entity?.Scene;

                // Get to root scene
                while (entityScene?.Parent != null)
                    entityScene = entityScene.Parent;
                while (newParentScene?.Parent != null)
                    newParentScene = newParentScene.Parent;

                // Check if root scene didn't change
                IsMovingInsideRootScene = (newParentScene != null && newParentScene == entityScene);

                // remove from old spot, if needed
                if (oldParent != null)
                    oldParent.Children.Remove(this);
                else
                {
                    lock (TransformProcessor.TransformationRoots)
                    {
                        TransformProcessor.TransformationRoots.Remove(this);
                    }
                }

                // add to new spot
                if (value != null)
                    value.Children.Add(this);
                else if (newParentScene != null)
                {
                    if (IsMovingInsideRootScene)
                    {
                        lock (TransformProcessor.TransformationRoots)
                        {
                            TransformProcessor.TransformationRoots.Add(this);
                        }
                    }
                    else
                        Entity.Scene = newParentScene;
                }

                IsMovingInsideRootScene = false;
            }
        }

        private void Entities_CollectionChanged(object sender, TrackingCollectionChangedEventArgs e) {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Updates the local matrix.
        /// If <see cref="UseTRS"/> is true, <see cref="LocalMatrix"/> will be updated from <see cref="Position"/>, <see cref="Rotation"/> and <see cref="Scale"/>.
        /// </summary>
        public void UpdateLocalMatrix()
        {
            // do we need to update with a VR hand?
            if (TrackVRHand != VirtualReality.TouchControllerHand.None && VRDeviceSystem.VRActive)
            {
                TouchController vrController = VRDeviceSystem.GetSystem.GetController(TrackVRHand);

                if (vrController != null && vrController.State != DeviceState.Invalid)
                {
                    Position = vrController.Position;
                    Rotation = vrController.Rotation;

                    if (TrackVRHand == TouchControllerHand.Left)
                    {
                        LastLeftHandTracked = this;
                    }
                    else
                    {
                        LastRightHandTracked = this;
                    }
                }
            }

            if (UseTRS)
            {
                Matrix.Transformation(ref Scale, ref Rotation, ref Position, out LocalMatrix);
            }
        }

        /// <summary>
        /// Updates the local matrix based on the world matrix and the parent entity's or containing scene's world matrix.
        /// </summary>
        public void UpdateLocalFromWorld()
        {
            if (Parent == null)
            {
                var scene = Entity?.Scene;
                if (scene != null)
                {
                    Matrix.Invert(ref scene.WorldMatrix, out var inverseSceneTransform);
                    Matrix.Multiply(ref WorldMatrix, ref inverseSceneTransform, out LocalMatrix);
                }
                else
                {
                    LocalMatrix = WorldMatrix;
                }
            }
            else
            {
                //We are not root so we need to derive the local matrix as well
                Matrix.Invert(ref Parent.WorldMatrix, out var inverseParent);
                Matrix.Multiply(ref WorldMatrix, ref inverseParent, out LocalMatrix);
            }
        }

        /// <summary>
        /// Updates the world matrix.
        /// It will first call <see cref="UpdateLocalMatrix"/> on self, and <see cref="UpdateWorldMatrix"/> on <see cref="Parent"/> if not null.
        /// Then <see cref="WorldMatrix"/> will be updated by multiplying <see cref="LocalMatrix"/> and parent <see cref="WorldMatrix"/> (if any).
        /// </summary>
        public void UpdateWorldMatrix(bool recurseToRoot = true, bool doPostProcessing = true)
        {
            UpdateLocalMatrix();
            UpdateWorldMatrixInternal(recurseToRoot, doPostProcessing);
        }

        /// <summary>
        /// Make this transform look at the target position
        /// </summary>
        /// <param name="target"></param>
        public void LookAt(Vector3 target)
        {
            // get difference of my position to target
            target -= WorldPosition();

            // inline normalize (also flip for handedness)
            float length = target.X * target.X + target.Y * target.Y + target.Z * target.Z;
            if (length != 1f && length != 0f)
            {
                length = 1.0f / (float)Math.Sqrt(length);
                target.X *= -length;
                target.Y *= -length;
                target.Z *= -length;
            }
            else
            {
                target.X = -target.X;
                target.Y = -target.Y;
                target.Z = -target.Z;
            }
            Vector3 fla_vect1;
            fla_vect1.X = 0;
            fla_vect1.Y = 1;
            fla_vect1.Z = 0;
            float tempx = (fla_vect1.Y * target.Z) - (fla_vect1.Z * target.Y);
            float tempy = (fla_vect1.Z * target.X) - (fla_vect1.X * target.Z);
            fla_vect1.Z = (fla_vect1.X * target.Y) - (fla_vect1.Y * target.X);
            fla_vect1.X = tempx;
            fla_vect1.Y = tempy;
            // inline normalize
            length = fla_vect1.X * fla_vect1.X + fla_vect1.Y * fla_vect1.Y + fla_vect1.Z * fla_vect1.Z;
            if (length != 1f && length != 0f)
            {
                length = 1.0f / (float)Math.Sqrt(length);
                fla_vect1.X *= length;
                fla_vect1.Y *= length;
                fla_vect1.Z *= length;
            }
            Vector3 fla_vect2;
            fla_vect2.X = target.X;
            fla_vect2.Y = target.Y;
            fla_vect2.Z = target.Z;
            //vect2.crossLocal(vect1);
            tempx = (fla_vect2.Y * fla_vect1.Z) - (fla_vect2.Z * fla_vect1.Y);
            tempy = (fla_vect2.Z * fla_vect1.X) - (fla_vect2.X * fla_vect1.Z);
            fla_vect2.Z = (fla_vect2.X * fla_vect1.Y) - (fla_vect2.Y * fla_vect1.X);
            fla_vect2.X = tempx;
            fla_vect2.Y = tempy;
            // inline normalize
            length = fla_vect2.X * fla_vect2.X + fla_vect2.Y * fla_vect2.Y + fla_vect2.Z * fla_vect2.Z;
            if (length != 1f && length != 0f)
            {
                length = 1.0f / (float)Math.Sqrt(length);
                fla_vect2.X *= length;
                fla_vect2.Y *= length;
                fla_vect2.Z *= length;
            }

            float t = fla_vect1.X + fla_vect2.Y + target.Z, w, x, y, z;
            // we protect the division by s by ensuring that s>=1
            if (t >= 0)
            { // |w| >= .5
                float s = (float)Math.Sqrt(t + 1); // |s|>=1 ...
                w = 0.5f * s;
                s = 0.5f / s;                 // so this division isn't bad
                x = (fla_vect2.Z - target.Y) * s;
                y = (target.X - fla_vect1.Z) * s;
                z = (fla_vect1.Y - fla_vect2.X) * s;
            }
            else if ((fla_vect1.X > fla_vect2.Y) && (fla_vect1.X > target.Z))
            {
                float s = (float)Math.Sqrt(1.0f + fla_vect1.X - fla_vect2.Y - target.Z); // |s|>=1
                x = s * 0.5f; // |x| >= .5
                s = 0.5f / s;
                y = (fla_vect1.Y + fla_vect2.X) * s;
                z = (target.X + fla_vect1.Z) * s;
                w = (fla_vect2.Z - target.Y) * s;
            }
            else if (fla_vect2.Y > target.Z)
            {
                float s = (float)Math.Sqrt(1.0f + fla_vect2.Y - fla_vect1.X - target.Z); // |s|>=1
                y = s * 0.5f; // |y| >= .5
                s = 0.5f / s;
                x = (fla_vect1.Y + fla_vect2.X) * s;
                z = (fla_vect2.Z + target.Y) * s;
                w = (target.X - fla_vect1.Z) * s;
            }
            else
            {
                float s = (float)Math.Sqrt(1.0f + target.Z - fla_vect1.X - fla_vect2.Y); // |s|>=1
                z = s * 0.5f; // |z| >= .5
                s = 0.5f / s;
                x = (target.X + fla_vect1.Z) * s;
                y = (fla_vect2.Z + target.Y) * s;
                w = (fla_vect1.Y - fla_vect2.X) * s;
            }
            float norm = w * w + x * x + y * y + z * z;
            float n = (float)(1.0f / Math.Sqrt(norm));
            w *= n;
            x *= n;
            y *= n;
            z *= n;

            // negate the worldtransform rotation?
            if (parent != null)
            {
                Rotation = new Quaternion(x, y, z, w) * Quaternion.Invert(parent.WorldRotation());
            }
            else
            {
                Rotation.X = x;
                Rotation.Y = y;
                Rotation.Z = z;
                Rotation.W = w;
            }
        }

        /// <summary>
        /// Gets the world position.
        /// Default call does not recalcuate the position. It just gets the last frame's position quickly.
        /// If you pass true to this function, it will update the world position (which is a costly procedure) to get the most up-to-date position.
        /// </summary>
        public Vector3 WorldPosition(bool recalculate = false)
        {
            if (recalculate) UpdateWorldMatrix(true, false);
            return parent == null ? Position : WorldMatrix.TranslationVector;
        }

        /// <summary>
        /// Gets the world scale.
        /// Default call does not recalcuate the scale. It just gets the last frame's scale quickly.
        /// If you pass true to this function, it will update the world position (which is a costly procedure) to get the most up-to-date scale.
        /// </summary>
        public Vector3 WorldScale(bool recalculate = false)
        {
            if (recalculate) UpdateWorldMatrix(true, false);
            if (parent == null) return Scale;
            WorldMatrix.GetScale(out Vector3 scale);
            return scale;
        }

        /// <summary>
        /// Gets the world rotation.
        /// Default call does not recalcuate the rotation. It just gets the last frame's rotation (relatively) quickly.
        /// If you pass true to this function, it will update the world position (which is a costly procedure) to get the most up-to-date rotation.
        /// </summary>
        public Quaternion WorldRotation(bool recalculate = false)
        {
            if (recalculate) UpdateWorldMatrix(true, false);
            if (parent != null && WorldMatrix.GetRotationQuaternion(out Quaternion q)) {
                return q;
            } else {
                return Rotation;
            }
        }

        /// <summary>
        /// Gets Forward vector for transform
        /// </summary>
        public Vector3 Forward(bool worldForward = false, bool recalculateWorld = false) {
            return RotationMatrix(worldForward, recalculateWorld).Forward;
        }

        /// <summary>
        /// Gets Left vector for transform
        /// </summary>
        public Vector3 Left(bool worldLeft = false, bool recalculateWorld = false) {
            return RotationMatrix(worldLeft, recalculateWorld).Left;
        }

        /// <summary>
        /// Gets Up vector for transform
        /// </summary>
        public Vector3 Up(bool worldUp = false, bool recalculateWorld = false) {
            return RotationMatrix(worldUp, recalculateWorld).Up;
        }

        /// <summary>
        /// Gets a rotation matrix for this transform.
        /// </summary>
        /// <param name="world">World rotation, or just local rotation?</param>
        /// <param name="recalculate">Recalculate world (which is slow), or use last frame info?</param>
        /// <returns>Rotation matrix</returns>
        public Matrix RotationMatrix(bool world = false, bool recalculate = false) {
            if (recalculate) UpdateWorldMatrix();
            return Matrix.RotationQuaternion(world ? WorldRotation() : Rotation);
        }

        internal void UpdateWorldMatrixInternal(bool recursive, bool postProcess = true)
        {
            if (TransformLink != null)
            {
                Matrix linkMatrix;
                TransformLink.ComputeMatrix(recursive, out linkMatrix);
                Matrix.Multiply(ref LocalMatrix, ref linkMatrix, out WorldMatrix);
            }
            else if (Parent != null)
            {
                if (recursive)
                    Parent.UpdateWorldMatrix(true, postProcess);

                if (HierarchyMode != HIERARCHY_MODE.Normal)
                {
                    Parent.WorldMatrix.GetRotationMatrix(out Matrix q);
                    if (HierarchyMode != HIERARCHY_MODE.IgnoreAllRotation)
                        LocalMatrix.TranslationVector = Vector3.Transform(Position, q).XYZ();
                    q.Invert();
                    Matrix cp = LocalMatrix;
                    Matrix.Multiply(ref cp, ref q, out LocalMatrix);
                }

                Matrix.Multiply(ref LocalMatrix, ref Parent.WorldMatrix, out WorldMatrix);
            }
            else
            {
                var scene = Entity?.Scene;
                if (scene != null)
                {
                    if (recursive)
                        scene.UpdateWorldMatrix();

                    Matrix.Multiply(ref LocalMatrix, ref scene.WorldMatrix, out WorldMatrix);
                }
                else
                {
                    WorldMatrix = LocalMatrix;
                }
            }

            if (postProcess)
            {
                for (int i = 0; i < PostOperations.Count; i++)
                {
                    PostOperations[i].Process(this);
                }

                if (AutoUpdateBatchedMesh != null)
                    AutoUpdateBatchedMesh.batchedMeshDraw.SetTransform(AutoUpdateBatchedMesh.index, WorldMatrix);
            }
        }

        [DataContract]
        public class TransformChildrenCollection : FastCollection<TransformComponent>
        {
            TransformComponent transform;
            Entity Entity => transform.Entity;

            public TransformChildrenCollection(TransformComponent transformParam)
            {
                transform = transformParam;
            }

            private void OnTransformAdded(TransformComponent item)
            {
                if (item.Parent != null)
                    throw new InvalidOperationException("This TransformComponent already has a Parent, detach it first.");

                item.parent = transform;

                Entity?.EntityManager?.OnHierarchyChanged(item.Entity);
                Entity?.EntityManager?.GetProcessor<TransformProcessor>().NotifyChildrenCollectionChanged(item, true);
            }
            private void OnTransformRemoved(TransformComponent item)
            {
                if (item.Parent != transform)
                    throw new InvalidOperationException("This TransformComponent's parent is not the expected value.");

                item.parent = null;

                Entity?.EntityManager?.OnHierarchyChanged(item.Entity);
                Entity?.EntityManager?.GetProcessor<TransformProcessor>().NotifyChildrenCollectionChanged(item, false);
            }
            
            /// <inheritdoc/>
            protected override void InsertItem(int index, TransformComponent item)
            {
                base.InsertItem(index, item);
                OnTransformAdded(item);
            }

            /// <inheritdoc/>
            protected override void RemoveItem(int index)
            {
                OnTransformRemoved(this[index]);
                base.RemoveItem(index);
            }

            /// <inheritdoc/>
            protected override void ClearItems()
            {
                for (var i = Count - 1; i >= 0; --i)
                    OnTransformRemoved(this[i]);
                base.ClearItems();
            }

            /// <inheritdoc/>
            protected override void SetItem(int index, TransformComponent item)
            {
                OnTransformRemoved(this[index]);

                base.SetItem(index, item);

                OnTransformAdded(this[index]);
            }
        }
    }
}
