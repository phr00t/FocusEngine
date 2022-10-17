// Copyright (c) Xenko contributors (https://xenko.com) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System;
using System.Collections.Generic;
using Xenko.Core;
using Xenko.Core.Annotations;
using Xenko.Core.Collections;
using Xenko.Core.Mathematics;
using Xenko.Rendering;
using BepuPhysics;
using BepuUtilities;
using Xenko.Engine;
using Xenko.Physics;
using BepuPhysics.Collidables;
using BepuPhysics.Constraints;
using System.Runtime.CompilerServices;
using Xenko.Core.Threading;
using System.Threading;
using System.Collections.Concurrent;
using BulletSharp.SoftBody;
using System.Collections;

namespace Xenko.Physics.Bepu
{
    [DataContract("BepuRigidbodyComponent")]
    [Display("Bepu Rigidbody (No GameStudio integration)")]
    public sealed class BepuRigidbodyComponent : BepuPhysicsComponent
    {
        /// <summary>
        /// Description of the body to be created when added to the scene
        /// </summary>
        [DataMember]
        public BodyDescription bodyDescription;

        [DataMemberIgnore]
        public BodyReference InternalBody;

        public override int HandleIndex => InternalBody.Handle.Value;

        /// <summary>
        /// Action to be called after simulation, but before transforms are set to new positions. Arguments are this and simulation time.
        /// </summary>
        [DataMemberIgnore]
        public Action<BepuRigidbodyComponent, float> ActionPerSimulationTick;

        [DataMemberIgnore]
        internal ConcurrentQueue<Action<BepuRigidbodyComponent>> queuedActions = new ConcurrentQueue<Action<BepuRigidbodyComponent>>();

        // default to true, since any new rigidbody added starts awake
        internal bool wasAwake = true;

        internal void SafeRun(Action<BepuRigidbodyComponent> a)
        {
            if (safeRun)
                a(this);
            else
                queuedActions.Enqueue(a);
        }

        internal enum RB_ACTION
        {
            Awake,
            Sleep,
            CcdMotionThreshold,
            SleepThreshold,
            UpdateInertia,
            SpeculativeMargin,
            ColliderShape,
            ApplyImpulse,
            ApplyImpulseOffset,
            ApplyTorqueImpulse,
            AngularVelocity,
            LinearVelocity
        }

        private static Action<BepuRigidbodyComponent>[] CachedDelegates;

        private static void PrepareDelegates()
        {
            CachedDelegates = new Action<BepuRigidbodyComponent>[13];

            CachedDelegates[(int)RB_ACTION.CcdMotionThreshold] = (rb) =>
            {
                rb.InternalBody.Collidable.Continuity = rb.bodyDescription.Collidable.Continuity;
            };

            CachedDelegates[(int)RB_ACTION.SleepThreshold] = (rb) =>
            {
                rb.InternalBody.Activity.SleepThreshold = rb.bodyDescription.Activity.SleepThreshold;
            };

            CachedDelegates[(int)RB_ACTION.UpdateInertia] = (rb) =>
            {
                rb.InternalBody.LocalInertia = rb.bodyDescription.LocalInertia;
            };

            CachedDelegates[(int)RB_ACTION.SpeculativeMargin] = (rb) =>
            {
                rb.InternalBody.Collidable.SpeculativeMargin = rb.bodyDescription.Collidable.SpeculativeMargin;
            };

            CachedDelegates[(int)RB_ACTION.ApplyImpulse] = (rb) =>
            {
                rb.IsActive = true;
                lock (rb.impulsesToApply)
                {
                    while (rb.impulsesToApply.Count > 0)
                        rb.InternalBody.ApplyLinearImpulse(BepuHelpers.ToBepu(rb.impulsesToApply.Dequeue()));

                    // update changed velocity
                    rb.bodyDescription.Velocity.Linear = rb.InternalBody.Velocity.Linear;
                }
            };

            CachedDelegates[(int)RB_ACTION.ApplyImpulseOffset] = (rb) =>
            {
                rb.IsActive = true;
                lock (rb.impulsesToApply)
                {
                    while (rb.impulsePairs.Count > 0)
                    {
                        Vector3[] pair = rb.impulsePairs.Dequeue();
                        rb.InternalBody.ApplyImpulse(BepuHelpers.ToBepu(pair[0]), BepuHelpers.ToBepu(pair[1]));
                    }

                    // update changed velocities
                    rb.bodyDescription.Velocity.Linear = rb.InternalBody.Velocity.Linear;
                    rb.bodyDescription.Velocity.Angular = rb.InternalBody.Velocity.Angular;
                }
            };

            CachedDelegates[(int)RB_ACTION.ApplyTorqueImpulse] = (rb) =>
            {
                rb.IsActive = true;
                lock (rb.impulsesToApply)
                {
                    while (rb.torquesToApply.Count > 0)
                        rb.InternalBody.ApplyAngularImpulse(BepuHelpers.ToBepu(rb.torquesToApply.Dequeue()));

                    // update changed velocity
                    rb.bodyDescription.Velocity.Angular = rb.InternalBody.Velocity.Angular;
                }
            };

            CachedDelegates[(int)RB_ACTION.AngularVelocity] = rb =>
            {
                if (rb.bodyDescription.Velocity.Angular != System.Numerics.Vector3.Zero)
                    rb.IsActive = true;

                ref BodyVelocity v = ref rb.InternalBody.Velocity;

                v.Angular.X = rb.bodyDescription.Velocity.Angular.X;
                v.Angular.Y = rb.bodyDescription.Velocity.Angular.Y;
                v.Angular.Z = rb.bodyDescription.Velocity.Angular.Z;
            };

            CachedDelegates[(int)RB_ACTION.LinearVelocity] = rb =>
            {
                if (rb.bodyDescription.Velocity.Linear != System.Numerics.Vector3.Zero)
                    rb.IsActive = true;

                ref BodyVelocity v = ref rb.InternalBody.Velocity;

                v.Linear.X = rb.bodyDescription.Velocity.Linear.X;
                v.Linear.Y = rb.bodyDescription.Velocity.Linear.Y;
                v.Linear.Z = rb.bodyDescription.Velocity.Linear.Z;
            };
        }

