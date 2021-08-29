// Copyright (c) Xenko contributors (https://xenko.com) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.
#if XENKO_GRAPHICS_API_VULKAN || XENKO_GRAPHICS_API_DIRECT3D11

using System;
using System.Runtime.CompilerServices;
using Xenko.Core.Mathematics;
using Xenko.Games;

namespace Xenko.VirtualReality
{
    internal class OpenVRTouchController : TouchController
    {
        private readonly OpenVR.Controller.Hand hand;
        private int controllerIndex = -1;
        private OpenVR.Controller controller;
        private DeviceState internalState;
        private Vector3 currentPos;
        private Vector3 currentLinearVelocity;
        private Vector3 currentAngularVelocity;
        private Quaternion currentRot;

        private Quaternion? holdOffset;
        private float _holdoffset;

        public override float HoldAngleOffset
        { 
            get => _holdoffset;
            set 
            {
                _holdoffset = value;

                holdOffset = Quaternion.RotationXDeg(_holdoffset);
            }
        }

        public override bool SwapTouchpadJoystick { get; set; }

        internal OpenVRTouchController(TouchControllerHand hand)
        {
            this.hand = (OpenVR.Controller.Hand)hand;
        }

        public override string DebugControllerState()
        {
            if (controller == null) return "No controller found!";
            return "Axis0: " + controller.State.rAxis0.x.ToString() + ", " + controller.State.rAxis0.y.ToString() + "\n" +
                   "Axis1: " + controller.State.rAxis1.x.ToString() + ", " + controller.State.rAxis1.y.ToString() + "\n" +
                   "Axis2: " + controller.State.rAxis2.x.ToString() + ", " + controller.State.rAxis2.y.ToString() + "\n" +
                   "Axis3: " + controller.State.rAxis3.x.ToString() + ", " + controller.State.rAxis3.y.ToString() + "\n" +
                   "buttonPressed: " + controller.State.ulButtonPressed.ToString() + "\n" +
                   "buttonTouched: " + controller.State.ulButtonTouched.ToString() + "\n" +
                   "packetNum: " + controller.State.unPacketNum.ToString();
        }

        public override void Update(GameTime gameTime)
        {
            var index = OpenVR.Controller.GetDeviceIndex(hand);

            if (controllerIndex != index)
            {
                if (index != -1)
                {
                    controller = new OpenVR.Controller(index);
                    controllerIndex = index;
                }
                else
                {
                    controller = null;
                }
            }

            if (controller != null)
            {
                controller.Update();

                Matrix mat;
                Vector3 vel, angVel;
                internalState = OpenVR.GetControllerPose(controllerIndex, out mat, out vel, out angVel);
                if (internalState != DeviceState.Invalid)
                {
                    Vector3 scale;
                    if (holdOffset.HasValue)
                    {
                        mat.Decompose(out scale, out Quaternion tempRot, out var currentPosPrescaled);
                        currentRot = holdOffset.Value * tempRot;
                        currentPos = currentPosPrescaled * HostDevice.BodyScaling;
                    }
                    else
                    {
                        mat.Decompose(out scale, out currentRot, out var currentPosPrescaled);
                        currentPos = currentPosPrescaled * HostDevice.BodyScaling;
                    }
                    currentLinearVelocity = vel * HostDevice.BodyScaling;
                    currentAngularVelocity = new Vector3(MathUtil.DegreesToRadians(angVel.X), MathUtil.DegreesToRadians(angVel.Y), MathUtil.DegreesToRadians(angVel.Z));
                }
            }

            base.Update(gameTime);
        }

        public override float Trigger => controller?.GetAxis(OpenVR.Controller.ButtonId.ButtonSteamVrTrigger).X ?? 0.0f;

        public override float Grip => controller?.GetPress(OpenVR.Controller.ButtonId.ButtonGrip) ?? false ? 1f : 0f;

        public override bool IndexPointing => !controller?.GetTouch(OpenVR.Controller.ButtonId.ButtonSteamVrTrigger) ?? false; //not so accurate

        public override bool IndexResting => controller?.GetTouch(OpenVR.Controller.ButtonId.ButtonSteamVrTrigger) ?? false;

        public override bool ThumbUp => !controller?.GetTouch(OpenVR.Controller.ButtonId.ButtonSteamVrTouchpad) ?? false;

