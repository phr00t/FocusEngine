// Copyright (c) Xenko contributors (https://xenko.com) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.
using System;
using System.Collections.Generic;
using Xenko.Core;
using Xenko.Core.Mathematics;
using Xenko.Games;
using Xenko.Graphics;

namespace Xenko.VirtualReality
{
    public class VRDeviceSystem : GameSystemBase
    {
        /// <summary>
        /// Mirroring options
        /// </summary>
        public enum MIRROR_OPTION
        {
            NONE = 0,
            BOTH_EYES = 1,
            SINGLE_EYE = 2
        };

        /// <summary>
        /// An active instance of the VRDeviceSystem
        /// </summary>
        public static VRDeviceSystem GetSystem { get; private set; }

        /// <summary>
        /// Is VR currently active and initialized? May take some frames to start returning true.
        /// </summary>
        public static bool VRActive
        {
            get
            {
                return GetSystem != null && GetSystem.Enabled && GetSystem.Device != null;
            }
        }

        /// <summary>
        /// Action to run when VR is fully initialized and ready to go. Argument is the initialized VRDevice
        /// </summary>
        public static Action<VRDevice> OnVRInitialized;

        /// <summary>
        /// Swap hands at a low level? Easy way to have right hand act like the left hand and vice versa.
        /// </summary>
        public bool SwapControllerHands;

        /// <summary>
        /// Which VR button to activate UI? Defaults to trigger.
        /// </summary>
        public static TouchControllerButton UIActivationButton = TouchControllerButton.Trigger;

        /// <summary>
        /// Which VR button to activate UI like you were right clicking a mouse? Defaults to Grip. Can be null if you don't want a second activation button.
        /// </summary>
        public static TouchControllerButton? UIActivationButton2 = TouchControllerButton.Grip;

        /// <summary>
        /// Which hand will be used to track pointer positions on UIComponents using that feature?
        /// </summary>
        public static TouchControllerHand UITrackingHand = TouchControllerHand.Right;

        /// <summary>
        /// Shortcut to getting VR hands
        /// </summary>
        /// <param name="hand">Which hand?</param>
        /// <returns>TouchController object, otherwise null</returns>
        public TouchController GetController(TouchControllerHand hand)
        {
            if (Device == null) return null;

            switch(hand)
            {
                case TouchControllerHand.Left:
                    return SwapControllerHands ? Device.RightHand : Device.LeftHand;
                case TouchControllerHand.Right:
                    return SwapControllerHands ? Device.LeftHand : Device.RightHand;
                default:
                    return null;
            }
        }

        private static bool physicalDeviceInUse;

        public VRDeviceSystem(IServiceRegistry registry) : base(registry)
        {
            GetSystem = this;

            EnabledChanged += OnEnabledChanged;

            DrawOrder = -100;
            UpdateOrder = -100;
        }

        public VRApi PreferredApi;

        public float PreferredScalings;

        public VRDevice Device { get; private set; }

        public MIRROR_OPTION RequireMirror;

        public bool PreviousUseCustomProjectionMatrix;

        public bool PreviousUseCustomViewMatrix;

        public Matrix PreviousCameraProjection;

        public float ResolutionScale;

        private void OnEnabledChanged(object sender, EventArgs eventArgs)
        {
            if (Enabled && Device == null)
            {
                double refreshRate = 90.0;

                if (physicalDeviceInUse)
                {
                    Device = null;
                    goto postswitch;
                }

                switch (PreferredApi)
                {
                    case VRApi.OpenXR:
#if XENKO_GRAPHICS_API_VULKAN
                        Device = new OpenXRHmd(Game);
#endif
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }

                if (Device != null)
                {
                    Device.Game = Game;

                    if (Device != null && !Device.CanInitialize)
                    {
                        Device.Dispose();
                        Device = null;
                    }
                }

postswitch:

                var deviceManager = (GraphicsDeviceManager)Services.GetService<IGraphicsDeviceManager>();
                if (Device != null)
                {
                    Game.TreatNotFocusedLikeMinimized = false;
                    Game.DrawEvenMinimized = true;
                    Game.IsFixedTimeStep = false;
                    deviceManager.SynchronizeWithVerticalRetrace = false;

                    Device.RenderFrameScaling = PreferredScalings;
                    Device.Enable(GraphicsDevice, deviceManager, RequireMirror);
                    Device.SetTrackingSpace(TrackingSpace.Standing);
                    physicalDeviceInUse = true;

                    // WaitGetPoses should throttle our application, so don't do it elsewhere
                    Game.TargetElapsedTime = TimeSpan.Zero;
                    Game.WindowMinimumUpdateRate.MinimumElapsedTime = TimeSpan.Zero;
                    Game.MinimizedMinimumUpdateRate.MinimumElapsedTime = TimeSpan.Zero;
                }

                // init virtual buttons for use with VR input
                Xenko.Input.VirtualButton.RegisterExternalVirtualButtonType(typeof(Xenko.VirtualReality.VRButtons));

                // run actions when VR is initialized?
                if (Device != null && OnVRInitialized != null) OnVRInitialized(Device);
            }
        }

        public override void Update(GameTime gameTime)
        {
            Device?.Update(gameTime);
        }

        public override void Draw(GameTime gameTime)
        {
            Device?.Draw(gameTime);
        }

        protected override void Destroy()
        {
            if (Device != null)
            {
                physicalDeviceInUse = false;
                Device.Dispose();
                Device = null;
            }
        }
    }
}