        internal void InternalColliderShapeReadd()
        {
            BepuSimulation bs = BepuSimulation.instance;

            using (bs.simulationLocker.WriteLock())
            {
                // let's check handle again now that we are in the lock, just in case
                if (InternalBody.Handle.Value == -1) return;

                // remove me with the old shape
                bs.internalSimulation.Bodies.Remove(InternalBody.Handle);
                BepuSimulation.RigidMappings[InternalBody.Handle.Value] = null;

                // add me with the new shape
                bodyDescription.Collidable = ColliderShape.GenerateDescription(bs.internalSimulation, SpeculativeMargin);
                InternalBody.Handle = bs.internalSimulation.Bodies.Add(bodyDescription);
                BepuSimulation.AddBodyReference(this);
            }
        }

        /// <summary>
        /// Gets a value indicating whether this instance is active (awake).
        /// </summary>
        /// <value>
        ///   <c>true</c> if this instance is active; otherwise, <c>false</c>.
        /// </value>
        [DataMemberIgnore]
        public bool IsActive
        {
            get
            {
                return wasAwake;
            }
            set
            {
                if (wasAwake == value) return;

                wasAwake = value;

                if (AddedToScene)
                {
                    BepuSimulation.instance.CriticalActions.Enqueue(new BepuSimulation.RBCriticalAction()
                    {
                        Action = value ? RB_ACTION.Awake : RB_ACTION.Sleep,
                        Body = this
                    });
                }
            }
        }

        /// <summary>
        /// Use continuous collision detection? Set greater than 0 to use, 0 or less to disable
        /// </summary>
        [DataMember(67)]
        public float CcdMotionThreshold
        {
            get
            {
                return bodyDescription.Collidable.Continuity.SweepConvergenceThreshold;
            }
            set
            {
                if (bodyDescription.Collidable.Continuity.SweepConvergenceThreshold == value) return;

                bodyDescription.Collidable.Continuity.SweepConvergenceThreshold = value;
                bodyDescription.Collidable.Continuity.Mode = value > 0 ? ContinuousDetectionMode.Continuous : ContinuousDetectionMode.Discrete;
                bodyDescription.Collidable.Continuity.MinimumSweepTimestep = value > 0 ? 1e-3f : 0f;

                if (AddedToScene)
                {
                    SafeRun(CachedDelegates[(int)RB_ACTION.CcdMotionThreshold]);
                }
            }
        }

