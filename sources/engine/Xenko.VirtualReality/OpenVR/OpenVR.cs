// Copyright (c) Xenko contributors (https://xenko.com) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.
#if XENKO_GRAPHICS_API_VULKAN || XENKO_GRAPHICS_API_DIRECT3D11

using System;
using System.Runtime.ExceptionServices;
using System.Text;
#if XENKO_GRAPHICS_API_DIRECT3D11
using SharpDX.Direct3D11;
#elif XENKO_GRAPHICS_API_VULKAN
using Vortice.Vulkan;
using static Vortice.Vulkan.Vulkan;
#endif
using Valve.VR;
using Xenko.Core.Threading;
using Xenko.Core;
using Xenko.Core.Mathematics;
using Xenko.Games;
using Xenko.Graphics;
using System.Runtime.InteropServices;

namespace Xenko.VirtualReality
{
    public static class OpenVR
    {
        public class Controller
        {
            // This helper can be used in a variety of ways.  Beware that indices may change
            // as new devices are dynamically added or removed, controllers are physically
            // swapped between hands, arms crossed, etc.
            public enum Hand
            {
                Left,
                Right,
            }

            public static int GetDeviceIndex(Hand hand)
            {
                var currentIndex = 0;
                for (uint index = 0; index < DevicePoses.Length; index++)
                {
                    if (Valve.VR.OpenVR.System.GetTrackedDeviceClass(index) == ETrackedDeviceClass.Controller)
                    {
                        if (hand == Hand.Left && Valve.VR.OpenVR.System.GetControllerRoleForTrackedDeviceIndex(index) == ETrackedControllerRole.LeftHand)
                        {
                            return currentIndex;
                        }

                        if (hand == Hand.Right && Valve.VR.OpenVR.System.GetControllerRoleForTrackedDeviceIndex(index) == ETrackedControllerRole.RightHand)
                        {
                            return currentIndex;
                        }

                        currentIndex++;
                    }
                }

                return -1;
            }

            public class ButtonMask
            {
                public const ulong System = (1ul << (int)EVRButtonId.k_EButton_System); // reserved
                public const ulong ApplicationMenu = (1ul << (int)EVRButtonId.k_EButton_ApplicationMenu);
                public const ulong Grip = (1ul << (int)EVRButtonId.k_EButton_Grip);
                public const ulong Axis0 = (1ul << (int)EVRButtonId.k_EButton_Axis0);
                public const ulong Axis1 = (1ul << (int)EVRButtonId.k_EButton_Axis1);
                public const ulong Axis2 = (1ul << (int)EVRButtonId.k_EButton_Axis2);
                public const ulong Axis3 = (1ul << (int)EVRButtonId.k_EButton_Axis3);
                public const ulong Axis4 = (1ul << (int)EVRButtonId.k_EButton_Axis4);
                public const ulong Touchpad = (1ul << (int)EVRButtonId.k_EButton_SteamVR_Touchpad);
                public const ulong Trigger = (1ul << (int)EVRButtonId.k_EButton_SteamVR_Trigger);
            }

            public enum ButtonId
            {
                ButtonSystem = 0,
                ButtonApplicationMenu = 1,
                ButtonGrip = 2,
                ButtonDPadLeft = 3,
                ButtonDPadUp = 4,
                ButtonDPadRight = 5,
                ButtonDPadDown = 6,
                ButtonA = 7,
                ButtonProximitySensor = 31,
                ButtonAxis0 = 32,
                ButtonAxis1 = 33,
                ButtonAxis2 = 34,
                ButtonAxis3 = 35,
                ButtonAxis4 = 36,
                ButtonSteamVrTouchpad = 32,
                ButtonSteamVrTrigger = 33,
                ButtonDashboardBack = 2,
                ButtonMax = 64,
            }

            public Controller(int controllerIndex)
            {
                var currentIndex = 0;
                for (uint index = 0; index < DevicePoses.Length; index++)
                {
                    if (Valve.VR.OpenVR.System.GetTrackedDeviceClass(index) == ETrackedDeviceClass.Controller)
                    {
                        if (currentIndex == controllerIndex)
                        {
                            ControllerIndex = index;
                            break;
                        }
                        currentIndex++;
                    }
                }
            }

