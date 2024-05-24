using System;
using System.Collections.Generic;
using System.Text;
using Xenko.Core;
using Xenko.Core.Mathematics;
using Xenko.Games;
using Xenko.Graphics.SDL;
using Xenko.Input;

namespace Xenko.Engine
{
    public class GamepadSystem
    {
        public static IGameControllerDevice PrimaryGamepad;
        private static Game internalGame;

        private static float Clamp(float a, float low, float high)
        {
            if (a > high) return high;
            if (a < low) return low;
            return a;
        }

        static GamepadSystem()
        {
            internalGame = ServiceRegistry.instance?.GetService<IGame>() as Game;
            int p = int.MinValue;
            for (int i = 0; i < internalGame.Input.GameControllerCount; i++)
            {
                var gp = internalGame.Input.GameControllers[i];
                if (gp.Priority > p)
                {
                    PrimaryGamepad = gp;
                    p = gp.Priority;
                }
            }
            Recalibrate();
            PointerX = internalGame.GraphicsDevice.Presenter.BackBuffer.Width / 2;
            PointerY = internalGame.GraphicsDevice.Presenter.BackBuffer.Height / 2;
            // preconfigure a "Click"
            GamepadInputConfig["Click"] = new Vector3(0f, 1f, 0f);
        }

        // (axis/button number, multiplier, axis if 1)
        private static Dictionary<string, Vector3> GamepadInputConfig = new Dictionary<string, Vector3>();
        private static SimpleSaver configSaver;

        public static void SaveAndLoadConfig(string filename = null, bool forcereload = false)
        {
            if (filename == null)
                filename = configSaver?.Filename;

            if (filename == null)
                throw new ArgumentException("No filename known to save gamepad data! Need to call this at least once with a filename.");

            if (configSaver == null || configSaver.Filename != filename || forcereload)
            {
                // load
                configSaver = new SimpleSaver(filename);
                string[] names = configSaver.Get("Names", new string[0]);
                Vector3[] configs = configSaver.Get("Configs", new Vector3[0]);
                for(int i=0; i<names.Length; i++)
                    GamepadInputConfig[names[i]] = configs[i];
            }
            else
            {
                // save
                List<string> names = new List<string>();
                List<Vector3> configs = new List<Vector3>();
                foreach (var pair in GamepadInputConfig)
                {
                    names.Add(pair.Key);
                    configs.Add(pair.Value);
                }
                configSaver.Set("Names", names.ToArray());
                configSaver.Set("Configs", configs.ToArray());
                configSaver.SaveToFile();
            }
        }

        public static float Deadzone = 0.05f;
        public static bool ExponentialAxis = false;

        public static float GetInput(string input, bool pressedOnly = false)
        {
            if (PrimaryGamepad == null || GamepadInputConfig.TryGetValue(input, out Vector3 config) == false) return 0f;
            if (config.Z == 1f)
            {
                float val = (PrimaryGamepad.GetAxis((int)config.X) - defaultAxis[(int)config.X]) * config.Y;
                float axis = Math.Abs(val) > Deadzone ? val : 0f;
                return ExponentialAxis ? axis * Math.Abs(axis) : axis;
            }

            bool buttonTrue = pressedOnly ? PrimaryGamepad.IsButtonPressed((int)config.X) : PrimaryGamepad.IsButtonDown((int)config.X);
            return buttonTrue ? 1f : 0f;
        }

        private static float[] defaultAxis;
        private static int framesToCollect;
        private static float PointerX, PointerY, PointerAccel = 0f;
        public static float PointerSpeed = 50f;

        private static void UpdateEmulatedDPadPointer(float tpf)
        {
            if (PrimaryGamepad == null) return;
            float xdiff = 0f, ydiff = 0f;
            int padPresses = (int)PrimaryGamepad.GetDPad(0);
            if ((padPresses & (int)GamePadButton.PadUp) != 0)
            {
                ydiff -= tpf * (PointerSpeed + PointerAccel);
            }
            else if ((padPresses & (int)GamePadButton.PadDown) != 0)
            {
                ydiff += tpf * (PointerSpeed + PointerAccel);
            }
            else ydiff = 0f;
            if ((padPresses & (int)GamePadButton.PadLeft) != 0)
            {
                xdiff -= tpf * (PointerSpeed + PointerAccel);
            }
            else if ((padPresses & (int)GamePadButton.PadRight) != 0)
            {
                xdiff += tpf * (PointerSpeed + PointerAccel);
            }
            else xdiff = 0f;
            if (xdiff != 0f || ydiff != 0f)
                PointerAccel = Clamp(PointerAccel + tpf * PointerSpeed, 10f, 1000f);
            else
                PointerAccel = PointerSpeed;
            PointerX = Clamp(PointerX + xdiff, 0f, internalGame.GraphicsDevice.Presenter.BackBuffer.Width);
            PointerY = Clamp(PointerY + ydiff, 0f, internalGame.GraphicsDevice.Presenter.BackBuffer.Height);
            Vector2 normalizedpos = new Vector2(PointerX / internalGame.GraphicsDevice.Presenter.BackBuffer.Width, PointerY / internalGame.GraphicsDevice.Presenter.BackBuffer.Height);
            internalGame.Input.Mouse.SetPosition(normalizedpos);
            if (GetInput("Click", true) == 1f)
            {
                internalGame.Window.EmulateMouseEvent(true, true, (int)PointerX, (int)PointerY);
                internalGame.Window.EmulateMouseEvent(false, true, (int)PointerX, (int)PointerY);
            }
        }

