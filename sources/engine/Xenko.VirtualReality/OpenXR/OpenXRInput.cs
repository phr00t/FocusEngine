using System;
using System.Collections.Generic;
using System.Text;
using Silk.NET.Core.Native;
using Silk.NET.OpenXR;

namespace Xenko.VirtualReality
{
    class OpenXRInput
    {
        // different types of input we are interested in
        public enum HAND_PATHS
        {
            BaseIndex = 0,
            Position = 1,
            TriggerValue = 2,
            ThumbstickX = 3,
            ThumbstickY = 4,
            ThumbstickClick = 5,
            TrackpadX = 6,
            TrackpadY = 7,
            TrackpadClick = 8,
            GripValue = 9,
            ButtonXA = 10, // x on left, a on right (or either index)
            ButtonYB = 11, // y on left, b on right (or either index)
            Menu = 12,
            System = 13, // may be inaccessible
            HapticOut = 14
        }
        public const int HAND_PATH_COUNT = 15;

        // most likely matches for input types above
        private static List<string>[] PathPriorities =
        {
            new List<string>() { "" }, // BaseIndex 0
            new List<string>() { "/input/grip/pose" }, // Position 1
            new List<string>() { "/input/trigger/value", "/input/trigger/click", "/input/select/value", "/input/select/click" }, // TriggerValue 2
            new List<string>() { "/input/thumbstick/x", "/input/trackpad/x" }, // ThumbstickX 3
            new List<string>() { "/input/thumbstick/y", "/input/trackpad/y" }, // ThumbstickY 4
            new List<string>() { "/input/thumbstick/click", "/input/trackpad/click" }, // ThumbstickClick 5
            new List<string>() { "/input/trackpad/x", "/input/thumbstick/x" }, // TrackpadX 6
            new List<string>() { "/input/trackpad/y", "/input/thumbstick/y" }, // TrackpadY 7
            new List<string>() { "/input/trackpad/click", "/input/thumbstick/click" }, // TrackpadClick 8
            new List<string>() { "/input/squeeze/value", "/input/squeeze/click" }, // GripValue 9
            new List<string>() { "/input/x/click", "/input/a/click" }, // Button1 10
            new List<string>() { "/input/y/click", "/input/b/click" }, // Button2 11
            new List<string>() { "/input/menu/click" }, // Menu 12
            new List<string>() { "/input/system/click" }, // System 13
            new List<string>() { "/output/haptic" }, // HapticOut 14
        };

        private static ActionType GetActionType(HAND_PATHS hp)
        {
            switch (hp)
            {
                case HAND_PATHS.BaseIndex:
                case HAND_PATHS.Position:
                    return ActionType.PoseInput;
                case HAND_PATHS.GripValue:
                case HAND_PATHS.ThumbstickX:
                case HAND_PATHS.ThumbstickY:
                case HAND_PATHS.TrackpadX:
                case HAND_PATHS.TrackpadY:
                case HAND_PATHS.TriggerValue:
                    return ActionType.FloatInput;
                case HAND_PATHS.HapticOut:
                    return ActionType.VibrationOutput;
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

        public static Silk.NET.OpenXR.Action GetAction(TouchControllerHand hand, TouchControllerButton button, bool YAxis = false)
        {
            switch (button)
            {
                case TouchControllerButton.ButtonXA:
                    return MappedActions[(int)hand, (int)HAND_PATHS.ButtonXA];
                case TouchControllerButton.ButtonYB:
                    return MappedActions[(int)hand, (int)HAND_PATHS.ButtonYB];
                case TouchControllerButton.Grip:
                    return MappedActions[(int)hand, (int)HAND_PATHS.GripValue];
                case TouchControllerButton.Menu:
                    return MappedActions[(int)hand, (int)HAND_PATHS.Menu];
                case TouchControllerButton.System:
                    return MappedActions[(int)hand, (int)HAND_PATHS.System];
                case TouchControllerButton.Trigger:
                    return MappedActions[(int)hand, (int)HAND_PATHS.TriggerValue];
                case TouchControllerButton.Thumbstick:
                    return MappedActions[(int)hand, YAxis ? (int)HAND_PATHS.ThumbstickY : (int)HAND_PATHS.ThumbstickX];
                case TouchControllerButton.Touchpad:
                    return MappedActions[(int)hand, YAxis ? (int)HAND_PATHS.TrackpadY : (int)HAND_PATHS.TrackpadX];
                default:
                    throw new ArgumentException("Don't know button: " + button);
            }
        }

        public static bool GetActionBool(TouchControllerHand hand, TouchControllerButton button, out bool wasChangedSinceLast)
        {
            ActionStateGetInfo getbool = new ActionStateGetInfo()
            {
                Action = GetAction(hand, button),
                Type = StructureType.TypeActionStateGetInfo
            };

            ActionStateBoolean boolresult = new ActionStateBoolean()
            {
                Type = StructureType.TypeActionStateBoolean
            };

            baseHMD.Xr.GetActionStateBoolean(baseHMD.globalSession, in getbool, ref boolresult);

            wasChangedSinceLast = boolresult.ChangedSinceLastSync == 1;
            return boolresult.CurrentState == 1;
        }

        public static float GetActionFloat(TouchControllerHand hand, TouchControllerButton button, out bool wasChangedSinceLast, bool YAxis = false)
        {
            ActionStateGetInfo getfloat = new ActionStateGetInfo()
            {
                Action = GetAction(hand, button, YAxis),
                Type = StructureType.TypeActionStateGetInfo
            };

            ActionStateFloat floatresult = new ActionStateFloat()
            {
                Type = StructureType.TypeActionStateFloat
            };

            baseHMD.Xr.GetActionStateFloat(baseHMD.globalSession, in getfloat, ref floatresult);

            wasChangedSinceLast = floatresult.ChangedSinceLastSync == 1;
            return floatresult.CurrentState;
        }

        public static unsafe void Initialize(OpenXRHmd hmd)
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

                        OpenXRHmd.CheckResult(hmd.Xr.SuggestInteractionProfileBinding(hmd.Instance, &suggested_bindings));
                    }
                }
            }
        }
    }
}
