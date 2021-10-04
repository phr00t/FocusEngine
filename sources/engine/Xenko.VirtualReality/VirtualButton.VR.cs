// Copyright (c) Xenko contributors (https://xenko.com) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.
using Xenko.Core.Mathematics;
using Xenko.Input;

namespace Xenko.VirtualReality
{
    /// <summary>
    /// VR virtual button.
    /// </summary>
    public class VRButtons : VirtualButton
    {
        public static readonly VirtualButton RightTrigger = new VRButtons("RightTrigger", (int)TouchControllerButton.Trigger | (1 << 16));

        public static readonly VirtualButton RightXA = new VRButtons("RightXA", (int)TouchControllerButton.ButtonXA | (1 << 16));

        public static readonly VirtualButton RightYB = new VRButtons("RightYB", (int)TouchControllerButton.ButtonYB | (1 << 16));

        public static readonly VirtualButton RightMenu = new VRButtons("RightMenu", (int)TouchControllerButton.Menu | (1 << 16));

        public static readonly VirtualButton RightGrip = new VRButtons("RightGrip", (int)TouchControllerButton.Grip | (1 << 16));

        public static readonly VirtualButton RightThumbstickUp = new VRButtons("RightThumbstickUp", (int)TouchControllerButton.Thumbstick | (1 << 16) | (0 << 17), true);

        public static readonly VirtualButton RightThumbstickDown = new VRButtons("RightThumbstickDown", (int)TouchControllerButton.Thumbstick | (1 << 16) | (1 << 17), true);

        public static readonly VirtualButton RightThumbstickLeft = new VRButtons("RightThumbstickLeft", (int)TouchControllerButton.Thumbstick | (1 << 16) | (2 << 17), true);

        public static readonly VirtualButton RightThumbstickRight = new VRButtons("RightThumbstickRight", (int)TouchControllerButton.Thumbstick | (1 << 16) | (3 << 17), true);

        public static readonly VirtualButton RightThumbstickCenter = new VRButtons("RightThumbstickCenter", (int)TouchControllerButton.Thumbstick | (1 << 16) | (4 << 17), true);

        public static readonly VirtualButton RightTouchpadUp = new VRButtons("RightTouchpadUp", (int)TouchControllerButton.Touchpad | (1 << 16) | (0 << 17), true);

        public static readonly VirtualButton RightTouchpadDown = new VRButtons("RightTouchpadDown", (int)TouchControllerButton.Touchpad | (1 << 16) | (1 << 17), true);

        public static readonly VirtualButton RightTouchpadLeft = new VRButtons("RightTouchpadLeft", (int)TouchControllerButton.Touchpad | (1 << 16) | (2 << 17), true);

        public static readonly VirtualButton RightTouchpadRight = new VRButtons("RightTouchpadRight", (int)TouchControllerButton.Touchpad | (1 << 16) | (3 << 17), true);

        public static readonly VirtualButton RightTouchpadCenter = new VRButtons("RightTouchpadCenter", (int)TouchControllerButton.Touchpad | (1 << 16) | (4 << 17), true);

        public static readonly VirtualButton LeftTrigger = new VRButtons("LeftTrigger", (int)TouchControllerButton.Trigger);

        public static readonly VirtualButton LeftXA = new VRButtons("LeftXA", (int)TouchControllerButton.ButtonXA);

        public static readonly VirtualButton LeftYB = new VRButtons("LeftYB", (int)TouchControllerButton.ButtonYB);

        public static readonly VirtualButton LeftMenu = new VRButtons("LeftMenu", (int)TouchControllerButton.Menu);

        public static readonly VirtualButton LeftGrip = new VRButtons("LeftGrip", (int)TouchControllerButton.Grip);

        public static readonly VirtualButton LeftThumbstickUp = new VRButtons("LeftThumbstickUp", (int)TouchControllerButton.Thumbstick | (0 << 17), true);

        public static readonly VirtualButton LeftThumbstickDown = new VRButtons("LeftThumbstickDown", (int)TouchControllerButton.Thumbstick | (1 << 17), true);

        public static readonly VirtualButton LeftThumbstickLeft = new VRButtons("LeftThumbstickLeft", (int)TouchControllerButton.Thumbstick | (2 << 17), true);

        public static readonly VirtualButton LeftThumbstickRight = new VRButtons("LeftThumbstickRight", (int)TouchControllerButton.Thumbstick | (3 << 17), true);

        public static readonly VirtualButton LeftThumbstickCenter = new VRButtons("LeftThumbstickCenter", (int)TouchControllerButton.Thumbstick | (4 << 17), true);

        public static readonly VirtualButton LeftTouchpadUp = new VRButtons("LeftTouchpadUp", (int)TouchControllerButton.Touchpad | (0 << 17), true);

        public static readonly VirtualButton LeftTouchpadDown = new VRButtons("LeftTouchpadDown", (int)TouchControllerButton.Touchpad | (1 << 17), true);

        public static readonly VirtualButton LeftTouchpadLeft = new VRButtons("LeftTouchpadLeft", (int)TouchControllerButton.Touchpad | (2 << 17), true);