        /// <summary>
        /// If we are collecting collisions, how many to store before we stop storing them? Defaults to 32. Prevents crazy counts when objects are heavily overlapping.
        /// </summary>
        [DataMember]
        public int CollectCollisionMaximumCount
        {
            get
            {
                return CurrentPhysicalContacts == null ? 0 : CurrentPhysicalContacts.Length;
            }
            set
            {
                if (value <= 0)
                {
                    _collectCollisions = false;
                    CurrentPhysicalContacts = null;
                    return;
                }

                if (CurrentPhysicalContacts == null || CurrentPhysicalContacts.Length != value)
                    CurrentPhysicalContacts = new BepuContact[value];
            }
        }
        
        /// <summary>
        /// Gets or sets if this element will store collisions in CurrentPhysicalContacts. Uses less CPU than ProcessCollisions
        /// </summary>
        /// <value>
        /// true, false
        /// </value>
        /// <userdoc>
        /// Stores contact points in a simple CurrentPhysicalContacts list, instead of new/update/ended events. Uses less CPU than ProcessCollisions and is multithreading supported
        /// </userdoc>
        [Display("Simple collision storage")]
        [DataMemberIgnore]
        public bool CollectCollisions
        {
            get
            {
                return _collectCollisions;
            }
            set
            {
                if (_collectCollisions == value) return;

                if (value)
                {
                    if (CurrentPhysicalContacts == null)
                        CurrentPhysicalContacts = new BepuContact[32];
                }
                else
                {
                    CurrentPhysicalContacts = null;
                }

                _collectCollisions = value;
            }
        }

        [DataMemberIgnore]
        private bool _collectCollisions = false;

        internal void resetProcessingContactsList()
        {
            if (CurrentPhysicalContacts == null || AddedToScene == false || IsActive == false) return;

            CurrentPhysicalContactsCount = Math.Min(CurrentPhysicalContacts.Length, processingPhysicalContactCount);
            processingPhysicalContactCount = 0;
        }

        [DataMemberIgnore]
        public BepuContact[] CurrentPhysicalContacts;

        [DataMemberIgnore]
        public int CurrentPhysicalContactsCount;

        [DataMemberIgnore]
        internal int processingPhysicalContactCount;

        private static readonly BodyInertia KinematicInertia = new BodyInertia()
        {
            InverseMass = 0f,
            InverseInertiaTensor = default
        };

        private RigidBodyTypes type = RigidBodyTypes.Dynamic;
        private Vector3 gravity = Vector3.Zero;

        private void setInternalBodyDefaults()
        {
            if (CachedDelegates == null)
                PrepareDelegates();

            bodyDescription.Pose.Orientation.W = 1f;
            bodyDescription.LocalInertia.InverseMass = 1f;
            bodyDescription.Activity.MinimumTimestepCountUnderThreshold = 32;
            bodyDescription.Activity.SleepThreshold = 0.01f;
            InternalBody.Bodies = BepuSimulation.instance?.internalSimulation?.Bodies;
            InternalBody.Handle.Value = -1;
        }

        /// <summary>
        /// Generates a rigid body component for an Entity. Has shortcuts for setting the collidershape and mass.
        /// </summary>
        /// <param name="collider_shape">Sets a collider shape. If null, one must be set later.</param>
        /// <param name="mass">Sets mass IF collider_shape is not null.</param>
        public BepuRigidbodyComponent(IShape collider_shape = null, float mass = 1f) : base()
        {
            setInternalBodyDefaults();
            
            if (collider_shape != null)
            {
                ColliderShape = collider_shape;
                Mass = mass;
            }
        }

        /// <summary>
        /// Default constructor with no shortcut for making a collider shape. Make sure to add one later!
        /// </summary>
        public BepuRigidbodyComponent() : base()
        {
            setInternalBodyDefaults();
        }

        public override TypedIndex ShapeIndex { get => bodyDescription.Collidable.Shape; }

        /// <summary>
        /// How slow does this need to be to sleep? Set less than 0 to never sleep
        /// </summary>
        [DataMember]
        public float SleepThreshold
        {
            get
            {
                return bodyDescription.Activity.SleepThreshold;
            }
            set
            {
                if (bodyDescription.Activity.SleepThreshold == value) return;

                bodyDescription.Activity.SleepThreshold = value;

                if (AddedToScene)
                {
                    SafeRun(CachedDelegates[(int)RB_ACTION.SleepThreshold]);
                }
            }
        }

