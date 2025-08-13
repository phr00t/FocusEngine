// Copyright (c) Xenko contributors (https://xenko.com) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Security;
using System.Threading;
using BepuPhysics;
using BepuPhysics.Collidables;
using BepuPhysics.CollisionDetection;
using BepuPhysics.Constraints;
using BepuPhysics.Trees;
using BepuUtilities;
using BepuUtilities.Memory;
using Xenko.Core;
using Xenko.Core.Collections;
using Xenko.Core.Diagnostics;
using Xenko.Core.Mathematics;
using Xenko.Core.Threading;
using Xenko.Engine;
using Xenko.Engine.Design;
using Xenko.Games;
using Xenko.Physics.Engine;
using Xenko.Rendering;

namespace Xenko.Physics.Bepu
{
    public class BepuSimulation : IDisposable
    {
        private const CollisionFilterGroups DefaultGroup = CollisionFilterGroups.DefaultFilter;
        private const CollisionFilterGroupFlags DefaultFlags = CollisionFilterGroupFlags.AllFilter;

        public BepuPhysics.Simulation internalSimulation;

        internal HashSet<BepuPhysicsComponent> ToBeAdded = new HashSet<BepuPhysicsComponent>(), ToBeRemoved = new HashSet<BepuPhysicsComponent>();

        public ReaderWriterLockSlim simulationLocker { get; private set; } = new ReaderWriterLockSlim();

        public Action<float> AlwaysExecuteBeforeSimulationStep = null;
        public ConcurrentQueue<Action<float>> ActionsBeforeSimulationStep = new ConcurrentQueue<Action<float>>();
        public ConcurrentQueue<Action<float>> ActionsAfterSimulationStep = new ConcurrentQueue<Action<float>>();
        internal ConcurrentQueue<RBCriticalAction> CriticalActions = new ConcurrentQueue<RBCriticalAction>();

        internal struct RBCriticalAction
        {
            public BepuRigidbodyComponent.RB_ACTION Action;
            public BepuRigidbodyComponent Body;
        }

        public static float TimeScale = 1f;
        public static int MaxSubSteps = 1;

        private static BepuSimulation _instance;
        public static BepuSimulation instance
        {
            get
            {
                if (_instance == null) BepuHelpers.AssureBepuSystemCreated();
                return _instance;
            }
            private set
            {
                _instance = value;
            }
        }

        [ThreadStatic]
        private static BufferPool threadStaticPool;

        /// <summary>
        /// Gets a thread-safe buffer pool
        /// </summary>
        public static BufferPool safeBufferPool
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                if (threadStaticPool == null)
                {
                    threadStaticPool = new BufferPool();
                    allBufferPools.Enqueue(threadStaticPool);
                }
                return threadStaticPool;
            }
        }

        private PoseIntegratorCallbacks poseCallbacks;

        private static ConcurrentQueue<BufferPool> allBufferPools = new ConcurrentQueue<BufferPool>();

        internal int clearRequested;
        private BepuSimpleThreadDispatcher threadDispatcher = new BepuSimpleThreadDispatcher();

#if DEBUG
        private static readonly Logger Log = GlobalLogger.GetLogger(typeof(Simulation).FullName);
