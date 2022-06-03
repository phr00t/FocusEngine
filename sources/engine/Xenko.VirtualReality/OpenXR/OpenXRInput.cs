using System;
using System.Collections.Generic;
using System.Text;
using Silk.NET.Core.Native;
using Silk.NET.OpenXR;
using Xenko.Core;

namespace Xenko.VirtualReality
{
    public class OpenXRInput
    {
        public static bool LogInputDetected = false;

        // different types of input we are interested in
        public enum HAND_PATHS
        {
            BaseIndex = 0,
            AimPosition = 1,
            GripPosition = 2,
            TriggerValue = 3,
            TriggerClick = 4,
            ThumbstickX = 5,
            ThumbstickY = 6,
            ThumbstickClick = 7,
            TrackpadX = 8,
            TrackpadY = 9,
            TrackpadClick = 10,
            TrackpadForce = 11,
            GripValue = 12,
            GripClick = 13,
            ButtonXA = 14, // x on left, a on right (or either index)
            ButtonYB = 15, // y on left, b on right (or either index)
            Menu = 16,
            System = 17, // may be inaccessible
            HapticOut = 18
        }
        public const int HAND_PATH_COUNT = 19;

        internal class TouchThumb
        {
            public bool HasTouch, HasThumb;
        }

        internal static HashSet<ulong> HasTouchpads = new HashSet<ulong>(), HasThumbsticks = new HashSet<ulong>();

        // most likely matches for input types above
        private static List<string>[] PathPriorities =
        {
            new List<string>() { "" }, // BaseIndex 0
            new List<string>() { "/input/aim/pose" }, // AimPosition 1
            new List<string>() { "/input/grip/pose" }, // GripPosition 2
            new List<string>() { "/input/trigger/value", "/input/select/value" }, // TriggerValue 3
            new List<string>() { "/input/trigger/click", "/input/select/click" }, // TriggerClick 4
            new List<string>() { "/input/thumbstick/x" }, // ThumbstickX 5
            new List<string>() { "/input/thumbstick/y" }, // ThumbstickY 6
            new List<string>() { "/input/thumbstick/click" }, // ThumbstickClick 7
            new List<string>() { "/input/trackpad/x" }, // TrackpadX 8
            new List<string>() { "/input/trackpad/y" }, // TrackpadY 9
            new List<string>() { "/input/trackpad/click" }, // TrackpadClick 10
            new List<string>() { "/input/trackpad/force" }, // TrackpadForce 11
            new List<string>() { "/input/squeeze/force", "/input/squeeze/value" }, // GripValue 12
            new List<string>() { "/input/squeeze/click" }, // GripClick 13
            new List<string>() { "/input/x/click", "/input/a/click" }, // ButtonXA 14
            new List<string>() { "/input/y/click", "/input/b/click" }, // ButtonYB 15
            new List<string>() { "/input/menu/click" }, // Menu 16
            new List<string>() { "/input/system/click" }, // System 17
            new List<string>() { "/output/haptic" }, // HapticOut 18
        };

        private static ActionType GetActionType(HAND_PATHS hp)
        {
            switch (hp)
            {
                case HAND_PATHS.BaseIndex:
                case HAND_PATHS.AimPosition:
                case HAND_PATHS.GripPosition:
                    return ActionType.PoseInput;
                case HAND_PATHS.GripValue:
                case HAND_PATHS.ThumbstickX:
                case HAND_PATHS.ThumbstickY:
                case HAND_PATHS.TrackpadX:
                case HAND_PATHS.TrackpadY:
                case HAND_PATHS.TriggerValue:
                case HAND_PATHS.TrackpadForce:
                    return ActionType.FloatInput;
                case HAND_PATHS.HapticOut:
                    return ActionType.VibrationOutput;
                case HAND_PATHS.GripClick:
                case HAND_PATHS.ThumbstickClick:
                case HAND_PATHS.TriggerClick:
                case HAND_PATHS.TrackpadClick:
                case HAND_PATHS.Menu:
                case HAND_PATHS.System:
                default:
                    return ActionType.BooleanInput;
            }
        }