            internal uint ControllerIndex;
            internal VRControllerState_t State;
            internal VRControllerState_t PreviousState;

            public bool GetPress(ulong buttonMask) { return (State.ulButtonPressed & buttonMask) != 0; }

            public bool GetPressDown(ulong buttonMask) { return (State.ulButtonPressed & buttonMask) != 0 && (PreviousState.ulButtonPressed & buttonMask) == 0; }

            public bool GetPressUp(ulong buttonMask) { return (State.ulButtonPressed & buttonMask) == 0 && (PreviousState.ulButtonPressed & buttonMask) != 0; }

            public bool GetPress(ButtonId buttonId) { return GetPress(1ul << (int)buttonId); }

            public bool GetPressDown(ButtonId buttonId) { return GetPressDown(1ul << (int)buttonId); }

            public bool GetPressUp(ButtonId buttonId) { return GetPressUp(1ul << (int)buttonId); }

            public bool GetTouch(ulong buttonMask) { return (State.ulButtonTouched & buttonMask) != 0; }

            public bool GetTouchDown(ulong buttonMask) { return (State.ulButtonTouched & buttonMask) != 0 && (PreviousState.ulButtonTouched & buttonMask) == 0; }

            public bool GetTouchUp(ulong buttonMask) { return (State.ulButtonTouched & buttonMask) == 0 && (PreviousState.ulButtonTouched & buttonMask) != 0; }

            public bool GetTouch(ButtonId buttonId) { return GetTouch(1ul << (int)buttonId); }

            public bool GetTouchDown(ButtonId buttonId) { return GetTouchDown(1ul << (int)buttonId); }

            public bool GetTouchUp(ButtonId buttonId) { return GetTouchUp(1ul << (int)buttonId); }

            public Vector2 GetAxis(ButtonId buttonId = ButtonId.ButtonSteamVrTouchpad)
            {               
                switch (buttonId)
                {
                    case ButtonId.ButtonAxis0: return new Vector2(State.rAxis0.x, State.rAxis0.y); // also touchpad
                    case ButtonId.ButtonAxis1: return new Vector2(State.rAxis1.x, State.rAxis1.y); // also trigger
                    case ButtonId.ButtonAxis2: return new Vector2(State.rAxis2.x, State.rAxis2.y); // hand trigger..?
                    case ButtonId.ButtonAxis3: return new Vector2(State.rAxis3.x, State.rAxis3.y); // index joystick
                    case ButtonId.ButtonAxis4: return new Vector2(State.rAxis4.x, State.rAxis4.y);
                }
                return Vector2.Zero;
            }

            public void Update()
            {
                PreviousState = State;
                Valve.VR.OpenVR.System.GetControllerState(ControllerIndex, ref State, (uint)Utilities.SizeOf<VRControllerState_t>());
            }
        }

        public class TrackedDevice
        {
            public TrackedDevice(int trackerIndex)
            {
                TrackerIndex = trackerIndex;
            }

            const int StringBuilderSize = 64;
            StringBuilder serialNumberStringBuilder = new StringBuilder(StringBuilderSize);
            internal string SerialNumber
            {
                get
                {
                    var error = ETrackedPropertyError.TrackedProp_Success;
                    serialNumberStringBuilder.Clear();
                    Valve.VR.OpenVR.System.GetStringTrackedDeviceProperty((uint)TrackerIndex, ETrackedDeviceProperty.Prop_SerialNumber_String, serialNumberStringBuilder, StringBuilderSize, ref error);
                    if (error == ETrackedPropertyError.TrackedProp_Success)
                        return serialNumberStringBuilder.ToString();
                    else
                        return "";
                }
            }

            internal float BatteryPercentage
            {
                get
                {
                    var error = ETrackedPropertyError.TrackedProp_Success;
                    var value = Valve.VR.OpenVR.System.GetFloatTrackedDeviceProperty((uint)TrackerIndex, ETrackedDeviceProperty.Prop_DeviceBatteryPercentage_Float, ref error);
                    if (error == ETrackedPropertyError.TrackedProp_Success)
                        return value;
                    else
                        return 0;
                }
            }

