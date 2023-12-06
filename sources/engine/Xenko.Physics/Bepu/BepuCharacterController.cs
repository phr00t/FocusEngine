using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using BepuPhysics.Collidables;
using Xenko.Core;
using Xenko.Core.Mathematics;
using Xenko.Engine;
using Xenko.Games;
using Xenko.Rendering.Images;
using Xenko.VirtualReality;

namespace Xenko.Physics.Bepu
{
    /// <summary>
    /// Helper object for handling character movement with a base physical body (and optional child camera/VR).
    /// </summary>
    public class BepuCharacterController
    {
        /// <summary>
        /// Generated rigidbody
        /// </summary>
        public BepuRigidbodyComponent Body { get; internal set; }

        /// <summary>
        /// Camera, if found off of the baseBody
        /// </summary>
        public CameraComponent Camera { get; internal set; }

        private VRFOV fovReduction;
        private static Game internalGame;
        private bool VR;

        public float Height { get; internal set; }
        public float Radius { get; internal set; }

        private static ConcurrentDictionary<Vector2, Capsule> CapsuleCache = new ConcurrentDictionary<Vector2, Capsule>();

        private Capsule getCapsule(float radius, float height)
        {
            var key = new Vector2(radius, height);
            if (CapsuleCache.TryGetValue(key, out var cap))
                return cap;

            float capsule_len = height - radius * 2f;
            if (capsule_len <= 0f)
                throw new ArgumentOutOfRangeException("Height cannot be less than 2*radius for capsule shape (BepuCharacterController for " + Body.Entity.Name);

            cap = new BepuPhysics.Collidables.Capsule(radius, capsule_len);
            CapsuleCache[key] = cap;

            return cap;
        }

        /// <summary>
        /// Make a new BepuCharacterController helper for an entity, also useful for VR. Automatically will break off VR-tracked from Camera to base if using VR
        /// </summary>
        public BepuCharacterController(Entity baseBody, float height = 1.7f, float radius = 0.5f, bool VRMode = false, CollisionFilterGroups physics_group = CollisionFilterGroups.CharacterFilter,
                                       CollisionFilterGroupFlags collides_with = CollisionFilterGroupFlags.StaticFilter | CollisionFilterGroupFlags.KinematicFilter | CollisionFilterGroupFlags.DefaultFilter |
                                       CollisionFilterGroupFlags.EnemyFilter | CollisionFilterGroupFlags.CharacterFilter, HashSet<Entity> AdditionalVREntitiesToDisconnectFromCamera = null)
        {
            Height = height;
            Radius = radius;

            Body = baseBody.Get<BepuRigidbodyComponent>();

            if (Body == null)
            {
                Body = new BepuRigidbodyComponent(getCapsule(radius, height));
                baseBody.Add(Body);
            }
            else if (!(Body.ColliderShape is Capsule))
                throw new ArgumentException(baseBody.Name + " already has a rigidbody, but it isn't a Capsule shape!");

            Body.CollisionGroup = physics_group;
            Body.CanCollideWith = collides_with;
            VR = VRMode;

            if (AdditionalVREntitiesToDisconnectFromCamera == null)
                AdditionalVREntitiesToDisconnectFromCamera = new HashSet<Entity>();

            // can we find an attached camera?
            foreach (Entity e in baseBody.GetChildren())
            {
                var camCheck = e.Get<CameraComponent>();
                if (camCheck != null)
                {
                    Camera = camCheck;
                    break;
                }
            }

            Body.AttachEntityAtBottom = true;
            Body.IgnorePhysicsRotation = true;
            Body.IgnorePhysicsPosition = VR && Camera != null;
            Body.RotationLock = true;
            Body.ActionPerSimulationTick = UpdatePerSimulationTick;

            if (internalGame == null) internalGame = ServiceRegistry.instance?.GetService<IGame>() as Game;

            if (Camera != null && VRMode)
            {
                // can we get a fov reduction?
                var pp = internalGame.SceneSystem.GraphicsCompositor.PostProcessing;
                if (pp != null)
                {
                    for (int i = 0; i < pp.Count; i++)
                    {
                        var p = pp[i];
                        if (p.VRFOVFilter != null)
                        {
                            fovReduction = p.VRFOVFilter;
                            break;
                        }
                    }
                }

                // VR sets the camera transform
                Camera.VRHeadSetsTransform = true;

                // can we find any tracked stuff to pick off?
                foreach (Entity e in Camera.Entity.GetChildren())
                {
                    if (e.Transform.TrackVRHand != TouchControllerHand.None)
                        AdditionalVREntitiesToDisconnectFromCamera.Add(e);
                }

                foreach (Entity e in AdditionalVREntitiesToDisconnectFromCamera)
                    if (e.Transform.Parent == Camera.Entity.Transform) e.Transform.Parent = baseBody.Transform;
            }
        }