        private static OpenXRHmd baseHMD;
        internal static Silk.NET.OpenXR.Action[,] MappedActions = new Silk.NET.OpenXR.Action[2, HAND_PATH_COUNT];

        public static string[] InteractionProfiles =
        {
            "/interaction_profiles/khr/simple_controller",
            "/interaction_profiles/google/daydream_controller",
            "/interaction_profiles/htc/vive_controller",
            "/interaction_profiles/htc/vive_pro",
            "/interaction_profiles/microsoft/motion_controller",
            "/interaction_profiles/hp/mixed_reality_controller",
            "/interaction_profiles/samsung/odyssey_controller",
            "/interaction_profiles/oculus/go_controller",
            "/interaction_profiles/oculus/touch_controller",
            "/interaction_profiles/valve/index_controller",
            "/interaction_profiles/htc/vive_cosmos_controller",
            "/interaction_profiles/huawei/controller",
            "/interaction_profiles/microsoft/hand_interaction",
            "/interaction_profiles/htc/vive_focus3_controller"
        };

        internal static unsafe bool IsPathSupported(OpenXRHmd hmd, ulong profile, ActionSuggestedBinding* suggested)
        {
            InteractionProfileSuggestedBinding suggested_bindings = new InteractionProfileSuggestedBinding()
            {
                Type = StructureType.TypeInteractionProfileSuggestedBinding,
                InteractionProfile = profile,
                CountSuggestedBindings = 1,
                SuggestedBindings = suggested
            };

            return hmd.Xr.SuggestInteractionProfileBinding(hmd.Instance, &suggested_bindings) == Result.Success;
        }

        public enum INPUT_DESIRED
        {
            VALUE = 0,
            YAXIS = 1,
            XAXIS = 2,
            CLICK = 3
        };

        public static Silk.NET.OpenXR.Action GetAction(TouchControllerHand hand, TouchControllerButton button, INPUT_DESIRED wantMode)
        {
            switch (button)
            {
                case TouchControllerButton.ButtonXA:
                    return MappedActions[(int)hand, (int)HAND_PATHS.ButtonXA];
                case TouchControllerButton.ButtonYB:
                    return MappedActions[(int)hand, (int)HAND_PATHS.ButtonYB];
                case TouchControllerButton.Grip:
                    return wantMode == INPUT_DESIRED.CLICK ? MappedActions[(int)hand, (int)HAND_PATHS.GripClick] : MappedActions[(int)hand, (int)HAND_PATHS.GripValue];
                case TouchControllerButton.Menu:
                    return MappedActions[(int)hand, (int)HAND_PATHS.Menu];
                case TouchControllerButton.System:
                    return MappedActions[(int)hand, (int)HAND_PATHS.System];
                case TouchControllerButton.Trigger:
                    return wantMode == INPUT_DESIRED.CLICK ? MappedActions[(int)hand, (int)HAND_PATHS.TriggerClick] : MappedActions[(int)hand, (int)HAND_PATHS.TriggerValue];
                case TouchControllerButton.Thumbstick:
                    switch (wantMode)
                    {
                        default:
                        case INPUT_DESIRED.CLICK:
                            return MappedActions[(int)hand, (int)HAND_PATHS.ThumbstickClick];
                        case INPUT_DESIRED.XAXIS:
                            return MappedActions[(int)hand, (int)HAND_PATHS.ThumbstickX];
                        case INPUT_DESIRED.YAXIS:
                            return MappedActions[(int)hand, (int)HAND_PATHS.ThumbstickY];
                    }
                case TouchControllerButton.Touchpad:
                    switch (wantMode)
                    {
                        default:
                        case INPUT_DESIRED.CLICK:
                            return MappedActions[(int)hand, (int)HAND_PATHS.TrackpadClick];
                        case INPUT_DESIRED.XAXIS:
                            return MappedActions[(int)hand, (int)HAND_PATHS.TrackpadX];
                        case INPUT_DESIRED.YAXIS:
                            return MappedActions[(int)hand, (int)HAND_PATHS.TrackpadY];
                        case INPUT_DESIRED.VALUE:
                            return MappedActions[(int)hand, (int)HAND_PATHS.TrackpadForce];
                    }
                default:
                    throw new ArgumentException("Don't know button: " + button);
            }
        }