            internal int TrackerIndex;
            internal ETrackedDeviceClass DeviceClass => Valve.VR.OpenVR.System.GetTrackedDeviceClass((uint)TrackerIndex);
        }

        private static readonly TrackedDevicePose_t[] DevicePoses = new TrackedDevicePose_t[Valve.VR.OpenVR.k_unMaxTrackedDeviceCount];
        private static readonly TrackedDevicePose_t[] GamePoses = new TrackedDevicePose_t[Valve.VR.OpenVR.k_unMaxTrackedDeviceCount];

        static OpenVR()
        {
            NativeLibraryHelper.PreloadLibrary("openvr_api.dll", typeof(OpenVR));
        }

        public static bool InitDone = false;

#if XENKO_GRAPHICS_API_VULKAN
        private static unsafe VRVulkanTextureData_t vkTexData;
        public static bool InitVulkan(GameBase baseGame) {
            vkTexData = new VRVulkanTextureData_t {
                m_pDevice = baseGame.GraphicsDevice.NativeDevice.Handle, // struct VkDevice_T *
                m_pPhysicalDevice = baseGame.GraphicsDevice.NativePhysicalDevice.Handle, // struct VkPhysicalDevice_T *
                m_pInstance = baseGame.GraphicsDevice.NativeInstance.Handle, // struct VkInstance_T *
                m_pQueue = baseGame.GraphicsDevice.NativeCommandQueue.Handle, // struct VkQueue_T *
                m_nQueueFamilyIndex = 0 // 0 is hardcoded index during vulkan creation
            };
            return true;
        }
#endif

        public static bool Init()
        {
            var err = EVRInitError.None;
            Valve.VR.OpenVR.Init(ref err);
            if (err != EVRInitError.None)
            {
                return false;
            }

            InitDone = true;

            return true;
        }

        public static void Shutdown()
        {
            if (!InitDone) return;
            Valve.VR.OpenVR.Shutdown();
            InitDone = false;
        }

        public static unsafe bool Submit(int eyeIndex, Texture texture, ref RectangleF viewport)
        {
            var bounds = new VRTextureBounds_t {
                uMin = viewport.X,
                uMax = viewport.Width,
                vMin = viewport.Y,
                vMax = viewport.Height,
            };
#if XENKO_GRAPHICS_API_VULKAN
            var vkTexDataCopy = new VRVulkanTextureData_t {
                m_pDevice = vkTexData.m_pDevice,
                m_pPhysicalDevice = vkTexData.m_pPhysicalDevice,
                m_pInstance = vkTexData.m_pInstance, 
                m_pQueue = vkTexData.m_pQueue, 
                m_nHeight = (uint)texture.Height,
                m_nWidth = (uint)texture.Width,
                m_nImage = (ulong)texture.NativeImage.Handle,
                m_nSampleCount = texture.IsMultisample ? (uint)texture.MultisampleCount : 1,
                m_nFormat = (uint)texture.NativeFormat
            };
            var tex = new Texture_t {
                eType = ETextureType.Vulkan,
                handle = (IntPtr)(&vkTexDataCopy),
                eColorSpace = EColorSpace.Auto
            };
            return Valve.VR.OpenVR.Compositor.Submit(eyeIndex == 0 ? EVREye.Eye_Left : EVREye.Eye_Right, ref tex, ref bounds, EVRSubmitFlags.Submit_Default) == EVRCompositorError.None;
#elif XENKO_GRAPHICS_API_DIRECT3D11
            var tex = new Texture_t {
                    eType = ETextureType.DirectX,
                    handle = texture.SharedHandle,
                    eColorSpace = EColorSpace.Auto,
            };
            return Valve.VR.OpenVR.Compositor.Submit(eyeIndex == 0 ? EVREye.Eye_Left : EVREye.Eye_Right, ref tex, ref bounds, EVRSubmitFlags.Submit_Default) == EVRCompositorError.None;
#endif
        }

