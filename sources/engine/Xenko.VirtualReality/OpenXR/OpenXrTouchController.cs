using System;
using System.Collections.Generic;
using System.Text;
using Silk.NET.OpenXR;
using Xenko.Core.Mathematics;
using Xenko.Games;

namespace Xenko.VirtualReality
{
    class OpenXrTouchController : TouchController
    {
        private OpenXRHmd baseHMD;
        private SpaceLocation handLocation;
        private TouchControllerHand myHand;

        public ulong[] hand_paths = new ulong[12];

        private bool[,] buttonWasDown = new bool[(int)TouchControllerButton.MaxCount, 2];

        public Space myAimSpace, myGripSpace;
        public Silk.NET.OpenXR.Action myAimAction, myGripAction;

        public OpenXrTouchController(OpenXRHmd hmd, TouchControllerHand whichHand)
        {
            baseHMD = hmd;
            handLocation.Type = StructureType.TypeSpaceLocation;
            myHand = whichHand;

            myAimAction = OpenXRInput.MappedActions[(int)myHand, (int)OpenXRInput.HAND_PATHS.AimPosition];
            ActionSpaceCreateInfo action_space_info = new ActionSpaceCreateInfo()
            {
                Type = StructureType.TypeActionSpaceCreateInfo,
                Action = myAimAction,
                PoseInActionSpace = new Posef(new Quaternionf(0f, 0f, 0f, 1f), new Vector3f(0f, 0f, 0f)),
            };
            OpenXRHmd.CheckResult(baseHMD.Xr.CreateActionSpace(baseHMD.globalSession, in action_space_info, ref myAimSpace), "CreateActionSpaceAim");

            myGripAction = OpenXRInput.MappedActions[(int)myHand, (int)OpenXRInput.HAND_PATHS.GripPosition];
            ActionSpaceCreateInfo action_grip_space_info = new ActionSpaceCreateInfo()
            {
                Type = StructureType.TypeActionSpaceCreateInfo,
                Action = myGripAction,
                PoseInActionSpace = action_space_info.PoseInActionSpace,
            };
            OpenXRHmd.CheckResult(baseHMD.Xr.CreateActionSpace(baseHMD.globalSession, in action_grip_space_info, ref myGripSpace), "CreateActionSpaceGrip");
        }

        private Vector3 currentPos;
        public override Vector3 Position => currentPos;

        private Quaternion currentRot;
        public override Quaternion Rotation => currentRot;

        private Vector3 currentVel;
        public override Vector3 LinearVelocity => currentVel;

        private Vector3 currentAngVel;
        public override Vector3 AngularVelocity => currentAngVel;

        public override DeviceState State => (handLocation.LocationFlags & SpaceLocationFlags.SpaceLocationPositionValidBit) != 0 ? DeviceState.Valid : DeviceState.OutOfRange;

        public override bool SwapTouchpadJoystick { get; set; }

        private Quaternion? holdOffsetGrip, holdOffsetPoint;
        private float _holdoffset_grip, _holdoffset_point;

        /// <summary>
        /// Shortcut to setting offset for both grip and point
        /// </summary>
        public override float HoldAngleOffset
        {
            set
            {
                HoldAngleOffset_Grip = value;
                HoldAngleOffset_Point = value;
            }
        }

        public override float HoldAngleOffset_Grip
        {
            get => _holdoffset_grip; 
            set
            {
                _holdoffset_grip = value;
                holdOffsetGrip = value == 0 ? null : Quaternion.RotationXDeg(_holdoffset_grip);
            }
        }

        public override float HoldAngleOffset_Point {
            get => _holdoffset_point;
            set
            {
                _holdoffset_point = value;
                holdOffsetPoint = value == 0 ? null : Quaternion.RotationXDeg(_holdoffset_point);
            }
        }

        public override bool HasThumbstick { get; internal set; }

        public override bool HasTouchpad { get; internal set; }