        /// <summary>
        /// Debug dump of all controller info
        /// </summary>
        public static string DumpInformation(bool configButtons = true, bool calibratedDefaults = true, bool currentInput = true)
        {
            if (PrimaryGamepad == null) return "No controller detected at start!";
            string info = "---------- CONTROLLER DATA DUMP ----------\n";
            info += "Primary Controller Name: " + PrimaryGamepad.Name + "\n";
            if (configButtons)
            {
                info += "- Configured Inputs:\n";
                foreach (var pair in GamepadInputConfig)
                    info += pair.Key + ", Index: " + pair.Value.X.ToString("0") + ", Multiplier: " + pair.Value.Y.ToString("0") + ", Axis? " + pair.Value.Z.ToString("0") + "\n";
            }
            if (calibratedDefaults)
            {
                info += "- Calibrated Defaults:\n";
                for (int i = 0; i < defaultAxis.Length; i++)
                    info += "Axis #" + i + ": " + defaultAxis[i].ToString("0.00") + "\n";
            }
            if (currentInput)
            {
                info += "- Current Input:\n";
                for (int i = 0; i < PrimaryGamepad.GetAxisCount(); i++)
                    info += "Axis #" + i + ": " + PrimaryGamepad.GetAxis(i).ToString("0.00") + "\n";
                for (int i = 0; i < PrimaryGamepad.GetButtonCount(); i++)
                    info += "Button #" + i + ": " + PrimaryGamepad.IsButtonDown(i) + "\n";
                info += "DPad 0: " + PrimaryGamepad.GetDPad(0).ToString() + "\n";
            }
            return info + "----------------------------------------------\n";
        }

        public static void Recalibrate()
        {
            defaultAxis = null;
            framesToCollect = 5;
        }

        public static void Update(bool useEmulatedPointer = false)
        {
            CollectDefaultAxisValues();
            if (useEmulatedPointer) UpdateEmulatedDPadPointer((float)internalGame.UpdateTime.Elapsed.TotalSeconds);
        }

        private static void CollectDefaultAxisValues()
        {
            if (PrimaryGamepad == null) return;
            // do we already have valid defaults?
            if (framesToCollect-- < 0) return;
            if (defaultAxis == null) defaultAxis = new float[PrimaryGamepad.GetAxisCount()];
            for (int i = 0; i < defaultAxis.Length; i++)
            {
                float a = PrimaryGamepad.GetAxis(i);
                if (Math.Abs(a) > defaultAxis[i]) defaultAxis[i] = a;
            }
        }

        /// <summary>
        /// Returns true if any significant input is being detected from a game controller.
        /// </summary>
        public static bool AnyInputDetected()
        {
            if (PrimaryGamepad == null || defaultAxis == null || framesToCollect > 0) return false;
            for (int i = 0; i < PrimaryGamepad.GetAxisCount(); i++)
            {
                var axis = PrimaryGamepad.GetAxis(i) - defaultAxis[i];
                if (axis > 0.5f)
                    return true;
                else if (axis < -0.5f)
                    return true;
            }
            for (int i = 0; i < PrimaryGamepad.GetButtonCount(); i++)
            {
                if (PrimaryGamepad.IsButtonDown(i)) return true;
            }
            if (PrimaryGamepad.GetDPad(0) != GamePadButton.None) return true;
            return false;
        }

        /// <summary>
        /// Configures a gamepad input name you pick. Call this and it will return true when it senses a press or axis change and associates the two together.
        /// </summary>
        /// <param name="input">Name you make up to monitor for gamepad input to use</param>
        /// <returns>true when input is detected and configured</returns>
        public static bool MonitorForInputConfig(string input)
        {
            if (PrimaryGamepad == null || defaultAxis == null || framesToCollect > 0) return false;
            for (int i = 0; i < PrimaryGamepad.GetAxisCount(); i++)
            {
                var axis = PrimaryGamepad.GetAxis(i) - defaultAxis[i];
                if (axis > 0.5f)
                {
                    GamepadInputConfig[input] = new Vector3(i, 1f, 1f);
                    return true;
                }
                else if (axis < -0.5f)
                {
                    GamepadInputConfig[input] = new Vector3(i, -1f, 1f);
                    return true;
                }
            }
            for (int i = 0; i < PrimaryGamepad.GetButtonCount(); i++)
            {
                var b = PrimaryGamepad.IsButtonDown(i);
                if (b)
                {
                    GamepadInputConfig[input] = new Vector3(i, 1f, 0f);
                    return true;
                }
            }
            return false;
        }
    }
}