        public static void GetEyeToHead(int eyeIndex, out Matrix pose)
        {
            pose = Matrix.Identity;
            var eye = eyeIndex == 0 ? EVREye.Eye_Left : EVREye.Eye_Right;
            var eyeToHead = Valve.VR.OpenVR.System.GetEyeToHeadTransform(eye);
            pose.M11 = eyeToHead.m0;
            pose.M21 = eyeToHead.m1;
            pose.M31 = eyeToHead.m2;
            pose.M41 = eyeToHead.m3;
            pose.M12 = eyeToHead.m4;
            pose.M22 = eyeToHead.m5;
            pose.M32 = eyeToHead.m6;
            pose.M42 = eyeToHead.m7;
            pose.M13 = eyeToHead.m8;
            pose.M23 = eyeToHead.m9;
            pose.M33 = eyeToHead.m10;
            pose.M43 = eyeToHead.m11;
        }

        public static void UpdatePoses()
        {
            Valve.VR.OpenVR.Compositor.WaitGetPoses(DevicePoses, GamePoses);
        }

        public static void Recenter()
        {
            Valve.VR.OpenVR.Chaperone.ResetZeroPose(Valve.VR.OpenVR.Compositor.GetTrackingSpace());
        }

        public static void SetTrackingSpace(ETrackingUniverseOrigin space)
        {
            Valve.VR.OpenVR.Compositor.SetTrackingSpace(space);
        }

        public static DeviceState GetControllerPose(int controllerIndex, out Matrix pose, out Vector3 velocity, out Vector3 angVelocity)
        {
            var currentIndex = 0;

            pose = Matrix.Identity;
            velocity = Vector3.Zero;
            angVelocity = Vector3.Zero;

            for (uint index = 0; index < DevicePoses.Length; index++)
            {
                if (Valve.VR.OpenVR.System.GetTrackedDeviceClass(index) == ETrackedDeviceClass.Controller)
                {
                    if (currentIndex == controllerIndex)
                    {
                        HmdMatrix34_t openVRPose = DevicePoses[index].mDeviceToAbsoluteTracking;
                        pose.M11 = openVRPose.m0;
                        pose.M21 = openVRPose.m1;
                        pose.M31 = openVRPose.m2;
                        pose.M41 = openVRPose.m3;
                        pose.M12 = openVRPose.m4;
                        pose.M22 = openVRPose.m5;
                        pose.M32 = openVRPose.m6;
                        pose.M42 = openVRPose.m7;
                        pose.M13 = openVRPose.m8;
                        pose.M23 = openVRPose.m9;
                        pose.M33 = openVRPose.m10;
                        pose.M43 = openVRPose.m11;

                        HmdVector3_t vel = DevicePoses[index].vVelocity;
                        velocity.X = vel.v0;
                        velocity.Y = vel.v1;
                        velocity.Z = vel.v2;

                        HmdVector3_t avel = DevicePoses[index].vAngularVelocity;
                        angVelocity.X = avel.v0;
                        angVelocity.Y = avel.v1;
                        angVelocity.Z = avel.v2;

                        var state = DeviceState.Invalid;
                        if (DevicePoses[index].bDeviceIsConnected && DevicePoses[index].bPoseIsValid)
                        {
                            state = DeviceState.Valid;
                        }
                        else if (DevicePoses[index].bDeviceIsConnected && !DevicePoses[index].bPoseIsValid && DevicePoses[index].eTrackingResult == ETrackingResult.Running_OutOfRange)
                        {
                            state = DeviceState.OutOfRange;
                        }

                        return state;
                    }
                    currentIndex++;
                }
            }

            return DeviceState.Invalid;
        }

