// Copyright (c) Xenko contributors (https://xenko.com) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.
#if XENKO_UI_SDL
using System;
using Xenko.Core.Mathematics;

namespace Xenko.Graphics.SDL
{
    using System.Collections.Generic;
    using System.IO;
    using System.Reflection;
#pragma warning disable SA1200 // Using directives must be placed correctly
    // Using is here otherwise it would conflict with the current namespace that also defines SDL.
    using SDL2;
#pragma warning restore SA1200 // Using directives must be placed correctly
    public class Window : IDisposable
    {
#if XENKO_GRAPHICS_API_OPENGL
        private IntPtr glContext;
#endif

        #region Initialization

        private SDL.SDL_Rect displayBounds;

        internal static bool _makeDefaultResolutionOnCrash = true;

        /// <summary>
        /// Initializes static members of the <see cref="Window"/> class.
        /// </summary>
        static Window()
        {
            // Preload proper SDL native library (depending on CPU type)
            Core.NativeLibrary.PreloadLibrary("SDL2.dll", typeof(Window));

            SDL.SDL_Init(SDL.SDL_INIT_EVERYTHING);
#if XENKO_GRAPHICS_API_OPENGL
            // Set our OpenGL version. It has to be done before any SDL window creation
            // SDL_GL_CONTEXT_CORE gives us only the newer version, deprecated functions are disabled
            int res = SDL.SDL_GL_SetAttribute(SDL.SDL_GLattr.SDL_GL_CONTEXT_PROFILE_MASK, (int)SDL.SDL_GLprofile.SDL_GL_CONTEXT_PROFILE_CORE);
            // 4.2 is the lowest version we support.
            res = SDL.SDL_GL_SetAttribute(SDL.SDL_GLattr.SDL_GL_CONTEXT_MAJOR_VERSION, 4);
#if XENKO_PLATFORM_MACOS
            res = SDL.SDL_GL_SetAttribute(SDL.SDL_GLattr.SDL_GL_CONTEXT_MINOR_VERSION, 1);
#else
            res = SDL.SDL_GL_SetAttribute(SDL.SDL_GLattr.SDL_GL_CONTEXT_MINOR_VERSION, 2);
#endif

#endif
        }

        /// <summary>
        /// Get the current display information of the display (defaults to 0, the first display).
        /// </summary>
        public static void GetDisplayInformation(out int width, out int height, out int refresh_rate, int display = 0) {
            SDL.SDL_GetDesktopDisplayMode(display, out SDL.SDL_DisplayMode mode);
            width = mode.w;
            height = mode.h;
            refresh_rate = mode.refresh_rate;
        }

        /// <summary>
        /// Gets which display index this window is on.
        /// </summary>
        /// <returns></returns>
        public int GetWindowDisplay() {
            return SDL.SDL_GetWindowDisplayIndex(SdlHandle);
        }

        /// <summary>
        /// Debug method of generating a Vulkan crash report
        /// </summary>
        public static bool _GenerateVulkanDeviceLostCrash = false;

        /// <summary>
        /// Returns a list of supported resolutions and refresh rates.
        /// </summary>
        /// <returns>List of width, height and refresh rates</returns>
        public static List<Vector3> GetDisplayModes(bool maxRefreshOnly = true, int display = 0)
        {
            int modes = SDL.SDL_GetNumDisplayModes(display);
            List<Vector3> modeList = new List<Vector3>();
            for (int i=0; i<modes; i++)
            {
                SDL.SDL_GetDisplayMode(display, i, out var mode);
                if (maxRefreshOnly)
                {
                    for (int j=0; j<modeList.Count; j++)
                    {
                        if (modeList[j].X == mode.w &&
                            modeList[j].Y == mode.h &&
                            modeList[j].Z < mode.refresh_rate)
                        {
                            modeList[j] = new Vector3(mode.w, mode.h, mode.refresh_rate);
                            goto next_mode;
                        }
                    }
                }
                modeList.Add(new Vector3(mode.w, mode.h, mode.refresh_rate));
                next_mode:;
            }
            return modeList;
        }

