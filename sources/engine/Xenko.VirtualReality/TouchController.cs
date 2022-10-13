// Copyright (c) Xenko contributors (https://xenko.com) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.
using System;
using Xenko.Core.Mathematics;
using Xenko.Games;

namespace Xenko.VirtualReality
{
    public abstract class TouchController : IDisposable
    {
        public virtual VRDevice HostDevice { get; internal set; }

        public abstract Vector3 Position { get; }

        public abstract Quaternion Rotation { get; }

        public abstract Vector3 LinearVelocity { get; }

        public abstract Vector3 AngularVelocity { get; }

        public abstract DeviceState State { get; }

        public virtual bool UseGripInsteadOfAimPose { get; set; }

        public virtual void Update(GameTime time)
        {           
        }

        public abstract bool SwapTouchpadJoystick { get; set; }

        public abstract bool HasTouchpad { get; internal set; }

        public abstract bool HasThumbstick { get; internal set;  }

        public abstract float HoldAngleOffset { set; }

        public abstract float HoldAngleOffset_Grip { get; set; }

        public abstract float HoldAngleOffset_Point { get; set; }

        public abstract float Trigger { get; }

        public abstract float Grip { get; }

        public abstract bool IndexPointing { get; }

        public abstract bool IndexResting { get; }

        public abstract bool ThumbUp { get; }

        public abstract bool ThumbResting { get; }

        public abstract Vector2 TouchpadAxis { get; }

        public abstract Vector2 ThumbstickAxis { get; }

        /// <summary>
        /// Vibrate the controller
        /// </summary>
        /// <param name="duration">Duration of vibration in seconds. If null, defaults to OpenXR's MIN_HAPTIC</param>
        /// <param name="frequency">Frequency of the duration. Defaults to 0, which is optimally picked by runtime. 3000hz is another option</param>
        /// <param name="amplitude">How big is the vibration? Defaults to 100%</param>
        /// <returns>true if successful</returns>
        public abstract bool Vibrate(double? duration = null, float frequency = 0f, float amplitude = 1f);

        /// <summary>
        /// Returns true if in this frame the button switched to pressed state
        /// </summary>
        /// <param name="button"></param>
        /// <returns></returns>
        public abstract bool IsPressedDown(TouchControllerButton button);

        /// <summary>
        /// Returns true if button switched is in the pressed state
        /// </summary>
        /// <param name="button"></param>
        /// <returns></returns>
        public abstract bool IsPressed(TouchControllerButton button);

        /// <summary>
        /// Returns true if in this frame the button was released
        /// </summary>
        /// <param name="button"></param>
        /// <returns></returns>
        public abstract bool IsPressReleased(TouchControllerButton button);

        /// <summary>
        /// Returns true if in this frame the button switched to pressed state
        /// </summary>
        /// <param name="button"></param>
        /// <returns></returns>
        public abstract bool IsTouchedDown(TouchControllerButton button);

        /// <summary>
        /// Returns true if button switched is in the pressed state
        /// </summary>
        /// <param name="button"></param>
        /// <returns></returns>
        public abstract bool IsTouched(TouchControllerButton button);

        /// <summary>
        /// Get a general axis result of the controller
        /// </summary>
        /// <param name="button"></param>
        /// <returns></returns>
        public abstract Vector2 GetAxis(int index);

        /// <summary>
        /// Returns true if in this frame the button was released
        /// </summary>
        /// <param name="button"></param>
        /// <returns></returns>
        public abstract bool IsTouchReleased(TouchControllerButton button);

        public abstract string DebugControllerState();

        public virtual void Dispose()
        {          
        }
    }
}