        public override float Trigger => OpenXRInput.GetActionFloat(myHand, TouchControllerButton.Trigger, out _, OpenXRInput.INPUT_DESIRED.VALUE);

        public override float Grip => OpenXRInput.GetActionFloat(myHand, TouchControllerButton.Grip, out _, OpenXRInput.INPUT_DESIRED.VALUE);

        public override bool IndexPointing => false;

        public override bool IndexResting => false;

        public override bool ThumbUp => false;

        public override bool ThumbResting => false;

        public override Vector2 TouchpadAxis => GetAxis((int)TouchControllerButton.Touchpad);

        public override Vector2 ThumbstickAxis => GetAxis((int)TouchControllerButton.Thumbstick);

        public override string DebugControllerState()
        {
            string val = "TouchpadAxis: " + TouchpadAxis.ToString() + "\n" +
                         "ThumbstickAxis: " + ThumbstickAxis.ToString() + "\n";
            for (int i=0;i<8;i++)
            {
                TouchControllerButton tcb = (TouchControllerButton)i;
                val += tcb.ToString() + "Pressed: " + IsPressed(tcb) + "\n";
                val += tcb.ToString() + "Down: " + IsPressedDown(tcb) + "\n";
                val += tcb.ToString() + "Released: " + IsPressReleased(tcb) + "\n";
            }
            return val;
        }

        internal TouchControllerButton PickCorrectAxis(TouchControllerButton asked)
        {
            // first check swap
            if (SwapTouchpadJoystick)
            {
                switch (asked)
                {
                    case TouchControllerButton.Thumbstick:
                        asked = TouchControllerButton.Touchpad;
                        break;
                    case TouchControllerButton.Touchpad:
                        asked = TouchControllerButton.Thumbstick;
                        break;
                }
            }

            // if this isn't available, try the other one
            switch(asked)
            {
                case TouchControllerButton.Touchpad:
                    return HasTouchpad ? TouchControllerButton.Touchpad : TouchControllerButton.Thumbstick;
                case TouchControllerButton.Thumbstick:
                    return HasThumbstick ? TouchControllerButton.Thumbstick : TouchControllerButton.Touchpad;
            }

            return asked;
        }

        public override Vector2 GetAxis(int index)
        {
            TouchControllerButton button = PickCorrectAxis(index == 0 ? TouchControllerButton.Thumbstick : TouchControllerButton.Touchpad);

            return new Vector2(OpenXRInput.GetActionFloat(myHand, button, out _, OpenXRInput.INPUT_DESIRED.XAXIS),
                               OpenXRInput.GetActionFloat(myHand, button, out _, OpenXRInput.INPUT_DESIRED.YAXIS));
        }

        public override bool IsPressed(TouchControllerButton button)
        {
            return OpenXRInput.GetActionBool(myHand, PickCorrectAxis(button), out _);
        }

        public override bool IsPressedDown(TouchControllerButton button)
        {
            bool isDownNow = OpenXRInput.GetActionBool(myHand, PickCorrectAxis(button), out _);
            bool wasPressedDown = isDownNow && buttonWasDown[(int)button, 1] == false;
            buttonWasDown[(int)button, 0] = isDownNow;
            return wasPressedDown;
        }

        public override bool IsPressReleased(TouchControllerButton button)
        {
            bool isDownNow = OpenXRInput.GetActionBool(myHand, PickCorrectAxis(button), out _);
            bool wasReleasedUp = !isDownNow && buttonWasDown[(int)button, 1] == true;
            buttonWasDown[(int)button, 0] = isDownNow;
            return wasReleasedUp;
        }

        public override bool IsTouched(TouchControllerButton button)
        {
            // unsupported right now
            return false;
        }

        public override bool IsTouchedDown(TouchControllerButton button)
        {
            // unsupported right now
            return false;
        }

        public override bool IsTouchReleased(TouchControllerButton button)
        {
            // unsupported right now
            return false;
        }