        internal static void GenerateGenericError(Exception e = null, string msg = null, string title = "Error in Application")
        {
            string error = msg ?? "There was an error!";
            if (e != null) error += "\n\nException: " + e.ToString();
            SDL.SDL_ShowSimpleMessageBox(SDL.SDL_MessageBoxFlags.SDL_MESSAGEBOX_ERROR, title, error, IntPtr.Zero);
            Console.Error.WriteLine(error);
            Console.Out.WriteLine(error);
            throw new Exception(error);
        }

        internal static void GenerateGenericInfo(string msg = null, string title = "Info")
        {
            string error = msg ?? "No info proivded!";
            SDL.SDL_ShowSimpleMessageBox(SDL.SDL_MessageBoxFlags.SDL_MESSAGEBOX_INFORMATION, title, error, IntPtr.Zero);
            Console.Error.WriteLine(error);
            Console.Out.WriteLine(error);
        }

        internal static void GenerateCreationError(Exception e = null)
        {
            string error = "There was an error making the game window. Does your video card support Vulkan?\nMake sure your video drivers are up to date and check Vulkan compatibility.\n\nError: " + SDL.SDL_GetError();
            if (e != null) error += "\n\nException: " + e.ToString();
            SDL.SDL_ShowSimpleMessageBox(SDL.SDL_MessageBoxFlags.SDL_MESSAGEBOX_ERROR, "Failed to make Game Window", error, IntPtr.Zero);
            Console.Error.WriteLine(error);
            Console.Out.WriteLine(error);
            throw new Exception(error);
        }

        internal static void GenerateSwapchainError(string err)
        {
            string error = "There was an error rendering to the screen. This is usually caused by ALT+TAB or switching desktops.\nOtherwise, try these things to fix it:\n\n1) Update your video drivers to latest\n2) Turn off 'fix apps that are blurry' in Windows 10\n3) Disable display scaling (use 100% display scale)\n4) Use a lower resolution or windowed-mode\n\nMonitor configurations or invalid resolution settings may also be to blame." + (_makeDefaultResolutionOnCrash ? "\nResolution settings have been set to default for the next run." : "");
            if (err != null) error += "\n\nDetails: " + err;
            if (_makeDefaultResolutionOnCrash)
            {
                try
                {
                    System.IO.File.WriteAllText(Path.GetDirectoryName(Assembly.GetEntryAssembly().Location) + "/DefaultResolution.txt",
                                                "1280\n" +
                                                "720\n" +
                                                "window\n" +
                                                "-1\n" +
                                                "0");
                }
                catch (Exception e2) { }
            }
            SDL.SDL_ShowSimpleMessageBox(SDL.SDL_MessageBoxFlags.SDL_MESSAGEBOX_ERROR, "Failed to Render To Screen", error, IntPtr.Zero);
            Console.Error.WriteLine(error);
            Console.Out.WriteLine(error);
            throw new Exception(error);
        }