        /// <summary>
        /// Forcefully cap the velocity of this rigidbody to this magnitude. Can prevent weird issues without the need of continuous collision detection.
        /// </summary>
        [DataMember]
        public float MaximumSpeed = 0f;

        /// <summary>
        /// Prevent this rigidbody from rotating or falling over?
        /// </summary>
        [DataMember]
        public bool RotationLock
        {
            get
            {
                return _rotationLock;
            }
            set
            {
                if (_rotationLock == value) return;

                _rotationLock = value;

                UpdateInertia();
            }
        }

        private bool _rotationLock = false;

        /// <summary>
        /// Gets or sets the mass of this Rigidbody
        /// </summary>
        /// <value>
        /// true, false
        /// </value>
        /// <userdoc>
        /// Objects with higher mass push objects with lower mass more when they collide. For large differences, use point values; for example, write 0.1 or 10, not 1 or 100000.
        /// </userdoc>
        [DataMember(80)]
        [DataMemberRange(0, 6)]
        public float Mass
        {
            get
            {
                return mass;
            }
            set
            {
                if (mass <= 0.00001f) mass = 0.00001f;

                mass = value;

                UpdateInertia();
            }
        }

        private void UpdateInertia(bool skipset = false)
        {
            if (type == RigidBodyTypes.Kinematic)
            {
                bodyDescription.LocalInertia = KinematicInertia;
            }
            else if (ColliderShape is IConvexShape ics && !_rotationLock)
            { 
                ics.ComputeInertia(mass, out bodyDescription.LocalInertia);
            }
            else if (_rotationLock)
            {
                bodyDescription.LocalInertia.InverseMass = 1f / mass;
                bodyDescription.LocalInertia.InverseInertiaTensor = default;
            }
            else if (ColliderShape is BepuPhysics.Collidables.Mesh m)
            {
                m.ComputeInertia(mass, out bodyDescription.LocalInertia);
            }

            if (skipset == false && AddedToScene)
            {
                SafeRun(CachedDelegates[(int)RB_ACTION.UpdateInertia]);
            }
        }

        [DataMember]
        public override float SpeculativeMargin
        {
            get => bodyDescription.Collidable.SpeculativeMargin;
            set
            {
                if (bodyDescription.Collidable.SpeculativeMargin == value) return;

                bodyDescription.Collidable.SpeculativeMargin = value;

                if (AddedToScene)
                {
                    SafeRun(CachedDelegates[(int)RB_ACTION.SpeculativeMargin]);
                }
            }
        }

        [DataMemberIgnore]
        public override IShape ColliderShape
        {
            get => base.ColliderShape;
            set
            {
                if (value == null || value.Equals(ColliderShape)) return;

                if (AddedToScene && ColliderShape != null)
                {
                    base.ColliderShape = value;

                    UpdateInertia(true);

                    BepuSimulation.instance.CriticalActions.Enqueue(new BepuSimulation.RBCriticalAction()
                    {
                        Action = RB_ACTION.ColliderShape,
                        Body = this
                    });
                }
                else
                {
                    base.ColliderShape = value;

                    UpdateInertia();
                }
            }
        }

        private float mass = 1f;

        /// <summary>
        /// Gets or sets the linear damping of this rigidbody
        /// </summary>
        /// <value>
        /// true, false
        /// </value>
        /// <userdoc>
        /// The amount of damping for directional forces
        /// </userdoc>
        [DataMember(85)]
        public float LinearDamping = 0f;

        /// <summary>
        /// Gets or sets the angular damping of this rigidbody
        /// </summary>
        /// <value>
        /// true, false
        /// </value>
        /// <userdoc>
        /// The amount of damping for rotational forces
        /// </userdoc>
        [DataMember(90)]
        public float AngularDamping = 0f;

        /// <summary>
        /// Gets or sets if this Rigidbody overrides world gravity
        /// </summary>
        /// <value>
        /// true, false
        /// </value>
        /// <userdoc>
        /// Override gravity with the vector specified in Gravity
        /// </userdoc>
        [DataMember(95)]
        public bool OverrideGravity;