#endif

        /// <summary>
        /// Totally disable the simulation if set to true. This points to the same Simulation.DisableSimulation boolean
        /// </summary>
        public static bool DisableSimulation
        {
            get => Simulation.DisableSimulation;
            set
            {
                Simulation.DisableSimulation = value;
            }
        }

        internal static BepuStaticColliderComponent[] StaticMappings = new BepuStaticColliderComponent[64];
        internal static BepuRigidbodyComponent[] RigidMappings = new BepuRigidbodyComponent[64];

        internal List<BepuRigidbodyComponent> AllRigidbodies = new List<BepuRigidbodyComponent>();

        /// <summary>
        /// Clears out the whole simulation of bodies, optionally clears all related buffers (like meshes) too
        /// </summary>
        /// <param name="clearBuffers">Clear out all buffers, like mesh shapes? Defaults to true</param>
        /// <param name="forceRightNow">Clear everything right now. Might cause crashes if simulation is happening at the same time!</param>
        public void Clear(bool clearBuffers = true, bool forceRightNow = false)
        {
            if (forceRightNow)
            {
                clearRequested = 0;

                using (simulationLocker.WriteLock())
                {
                    internalSimulation.Clear();

                    if (clearBuffers)
                    {
                        while(allBufferPools.TryDequeue(out var pool))
                            pool.Clear();
                    }

                    for (int i = 0; i < RigidMappings.Length; i++)
                        RigidMappings[i] = null;
                    for (int i = 0; i < StaticMappings.Length; i++)
                        StaticMappings[i] = null;

                    AllRigidbodies.Clear();
                }

                return;
            }

            clearRequested = clearBuffers ? 2 : 1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static BepuRigidbodyComponent getRigidFromIndex(int index)
        {
            return RigidMappings[instance.internalSimulation.Bodies.ActiveSet.IndexToHandle[index].Value];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static BepuPhysicsComponent getFromHandle(int handle, CollidableMobility mobility)
        {
            return mobility == CollidableMobility.Static ? (BepuPhysicsComponent)StaticMappings[handle] : (BepuPhysicsComponent)RigidMappings[handle];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static BepuPhysicsComponent getFromReference(CollidableReference cref)
        {
            return cref.Mobility == CollidableMobility.Static ? (BepuPhysicsComponent)StaticMappings[cref.StaticHandle.Value] : (BepuPhysicsComponent)RigidMappings[cref.BodyHandle.Value];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool CanCollide(int source, CollidableMobility sourcemob, int target, CollidableMobility targetmob)
        {
            return ((uint)getFromHandle(source, sourcemob).CollisionGroup & (uint)getFromHandle(target, targetmob).CanCollideWith) != 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool CanCollide(CollidableReference a, CollidableReference b)
        {
            return ((uint)getFromReference(a).CollisionGroup & (uint)getFromReference(b).CanCollideWith) != 0;
        }

        unsafe struct NarrowPhaseCallbacks : INarrowPhaseCallbacks
        {
            /// <summary>
            /// Performs any required initialization logic after the Simulation instance has been constructed.
            /// </summary>
            /// <param name="simulation">Simulation that owns these callbacks.</param>
            public void Initialize(BepuPhysics.Simulation simulation)
            {
                //Often, the callbacks type is created before the simulation instance is fully constructed, so the simulation will call this function when it's ready.
                //Any logic which depends on the simulation existing can be put here.
            }

            /// <summary>
            /// Chooses whether to allow contact generation to proceed for two overlapping collidables.
            /// </summary>
            /// <param name="workerIndex">Index of the worker that identified the overlap.</param>
            /// <param name="a">Reference to the first collidable in the pair.</param>
            /// <param name="b">Reference to the second collidable in the pair.</param>
            /// <returns>True if collision detection should proceed, false otherwise.</returns>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool AllowContactGeneration(int workerIndex, CollidableReference a, CollidableReference b)
            {
                //Before creating a narrow phase pair, the broad phase asks this callback whether to bother with a given pair of objects.
                //This can be used to implement arbitrary forms of collision filtering. See the RagdollDemo or NewtDemo for examples.
                //Here, we'll make sure at least one of the two bodies is dynamic.
                //The engine won't generate static-static pairs, but it will generate kinematic-kinematic pairs.
                //That's useful if you're trying to make some sort of sensor/trigger object, but since kinematic-kinematic pairs
                //can't generate constraints (both bodies have infinite inertia), simple simulations can just ignore such pairs.
                return (a.Mobility == CollidableMobility.Dynamic || b.Mobility == CollidableMobility.Dynamic) && CanCollide(a, b);
            }

            /// <summary>
            /// Chooses whether to allow contact generation to proceed for the children of two overlapping collidables in a compound-including pair.
            /// </summary>
            /// <param name="pair">Parent pair of the two child collidables.</param>
            /// <param name="childIndexA">Index of the child of collidable A in the pair. If collidable A is not compound, then this is always 0.</param>
            /// <param name="childIndexB">Index of the child of collidable B in the pair. If collidable B is not compound, then this is always 0.</param>
            /// <returns>True if collision detection should proceed, false otherwise.</returns>
            /// <remarks>This is called for each sub-overlap in a collidable pair involving compound collidables. If neither collidable in a pair is compound, this will not be called.
            /// For compound-including pairs, if the earlier call to AllowContactGeneration returns false for owning pair, this will not be called. Note that it is possible
            /// for this function to be called twice for the same subpair if the pair has continuous collision detection enabled; 
            /// the CCD sweep test that runs before the contact generation test also asks before performing child pair tests.</remarks>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool AllowContactGeneration(int workerIndex, CollidablePair pair, int childIndexA, int childIndexB)
            {
                //This is similar to the top level broad phase callback above. It's called by the narrow phase before generating
                //subpairs between children in parent shapes. 
                //This only gets called in pairs that involve at least one shape type that can contain multiple children, like a Compound.
                return CanCollide(pair.A, pair.B);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void RecordContact<TManifold>(BepuPhysicsComponent A, BepuPhysicsComponent B, ref TManifold manifold) where TManifold : struct, IContactManifold<TManifold>
            {
                // sanity checking
                if (A == null || B == null || manifold.Count == 0) return;

                BepuRigidbodyComponent ar = (A as BepuRigidbodyComponent);
                BepuRigidbodyComponent br = (B as BepuRigidbodyComponent);
                // do we want to store this collision?
                if ((ar?.CollectCollisions ?? false))
                {
                    int index = Interlocked.Increment(ref ar.processingPhysicalContactCount) - 1;
                    if (index < ar.CurrentPhysicalContacts.Length)
                    {
                        ar.CurrentPhysicalContacts[index].A = A;
                        ar.CurrentPhysicalContacts[index].B = B;
                        ar.CurrentPhysicalContacts[index].Normal = BepuHelpers.ToXenko(manifold.SimpleGetNormal());
                        ar.CurrentPhysicalContacts[index].Offset = BepuHelpers.ToXenko(manifold.SimpleGetOffset());
                    }
                }
                if ((br?.CollectCollisions ?? false))
                {
                    int index = Interlocked.Increment(ref br.processingPhysicalContactCount) - 1;
                    if (index < br.CurrentPhysicalContacts.Length)
                    {
                        br.CurrentPhysicalContacts[index].A = B;
                        br.CurrentPhysicalContacts[index].B = A;
                        br.CurrentPhysicalContacts[index].Normal = -BepuHelpers.ToXenko(manifold.SimpleGetNormal());
                        br.CurrentPhysicalContacts[index].Offset = B.Position - (A.Position + BepuHelpers.ToXenko(manifold.SimpleGetOffset()));
                    }
                }
            }

            /// <summary>
            /// Provides a notification that a manifold has been created between the children of two collidables in a compound-including pair.
            /// Offers an opportunity to change the manifold's details. 
            /// </summary>
            /// <param name="workerIndex">Index of the worker thread that created this manifold.</param>
            /// <param name="pair">Pair of collidables that the manifold was detected between.</param>
            /// <param name="childIndexA">Index of the child of collidable A in the pair. If collidable A is not compound, then this is always 0.</param>
            /// <param name="childIndexB">Index of the child of collidable B in the pair. If collidable B is not compound, then this is always 0.</param>
            /// <param name="manifold">Set of contacts detected between the collidables.</param>
            /// <returns>True if this manifold should be considered for constraint generation, false otherwise.</returns>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool ConfigureContactManifold(int workerIndex, CollidablePair pair, int childIndexA, int childIndexB, ref ConvexContactManifold manifold)
            {
                BepuPhysicsComponent A = getFromReference(pair.A);
                BepuPhysicsComponent B = getFromReference(pair.B);
                if (((uint)A.CanCollideWith & (uint)B.CollisionGroup) != 0)
                {
                    RecordContact(A, B, ref manifold);
                    return !A.GhostBody && !B.GhostBody;
                }
                return false;
            }

            /// <summary>
            /// Releases any resources held by the callbacks. Called by the owning narrow phase when it is being disposed.
            /// </summary>
            public void Dispose()
            {
            }

            /// <summary>
            /// Provides a notification that a manifold has been created for a pair. Offers an opportunity to change the manifold's details. 
            /// </summary>
            /// <param name="workerIndex">Index of the worker thread that created this manifold.</param>
            /// <param name="pair">Pair of collidables that the manifold was detected between.</param>
            /// <param name="manifold">Set of contacts detected between the collidables.</param>
            /// <param name="pairMaterial">Material properties of the manifold.</param>
            /// <returns>True if a constraint should be created for the manifold, false otherwise.</returns>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool ConfigureContactManifold<TManifold>(int workerIndex, CollidablePair pair, ref TManifold manifold, out PairMaterialProperties pairMaterial) where TManifold : unmanaged, IContactManifold<TManifold>
            {
                BepuPhysicsComponent a = getFromReference(pair.A);
                BepuPhysicsComponent b = getFromReference(pair.B);
                pairMaterial.FrictionCoefficient = a.FrictionCoefficient * b.FrictionCoefficient;
                pairMaterial.MaximumRecoveryVelocity = (a.MaximumRecoveryVelocity + b.MaximumRecoveryVelocity) * 0.5f;
                pairMaterial.SpringSettings.AngularFrequency = (a.SpringSettings.AngularFrequency + b.SpringSettings.AngularFrequency) * 0.5f;
                pairMaterial.SpringSettings.TwiceDampingRatio = (a.SpringSettings.TwiceDampingRatio + b.SpringSettings.TwiceDampingRatio) * 0.5f;
                if (((uint)a.CanCollideWith & (uint)b.CollisionGroup) != 0)
                {
                    RecordContact(a, b, ref manifold);
                    return !a.GhostBody && !b.GhostBody;
                }
                return false;
            }
        }

        //Note that the engine does not require any particular form of gravity- it, like all the contact callbacks, is managed by a callback.
        private struct PoseIntegratorCallbacks : IPoseIntegratorCallbacks
        {
            System.Numerics.Vector3 gravityDt;
            float indt;

            /// <summary>
            /// Gets how the pose integrator should handle angular velocity integration.
            /// </summary>
            public AngularIntegrationMode AngularIntegrationMode => AngularIntegrationMode.Nonconserving; //Don't care about fidelity in this demo!

            /// <summary>
            /// Called prior to integrating the simulation's active bodies. When used with a substepping timestepper, this could be called multiple times per frame with different time step values.
            /// </summary>
            /// <param name="dt">Current time step duration.</param>
            public void PrepareForIntegration(float dt)
            {
                //No reason to recalculate gravity * dt for every body; just cache it ahead of time.
                gravityDt = instance._gravity * dt;
                indt = dt;
            }

            /// <summary>
            /// Callback called for each active body within the simulation during body integration.
            /// </summary>
            /// <param name="bodyIndex">Index of the body being visited.</param>
            /// <param name="pose">Body's current pose.</param>
            /// <param name="localInertia">Body's current local inertia.</param>
            /// <param name="workerIndex">Index of the worker thread processing this body.</param>
            /// <param name="velocity">Reference to the body's current velocity to integrate.</param>
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void IntegrateVelocity(int bodyIndex, in RigidPose pose, in BodyInertia localInertia, int workerIndex, ref BodyVelocity velocity)
            {
                //Note that we avoid accelerating kinematics. Kinematics are any body with an inverse mass of zero (so a mass of ~infinity). No force can move them.
                if (localInertia.InverseMass > 0f)
                {
                    BepuRigidbodyComponent rb = BepuSimulation.getRigidFromIndex(bodyIndex) as BepuRigidbodyComponent;

                    velocity.Linear += rb.OverrideGravity ? BepuHelpers.ToBepu(rb.Gravity) * indt : gravityDt;

                    // damping?
                    if (rb.LinearDamping > 0f)
                        velocity.Linear -= velocity.Linear * indt * rb.LinearDamping;

                    if (rb.AngularDamping > 0f)
                        velocity.Angular -= velocity.Angular * indt * rb.AngularDamping;

                    // velocity cap?
                    if (rb.MaximumSpeed > 0f)
                    {
                        float sqrmag = velocity.Linear.LengthSquared();
                        if (sqrmag > rb.MaximumSpeed * rb.MaximumSpeed)
                            velocity.Linear *= rb.MaximumSpeed / (float)Math.Sqrt(sqrmag);
                    }
                }
            }

            public void Initialize(BepuPhysics.Simulation simulation) { }
        }

        /// <summary>
        /// Initializes the Physics engine using the specified flags.
        /// </summary>
        /// <param name="processor"></param>
        /// <param name="configuration"></param>
        /// <exception cref="System.NotImplementedException">SoftBody processing is not yet available</exception>
        internal BepuSimulation(int iterations)
        {
            Game GameService = ServiceRegistry.instance.GetService<IGame>() as Game;
            GameService.SceneSystem.SceneInstance.EntityAdded += EntityAdded;
            GameService.SceneSystem.SceneInstance.EntityRemoved += EntityRemoved;
            GameService.SceneSystem.SceneInstance.ComponentChanged += ComponentChanged;

            poseCallbacks = new PoseIntegratorCallbacks();

            // we will give the simulation its own bufferpool
            internalSimulation = BepuPhysics.Simulation.Create(new BufferPool(), new NarrowPhaseCallbacks(), poseCallbacks, new BepuPhysics.PositionLastTimestepper(), iterations);
            instance = this;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void EntityAdded(object sender, Entity e)
        {
            foreach (BepuPhysicsComponent bpc in e.GetAll<BepuPhysicsComponent>())
                if (bpc.AutomaticAdd && bpc.ColliderShape != null) bpc.AddedToScene = true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void EntityRemoved(object sender, Entity e)
        {
            foreach (BepuPhysicsComponent bpc in e.GetAll<BepuPhysicsComponent>())
                bpc.AddedToScene = false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ComponentChanged(object sender, EntityComponentEventArgs e)
        {
            if (e.PreviousComponent is BepuPhysicsComponent rem) rem.AddedToScene = false;
            if (e.NewComponent is BepuPhysicsComponent add && add.AutomaticAdd && add.ColliderShape != null) add.AddedToScene = true;
        }

        /// <summary>
        /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
        /// </summary>
        public void Dispose()
        {
            Clear(true, true);
        }

        public RenderGroup ColliderShapesRenderGroup { get; set; } = RenderGroup.Group0;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void AddBodyReference(BepuStaticColliderComponent component)
        {
            if (component.HandleIndex >= StaticMappings.Length)
                Array.Resize(ref StaticMappings, StaticMappings.Length * 2);

            StaticMappings[component.HandleIndex] = component;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void AddBodyReference(BepuRigidbodyComponent component)
        {
            if (component.HandleIndex >= RigidMappings.Length)
                Array.Resize(ref RigidMappings, RigidMappings.Length * 2);

            RigidMappings[component.HandleIndex] = component;
        }

        internal void ProcessAdds()
        {
            foreach (BepuPhysicsComponent component in ToBeAdded)
            {
                if (component.HandleIndex > -1) continue; // already added
                if (component is BepuStaticColliderComponent scc)
                {
                    // static stuff needs positions set first
                    scc.preparePose();
                    using (simulationLocker.WriteLock())
                    {
                        scc.staticDescription.Collidable = scc.ColliderShape.GenerateDescription(internalSimulation, scc.SpeculativeMargin);
                        scc.myStaticHandle = internalSimulation.Statics.Add(scc.staticDescription);
                        AddBodyReference(scc);
                    }
                }
                else if (component is BepuRigidbodyComponent rigidBody)
                {
                    if (!rigidBody.IgnorePhysicsPosition)
                    {
                        if (!rigidBody.newPos) rigidBody.bodyDescription.Pose.Position = BepuHelpers.ToBepu(component.Entity.Transform.WorldPosition());
                        if (rigidBody.LocalPhysicsOffset.HasValue) rigidBody.bodyDescription.Pose.Position -= BepuHelpers.ToBepu(rigidBody.LocalPhysicsOffset.Value);
                    }
                    if (!rigidBody.RotationLock && !rigidBody.IgnorePhysicsRotation && !rigidBody.newRotation) rigidBody.bodyDescription.Pose.Orientation = BepuHelpers.ToBepu(component.Entity.Transform.WorldRotation());
                    using (simulationLocker.WriteLock())
                    {
                        rigidBody.bodyDescription.Collidable = rigidBody.ColliderShape.GenerateDescription(internalSimulation, rigidBody.SpeculativeMargin);
                        AllRigidbodies.Add(rigidBody);
                        rigidBody.InternalBody.Handle = internalSimulation.Bodies.Add(rigidBody.bodyDescription);
                        AddBodyReference(rigidBody);
                        // are we starting inactive?
                        if (rigidBody.wasAwake == false) rigidBody.InternalBody.Awake = false;
                    }
                }
            }
            ToBeAdded.Clear();
        }

        internal void ProcessRemovals()
        {
            foreach (BepuPhysicsComponent component in ToBeRemoved)
            {
                if (component.HandleIndex == -1) continue; // already removed
                if (component is BepuStaticColliderComponent scc)
                {
                    StaticHandle sh = scc.myStaticHandle;
                    using(simulationLocker.WriteLock())
                    {
                        scc.myStaticHandle.Value = -1;
                        internalSimulation.Statics.Remove(sh);
                        StaticMappings[sh.Value] = null;
                    }
                    if (scc.DisposeMeshOnDetach) scc.DisposeMesh();
                } 
                else if (component is BepuRigidbodyComponent rigidBody)
                {
                    BodyHandle bh = rigidBody.InternalBody.Handle;
                    using(simulationLocker.WriteLock())
                    {
                        rigidBody.InternalBody.Handle.Value = -1;
                        internalSimulation.Bodies.Remove(bh);
                        RigidMappings[bh.Value] = null;
                        AllRigidbodies.Remove(rigidBody);
                    }
                    rigidBody.wasAwake = true; // don't remember whether we were asleep or not, go to default
                    rigidBody.processingPhysicalContactCount = 0;
                    rigidBody.CurrentPhysicalContactsCount = 0;
                }
            }
            ToBeRemoved.Clear();
        }

        struct RayHitClosestHandler : IRayHitHandler
        {
            public CollisionFilterGroupFlags findGroups;
            public float furthestHitSoFar, startLength;
            public BepuHitResult HitCollidable;
            public BepuPhysicsComponent skipComponent;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool AllowTest(CollidableReference collidable)
            {
                return ((uint)getFromReference(collidable).CollisionGroup & (uint)findGroups) != 0;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool AllowTest(CollidableReference collidable, int childIndex)
            {
                return ((uint)getFromReference(collidable).CollisionGroup & (uint)findGroups) != 0;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void OnRayHit(in RayData ray, ref float maximumT, float t, in System.Numerics.Vector3 normal, CollidableReference collidable, int childIndex)
            {
                if (t < furthestHitSoFar)
                {
                    var component = getFromReference(collidable);
                    if (component == skipComponent) return;

                    //Cache the earliest impact.
                    furthestHitSoFar = t;
                    HitCollidable.HitFraction = t / startLength;
                    HitCollidable.Normal.X = normal.X;
                    HitCollidable.Normal.Y = normal.Y;
                    HitCollidable.Normal.Z = normal.Z;
                    HitCollidable.Point.X = ray.Origin.X + ray.Direction.X * t;
                    HitCollidable.Point.Y = ray.Origin.Y + ray.Direction.Y * t;
                    HitCollidable.Point.Z = ray.Origin.Z + ray.Direction.Z * t;
                    HitCollidable.Collider = component;
                    HitCollidable.Succeeded = true;
                }

                //We are only interested in the earliest hit. This callback is executing within the traversal, so modifying maximumT informs the traversal
                //that it can skip any AABBs which are more distant than the new maximumT.
                if (t < maximumT)
                    maximumT = t;
            }
        }

        struct RayHitAllHandler : IRayHitHandler
        {
            public CollisionFilterGroupFlags hitGroups;
            public float startLength;
            public List<BepuHitResult> HitCollidables;
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool AllowTest(CollidableReference collidable)
            {
                return ((uint)getFromReference(collidable).CollisionGroup & (uint)hitGroups) != 0;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool AllowTest(CollidableReference collidable, int childIndex)
            {
                return ((uint)getFromReference(collidable).CollisionGroup & (uint)hitGroups) != 0;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void OnRayHit(in RayData ray, ref float maximumT, float t, in System.Numerics.Vector3 normal, CollidableReference collidable, int childIndex)
            {
                BepuHitResult HitCollidable = new BepuHitResult();
                HitCollidable.HitFraction = t / startLength;
                HitCollidable.Normal.X = normal.X;
                HitCollidable.Normal.Y = normal.Y;
                HitCollidable.Normal.Z = normal.Z;
                HitCollidable.Point.X = ray.Origin.X + ray.Direction.X * t;
                HitCollidable.Point.Y = ray.Origin.Y + ray.Direction.Y * t;
                HitCollidable.Point.Z = ray.Origin.Z + ray.Direction.Z * t;
                HitCollidable.Collider = getFromReference(collidable);
                HitCollidable.Succeeded = true;
                HitCollidables.Add(HitCollidable);
            }
        }

        /// <summary>
        /// Raycasts and returns the closest hit
        /// </summary>
        /// <param name="from">From.</param>
        /// <param name="to">To.</param>
        /// <param name="filterGroup">The collision group of this raycast</param>
        /// <param name="hitGroups">The collision group that this raycast can collide with</param>
        /// <returns>The list with hit results.</returns>
        public BepuHitResult Raycast(Vector3 from, Vector3 to, CollisionFilterGroupFlags hitGroups = DefaultFlags, BepuPhysicsComponent skipComponent = null)
        {
            Vector3 diff = to - from;
            float length = diff.Length();
            float inv = 1.0f / length;
            diff.X *= inv;
            diff.Y *= inv;
            diff.Z *= inv;
            return Raycast(from, diff, length, hitGroups, skipComponent);
        }

        /// <summary>
        /// Raycasts and returns the closest hit, if possible without locking the broadphase
        /// </summary>
        /// <param name="from">From.</param>
        /// <param name="to">To.</param>
        /// <param name="filterGroup">The collision group of this raycast</param>
        /// <param name="hitGroups">The collision group that this raycast can collide with</param>
        /// <returns>true if result was successfully updated, false otherwise</returns>
        public bool TryFastRaycast(out BepuHitResult result, Vector3 from, Vector3 to, CollisionFilterGroupFlags hitGroups = DefaultFlags, BepuPhysicsComponent skipComponent = null)
        {
            Vector3 diff = to - from;
            float length = diff.Length();
            float inv = 1.0f / length;
            diff.X *= inv;
            diff.Y *= inv;
            diff.Z *= inv;
            return TryFastRaycast(out result, from, diff, length, hitGroups, skipComponent);
        }

        /// <summary>
        /// Raycasts and returns the closest hit
        /// </summary>
        /// <param name="from">From.</param>
        /// <param name="direction">normalized direction<param>
        /// <param name="filterGroup">The collision group of this raycast</param>
        /// <param name="hitGroups">The collision group that this raycast can collide with</param>
        /// <returns>The list with hit results.</returns>
        public BepuHitResult Raycast(Vector3 from, Vector3 direction, float length, CollisionFilterGroupFlags hitGroups = DefaultFlags, BepuPhysicsComponent skipComponent = null)
        {
            RayHitClosestHandler rhch = new RayHitClosestHandler()
            {
                findGroups = hitGroups,
                startLength = length,
                furthestHitSoFar = float.MaxValue,
                skipComponent = skipComponent
            };
            using(simulationLocker.ReadLock())
            {
                using (internalSimulation.BroadphaseLocker.ReadLock())
                {
                    internalSimulation.RayCast(new System.Numerics.Vector3(from.X, from.Y, from.Z), new System.Numerics.Vector3(direction.X, direction.Y, direction.Z), length, ref rhch);
                }
            }
            return rhch.HitCollidable;
        }

        /// <summary>
        /// Tries to do a raycast right now without locking the broadphase
        /// </summary>
        /// <param name="from">From.</param>
        /// <param name="direction">normalized direction</param>
        /// <param name="filterGroup">The collision group of this raycast</param>
        /// <param name="hitGroups">The collision group that this raycast can collide with</param>
        /// <returns>true if a successful raycast was done, false if result is left untouched</returns>
        public bool TryFastRaycast(out BepuHitResult result, Vector3 from, Vector3 direction, float length, CollisionFilterGroupFlags hitGroups = DefaultFlags, BepuPhysicsComponent skipComponent = null)
        {
            RayHitClosestHandler rhch = new RayHitClosestHandler()
            {
                findGroups = hitGroups,
                startLength = length,
                furthestHitSoFar = float.MaxValue,
                skipComponent = skipComponent
            };
            using (simulationLocker.ReadLock())
            {
                if (internalSimulation.BroadphaseLocker.TryEnterReadLock(0))
                {
                    internalSimulation.RayCast(new System.Numerics.Vector3(from.X, from.Y, from.Z), new System.Numerics.Vector3(direction.X, direction.Y, direction.Z), length, ref rhch);
                    internalSimulation.BroadphaseLocker.ExitReadLock();
                    result = rhch.HitCollidable;
                    return true;
                }
            }
            result = default;
            return false;
        }

        /// <summary>
        /// Raycasts penetrating any shape the ray encounters.
        /// Filtering by CollisionGroup
        /// </summary>
        /// <param name="from">From.</param>
        /// <param name="to">To.</param>
        /// <param name="resultsOutput">The list to fill with results.</param>
        /// <param name="filterGroup">The collision group of this raycast</param>
        /// <param name="hitGroups">The collision group that this raycast can collide with</param>
        public void RaycastPenetrating(Vector3 from, Vector3 to, List<BepuHitResult> resultsOutput, CollisionFilterGroupFlags hitGroups = DefaultFlags)
        {
            Vector3 diff = to - from;
            float length = diff.Length();
            float inv = 1.0f / length;
            diff.X *= inv;
            diff.Y *= inv;
            diff.Z *= inv;
            RaycastPenetrating(from, diff, length, resultsOutput, hitGroups);
        }

        /// <summary>
        /// Raycasts penetrating any shape the ray encounters.
        /// Filtering by CollisionGroup
        /// </summary>
        /// <param name="from">From.</param>
        /// <param name="to">To.</param>
        /// <param name="resultsOutput">The list to fill with results.</param>
        /// <param name="filterGroup">The collision group of this raycast</param>
        /// <param name="hitGroups">The collision group that this raycast can collide with</param>
        public void RaycastPenetrating(Vector3 from, Vector3 direction, float length, List<BepuHitResult> resultsOutput, CollisionFilterGroupFlags hitGroups = DefaultFlags)
        {
            RayHitAllHandler rhch = new RayHitAllHandler()
            {
                hitGroups = hitGroups,
                HitCollidables = resultsOutput,
                startLength = length
            };
            using (simulationLocker.ReadLock())
            {
                using (internalSimulation.BroadphaseLocker.ReadLock())
                {
                    internalSimulation.RayCast(new System.Numerics.Vector3(from.X, from.Y, from.Z), new System.Numerics.Vector3(direction.X, direction.Y, direction.Z), length, ref rhch);
                }
            }
        }

        struct SweepTestFirst : ISweepHitHandler
        {
            public CollisionFilterGroupFlags hitGroups;
            public BepuHitResult result;
            public float furthestHitSoFar, startLength;
            public BepuPhysicsComponent skipComponent;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool AllowTest(CollidableReference collidable)
            {
                return ((uint)getFromReference(collidable).CollisionGroup & (uint)hitGroups) != 0;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool AllowTest(CollidableReference collidable, int child)
            {
                return ((uint)getFromReference(collidable).CollisionGroup & (uint)hitGroups) != 0;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void OnHit(ref float maximumT, float t, in System.Numerics.Vector3 hitLocation, in System.Numerics.Vector3 hitNormal, CollidableReference collidable)
            {
                if (t < furthestHitSoFar)
                {
                    var component = getFromReference(collidable);
                    if (component == skipComponent) return;

                    furthestHitSoFar = t;
                    result.Succeeded = true;
                    result.Collider = component;
                    result.Normal.X = hitNormal.X;
                    result.Normal.Y = hitNormal.Y;
                    result.Normal.Z = hitNormal.Z;
                    result.Point.X = hitLocation.X;
                    result.Point.Y = hitLocation.Y;
                    result.Point.Z = hitLocation.Z;
                    result.HitFraction = t / startLength;
                }

                //Changing the maximum T value prevents the traversal from visiting any leaf nodes more distant than that later in the traversal.
                //It is effectively an optimization that you can use if you only care about the time of first impact.
                if (t < maximumT)
                    maximumT = t;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void OnHitAtZeroT(ref float maximumT, CollidableReference collidable)
            {
                var component = getFromReference(collidable);
                if (component == skipComponent) return;

                result.Succeeded = true;
                result.Collider = component;
                maximumT = 0;
                furthestHitSoFar = 0;
            }
        }

        struct SweepTestAll : ISweepHitHandler
        {
            public CollisionFilterGroupFlags hitGroups;
            public List<BepuHitResult> results;
            public float startLength;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool AllowTest(CollidableReference collidable)
            {
                return ((uint)getFromReference(collidable).CollisionGroup & (uint)hitGroups) != 0;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool AllowTest(CollidableReference collidable, int child)
            {
                return ((uint)getFromReference(collidable).CollisionGroup & (uint)hitGroups) != 0;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void OnHit(ref float maximumT, float t, in System.Numerics.Vector3 hitLocation, in System.Numerics.Vector3 hitNormal, CollidableReference collidable)
            {
                BepuHitResult result = new BepuHitResult();
                result.Succeeded = true;
                result.Collider = getFromReference(collidable);
                result.Normal.X = hitNormal.X;
                result.Normal.Y = hitNormal.Y;
                result.Normal.Z = hitNormal.Z;
                result.Point.X = hitLocation.X;
                result.Point.Y = hitLocation.Y;
                result.Point.Z = hitLocation.Z;
                result.HitFraction = t / startLength;
                results.Add(result);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public void OnHitAtZeroT(ref float maximumT, CollidableReference collidable)
            {
                BepuHitResult result = new BepuHitResult();
                result.Succeeded = true;
                result.Collider = getFromReference(collidable);
                results.Add(result);
            }
        }

        /// <summary>
        /// Performs a sweep test using a collider shape and returns the closest hit
        /// </summary>
        /// <param name="shape">The shape.</param>
        /// <param name="from">From.</param>
        /// <param name="to">To.</param>
        /// <param name="filterGroup">The collision group of this shape sweep</param>
        /// <param name="filterFlags">The collision group that this shape sweep can collide with</param>
        /// <returns></returns>
        /// <exception cref="System.Exception">This kind of shape cannot be used for a ShapeSweep.</exception>
        public BepuHitResult ShapeSweep<TShape>(TShape shape, Vector3 position, Xenko.Core.Mathematics.Quaternion rotation, Vector3 endpoint, CollisionFilterGroupFlags hitGroups = DefaultFlags, BepuPhysicsComponent skipComponent = null) where TShape : unmanaged, IConvexShape
        {
            Vector3 diff = endpoint - position;
            float length = diff.Length();
            float inv = 1.0f / length;
            diff.X *= inv;
            diff.Y *= inv;
            diff.Z *= inv;
            return ShapeSweep(shape, position, rotation, diff, length, hitGroups, skipComponent);
        }

        /// <summary>
        /// Performs a sweep test using a collider shape and returns the closest hit
        /// </summary>
        /// <param name="shape">The shape.</param>
        /// <param name="from">From.</param>
        /// <param name="to">To.</param>
        /// <param name="filterGroup">The collision group of this shape sweep</param>
        /// <param name="filterFlags">The collision group that this shape sweep can collide with</param>
        /// <returns></returns>
        /// <exception cref="System.Exception">This kind of shape cannot be used for a ShapeSweep.</exception>
        public BepuHitResult ShapeSweep<TShape>(TShape shape, Vector3 position, Xenko.Core.Mathematics.Quaternion rotation, Vector3 direction, float length, CollisionFilterGroupFlags hitGroups = DefaultFlags, BepuPhysicsComponent skipComponent = null) where TShape : unmanaged, IConvexShape
        {
            SweepTestFirst sshh = new SweepTestFirst()
            {
                hitGroups = hitGroups,
                startLength = length,
                furthestHitSoFar = float.MaxValue,
                skipComponent = skipComponent
            };
            RigidPose rp = new RigidPose();
            rp.Position.X = position.X;
            rp.Position.Y = position.Y;
            rp.Position.Z = position.Z;
            rp.Orientation.X = rotation.X;
            rp.Orientation.Y = rotation.Y;
            rp.Orientation.Z = rotation.Z;
            rp.Orientation.W = rotation.W;
            using (simulationLocker.ReadLock())
            {
                using (internalSimulation.BroadphaseLocker.ReadLock())
                {
                    internalSimulation.Sweep(shape, rp, new BodyVelocity(new System.Numerics.Vector3(direction.X, direction.Y, direction.Z)), length, safeBufferPool, ref sshh);
                }
            }
            return sshh.result;
        }

        /// <summary>
        /// Performs a sweep test using a collider shape and never stops until "to"
        /// </summary>
        /// <param name="shape">The shape.</param>
        /// <param name="from">From.</param>
        /// <param name="to">To.</param>
        /// <param name="resultsOutput">The list to fill with results.</param>
        /// <param name="filterGroup">The collision group of this shape sweep</param>
        /// <param name="filterFlags">The collision group that this shape sweep can collide with</param>
        /// <exception cref="System.Exception">This kind of shape cannot be used for a ShapeSweep.</exception>
        public void ShapeSweepPenetrating<TShape>(TShape shape, Vector3 position, Xenko.Core.Mathematics.Quaternion rotation, Vector3 endpoint, List<BepuHitResult> output, CollisionFilterGroupFlags hitGroups = DefaultFlags) where TShape : unmanaged, IConvexShape
        {
            Vector3 diff = endpoint - position;
            float length = diff.Length();
            float inv = 1.0f / length;
            diff.X *= inv;
            diff.Y *= inv;
            diff.Z *= inv;
            ShapeSweepPenetrating(shape, position, rotation, diff, length, output, hitGroups);
        }

        /// <summary>
        /// Performs a sweep test using a collider shape and never stops until "to"
        /// </summary>
        /// <param name="shape">The shape.</param>
        /// <param name="from">From.</param>
        /// <param name="to">To.</param>
        /// <param name="resultsOutput">The list to fill with results.</param>
        /// <param name="filterGroup">The collision group of this shape sweep</param>
        /// <param name="filterFlags">The collision group that this shape sweep can collide with</param>
        /// <exception cref="System.Exception">This kind of shape cannot be used for a ShapeSweep.</exception>
        public void ShapeSweepPenetrating<TShape>(TShape shape, Vector3 position, Xenko.Core.Mathematics.Quaternion rotation, Vector3 direction, float length, List<BepuHitResult> output, CollisionFilterGroupFlags hitGroups = DefaultFlags) where TShape : unmanaged, IConvexShape
        {
            SweepTestAll sshh = new SweepTestAll()
            {
                hitGroups = hitGroups,
                startLength = length,
                results = output
            };
            RigidPose rp = new RigidPose();
            rp.Position.X = position.X;
            rp.Position.Y = position.Y;
            rp.Position.Z = position.Z;
            rp.Orientation.X = rotation.X;
            rp.Orientation.Y = rotation.Y;
            rp.Orientation.Z = rotation.Z;
            rp.Orientation.W = rotation.W;
            using (simulationLocker.ReadLock())
            {
                using (internalSimulation.BroadphaseLocker.ReadLock())
                {
                    internalSimulation.Sweep(shape, rp, new BodyVelocity(new System.Numerics.Vector3(direction.X, direction.Y, direction.Z)), length, safeBufferPool, ref sshh);
                }
            }
        }

        private System.Numerics.Vector3 _gravity = new System.Numerics.Vector3(0f, -9.81f, 0f);

        /// <summary>
        /// Gets or sets the gravity.
        /// </summary>
        /// <value>
        /// The gravity.
        /// </value>
        /// <exception cref="System.Exception">
        /// Cannot perform this action when the physics engine is set to CollisionsOnly
        /// or
        /// Cannot perform this action when the physics engine is set to CollisionsOnly
        /// </exception>
        public Vector3 Gravity
        {
            get
            {
                return new Vector3(_gravity.X, _gravity.Y, _gravity.Z);
            }
            set
            {
                _gravity.X = value.X;
                _gravity.Y = value.Y;
                _gravity.Z = value.Z;
            }
        }

        internal void Simulate(float deltaTime)
        {
            if (internalSimulation == null) return;

            using (simulationLocker.ReadLock())
            {
                internalSimulation.Timestep(deltaTime * TimeScale, threadDispatcher);
            }
        }
    }
}