        /// <summary>
        /// If flying is true, gravity is set to zero and Donttouch_Y is set to false
        /// </summary>
        public bool Flying
        {
            get => Body.OverrideGravity;
            set
            {
                Body.OverrideGravity = value;
                Body.Gravity = Vector3.Zero;
                DontTouch_Y = !value;
            }
        }

        /// <summary>
        /// This uses some CPU, but can monitor things like OnGround() functionality
        /// </summary>
        public bool TrackCollisions
        {
            set
            {
                Body.CollectCollisionMaximumCount = value ? 8 : 0;
                Body.CollectCollisions = value;
                Body.SleepThreshold = value ? -1f : 0.01f;
                if (value) Body.IsActive = true;
            }
            get => Body.CollectCollisions;
        }

        /// <summary>
        /// Returns a contact if this is considered on the ground. Requires TrackCollisions to be true
        /// </summary>
        /// <param name="threshold"></param>
        /// <returns></returns>
        public BepuContact? OnGround(float threshold = 0.75f)
        {
            if (TrackCollisions == false)
                throw new InvalidOperationException("You need to set TrackCollisions to true for OnGround to work for CharacterCollision: " + Body.Entity.Name);

            try
            {
                Vector3 reverseGravity = -(Body.OverrideGravity ? Body.Gravity : BepuSimulation.instance.Gravity);
                reverseGravity.Normalize();
                for (int i = 0; i < Body.CurrentPhysicalContactsCount; i++)
                {
                    var contact = Body.CurrentPhysicalContacts[i];
                    if (Vector3.Dot(contact.Normal, reverseGravity) > threshold) return contact;
                }
            }
            catch (Exception) { }
            return null;
        }

        private bool forceBlackout;

        /// <summary>
        /// VR loading can cause annoying flicker. Turn this on just before loading to black out the screen (using the FOV reduction post processing filter). Turn off when done loading.
        /// </summary>
        public void SetVRLoadingBlackout(bool on)
        {
            if (!VR || fovReduction == null || forceBlackout == on) return;

            forceBlackout = on;

            if (on)
            {
                fovReduction.Enabled = true;
                fovReduction.Radius = 0f;
            }
            else
            {
                fovReduction.Enabled = false;
                fovReduction.Radius = 1f;
            }
        }

        public enum RESIZE_POSITION_OPTION
        {
            RepositionNone = 0,
            RepositionAll = 1,
            RepositionBaseEntityOnly = 2
        }

        /// <summary>
        /// This can change the shape of the rigidbody easily
        /// </summary>
        public void Resize(float? height, float? radius = null, RESIZE_POSITION_OPTION reposition = RESIZE_POSITION_OPTION.RepositionAll)
        {
            float useh = height ?? Height;
            float user = radius ?? Radius;

            if (useh == Height && user == Radius) return;

            Body.ColliderShape = getCapsule(user, useh);

            Height = useh;
            Radius = user;

            switch (reposition)
            {
                case RESIZE_POSITION_OPTION.RepositionAll:
                    SetPosition(Body.Entity.Transform.Position);
                    break;
                case RESIZE_POSITION_OPTION.RepositionBaseEntityOnly:
                    SetPosition(Body.Entity.Transform.Position, false);
                    break;
            }
        }

        /// <summary>
        /// Jump! Will set ApplySingleImpulse (overwriting anything that was there already)
        /// </summary>
        public void Jump(float amount)
        {
            ApplySingleImpulse = new Vector3(0f, amount, 0f);
        }

        /// <summary>
        /// Set how you want this character to move
        /// </summary>
        public Vector3 DesiredMovement;

        /// <summary>
        /// How to dampen the different axis during updating? Defaults to (15,0,15)
        /// </summary>
        public Vector3? MoveDampening = new Vector3(15f, 0f, 15f);

        /// <summary>
        /// Applying a single impulse to this (useful for jumps or pushes)
        /// </summary>
        public Vector3? ApplySingleImpulse;

        /// <summary>
        /// Only operate on X/Z in all situations? Useful for non-flying characters
        /// </summary>
        public bool DontTouch_Y = true;