        /// <summary>
        /// Gets or sets the gravity acceleration applied to this RigidBody
        /// </summary>
        /// <value>
        /// A vector representing moment and direction
        /// </value>
        /// <userdoc>
        /// The gravity acceleration applied to this rigidbody
        /// </userdoc>
        [DataMember(100)]
        public Vector3 Gravity;

        /// <summary>
        /// Gets or sets the type.
        /// </summary>
        /// <value>
        /// The type.
        /// </value>
        public RigidBodyTypes RigidBodyType
        {
            get
            {
                return type;
            }
            set
            {
                if (type == value) return;

                type = value;

                UpdateInertia();
            }
        }

        /// <summary>
        /// Set this to true to add this object to the physics simulation. Will automatically remove itself when the entity. is removed from the scene. Will NOT automatically add the rigidbody
        /// to the scene when the entity is added, though.
        /// </summary>
        [DataMemberIgnore]
        public override bool AddedToScene
        {
            get
            {
                return InternalBody.Handle.Value != -1; 
            }
            set
            {
                if (value)
                {
                    if (ColliderShape == null)
                        throw new InvalidOperationException(Entity.Name + " has no ColliderShape, can't be added!");

                    if (BepuHelpers.SanityCheckShape(ColliderShape) == false)
                        throw new InvalidOperationException(Entity.Name + " has a broken ColliderShape! Check sizes and/or children count.");

                    lock (BepuSimulation.instance.ToBeAdded)
                    {
                        BepuSimulation.instance.ToBeAdded.Add(this);
                        BepuSimulation.instance.ToBeRemoved.Remove(this);
                    }
                }
                else
                {
                    lock (BepuSimulation.instance.ToBeAdded)
                    {
                        BepuSimulation.instance.ToBeRemoved.Add(this);
                        BepuSimulation.instance.ToBeAdded.Remove(this);
                    }
                }
            }
        }

        private Queue<Vector3> impulsesToApply = new Queue<Vector3>();

        /// <summary>
        /// Applies the impulse.
        /// </summary>
        /// <param name="impulse">The impulse.</param>
        public void ApplyImpulse(Vector3 impulse)
        {
            if (AddedToScene)
            {
                lock (impulsesToApply)
                {
                    impulsesToApply.Enqueue(impulse);
                }

                SafeRun(CachedDelegates[(int)RB_ACTION.ApplyImpulse]);
            }
        }

        private Queue<Vector3[]> impulsePairs;

        /// <summary>
        /// Applies the impulse.
        /// </summary>
        /// <param name="impulse">The impulse.</param>
        /// <param name="localOffset">The local offset.</param>
        public void ApplyImpulse(Vector3 impulse, Vector3 localOffset)
        {
            if (AddedToScene)
            {
                if (impulsePairs == null)
                    impulsePairs = new Queue<Vector3[]>();

                lock (impulsesToApply)
                {
                    impulsePairs.Enqueue(new Vector3[] { impulse, localOffset });
                }

                SafeRun(CachedDelegates[(int)RB_ACTION.ApplyImpulseOffset]);
            }
        }

        private Queue<Vector3> torquesToApply;

        /// <summary>
        /// Applies the torque impulse.
        /// </summary>
        /// <param name="torque">The torque.</param>
        public void ApplyTorqueImpulse(Vector3 torque)
        {
            if (AddedToScene)
            {
                if (torquesToApply == null)
                    torquesToApply = new Queue<Vector3>();

                lock (impulsesToApply)
                {
                    torquesToApply.Enqueue(torque);
                }

                SafeRun(CachedDelegates[(int)RB_ACTION.ApplyTorqueImpulse]);
            }
        }

        internal bool newPos = false, newRotation = false;

        [DataMemberIgnore]
        public override Vector3 Position
        {
            get
            {
                return BepuHelpers.ToXenko(bodyDescription.Pose.Position);
            }
            set
            {
                bodyDescription.Pose.Position.X = value.X;
                bodyDescription.Pose.Position.Y = value.Y;
                bodyDescription.Pose.Position.Z = value.Z;
                newPos = true;
            }
        }

        [DataMemberIgnore]
        public override Xenko.Core.Mathematics.Quaternion Rotation
        {
            get
            {
                return BepuHelpers.ToXenko(bodyDescription.Pose.Orientation);
            }
            set
            {
                bodyDescription.Pose.Orientation.X = value.X;
                bodyDescription.Pose.Orientation.Y = value.Y;
                bodyDescription.Pose.Orientation.Z = value.Z;
                bodyDescription.Pose.Orientation.W = value.W;
                newRotation = true;
            }
        }