        public override unsafe bool Vibrate(double? duration = null, float frequency = 0f, float amplitude = 1f)
        {
            HapticActionInfo hai = new HapticActionInfo()
            {
                Action = OpenXRInput.MappedActions[(int)myHand, (int)OpenXRInput.HAND_PATHS.HapticOut],
                Type = StructureType.TypeHapticActionInfo               
            };

            HapticVibration hv = new HapticVibration()
            {
                Amplitude = amplitude,                
                Frequency = frequency,
                Duration = duration.HasValue ? (long)Math.Round(1000000000.0 * duration.Value) : -1,
                Type = StructureType.TypeHapticVibration
            };

            return baseHMD.Xr.ApplyHapticFeedback(baseHMD.globalSession, &hai, (HapticBaseHeader*)&hv) == Result.Success;
        }

        public override unsafe void Update(GameTime time)
        {
            // move our data on historical button down state over
            for (int i = 0; i < (int)TouchControllerButton.MaxCount; i++)
                buttonWasDown[i, 1] = buttonWasDown[i, 0];

            ActionStatePose hand_pose_state = new ActionStatePose()
            {
                Type = StructureType.TypeActionStatePose,
            };

            ActionStateGetInfo get_info = new ActionStateGetInfo()
            {
                Type = StructureType.TypeActionStateGetInfo,
                Action = UseGripInsteadOfAimPose ? myGripAction : myAimAction,
            };

            SpaceVelocity sv = new SpaceVelocity()
            {
                Type = StructureType.TypeSpaceVelocity
            };

            handLocation.Next = &sv;

            baseHMD.Xr.GetActionStatePose(baseHMD.globalSession, in get_info, ref hand_pose_state);

            baseHMD.Xr.LocateSpace(UseGripInsteadOfAimPose ? myGripSpace : myAimSpace, baseHMD.globalPlaySpace,
                                   baseHMD.globalFrameState.PredictedDisplayTime, ref handLocation);

            if ((sv.VelocityFlags & SpaceVelocityFlags.SpaceVelocityLinearValidBit) == 0 || sv.LinearVelocity.X == 0f && sv.LinearVelocity.Y == 0f && sv.LinearVelocity.Z == 0f)
            {
                // invalid linear velocity, try calculating it based on position difference
                currentVel.X = handLocation.Pose.Position.X - currentPos.X;
                currentVel.Y = handLocation.Pose.Position.Y - currentPos.Y;
                currentVel.Z = handLocation.Pose.Position.Z - currentPos.Z;
                currentVel /= (float)time.Elapsed.TotalSeconds;
            }
            else
            {
                currentVel.X = sv.LinearVelocity.X;
                currentVel.Y = sv.LinearVelocity.Y;
                currentVel.Z = sv.LinearVelocity.Z;
            }

            currentAngVel.X = sv.AngularVelocity.X;
            currentAngVel.Y = sv.AngularVelocity.Y;
            currentAngVel.Z = sv.AngularVelocity.Z;

            currentPos.X = handLocation.Pose.Position.X;
            currentPos.Y = handLocation.Pose.Position.Y;
            currentPos.Z = handLocation.Pose.Position.Z;

            currentVel *= baseHMD.BodyScaling;
            currentPos *= baseHMD.BodyScaling;

            Quaternion? holdOffset = UseGripInsteadOfAimPose ? holdOffsetGrip : holdOffsetPoint;

            if (holdOffset.HasValue)
            {
                Quaternion orig = new Quaternion(handLocation.Pose.Orientation.X, handLocation.Pose.Orientation.Y,
                                                 handLocation.Pose.Orientation.Z, handLocation.Pose.Orientation.W);
                currentRot = holdOffset.Value * orig;
            }
            else
            {
                currentRot.X = handLocation.Pose.Orientation.X;
                currentRot.Y = handLocation.Pose.Orientation.Y;
                currentRot.Z = handLocation.Pose.Orientation.Z;
                currentRot.W = handLocation.Pose.Orientation.W;
            }
        }
    }
}