        public static bool GetActionBool(TouchControllerHand hand, TouchControllerButton button, out bool wasChangedSinceLast, bool fallback = false)
        {
            ActionStateGetInfo getbool = new ActionStateGetInfo()
            {
                Action = GetAction(hand, button, INPUT_DESIRED.CLICK),
                Type = StructureType.TypeActionStateGetInfo
            };

            ActionStateBoolean boolresult = new ActionStateBoolean()
            {
                Type = StructureType.TypeActionStateBoolean
            };

            baseHMD.Xr.GetActionStateBoolean(baseHMD.globalSession, in getbool, ref boolresult);

            if (boolresult.IsActive == 0)
            {
                if (fallback)
                {
                    // couldn't find an input...
                    wasChangedSinceLast = false;
                    return false;
                }

                // fallback if couldn't find bool
                return GetActionFloat(hand, button, out wasChangedSinceLast, INPUT_DESIRED.VALUE, true) >= 0.75f;
            }

            wasChangedSinceLast = boolresult.ChangedSinceLastSync == 1;
            return boolresult.CurrentState == 1;
        }

        public static float GetActionFloat(TouchControllerHand hand, TouchControllerButton button, out bool wasChangedSinceLast, INPUT_DESIRED inputMode, bool fallback = false)
        {
            ActionStateGetInfo getfloat = new ActionStateGetInfo()
            {
                Action = GetAction(hand, button, inputMode),
                Type = StructureType.TypeActionStateGetInfo
            };

            ActionStateFloat floatresult = new ActionStateFloat()
            {
                Type = StructureType.TypeActionStateFloat
            };

            baseHMD.Xr.GetActionStateFloat(baseHMD.globalSession, in getfloat, ref floatresult);

            if (floatresult.IsActive == 0)
            {
                if (fallback)
                {
                    // couldn't find an input...
                    wasChangedSinceLast = false;
                    return 0f;
                }

                // fallback if couldn't find float
                return GetActionBool(hand, button, out wasChangedSinceLast, true) ? 1f : 0f;
            }

            wasChangedSinceLast = floatresult.ChangedSinceLastSync == 1;
            return floatresult.CurrentState;
        }

        /// <summary>
        /// Checks OpenXR to see what the current interaction profile is. Defaults to checking the left controller.
        /// </summary>
        /// <returns>String of the interaction path.</returns>
        public static string GetCurrentInteractionProfile(string checkpath = "/user/hand/left")
        {
            if (baseHMD == null || baseHMD.Xr == null)
                return "No HMD enabled or fully initialized yet!";

            ulong path = 0;
            InteractionProfileState state = new InteractionProfileState() { Type = StructureType.TypeInteractionProfileState };
            OpenXRHmd.CheckResult(baseHMD.Xr.StringToPath(baseHMD.Instance, checkpath, ref path), "StringToPath");
            OpenXRHmd.CheckResult(baseHMD.Xr.GetCurrentInteractionProfile(baseHMD.globalSession, path, ref state), "GetCurrentInteractionProfile");
            byte[] buf = new byte[256];
            uint outlen = 0;
            OpenXRHmd.CheckResult(baseHMD.Xr.PathToString(baseHMD.Instance, state.InteractionProfile, (uint)buf.Length, ref outlen, ref buf[0]), "PathToString");
            if (outlen == 0) return "Error getting Interaction Profile String";
            return System.Text.Encoding.Default.GetString(buf, 0, (int)outlen - 1); // -1 to get rid of null character
        }