        public static DeviceState GetTrackerPose(int trackerIndex, ref Matrix pose, ref Vector3 velocity, ref Vector3 angVelocity)
        {
            var index = trackerIndex;

            HmdMatrix34_t openVRPose = DevicePoses[index].mDeviceToAbsoluteTracking;
            pose.M11 = openVRPose.m0;
            pose.M21 = openVRPose.m1;
            pose.M31 = openVRPose.m2;
            pose.M41 = openVRPose.m3;
            pose.M12 = openVRPose.m4;
            pose.M22 = openVRPose.m5;
            pose.M32 = openVRPose.m6;
            pose.M42 = openVRPose.m7;
            pose.M13 = openVRPose.m8;
            pose.M23 = openVRPose.m9;
            pose.M33 = openVRPose.m10;
            pose.M43 = openVRPose.m11;

            HmdVector3_t vel = DevicePoses[index].vVelocity;
            velocity.X = vel.v0;
            velocity.Y = vel.v1;
            velocity.Z = vel.v2;

            HmdVector3_t avel = DevicePoses[index].vAngularVelocity;
            angVelocity.X = avel.v0;
            angVelocity.Y = avel.v1;
            angVelocity.Z = avel.v2;

            var state = DeviceState.Invalid;
            if (DevicePoses[index].bDeviceIsConnected && DevicePoses[index].bPoseIsValid)
            {
                state = DeviceState.Valid;
            }
            else if (DevicePoses[index].bDeviceIsConnected && !DevicePoses[index].bPoseIsValid && DevicePoses[index].eTrackingResult == ETrackingResult.Running_OutOfRange)
            {
                state = DeviceState.OutOfRange;
            }

            return state;
        }

        public static DeviceState GetHeadPose(out Matrix pose, out Vector3 linearVelocity, out Vector3 angularVelocity)
        {
            pose = Matrix.Identity;
            linearVelocity = Vector3.Zero;
            angularVelocity = Vector3.Zero;
   
            for (uint index = 0; index < DevicePoses.Length; index++)
            {
                if (Valve.VR.OpenVR.System.GetTrackedDeviceClass(index) == ETrackedDeviceClass.HMD)
                {
                    HmdMatrix34_t openVRPose = DevicePoses[index].mDeviceToAbsoluteTracking;
                    pose.M11 = openVRPose.m0;
                    pose.M21 = openVRPose.m1;
                    pose.M31 = openVRPose.m2;
                    pose.M41 = openVRPose.m3;
                    pose.M12 = openVRPose.m4;
                    pose.M22 = openVRPose.m5;
                    pose.M32 = openVRPose.m6;
                    pose.M42 = openVRPose.m7;
                    pose.M13 = openVRPose.m8;
                    pose.M23 = openVRPose.m9;
                    pose.M33 = openVRPose.m10;
                    pose.M43 = openVRPose.m11;

                    HmdVector3_t vel = DevicePoses[index].vVelocity;
                    linearVelocity.X = vel.v0;
                    linearVelocity.Y = vel.v1;
                    linearVelocity.Z = vel.v2;

                    HmdVector3_t avel = DevicePoses[index].vAngularVelocity;
                    angularVelocity.X = avel.v0;
                    angularVelocity.Y = avel.v1;
                    angularVelocity.Z = avel.v2;

                    var state = DeviceState.Invalid;
                    if (DevicePoses[index].bDeviceIsConnected && DevicePoses[index].bPoseIsValid)
                    {
                        state = DeviceState.Valid;
                    }
                    else if (DevicePoses[index].bDeviceIsConnected && !DevicePoses[index].bPoseIsValid && DevicePoses[index].eTrackingResult == ETrackingResult.Running_OutOfRange)
                    {
                        state = DeviceState.OutOfRange;
                    }

                    return state;
                }
            }

            return DeviceState.Invalid;
        }

        public static void GetProjection(int eyeIndex, float near, float far, out Matrix projection)
        {
            projection = Matrix.Identity;
            var eye = eyeIndex == 0 ? EVREye.Eye_Left : EVREye.Eye_Right;
            var proj = Valve.VR.OpenVR.System.GetProjectionMatrix(eye, near, far);
            projection.M11 = proj.m0;
            projection.M21 = proj.m1;
            projection.M31 = proj.m2;
            projection.M41 = proj.m3;
            projection.M12 = proj.m4;
            projection.M22 = proj.m5;
            projection.M32 = proj.m6;
            projection.M42 = proj.m7;
            projection.M13 = proj.m8;
            projection.M23 = proj.m9;
            projection.M33 = proj.m10;
            projection.M43 = proj.m11;
            projection.M14 = proj.m12;
            projection.M24 = proj.m13;
            projection.M34 = proj.m14;
            projection.M44 = proj.m15;
        }