        public override bool ThumbResting => controller?.GetTouch(OpenVR.Controller.ButtonId.ButtonSteamVrTouchpad) ?? false;

        public override Vector2 TouchpadAxis => controller?.GetAxis(SwapTouchpadJoystick ? OpenVR.Controller.ButtonId.ButtonAxis2 : OpenVR.Controller.ButtonId.ButtonAxis0) ?? Vector2.Zero;

        public override Vector2 ThumbstickAxis => controller?.GetAxis(SwapTouchpadJoystick ? OpenVR.Controller.ButtonId.ButtonAxis0 : OpenVR.Controller.ButtonId.ButtonAxis2) ?? Vector2.Zero;

        private OpenVR.Controller.ButtonId ToOpenVrButton(TouchControllerButton button)
        {
            switch (button)
            {
                case TouchControllerButton.Thumbstick:
                    return SwapTouchpadJoystick ? OpenVR.Controller.ButtonId.ButtonSteamVrTouchpad : OpenVR.Controller.ButtonId.ButtonAxis2;
                case TouchControllerButton.ButtonXA:
                    return OpenVR.Controller.ButtonId.ButtonA;
                case TouchControllerButton.Touchpad:
                    return SwapTouchpadJoystick ? OpenVR.Controller.ButtonId.ButtonAxis2 : OpenVR.Controller.ButtonId.ButtonSteamVrTouchpad;              
                case TouchControllerButton.Trigger:
                    return OpenVR.Controller.ButtonId.ButtonSteamVrTrigger;
                case TouchControllerButton.Grip:
                    return OpenVR.Controller.ButtonId.ButtonGrip;
                case TouchControllerButton.ButtonYB:
                case TouchControllerButton.Menu:
                    return OpenVR.Controller.ButtonId.ButtonApplicationMenu;
                case TouchControllerButton.System:
                    return OpenVR.Controller.ButtonId.ButtonSystem;
                default:
                    return OpenVR.Controller.ButtonId.ButtonMax;
            }
        }

        public override bool UseGripInsteadOfAimPose 
        { 
            get => throw new NotImplementedException("Only OpenXR for UseGripInsteadOfAimPose");
            set => throw new NotImplementedException("Only OpenXR for UseGripInsteadOfAimPose");
        }

        public override bool HasThumbstick
        {
            get => throw new NotImplementedException("Only OpenXR for HasThumbstick");
            internal set => throw new NotImplementedException("Only OpenXR for HasThumbstick");
        }

        public override bool HasTouchpad
        {
            get => throw new NotImplementedException("Only OpenXR for HasThumbstick");
            internal set => throw new NotImplementedException("Only OpenXR for HasThumbstick");
        }

        public override bool IsPressedDown(TouchControllerButton button)
        {
            return controller?.GetPressDown(ToOpenVrButton(button)) ?? false;
        }

        public override bool IsTouchedDown(TouchControllerButton button)
        {
            return controller?.GetTouchDown(ToOpenVrButton(button)) ?? false;
        }

        public override bool IsPressed(TouchControllerButton button)
        {
            return controller?.GetPress(ToOpenVrButton(button)) ?? false;
        }

        public override bool IsTouched(TouchControllerButton button)
        {
            return controller?.GetTouch(ToOpenVrButton(button)) ?? false;
        }

        public override bool IsPressReleased(TouchControllerButton button)
        {
            return controller?.GetPressUp(ToOpenVrButton(button)) ?? false;
        }

        public override bool IsTouchReleased(TouchControllerButton button)
        {
            return controller?.GetTouchUp(ToOpenVrButton(button)) ?? false;
        }

        public override bool Vibrate(float amount = 1f)
        {
            if (amount <= 0f) return false;
            Valve.VR.OpenVR.System.TriggerHapticPulse(controller.ControllerIndex, 0, (ushort)(1000f * amount));
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public override Vector2 GetAxis(int index)
        {
            return controller?.GetAxis((OpenVR.Controller.ButtonId)(index + (int)OpenVR.Controller.ButtonId.ButtonAxis0)) ?? Vector2.Zero;
        }

        public override Vector3 Position => currentPos;

        public override Quaternion Rotation => currentRot;

        public override Vector3 LinearVelocity => currentLinearVelocity;

        public override Vector3 AngularVelocity => currentAngularVelocity;

        public override DeviceState State => internalState;
    }
}

#endif