        /// <summary>
        /// Even if we are flying, should we ignore VR headset Y changes for physics positioning? You generally should leave this as true.
        /// </summary>
        public bool IgnoreVRHeadsetYPhysics = true;

        /// <summary>
        /// Push the character with forces (true) or set velocity directly (false)
        /// </summary>
        public bool UseImpulseMovement = true;

        /// <summary>
        /// Multiplier for the impulse movement (defaults to 100)
        /// </summary>
        public float ImpulseMovementMultiplier = 125f;

        /// <summary>
        /// Multiplier for velocity movement (defaults to 3)
        /// </summary>
        public float VelocityMovementMultiplier = 3f;

        /// <summary>
        /// How height to set the camera when positioning, if using camera?
        /// </summary>
        public float CameraHeightPercent = 0.95f;

        /// <summary>
        /// If you'd like to perform an additional physics tick action on this rigidbody, use this
        /// </summary>
        public Action<BepuRigidbodyComponent, float> AdditionalPerPhysicsAction = null;

        private Vector3 oldPos;

        private void UpdatePerSimulationTick(BepuRigidbodyComponent _body, float frame_time)
        {
            // make sure we are awake if we want to be moving
            if (Body.IsActive == false)
                Body.IsActive = DesiredMovement != Vector3.Zero || ApplySingleImpulse.HasValue;

            if (Body.IgnorePhysicsPosition)
            {
                // use the last velocity to move our base
                Body.Entity.Transform.Position += (Body.Position - oldPos);
                oldPos = Body.Position;
            }

            // try to push our body
            if (UseImpulseMovement)
            {
                // get rid of y if we are not operating on it
                if (DontTouch_Y) DesiredMovement.Y = 0f;
                Body.InternalBody.ApplyLinearImpulse(BepuHelpers.ToBepu(DesiredMovement * frame_time * Body.Mass * ImpulseMovementMultiplier));
            }
            else if (DontTouch_Y)
            {
                Vector3 originalVel = Body.LinearVelocity;
                Vector3 newmove = new Vector3(DesiredMovement.X * VelocityMovementMultiplier, originalVel.Y, DesiredMovement.Z * VelocityMovementMultiplier);
                Body.InternalBody.Velocity.Linear = BepuHelpers.ToBepu(newmove);
            }
            else Body.InternalBody.Velocity.Linear = BepuHelpers.ToBepu(DesiredMovement * VelocityMovementMultiplier);

            // single impulse to apply?
            if (ApplySingleImpulse.HasValue)
            {
                Body.InternalBody.ApplyLinearImpulse(BepuHelpers.ToBepu(ApplySingleImpulse.Value));
                ApplySingleImpulse = null;
            }

            // apply MoveDampening, if any
            if (MoveDampening != null)
            {
                var vel = Body.InternalBody.Velocity.Linear;
                vel.X *= 1f - frame_time * MoveDampening.Value.X;
                vel.Y *= 1f - frame_time * MoveDampening.Value.Y;
                vel.Z *= 1f - frame_time * MoveDampening.Value.Z;
                Body.InternalBody.Velocity.Linear = vel;
            }

            // do we need to move our base body toward our camera head?
            if (Camera != null && VR)
            {
                Vector3 finalpos = Camera.Entity.Transform.WorldPosition();
                if (DontTouch_Y || IgnoreVRHeadsetYPhysics) finalpos.Y = Body.Position.Y;
                float xDist = Body.Position.X - finalpos.X;
                float yDist = Body.Position.Y - finalpos.Y;
                float zDist = Body.Position.Z - finalpos.Z;
                if (xDist * xDist + yDist * yDist + zDist * zDist > VRPhysicsMoveThreshold * VRPhysicsMoveThreshold)
                {
                    if (VRPhysicsCollisionCheckBeforeMove)
                    {
                        Vector3 gravitybump = -(Body.OverrideGravity ? Body.Gravity : BepuSimulation.instance.Gravity);
                        gravitybump.Normalize();
                        gravitybump *= 0.05f;
                        var result = BepuSimulation.instance.ShapeSweep<Capsule>((Capsule)Body.ColliderShape, Body.Position + gravitybump, Body.Rotation, finalpos + gravitybump, Body.CanCollideWith, Body);
                        if (result.Succeeded == false)
                        {
                            Body.Position = finalpos;
                            oldPos = finalpos;
                        }
                    }
                    else
                    {
                        Body.Position = finalpos;
                        oldPos = finalpos;
                    }
                }
            }

            if (AdditionalPerPhysicsAction != null)
                AdditionalPerPhysicsAction(_body, frame_time);
        }