        internal static unsafe void Initialize(OpenXRHmd hmd)
        {
            baseHMD = hmd;

            // make actions
            for (int i=0; i<HAND_PATH_COUNT; i++)
            {
                for (int j=0; j<2; j++)
                {
                    ActionCreateInfo action_info = new ActionCreateInfo()
                    {
                        Type = StructureType.TypeActionCreateInfo,
                        ActionType = GetActionType((HAND_PATHS)i),
                    };

                    Span<byte> aname = new Span<byte>(action_info.ActionName, 32);
                    Span<byte> lname = new Span<byte>(action_info.LocalizedActionName, 32);
                    string fullname = ((HAND_PATHS)i).ToString() + "H" + j.ToString() + '\0';
                    SilkMarshal.StringIntoSpan(fullname.ToLower(), aname);
                    SilkMarshal.StringIntoSpan(fullname, lname);

                    fixed (Silk.NET.OpenXR.Action* aptr = &MappedActions[j, i])
                        hmd.Xr.CreateAction(hmd.globalActionSet, &action_info, aptr);
                }
            }

            string allInputDetected = "";

            // probe bindings for all profiles
            for (int i=0; i<InteractionProfiles.Length; i++)
            {
                ulong profile = 0;
                hmd.Xr.StringToPath(hmd.Instance, InteractionProfiles[i], ref profile);

                List<ActionSuggestedBinding> bindings = new List<ActionSuggestedBinding>();
                // for each hand...
                for (int hand=0; hand<2; hand++)
                {
                    // for each path we want to bind...
                    for (int path=0; path<HAND_PATH_COUNT; path++)
                    {
                        // list all possible paths that might be valid and pick the first one
                        List<string> possiblePaths = PathPriorities[path];
                        for (int pathattempt=0; pathattempt<possiblePaths.Count; pathattempt++)
                        {
                            // get the hand at the start, then put in the attempt
                            string final_path = hand == (int)TouchControllerHand.Left ? "/user/hand/left" : "/user/hand/right";
                            final_path += possiblePaths[pathattempt];

                            ulong hp_ulong = 0;
                            hmd.Xr.StringToPath(hmd.Instance, final_path, ref hp_ulong);

                            var suggest = new ActionSuggestedBinding()
                            {
                                Action = MappedActions[hand, path],
                                Binding = hp_ulong
                            };

                            if (IsPathSupported(hmd, profile, &suggest))
                            {
                                // note that this controller has a touchpad/thumbstick
                                switch ((HAND_PATHS)path)
                                {
                                    case HAND_PATHS.ThumbstickX:
                                        HasThumbsticks.Add(profile);
                                        break;
                                    case HAND_PATHS.TrackpadX:
                                        HasTouchpads.Add(profile);
                                        break;
                                }

                                if (LogInputDetected)
                                    allInputDetected += "\nGot " + final_path + " for " + InteractionProfiles[i];

                                // got one!
                                bindings.Add(suggest);
                                break;
                            }
                        }
                    }
                }

                // ok, we got all supported paths for this profile, lets do the final suggestion with all of them
                if (bindings.Count > 0)
                {
                    ActionSuggestedBinding[] final_bindings = bindings.ToArray();
                    fixed (ActionSuggestedBinding* asbptr = &final_bindings[0])
                    {
                        InteractionProfileSuggestedBinding suggested_bindings = new InteractionProfileSuggestedBinding()
                        {
                            Type = StructureType.TypeInteractionProfileSuggestedBinding,
                            InteractionProfile = profile,
                            CountSuggestedBindings = (uint)final_bindings.Length,
                            SuggestedBindings = asbptr
                        };

                        OpenXRHmd.CheckResult(hmd.Xr.SuggestInteractionProfileBinding(hmd.Instance, &suggested_bindings), "SuggestInteractionProfileBinding");
                    }
                }
            }

            if (LogInputDetected)
                ErrorFileLogger.WriteLogToFile(allInputDetected);
        }
    }
}