        internal static void GeneratePresentError()
        {
            string error = "There was an error drawing to the screen. This is very likely due to old video drivers.\n\nPlease visit NVIDIA's, AMD's or Intel's website to update to the latest video drivers available.\nIf that doesn't help, report this issue in a forum or Discord server.";
            SDL.SDL_ShowSimpleMessageBox(SDL.SDL_MessageBoxFlags.SDL_MESSAGEBOX_ERROR, "Present Error! Try updating Video Drivers", error, IntPtr.Zero);
            Console.Error.WriteLine(error);
            throw new Exception(error);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Window"/> class with <paramref name="title"/> as the title of the Window.
        /// </summary>
        /// <param name="title">Title of the window, see Text property.</param>
        public Window(string title, int width, int height, bool fullscreen, int display_index)
        {
            var flags = SDL.SDL_WindowFlags.SDL_WINDOW_VULKAN | SDL.SDL_WindowFlags.SDL_WINDOW_ALLOW_HIGHDPI;

            // don't try using a resolution too large
            SDL.SDL_GetDesktopDisplayMode(display_index, out SDL.SDL_DisplayMode mode);
            if (mode.w <= width || mode.h <= height)
            {
                flags |= SDL.SDL_WindowFlags.SDL_WINDOW_FULLSCREEN_DESKTOP;
                width = mode.w;
                height = mode.h;
            }
            else if (fullscreen)
                flags |= SDL.SDL_WindowFlags.SDL_WINDOW_FULLSCREEN;

            OriginalSize = new Size2(width, height);

            // bounds so we know where this display is
            SDL.SDL_GetDisplayBounds(display_index, out displayBounds);

            try
            {
                // Create the SDL window and then extract the native handle.
                SdlHandle = SDL.SDL_CreateWindow(title, displayBounds.x + (displayBounds.w / 2) - (width / 2), displayBounds.y + (displayBounds.h / 2) - (height / 2), width, height, flags);
            }
            catch(Exception e)
            {
                GenerateCreationError(e);
            }

            if (SdlHandle == IntPtr.Zero)
            {
                GenerateCreationError();
            }
            else
            {
                SDL.SDL_SysWMinfo info = default(SDL.SDL_SysWMinfo);

                try
                {
                    SDL.SDL_VERSION(out info.version);
                    if (SDL.SDL_GetWindowWMInfo(SdlHandle, ref info) == SDL.SDL_bool.SDL_FALSE)
                        GenerateCreationError();
                } catch (Exception e)
                {
                    GenerateCreationError(e);
                }

#if XENKO_PLATFORM_WINDOWS_DESKTOP
                Handle = info.info.win.window;
#elif XENKO_PLATFORM_LINUX
                Handle = info.info.x11.window;
                Display = info.info.x11.display;
#elif XENKO_PLATFORM_MACOS
                Handle = info.info.cocoa.window;
#endif
                Application.RegisterWindow(this);
                Application.ProcessEvents();
            }
        }
        #endregion

        /// <summary>
        /// Move window to back.
        /// </summary>
        public virtual void SendToBack()
        {
                // FIXME: This is not yet implemented on SDL. We are using SDL_SetWindowPosition in
                // FIXME: the hope that it will apply the new hint.
            Point loc = Location;
            SDL.SDL_SetHint(SDL.SDL_HINT_ALLOW_TOPMOST, "0");
            SDL.SDL_SetWindowPosition(SdlHandle, loc.X, loc.Y);
        }

        /// <summary>
        /// Move window to front.
        /// </summary>
        public virtual void BringToFront()
        {
                // FIXME: This is not yet implemented on SDL. We are using SDL_SetWindowPosition in
                // FIXME: the hope that it will apply the new hint.
            Point loc = Location;
            SDL.SDL_SetHint(SDL.SDL_HINT_ALLOW_TOPMOST, "1");
            SDL.SDL_SetWindowPosition(SdlHandle, loc.X, loc.Y);
        }

        /// <summary>
        /// Get the mouse position on screen.
        /// </summary>
        public static Point MousePosition
        {
            get { return Application.MousePosition; }
        }

        /// <summary>
        /// Get the coordinate of the mouse in Window coordinates
        /// </summary>
        public Point RelativeCursorPosition
        {
            get
            {
                int x, y;
                SDL.SDL_GetMouseState(out x, out y);
                return new Point(x, y);
            }
            set
            {
                SDL.SDL_WarpMouseInWindow(SdlHandle, value.X, value.Y);
            }
        }

        /// <summary>
        /// Make the window topmost
        /// </summary>
        public bool TopMost
        {
            get { return SDL.SDL_GetHint(SDL.SDL_HINT_ALLOW_TOPMOST) == "1"; }
            set
            {
                // FIXME: This is not yet implemented on SDL. We are using SDL_SetWindowPosition in
                // FIXME: the hope that it will apply the new hint.
                Point loc = Location;
                SDL.SDL_SetHint(SDL.SDL_HINT_ALLOW_TOPMOST, (value ? "1" : "0"));
                SDL.SDL_SetWindowPosition(SdlHandle, loc.X, loc.Y);
            }
        }

        /// <summary>
        /// Show window. The first time a window is shown we execute any actions from <see cref="HandleCreated"/>.
        /// </summary>
        public void Show()
        {
            SDL.SDL_ShowWindow(SdlHandle);
        }

        public static int GetNumberOfDisplays()
        {
            return SDL.SDL_GetNumVideoDisplays();
        }

        /// <summary>
        /// Are we showing the window in full screen mode?
        /// </summary>
        public bool IsFullScreen
        {
            get
            {
                uint flags = SDL.SDL_GetWindowFlags(SdlHandle);
                return (flags & (uint)SDL.SDL_WindowFlags.SDL_WINDOW_FULLSCREEN) != 0 ||
                       (flags & (uint)SDL.SDL_WindowFlags.SDL_WINDOW_FULLSCREEN_DESKTOP) != 0;

            }
            set
            {
                if (IsFullScreen == value) return;
                int displayIndex = SDL.SDL_GetWindowDisplayIndex(SdlHandle);
                SDL.SDL_GetDesktopDisplayMode(displayIndex, out SDL.SDL_DisplayMode nativemode);
                SDL.SDL_GetDisplayBounds(displayIndex, out displayBounds);
                if (value) {
                    Location = new Point(displayBounds.x, displayBounds.y);
                    SDL.SDL_SetWindowFullscreen(SdlHandle, nativemode.w <= ClientSize.Width || nativemode.h <= ClientSize.Height ? (uint)SDL.SDL_WindowFlags.SDL_WINDOW_FULLSCREEN_DESKTOP : (uint)SDL.SDL_WindowFlags.SDL_WINDOW_FULLSCREEN);
                }
                else {
                    Location = new Point(displayBounds.x + (int)((nativemode.w - ClientSize.Width) * 0.5f),
                                         displayBounds.y + (int)((nativemode.h - ClientSize.Height) * 0.5));
                    SDL.SDL_SetWindowFullscreen(SdlHandle, 0);
                }
            }
        }

        public void RecenterWindow()
        {
            int displayIndex = SDL.SDL_GetWindowDisplayIndex(SdlHandle);
            SDL.SDL_GetDesktopDisplayMode(displayIndex, out SDL.SDL_DisplayMode nativemode);
            SDL.SDL_GetDisplayBounds(displayIndex, out displayBounds);
            Location = new Point(displayBounds.x + (int)((nativemode.w - ClientSize.Width) * 0.5f),
                                 displayBounds.y + (int)((nativemode.h - ClientSize.Height) * 0.5));
        }

        /// <summary>
        /// Is current window visible?
        /// </summary>
        public bool Visible
        {
            get
            {
                return (SDL.SDL_GetWindowFlags(SdlHandle) & (uint)SDL.SDL_WindowFlags.SDL_WINDOW_SHOWN) != 0;
            }
            set
            {
                if (value)
                {
                    SDL.SDL_ShowWindow(SdlHandle);
                }
                else
                {
                    SDL.SDL_HideWindow(SdlHandle);
                }
            }
        }

        /// <summary>
        /// State of the window which can be either of Normal, Maximized or Minimized.
        /// </summary>
        public FormWindowState WindowState
        {
            get
            {
                uint flags = SDL.SDL_GetWindowFlags(SdlHandle);
                if ((flags & (uint)SDL.SDL_WindowFlags.SDL_WINDOW_FULLSCREEN) != 0)
                {
                    return FormWindowState.Fullscreen;
                }
                if ((flags & (uint)SDL.SDL_WindowFlags.SDL_WINDOW_MAXIMIZED) != 0)
                {
                    return FormWindowState.Maximized;
                }
                else if ((flags & (uint)SDL.SDL_WindowFlags.SDL_WINDOW_MINIMIZED) != 0)
                {
                    return FormWindowState.Minimized;
                }
                else
                {
                    return FormWindowState.Normal;
                }
            }
            set
            {
                switch (value)
                {
                    case FormWindowState.Fullscreen:
                        SDL.SDL_SetWindowFullscreen(SdlHandle, (uint)SDL.SDL_WindowFlags.SDL_WINDOW_FULLSCREEN);
                        break;
                    case FormWindowState.Maximized:
                        SDL.SDL_MaximizeWindow(SdlHandle);
                        break;
                    case FormWindowState.Minimized:
                        SDL.SDL_MinimizeWindow(SdlHandle);
                        break;
                    case FormWindowState.Normal:
                        SDL.SDL_RestoreWindow(SdlHandle);
                        break;
                }
            }
        }

        /// <summary>
        /// Is current window focused?
        /// </summary>
        public bool Focused
        {
            get
            {
                return (SDL.SDL_GetWindowFlags(SdlHandle) & (uint)SDL.SDL_WindowFlags.SDL_WINDOW_INPUT_FOCUS) != 0;
            }
        }

        /// <summary>
        /// Does current window offer a maximize button?
        /// </summary>
        /// <remarks>Setter is not implemented on SDL, since we do have callers, for the time being, the code does nothing instead of throwing an exception.</remarks>
        public bool MaximizeBox
        {
            get { return FormBorderStyle == FormBorderStyle.Sizable; }
            set
            {
                // FIXME: How to implement this since this is being called.
            }
        }

        public Size2? OriginalSize { get; private set; }

        /// <summary>
        /// Size of window.
        /// </summary>
        public Size2 Size
        {
            get
            {
                int w, h;
                SDL.SDL_GetWindowSize(SdlHandle, out w, out h);
                return new Size2(w, h);
            }
            set 
            {
                if (Size != value)
                    SDL.SDL_SetWindowSize(SdlHandle, value.Width, value.Height);
            }
        }

        /// <summary>
        /// Size of the client area of a window.
        /// </summary>
        public unsafe Size2 ClientSize
        {
            get
            {
                return Size;
            }
            set
            {
                Size = value;
            }
        }

        /// <summary>
        /// Size of client area expressed as a rectangle.
        /// </summary>
        public unsafe Rectangle ClientRectangle
        {
            get
            {
#if XENKO_GRAPHICS_API_OPENGL || XENKO_GRAPHICS_API_VULKAN
                int w, h;
                SDL.SDL_GL_GetDrawableSize(SdlHandle, out w, out h);
                return new Rectangle(0, 0, w, h);
#else
                SDL.SDL_Surface *surfPtr = (SDL.SDL_Surface*)SDL.SDL_GetWindowSurface(SdlHandle);
                return new Rectangle(0, 0, surfPtr->w, surfPtr->h);
#endif
            }
            set
            {
                // FIXME: We need to adapt the ClientRectangle to an actual Size to take into account borders.
                // FIXME: On Windows you do this by using AdjustWindowRect.
                SDL.SDL_SetWindowSize(SdlHandle, value.Width, value.Height);
                SDL.SDL_SetWindowPosition(SdlHandle, value.X, value.Y);
            }
        }

        /// <summary>
        /// Coordinates of the top-left corner of the window in screen coordinate.
        /// </summary>
        public Point Location
        {
            get
            {
                int x, y;
                SDL.SDL_GetWindowPosition(SdlHandle, out x, out y);
                return new Point(x, y);
            }
            set
            {
                SDL.SDL_SetWindowPosition(SdlHandle, value.X, value.Y);
            }
        }

        /// <summary>
        /// Text of the title of the Window.
        /// </summary>
        public string Text
        {
            get { return SDL.SDL_GetWindowTitle(SdlHandle); }
            set { SDL.SDL_SetWindowTitle(SdlHandle, value); }
        }

        /// <summary>
        /// Style of border. Currently can only be Sizable or FixedSingle.
        /// </summary>
        public FormBorderStyle FormBorderStyle
        {
            get
            {
                uint flags = SDL.SDL_GetWindowFlags(SdlHandle);
                if ((flags & (uint)SDL.SDL_WindowFlags.SDL_WINDOW_RESIZABLE) != 0)
                {
                    return FormBorderStyle.Sizable;
                }
                else
                {
                    return FormBorderStyle.FixedSingle;
                }
            }
            set
            {
                SDL.SDL_SetWindowResizable(SdlHandle, value == FormBorderStyle.Sizable ? SDL.SDL_bool.SDL_TRUE : SDL.SDL_bool.SDL_FALSE);
            }
        }

        public void SetRelativeMouseMode(bool enabled)
        {
            SDL.SDL_SetRelativeMouseMode(enabled ? SDL.SDL_bool.SDL_TRUE : SDL.SDL_bool.SDL_FALSE);
        }

        // Events that one can hook up.
        public delegate void MouseButtonDelegate(SDL.SDL_MouseButtonEvent e);
        public delegate void MouseMoveDelegate(SDL.SDL_MouseMotionEvent e);
        public delegate void MouseWheelDelegate(SDL.SDL_MouseWheelEvent e);
        public delegate void TextEditingDelegate(SDL.SDL_TextEditingEvent e);
        public delegate void TextInputDelegate(SDL.SDL_TextInputEvent e);
        public delegate void WindowEventDelegate(SDL.SDL_WindowEvent e);
        public delegate void KeyDelegate(SDL.SDL_KeyboardEvent e);
        public delegate void JoystickDeviceChangedDelegate(int which);
        public delegate void TouchFingerDelegate(SDL.SDL_TouchFingerEvent e);
        public delegate void NotificationDelegate();

        public event MouseButtonDelegate PointerButtonPressActions;
        public event MouseButtonDelegate PointerButtonReleaseActions;
        public event MouseWheelDelegate MouseWheelActions;
        public event MouseMoveDelegate MouseMoveActions;
        public event KeyDelegate KeyDownActions;
        public event KeyDelegate KeyUpActions;
        public event TextEditingDelegate TextEditingActions;
        public event TextInputDelegate TextInputActions;
        public event NotificationDelegate CloseActions;
        public event JoystickDeviceChangedDelegate JoystickDeviceAdded;
        public event JoystickDeviceChangedDelegate JoystickDeviceRemoved;
        public event TouchFingerDelegate FingerMoveActions;
        public event TouchFingerDelegate FingerPressActions;
        public event TouchFingerDelegate FingerReleaseActions;
        public event WindowEventDelegate ResizeBeginActions;
        public event WindowEventDelegate ResizeEndActions;
        public event WindowEventDelegate ActivateActions;
        public event WindowEventDelegate DeActivateActions;
        public event WindowEventDelegate MinimizedActions;
        public event WindowEventDelegate MaximizedActions;
        public event WindowEventDelegate RestoredActions;
        public event WindowEventDelegate MouseEnterActions;
        public event WindowEventDelegate MouseLeaveActions;
        public event WindowEventDelegate FocusGainedActions;
        public event WindowEventDelegate FocusLostActions;

        /// <summary>
        /// Process events for the current window
        /// </summary>
        public virtual void ProcessEvent(SDL.SDL_Event e)
        {
            switch (e.type)
            {
                case SDL.SDL_EventType.SDL_QUIT:
                        // When SDL sends a SDL_QUIT message, we have actually clicked on the close button.
                    CloseActions?.Invoke();
                    break;

                case SDL.SDL_EventType.SDL_MOUSEBUTTONDOWN:
                    PointerButtonPressActions?.Invoke(e.button);
                    break;

                case SDL.SDL_EventType.SDL_MOUSEBUTTONUP:
                    PointerButtonReleaseActions?.Invoke(e.button);
                    break;

                case SDL.SDL_EventType.SDL_MOUSEMOTION:
                    MouseMoveActions?.Invoke(e.motion);
                    break;

                case SDL.SDL_EventType.SDL_MOUSEWHEEL:
                    MouseWheelActions?.Invoke(e.wheel);
                    break;

                case SDL.SDL_EventType.SDL_KEYDOWN:
                    KeyDownActions?.Invoke(e.key);
                    break;

                case SDL.SDL_EventType.SDL_KEYUP:
                    KeyUpActions?.Invoke(e.key);
                    break;

                case SDL.SDL_EventType.SDL_TEXTEDITING:
                    TextEditingActions?.Invoke(e.edit);
                    break;

                case SDL.SDL_EventType.SDL_TEXTINPUT:
                    TextInputActions?.Invoke(e.text);
                    break;

                case SDL.SDL_EventType.SDL_JOYDEVICEADDED:
                    JoystickDeviceAdded?.Invoke(e.jdevice.which);
                    break;

                case SDL.SDL_EventType.SDL_JOYDEVICEREMOVED:
                    JoystickDeviceRemoved?.Invoke(e.jdevice.which);
                    break;

                case SDL.SDL_EventType.SDL_FINGERMOTION:
                    FingerMoveActions?.Invoke(e.tfinger);
                    break;

                case SDL.SDL_EventType.SDL_FINGERDOWN:
                    FingerPressActions?.Invoke(e.tfinger);
                    break;

                case SDL.SDL_EventType.SDL_FINGERUP:
                    FingerReleaseActions?.Invoke(e.tfinger);
                    break;

                case SDL.SDL_EventType.SDL_WINDOWEVENT:
                {
                    switch (e.window.windowEvent)
                    {
                        case SDL.SDL_WindowEventID.SDL_WINDOWEVENT_SIZE_CHANGED:
                            ResizeBeginActions?.Invoke(e.window);
                            break;

                        case SDL.SDL_WindowEventID.SDL_WINDOWEVENT_RESIZED:
                            ResizeEndActions?.Invoke(e.window);
                            break;

                        case SDL.SDL_WindowEventID.SDL_WINDOWEVENT_CLOSE:
                            CloseActions?.Invoke();
                            break;

                        case SDL.SDL_WindowEventID.SDL_WINDOWEVENT_SHOWN:
                            ActivateActions?.Invoke(e.window);
                            break;

                        case SDL.SDL_WindowEventID.SDL_WINDOWEVENT_HIDDEN:
                            DeActivateActions?.Invoke(e.window);
                            break;

                        case SDL.SDL_WindowEventID.SDL_WINDOWEVENT_MINIMIZED:
                            MinimizedActions?.Invoke(e.window);
                            break;

                        case SDL.SDL_WindowEventID.SDL_WINDOWEVENT_MAXIMIZED:
                            MaximizedActions?.Invoke(e.window);
                            break;

                        case SDL.SDL_WindowEventID.SDL_WINDOWEVENT_RESTORED:
                            RestoredActions?.Invoke(e.window);
                            break;

                        case SDL.SDL_WindowEventID.SDL_WINDOWEVENT_ENTER:
                            MouseEnterActions?.Invoke(e.window);
                            break;

                        case SDL.SDL_WindowEventID.SDL_WINDOWEVENT_LEAVE:
                            MouseLeaveActions?.Invoke(e.window);
                            break;

                        case SDL.SDL_WindowEventID.SDL_WINDOWEVENT_FOCUS_GAINED:
                            e.window.data1 = IsFullScreen ? 1 : 0;
                            FocusGainedActions?.Invoke(e.window);
                            break;

                        case SDL.SDL_WindowEventID.SDL_WINDOWEVENT_FOCUS_LOST:
                            e.window.data1 = IsFullScreen ? 2 : 0;
                            FocusLostActions?.Invoke(e.window);
                            break;
                    }
                    break;
                }
            }
        }

        /// <summary>
        /// Platform specific handle for Window:
        /// - On Windows: the HWND of the window
        /// - On Unix: the Window ID (XID). Note that on Unix, the value is 32-bit (See X11/X.h for the typedef of XID).
        /// </summary>
        public IntPtr Handle { get; private set; }

#if XENKO_PLATFORM_LINUX
        /// <summary>
        /// Display of current Window.
        /// </summary>
        public IntPtr Display { get; private set;}

        /// <summary>
        /// Given a Xlib display pointer, returns the corresponding Xcb connection.
        /// </summary>
        /// <param name="display">The Xlib display pointer.</param>
        /// <returns>A Xcb connection pointer.</returns>
        [System.Runtime.InteropServices.DllImport("libX11-xcb")]
        private static extern IntPtr XGetXCBConnection(IntPtr display);

        /// <summary>
        /// Associated XcbConnection for <see cref="Display"/>. Null pointer if none available.
        /// </summary>
        public IntPtr XcbConnection
        {
            get
            {
                try
                {
                    return XGetXCBConnection(Display);
                }
                catch (Exception)
                {
                    return IntPtr.Zero;
                }
            }
        }
#endif

        /// <summary>
        /// The SDL window handle.
        /// </summary>
        public IntPtr SdlHandle { get; private set; }

        /// <summary>
        /// Is the Window still alive?
        /// </summary>
        public bool Exists
        {
            get { return SdlHandle != IntPtr.Zero; }
        }
#if XENKO_GRAPHICS_API_OPENGL
        /// <summary>
        /// Current instance as seen as a IWindowInfo.
        /// </summary>
        public OpenTK.Platform.IWindowInfo WindowInfo
        {
            get
            {
                    // Create the proper Sdl2WindowInfo context.
                return OpenTK.Platform.Utilities.CreateSdl2WindowInfo(SdlHandle);
            }
        }

        /// <summary>
        /// The OpenGL Context if any
        /// </summary>
        public OpenTK.Graphics.IGraphicsContext DummyGLContext;
#endif

        #region Disposal
        ~Window()
        {
            Dispose(false);
        }

        /// <summary>
        /// Have we already disposed of the current object?
        /// </summary>
        public bool IsDisposed
        {
            get { return SdlHandle == IntPtr.Zero; }
        }

        /// <summary>
        /// Actions to be called when we dispose of current.
        /// </summary>
        public event EventHandler Disposed;

        /// <summary>
        /// Dispose of current Window.
        /// </summary>
        /// <param name="disposing">If <c>false</c> we are being called from the Finalizer.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (SdlHandle != IntPtr.Zero)
            {
                if (disposing)
                {
                    // Dispose managed state (managed objects).
                    Disposed?.Invoke(this, EventArgs.Empty);
                    Application.UnregisterWindow(this);
                }

#if XENKO_GRAPHICS_API_OPENGL
                // Dispose OpenGL context
                DummyGLContext?.Dispose();
                DummyGLContext = null;
                if (glContext != IntPtr.Zero)
                {
                    SDL.SDL_GL_DeleteContext(glContext);
                    glContext = IntPtr.Zero;
                }
#endif

                // Free unmanaged resources (unmanaged objects) and override a finalizer below.
                SDL.SDL_DestroyWindow(SdlHandle);
                SdlHandle = IntPtr.Zero;
                Handle = IntPtr.Zero;
            }
        }
  
        // This code added to correctly implement the disposable pattern.
        public void Dispose()
        {
                // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
            Dispose(true);
                // Performance improvement to avoid being called a second time by the GC.
            GC.SuppressFinalize(this);
        }
#endregion
    }
}
#endif