        private float desiredPitch, pitch, yaw, desiredYaw;
        private bool shouldFlickTurn = true;

        public float MouseSensitivity = 3f;
        public bool InvertY = false;
        public bool VRSmoothTurn = false;
        public float VRSmoothTurnRate = 125f;
        public float VRSnapTurnAmount = 45f;
        public bool VRPressToTurn = false;
        public bool VRComfortMode = false;
        public float VRFOVReductionMin = 0.55f;
        public float VRFOVReductionSpeed = 10f;
        public bool VRPhysicsCollisionCheckBeforeMove = true;
        public float VRPhysicsMoveThreshold = 0.2f;

        /// <summary>
        /// Removes any FOV reduction caused by movement or forced-on state. Same as calling SetVRLoadingBlackout(false)
        /// </summary>
        public void ResetVRFOV()
        {
            forceBlackout = false;

            if (!VR || fovReduction == null) return;

            fovReduction.Enabled = false;
            fovReduction.Radius = 1f;
        }

        private void UpdateVRFOV(bool ForceFOVReduction, float frameTime)
        {
            float desiredRadius;
            if (ForceFOVReduction)
            {
                desiredRadius = VRFOVReductionMin;
            }
            else
            {
                Vector3 vel = Body.LinearVelocity;
                float speed = vel.Length();
                if (speed < 0.1f)
                {
                    desiredRadius = 1f;
                }
                else
                {
                    desiredRadius = Vector3.Dot(Camera.Entity.Transform.Forward(true), vel.Normalized());
                    if (desiredRadius < VRFOVReductionMin) desiredRadius = VRFOVReductionMin;
                    desiredRadius *= desiredRadius;
                }
            }
            if (fovReduction.Radius > desiredRadius)
            {
                frameTime *= VRFOVReductionSpeed;
                if (frameTime > 1f) frameTime = 1f;
            }
            fovReduction.Radius = desiredRadius * frameTime + fovReduction.Radius * (1f - frameTime);
            fovReduction.Enabled = fovReduction.Radius < 1f;
        }

        private void SetRotateButKeepCameraPos(Quaternion rotation)
        {
            var existingPosition = Camera.Entity.Transform.WorldPosition();
            Body.Entity.Transform.Rotation = rotation;
            var newPosition = Camera.Entity.Transform.WorldPosition(true);
            Body.Entity.Transform.Position -= (newPosition - existingPosition);
        }

        private void RotateButKeepCameraPos(Quaternion rotation)
        {
            var existingPosition = Camera.Entity.Transform.WorldPosition();
            Body.Entity.Transform.Rotation *= rotation;
            var newPosition = Camera.Entity.Transform.WorldPosition(true);
            Body.Entity.Transform.Position -= (newPosition - existingPosition);
        }

        /// <summary>
        /// Sets how the camera is looking for a player character. Has no effect in VR
        /// </summary>
        public void SetPlayerLook(float? yaw = null, float? pitch = null)
        {
            if (Camera == null || VR) return;
            this.yaw = this.desiredYaw = yaw ?? this.yaw;
            this.pitch = this.desiredPitch = pitch ?? this.pitch;
            Camera.Entity.Transform.Rotation = Quaternion.RotationYawPitchRoll(this.yaw, this.pitch, 0f);
        }

        /// <summary>
        /// Makes this character look at the target. flattenY will always be true in VR
        /// </summary>
        public void LookAt(Vector3 target, bool flattenY = false)
        {
            Vector3 myPos = Camera != null ? Camera.Entity.Transform.WorldPosition() : Body.Position;
            Vector3 diff = target - myPos;
            if (flattenY || VR) diff.Y = 0f;
            if (Camera != null)
            {
                if (!VR)
                {
                    Quaternion.LookAt(ref Camera.Entity.Transform.Rotation, diff);
                    var ypr = Camera.Entity.Transform.Rotation.YawPitchRoll;
                    yaw = desiredYaw = ypr.X;
                    pitch = desiredPitch = ypr.Y;
                }
                else
                {
                    Quaternion temp = Quaternion.Identity;
                    Quaternion camrot = Camera.Entity.Transform.Rotation;
                    camrot.Invert();
                    Quaternion.LookAt(ref temp, diff);
                    SetRotateButKeepCameraPos(temp * camrot);
                }
            }
            else Quaternion.LookAt(ref Body.Entity.Transform.Rotation, diff);
        }