        public static readonly VirtualButton LeftTouchpadRight = new VRButtons("LeftTouchpadRight", (int)TouchControllerButton.Touchpad | (3 << 17), true);

        public static readonly VirtualButton LeftTouchpadCenter = new VRButtons("LeftTouchpadCenter", (int)TouchControllerButton.Touchpad | (4 << 17), true);

        protected VRButtons(string name, int id, bool isPositiveAndNegative = false)
            : base(name, VirtualButtonType.VR, id, isPositiveAndNegative)
        {
        }

        public bool IsRightHand => ((Index & (1 << 16)) != 0) == !VRDeviceSystem.GetSystem.SwapControllerHands;
        public bool IsRightHandIgnoreSwapped => ((Index & (1 << 16)) != 0);
        public TouchControllerButton GetTouchControllerButton => (TouchControllerButton)(Index & 0xFF);

        public override float GetValue()
        {
            TouchController tc = VRDeviceSystem.GetSystem?.GetController((Index & (1 << 16)) != 0 ? TouchControllerHand.Right : TouchControllerHand.Left);
            if (tc == null) return 0f;
            TouchControllerButton button = (TouchControllerButton)(Index & 0xFF);
            switch (button)
            {
                case TouchControllerButton.Grip:
                    return tc.Grip;
                case TouchControllerButton.Thumbstick:
                    return (Index & (1 << 17)) != 0 ? tc.ThumbstickAxis.Y : tc.ThumbstickAxis.X;
                case TouchControllerButton.Touchpad:
                    return (Index & (1 << 17)) != 0 ? tc.TouchpadAxis.Y : tc.TouchpadAxis.X;
                case TouchControllerButton.Trigger:
                    return tc.Trigger;
                default:
                    return tc.IsPressed(button) ? 1f : 0f;
            }
        }

        public override bool IsDown()
        {
            TouchController tc = VRDeviceSystem.GetSystem?.GetController((Index & (1 << 16)) != 0 ? TouchControllerHand.Right : TouchControllerHand.Left);
            if (tc == null) return false;
            TouchControllerButton strippedButton = (TouchControllerButton)(Index & 0xFF);
            if (tc.IsPressed(strippedButton) == false) return false;
            Vector2 axis;
            switch (strippedButton)
            {
                default:
                    return true;
                case TouchControllerButton.Thumbstick:
                    axis = tc.ThumbstickAxis;
                    break;
                case TouchControllerButton.Touchpad:
                    axis = tc.TouchpadAxis;
                    break;
            }
            switch (Index >> 17)
            {
                case 0: // up
                    return axis.Y > 0.5f;
                case 1: // down
                    return axis.Y < -0.5f;
                case 2: // left
                    return axis.X < -0.5f;
                case 3: // right
                    return axis.X > 0.5f;
                case 4: // center
                    return axis.X > -0.5f && axis.X < 0.5f && axis.Y < 0.5f && axis.Y > -0.5f;
            }
            return false;
        }

        public override bool IsPressed()
        {
            TouchController tc = VRDeviceSystem.GetSystem?.GetController((Index & (1 << 16)) != 0 ? TouchControllerHand.Right : TouchControllerHand.Left);
            if (tc == null) return false;
            TouchControllerButton strippedButton = (TouchControllerButton)(Index & 0xFF);
            if (tc.IsPressedDown(strippedButton) == false) return false;
            Vector2 axis;
            switch (strippedButton)
            {
                default:
                    return true;
                case TouchControllerButton.Thumbstick:
                    axis = tc.ThumbstickAxis;
                    break;
                case TouchControllerButton.Touchpad:
                    axis = tc.TouchpadAxis;
                    break;
            }
            switch (Index >> 17)
            {
                case 0: // up
                    return axis.Y > 0.5f;
                case 1: // down
                    return axis.Y < -0.5f;
                case 2: // left
                    return axis.X < -0.5f;
                case 3: // right
                    return axis.X > 0.5f;
                case 4: // center
                    return axis.X > -0.5f && axis.X < 0.5f && axis.Y < 0.5f && axis.Y > -0.5f;
            }
            return false;
        }

        public override bool IsReleased()
        {
            TouchController tc = VRDeviceSystem.GetSystem?.GetController((Index & (1 << 16)) != 0 ? TouchControllerHand.Right : TouchControllerHand.Left);
            if (tc == null) return false;
            TouchControllerButton strippedButton = (TouchControllerButton)(Index & 0xFF);
            if (tc.IsPressReleased(strippedButton) == false) return false;
            Vector2 axis;
            switch (strippedButton)
            {
                default:
                    return true;
                case TouchControllerButton.Thumbstick:
                    axis = tc.ThumbstickAxis;
                    break;
                case TouchControllerButton.Touchpad:
                    axis = tc.TouchpadAxis;
                    break;
            }
            switch (Index >> 17)
            {
                case 0: // up
                    return axis.Y > 0.5f;
                case 1: // down
                    return axis.Y < -0.5f;
                case 2: // left
                    return axis.X < -0.5f;
                case 3: // right
                    return axis.X > 0.5f;
                case 4: // center
                    return axis.X > -0.5f && axis.X < 0.5f && axis.Y < 0.5f && axis.Y > -0.5f;
            }
            return false;
        }
    }
}