        public static void ShowMirror()
        {
            Valve.VR.OpenVR.Compositor.ShowMirrorWindow();
        }

        public static void HideMirror()
        {
            Valve.VR.OpenVR.Compositor.HideMirrorWindow();
        }

        public static Texture GetMirrorTexture(GraphicsDevice device, int eyeIndex)
        {
#if XENKO_GRAPHICS_API_DIRECT3D11
            var nativeDevice = device.NativeDevice.NativePointer;
            var eyeTexSrv = IntPtr.Zero;
            Valve.VR.OpenVR.Compositor.GetMirrorTextureD3D11(eyeIndex == 0 ? EVREye.Eye_Left : EVREye.Eye_Right, nativeDevice, ref eyeTexSrv);
            var tex = new Texture(device);
            tex.InitializeFromImpl(new ShaderResourceView(eyeTexSrv));
            return tex;
#else 
            // unfortunately no mirror function for Vulkan (not implemented for OpenGL) see https://github.com/ValveSoftware/openvr/issues/1053
            return new Texture(device);
#endif
        }

        public static ulong CreateOverlay()
        {
            var layerKeyName = Guid.NewGuid().ToString();
            ulong handle = 0;
            return Valve.VR.OpenVR.Overlay.CreateOverlay(layerKeyName, layerKeyName, ref handle) == EVROverlayError.None ? handle : 0;
        }

        public static void InitOverlay(ulong overlayId)
        {
            Valve.VR.OpenVR.Overlay.SetOverlayInputMethod(overlayId, VROverlayInputMethod.None);
            Valve.VR.OpenVR.Overlay.SetOverlayFlag(overlayId, VROverlayFlags.SortWithNonSceneOverlays, true);
        }

        public static bool SubmitOverlay(ulong overlayId, Texture texture)
        {
            var tex = new Texture_t
            {
                eType = ETextureType.Vulkan,
                eColorSpace = EColorSpace.Auto,
                handle = texture.SharedHandle, //texture.NativeResource.NativePointer,
            };
           
            return Valve.VR.OpenVR.Overlay.SetOverlayTexture(overlayId, ref tex) == EVROverlayError.None;
        }

        public static unsafe void SetOverlayParams(ulong overlayId, Matrix transform, bool followsHead, Vector2 surfaceSize)
        {
            Valve.VR.OpenVR.Overlay.SetOverlayWidthInMeters(overlayId, 1.0f);

            transform = Matrix.Scaling(new Vector3(surfaceSize.X, surfaceSize.Y, 1.0f)) * transform;

            if (followsHead)
            {
                HmdMatrix34_t pose = new HmdMatrix34_t();
                Utilities.CopyMemory((IntPtr)Core.Interop.Fixed(ref pose), (IntPtr)Core.Interop.Fixed(ref transform), Utilities.SizeOf<HmdMatrix34_t>());
                Valve.VR.OpenVR.Overlay.SetOverlayTransformTrackedDeviceRelative(overlayId, 0, ref pose);
            }
            else
            {
                HmdMatrix34_t pose = new HmdMatrix34_t();
                Utilities.CopyMemory((IntPtr)Core.Interop.Fixed(ref pose), (IntPtr)Core.Interop.Fixed(ref transform), Utilities.SizeOf<HmdMatrix34_t>());
                Valve.VR.OpenVR.Overlay.SetOverlayTransformAbsolute(overlayId, ETrackingUniverseOrigin.TrackingUniverseSeated, ref pose);
            }
        }

        public static void SetOverlayEnabled(ulong overlayId, bool enabled)
        {
            if (enabled)
                Valve.VR.OpenVR.Overlay.ShowOverlay(overlayId);
            else
                Valve.VR.OpenVR.Overlay.HideOverlay(overlayId);
        }
    }
}

#endif