        /// <summary>
        /// Use this to handle mouse/VR look, which operates on a camera (if found)
        /// </summary>
        public void HandleMouseAndVRLook()
        {
            float frame_time = (float)internalGame.UpdateTime.Elapsed.TotalSeconds;

            if (Camera == null)
                throw new ArgumentNullException("No camera to look with!");

            if (VR)
            {
                bool fov_check = false;

                if (VRSmoothTurn)
                {
                    // smooth turning
                    var rightController = VRDeviceSystem.GetSystem?.GetController(TouchControllerHand.Right);
                    if (rightController != null)
                    {
                        // wait, are we suppose to be pressing?
                        if (VRPressToTurn && rightController.IsPressed(TouchControllerButton.Thumbstick) == false) return;
                        // are we pushing enough?
                        Vector2 thumb = rightController.ThumbstickAxis;
                        if (thumb.X > 0.1f || thumb.X < -0.1f)
                        {
                            RotateButKeepCameraPos(global::Xenko.Core.Mathematics.Quaternion.RotationYDeg(thumb.X * frame_time * -VRSmoothTurnRate));
                            fov_check = true;
                        }
                    }
                }
                else
                {
                    // snap turning
                    if (VRPressToTurn)
                    {
                        if (VRButtons.LeftThumbstickLeft.IsPressed())
                            RotateButKeepCameraPos(Quaternion.RotationYDeg(VRSnapTurnAmount));
                        else if (VRButtons.LeftThumbstickRight.IsPressed())
                            RotateButKeepCameraPos(Quaternion.RotationYDeg(-VRSnapTurnAmount));
                    }
                    else
                    {
                        // flick to snap turn
                        var rightController = VRDeviceSystem.GetSystem?.GetController(TouchControllerHand.Right);
                        if (rightController != null)
                        {
                            Vector2 thumb = rightController.ThumbstickAxis;
                            if (thumb.X > 0.5f)
                            {
                                if (shouldFlickTurn)
                                {
                                    RotateButKeepCameraPos(Quaternion.RotationYDeg(-VRSnapTurnAmount));
                                    shouldFlickTurn = false;
                                }
                            }
                            else if (thumb.X < -0.5f)
                            {
                                if (shouldFlickTurn)
                                {
                                    RotateButKeepCameraPos(Quaternion.RotationYDeg(VRSnapTurnAmount));
                                    shouldFlickTurn = false;
                                }
                            }
                            else shouldFlickTurn = true;
                        }
                    }
                }

                if (fovReduction != null && !forceBlackout)
                {
                    if (VRComfortMode)
                        UpdateVRFOV(fov_check, frame_time);
                    else
                        fovReduction.Enabled = false;
                }

                return;
            }

            Vector2 rotationDelta = internalGame.Input.MouseDelta;

            // Take shortest path
            float deltaPitch = desiredPitch - pitch;
            float deltaYaw = (desiredYaw - yaw) % MathUtil.TwoPi;
            if (deltaYaw < 0) deltaYaw += MathUtil.TwoPi;
            if (deltaYaw > MathUtil.Pi) deltaYaw -= MathUtil.TwoPi;
            desiredYaw = yaw + deltaYaw;

            // Perform orientation transition
            yaw = Math.Abs(deltaYaw) < frame_time ? desiredYaw : yaw + frame_time * Math.Sign(deltaYaw);
            pitch = Math.Abs(deltaPitch) < frame_time ? desiredPitch : pitch + frame_time * Math.Sign(deltaPitch);

            desiredYaw = yaw -= 1.333f * rotationDelta.X * MouseSensitivity; // we want to rotate faster Horizontally and Vertically
            desiredPitch = pitch = MathUtil.Clamp(pitch - rotationDelta.Y * (InvertY ? -MouseSensitivity : MouseSensitivity), -MathUtil.PiOverTwo + 0.05f, MathUtil.PiOverTwo - 0.05f);

            Camera.Entity.Transform.Rotation = Quaternion.RotationYawPitchRoll(yaw, pitch, 0);
        }

        /// <summary>
        /// Set our position and center the camera (if used) on this
        /// </summary>
        public void SetPosition(Vector3 position, bool updateCamera = true)
        {
            if (Camera != null && !VR && updateCamera) {
                Camera.Entity.Transform.Position.X = 0f;
                Camera.Entity.Transform.Position.Y = Height * CameraHeightPercent;
                Camera.Entity.Transform.Position.Z = 0f;
            }
            Body.Entity.Transform.Position = position;
            position.Y += Height * 0.5f;
            Body.Position = position;
            oldPos = position;
        }
    }
}