        /// <summary>
        /// Gets or sets the angular velocity.
        /// </summary>
        /// <value>
        /// The angular velocity.
        /// </value>
        [DataMemberIgnore]
        public Vector3 AngularVelocity
        {
            get
            {
                return BepuHelpers.ToXenko(bodyDescription.Velocity.Angular);
            }
            set
            {
                if (bodyDescription.Velocity.Angular == BepuHelpers.ToBepu(value)) return;

                lock (impulsesToApply)
                {
                    // since we are applying a velocity, get rid of previous impulses which would normally be overwritten
                    torquesToApply?.Clear();

                    bodyDescription.Velocity.Angular.X = value.X;
                    bodyDescription.Velocity.Angular.Y = value.Y;
                    bodyDescription.Velocity.Angular.Z = value.Z;
                }

                if (AddedToScene)
                {
                    SafeRun(CachedDelegates[(int)RB_ACTION.AngularVelocity]);
                }
            }
        }

        /// <summary>
        /// Gets or sets the linear velocity.
        /// </summary>
        /// <value>
        /// The linear velocity.
        /// </value>
        [DataMemberIgnore]
        public Vector3 LinearVelocity
        {
            get
            {
                return BepuHelpers.ToXenko(bodyDescription.Velocity.Linear);
            }
            set
            {
                if (bodyDescription.Velocity.Linear == BepuHelpers.ToBepu(value)) return;

                lock (impulsesToApply)
                {
                    // since we are applying a velocity, get rid of previous impulses which would normally be overwritten
                    impulsesToApply.Clear();
                    impulsePairs?.Clear();

                    bodyDescription.Velocity.Linear.X = value.X;
                    bodyDescription.Velocity.Linear.Y = value.Y;
                    bodyDescription.Velocity.Linear.Z = value.Z;
                }

                if (AddedToScene)
                {
                    SafeRun(CachedDelegates[(int)RB_ACTION.LinearVelocity]);
                }
            }
        }

        /// <summary>
        /// When updating the associated TransformComponent, should we not set rotation?
        /// </summary>
        [DataMember(69)]
        public bool IgnorePhysicsRotation = false;

        /// <summary>
        /// When updating the associated TransformComponent, should we not set position?
        /// </summary>
        [DataMember(75)]
        public bool IgnorePhysicsPosition = false;

        [DataMemberIgnore]
        public Vector3? LocalPhysicsOffset = null;

        /// <summary>
        /// Automatically use the bottom of this physics component for positioning the attached entity
        /// </summary>
        [DataMemberIgnore]
        public bool AttachEntityAtBottom { get; set; }

        [DataMemberIgnore]
        public Vector3 VelocityLinearChange { get; private set; }
            
        [DataMemberIgnore]
        public Vector3 VelocityAngularChange { get; private set; }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void UpdateCachedPoseAndVelocity()
        {
            wasAwake = InternalBody.Awake;
            if (newPos || newRotation)
            {
                if (newPos) { newPos = false; InternalBody.Pose.Position = bodyDescription.Pose.Position; }
                if (newRotation) { newRotation = false; InternalBody.Pose.Orientation = bodyDescription.Pose.Orientation; }
                if (!wasAwake) InternalBody.UpdateBounds();
            }
            bodyDescription.Pose = InternalBody.Pose;
            BodyVelocity bv = InternalBody.Velocity;
            VelocityLinearChange = BepuHelpers.ToXenko(bv.Linear - bodyDescription.Velocity.Linear);
            VelocityAngularChange = BepuHelpers.ToXenko(bv.Angular - bodyDescription.Velocity.Angular);
            bodyDescription.Velocity = bv;
        }

        /// <summary>
        /// Updades the graphics transformation from the given physics transformation
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void UpdateTransformationComponent(Entity entity)
        {
            if (IgnorePhysicsPosition == false)
            {
                Vector3 pos = Position;
                if (AttachEntityAtBottom) pos.Y = InternalBody.BoundingBox.Min.Y;
                if (LocalPhysicsOffset.HasValue) pos += LocalPhysicsOffset.Value;
                entity.Transform.Position = pos;
            }
            if (IgnorePhysicsRotation == false) entity.Transform.Rotation = Rotation;
        }
    }
}
