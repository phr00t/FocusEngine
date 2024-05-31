// Copyright (c) Xenko contributors (https://xenko.com) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.
//
// Copyright (c) 2010-2013 SharpDX - Alexandre Mutel
// 
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.

using System;
using System.IO;
using System.Reflection;
using System.Threading;
using Xenko.Core;
using Xenko.Core.Annotations;
using Xenko.Core.Diagnostics;
using Xenko.Core.IO;
using Xenko.Core.Serialization.Contents;
using Xenko.Games.Time;
using Xenko.Graphics;
using Xenko.Graphics.SDL;

namespace Xenko.Games
{
    /// <summary>
    /// The game.
    /// </summary>
    public abstract class GameBase : ComponentBase, IGame
    {
        #region Fields

        private readonly GameTime updateTime;
        private readonly GameTime drawTime;
        private readonly TimerTick playTimer;
        private readonly TimerTick updateTimer;
        private readonly int[] lastUpdateCount;
        private readonly float updateCountAverageSlowLimit;
        private readonly GamePlatform gamePlatform;
        private IGraphicsDeviceService graphicsDeviceService;
        protected IGraphicsDeviceManager graphicsDeviceManager;
        private ResumeManager resumeManager;
        private bool isExiting;
        private bool suppressDraw;
        private bool beginDrawOk;

        private TimeSpan defaultTimeSpan = TimeSpan.FromTicks(1);
        private TimeSpan totalUpdateTime;
        private TimeSpan totalDrawTime;
        private readonly TimeSpan maximumElapsedTime;
        private TimeSpan accumulatedElapsedGameTime;
        private TimeSpan lastFrameElapsedGameTime;
        private int nextLastUpdateCountIndex;
        private bool forceElapsedTimeToZero;

        private readonly TimerTick timer;

        protected readonly ILogger Log;

        private bool isMouseVisible;

        internal bool SlowDownDrawCalls;

        internal object TickLock = new object();

        #endregion

        #region Constructors and Destructors

        /// <summary>
        /// Initializes a new instance of the <see cref="GameBase" /> class.
        /// </summary>
        protected GameBase()
        {
            // Internals
            Log = GlobalLogger.GetLogger(GetType().GetTypeInfo().Name);
            updateTime = new GameTime();
            drawTime = new GameTime();
            playTimer = new TimerTick();
            updateTimer = new TimerTick();
            totalUpdateTime = new TimeSpan();
            timer = new TimerTick();
            IsFixedTimeStep = false;
            maximumElapsedTime = TimeSpan.FromMilliseconds(500.0);
            lastUpdateCount = new int[4];
            nextLastUpdateCountIndex = 0;
            TargetElapsedTime = defaultTimeSpan; // default empty timespan, will be set on window create if not set elsewhere

            TreatNotFocusedLikeMinimized = true;
            DrawEvenMinimized            = false;
            WindowMinimumUpdateRate      = new ThreadThrottler(defaultTimeSpan); // will be set when window gets created with window's refresh rate
            MinimizedMinimumUpdateRate   = new ThreadThrottler(TimeSpan.FromTicks(TimeSpan.TicksPerSecond / 15)); // by default 15 updates per second while minimized

            // Calculate the updateCountAverageSlowLimit (assuming moving average is >=3 )
            // Example for a moving average of 4:
            // updateCountAverageSlowLimit = (2 * 2 + (4 - 2)) / 4 = 1.5f
            const int BadUpdateCountTime = 2; // number of bad frame (a bad frame is a frame that has at least 2 updates)
            var maxLastCount = 2 * Math.Min(BadUpdateCountTime, lastUpdateCount.Length);
            updateCountAverageSlowLimit = (float)(maxLastCount + (lastUpdateCount.Length - maxLastCount)) / lastUpdateCount.Length;

            // Externals
            Services = new ServiceRegistry(true);

            // Database file provider
            Services.AddService<IDatabaseFileProviderService>(new DatabaseFileProviderService(null));

            LaunchParameters = new LaunchParameters();
            GameSystems = new GameSystemCollection(Services);
            Services.AddService<IGameSystemCollection>(GameSystems);

            // Create Platform
            gamePlatform = GamePlatform.Create(this);
            gamePlatform.Activated += GamePlatform_Activated;
            gamePlatform.Deactivated += GamePlatform_Deactivated;
            gamePlatform.Exiting += GamePlatform_Exiting;
            gamePlatform.WindowCreated += GamePlatformOnWindowCreated;

            // Setup registry
            Services.AddService<IGame>(this);
            Services.AddService<IGraphicsDeviceFactory>(gamePlatform);
            Services.AddService<IGamePlatform>(gamePlatform);

            IsActive = true;
        }

        #endregion

        #region Public Events

        /// <summary>
        /// Occurs when [activated].
        /// </summary>
        public event EventHandler<EventArgs> Activated;

        /// <summary>
        /// Occurs when [deactivated].
        /// </summary>
        public event EventHandler<EventArgs> Deactivated;

        /// <summary>
        /// Occurs when [exiting].
        /// </summary>
        public event EventHandler<EventArgs> Exiting;

        /// <summary>
        /// Occurs when [window created].
        /// </summary>
        public event EventHandler<EventArgs> WindowCreated;

        #endregion

        #region Public Properties

        /// <summary>
        /// Gets the current update time from the start of the game.
        /// </summary>
        /// <value>The current update time.</value>
        public GameTime UpdateTime
        {
            get
            {
                return updateTime;
            }
        }

        /// <summary>
        /// Gets the current draw time from the start of the game.
        /// </summary>
        /// <value>The current update time.</value>
        public GameTime DrawTime
        {
            get
            {
                return drawTime;
            }
        }

        /// <summary>
        /// Gets the draw interpolation factor, which is (<see cref="UpdateTime"/> - <see cref="DrawTime"/>) / <see cref="TargetElapsedTime"/>.
        /// If <see cref="IsFixedTimeStep"/> is false, it will be 0 as <see cref="UpdateTime"/> and <see cref="DrawTime"/> will be equal.
        /// </summary>
        /// <value>
        /// The draw interpolation factor.
        /// </value>
        public float DrawInterpolationFactor { get; private set; }

        /// <summary>
        /// Gets the play time, can be changed to match to the time of the current rendering scene.
        /// </summary>
        /// <value>The play time.</value>
        public TimerTick PlayTime
        {
            get
            {
                return playTimer;
            }
        }

        /// <summary>
        /// Gets the <see cref="ContentManager"/>.
        /// </summary>
        public ContentManager Content { get; private set; }

        /// <summary>
        /// Gets the game components registered by this game.
        /// </summary>
        /// <value>The game components.</value>
        public GameSystemCollection GameSystems { get; private set; }

        /// <summary>
        /// Gets the game context.
        /// </summary>
        /// <value>The game context.</value>
        public GameContext Context { get; private set; }

        /// <summary>
        /// Gets the graphics device.
        /// </summary>
        /// <value>The graphics device.</value>
        public GraphicsDevice GraphicsDevice { get; private set; }

        public GraphicsContext GraphicsContext { get; private set; }

        /// <summary>
        /// Gets or sets the inactive sleep time.
        /// </summary>
        /// <value>The inactive sleep time.</value>
        public TimeSpan InactiveSleepTime { get; set; }

        /// <summary>
        /// Gets a value indicating whether this instance is active.
        /// </summary>
        /// <value><c>true</c> if this instance is active; otherwise, <c>false</c>.</value>
        public bool IsActive { get; private set; }

        /// <summary>
        /// Gets or sets a value indicating whether this instance is fixed time step.
        /// </summary>
        /// <value><c>true</c> if this instance is fixed time step; otherwise, <c>false</c>.</value>
        public bool IsFixedTimeStep { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether this instance should force exactly one update step per one draw step
        /// </summary>
        /// <value><c>true</c> if this instance forces one update step per one draw step; otherwise, <c>false</c>.</value>
        public bool ForceOneUpdatePerDraw { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether draw can happen as fast as possible, even when <see cref="IsFixedTimeStep"/> is set.
        /// </summary>
        /// <value><c>true</c> if this instance allows desychronized drawing; otherwise, <c>false</c>.</value>
        public bool IsDrawDesynchronized { get; set; }

        public bool EarlyExit { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the mouse should be visible.
        /// </summary>
        /// <value><c>true</c> if the mouse should be visible; otherwise, <c>false</c>.</value>
        public bool IsMouseVisible
        {
            get
            {
                return isMouseVisible;
            }

            set
            {
                isMouseVisible = value;
                if (Window != null)
                {
                    Window.IsMouseVisible = value;
                }
            }
        }

        /// <summary>
        /// Gets the launch parameters.
        /// </summary>
        /// <value>The launch parameters.</value>
        public LaunchParameters LaunchParameters { get; private set; }

        /// <summary>
        /// Gets a value indicating whether is running.
        /// </summary>
        public bool IsRunning { get; private set; }

        /// <summary>
        /// Gets the service container.
        /// </summary>
        /// <value>The service container.</value>
        [NotNull]
        public ServiceRegistry Services { get; }

        /// <summary>
        /// Gets or sets the target elapsed time, this is the duration of each tick/update 
        /// when <see cref="IsFixedTimeStep"/> is enabled.
        /// </summary>
        /// <value>The target elapsed time.</value>
        public TimeSpan TargetElapsedTime { get; set; }

        /// <summary>
        /// Access to the throttler used to set the minimum time allowed between each updates, 
        /// set it's <see cref="ThreadThrottler.MinimumElapsedTime"/> to TimeSpan.FromSeconds(1d / yourFramePerSeconds) to control the maximum frames per second.
        /// </summary>
        public ThreadThrottler WindowMinimumUpdateRate { get; }

        /// <summary>
        /// Access to the throttler used to set the minimum time allowed between each updates while the window is minimized and,
        /// depending on <see cref="TreatNotFocusedLikeMinimized"/>, while unfocused.
        /// </summary>
        public ThreadThrottler MinimizedMinimumUpdateRate { get; }

        /// <summary>
        /// Considers windows without user focus like a minimized window for <see cref="MinimizedMinimumUpdateRate"/> 
        /// </summary>
        public bool TreatNotFocusedLikeMinimized { get; set; }

        /// <summary>
        /// Draw even when minimized? Needed in VR
        /// </summary>
        public bool DrawEvenMinimized { get; set; }

        /// <summary>
        /// Gets the abstract window.
        /// </summary>
        /// <value>The window.</value>
        public GameWindow Window
        {
            get
            {
                if (gamePlatform != null)
                {
                    return gamePlatform.MainWindow;
                }
                return null;
            }
        }

        public abstract void ConfirmRenderingSettings(bool gameCreation);

        /// <summary>
        /// Gets the full name of the device this game is running if available
        /// </summary>
        public string FullPlatformName => gamePlatform.FullName;

        public GameState State { get; set; }

        #endregion

        #region Public Methods and Operators

        /// <summary>
        /// Exits the game.
        /// </summary>
        public void Exit()
        {
            isExiting = true;
            gamePlatform.Exit();
        }

        /// <summary>
        /// Resets the elapsed time counter.
        /// </summary>
        public void ResetElapsedTime()
        {
            forceElapsedTimeToZero = true;
            Array.Clear(lastUpdateCount, 0, lastUpdateCount.Length);
            nextLastUpdateCountIndex = 0;
        }

        internal void InitializeBeforeRun()
        {
            try
            {
                using (var profile = Profiler.Begin(GameProfilingKeys.GameInitialize))
                {
                    // Initialize this instance and all game systems before trying to create the device.
                    Initialize();

                    // Make sure that the device is already created
                    graphicsDeviceManager.CreateDevice();

                    // Gets the graphics device service
                    graphicsDeviceService = Services.GetService<IGraphicsDeviceService>();
                    if (graphicsDeviceService == null)
                    {
                        throw new InvalidOperationException("No GraphicsDeviceService found");
                    }

                    // Checks the graphics device
                    if (graphicsDeviceService.GraphicsDevice == null)
                    {
                        throw new InvalidOperationException("No GraphicsDevice found");
                    }

                    // Setup the graphics device if it was not already setup.
                    SetupGraphicsDeviceEvents();

                    // Bind Graphics Context enabling initialize to use GL API eg. SetData to texture ...etc
                    BeginDraw();

                    LoadContentInternal();

                    IsRunning = true;

                    BeginRun();

                    timer.Reset();
                    updateTime.Reset(totalUpdateTime);

                    // Run the first time an update
                    updateTimer.Reset();
                    using (Profiler.Begin(GameProfilingKeys.GameUpdate))
                    {
                        Update(updateTime);
                    }
                    updateTimer.Tick();

                    // Reset PlayTime
                    playTimer.Reset();

                    // Unbind Graphics Context without presenting
                    EndDraw(false);
                }
            }
            catch (Exception ex)
            {
                Log.Error("Unexpected exception", ex);
                throw;
            }
        }

        /// <summary>
        /// If AutoLoadDefaultSettings is true, these values will be set on game start. Set width & height to int.MaxValue to use highest native values.
        /// </summary>
        /// <returns>true if default settings changed</returns>
        public bool SetDefaultSettings(int width, int height, bool fullscreen, float? fov = null, int displayindex = 0)
        {
            GetDefaultSettings(out int current_width, out int current_height, out bool current_fullscreen, out float outfov, out int outindex);
            if (width == current_width && height == current_height && current_fullscreen == fullscreen && (fov.HasValue == false || fov == outfov) && outindex == displayindex) return false;
            try
            {
                System.IO.File.WriteAllText(Path.GetDirectoryName(Assembly.GetEntryAssembly().Location) + "/DefaultResolution.txt",
                                            width.ToString() + "\n" +
                                            height.ToString() + "\n" +
                                            (fullscreen ? "fullscreen" : "window") + "\n" +
                                            (fov ?? outfov).ToString() + "\n" +
                                            displayindex.ToString());
                return true;
            }
            catch (Exception e)
            {
                return false;
            }
        }

        private bool settingsOverride = false, settingsOverrideFS;
        private int settingsOverrideW, settingsOverrideH, settingsOverrideDisplay;

        /// <summary>
        /// Temporarily uses different settings this run, without changing the saved default settings. Set width & height to int.MaxValue to use highest native values.
        /// </summary>
        public void OverrideDefaultSettings(int width, int height, bool fullscreen, int displayindex = 0)
        {
            settingsOverride = true;
            settingsOverrideW = width;
            settingsOverrideH = height;
            settingsOverrideFS = fullscreen;
            settingsOverrideDisplay = displayindex;
        }

        /// <summary>
        /// Gets default settings that will be used on game startup, if AutoLoadDefaultSettings is true. Caps resolution to native display resolution.
        /// </summary>
        public void GetDefaultSettings(out int width, out int height, out bool fullscreen, out float fov, out int displayindex, Window useSDLWindow = null)
        {
            string defaultFile = Path.GetDirectoryName(Assembly.GetEntryAssembly().Location) + "/DefaultResolution.txt";
            // default settings are maximum native resolution
            bool gotCustomWH = false;
            width = int.MaxValue;
            height = int.MaxValue;
            displayindex = 0;
            fullscreen = true;
            fov = -1f;
            // wait, are we overriding settings?
            if (settingsOverride)
            {
                width = settingsOverrideW;
                height = settingsOverrideH;
                fullscreen = settingsOverrideFS;
                displayindex = settingsOverrideDisplay;
                gotCustomWH = true;
            }
            else if (File.Exists(defaultFile))
            {
                try
                {
                    string[] vals = File.ReadAllLines(defaultFile);
                    if (vals.Length >= 2)
                        gotCustomWH = int.TryParse(vals[0].Trim(), out width) && int.TryParse(vals[1].Trim(), out height);
                    if (vals.Length >= 3)
                        fullscreen = vals[2].Trim().ToLower().StartsWith("full");
                    if (vals.Length >= 4)
                        float.TryParse(vals[3].Trim(), out fov);
                    if (vals.Length >= 5)
                        int.TryParse(vals[4].Trim(), out displayindex);
                }
                catch (Exception e) { }
            }
            try
            {
                // cap values to native resolution (try to use display window)
                int native_width, native_height, refresh_rate;
                if (useSDLWindow is Window gwsdl) displayindex = gwsdl.GetWindowDisplay();
                Graphics.SDL.Window.GetDisplayInformation(out native_width, out native_height, out refresh_rate, displayindex);
                if (width >= native_width || height >= native_height)
                {
                    // force fullscreen if using native or higher,
                    // as crashes can happen on some hardware if using this big of a window
                    width = native_width;
                    height = native_height;
                    fullscreen = true;
                }
                gotCustomWH = true;
            }
            catch (Exception e) { }
            // make sure we got something valid
            if (gotCustomWH == false)
            {
                width = 1280;
                height = 720;
                fullscreen = false;
            }
        }

#if XENKO_PLATFORM_WINDOWS_DESKTOP && XENKO_GRAPHICS_API_VULKAN
        [System.Runtime.InteropServices.DllImport("shcore.dll")]
        private static extern bool SetProcessDpiAwareness(int dpimode);
#endif

        /// <summary>
        /// Call this method to initialize the game, begin running the game loop, and start processing events for the game.
        /// </summary>
        /// <param name="gameContext">The window Context for this game.</param>
        /// <exception cref="System.InvalidOperationException">Cannot run this instance while it is already running</exception>
        public void Run(GameContext gameContext = null)
        {
            if (IsRunning)
            {
                throw new InvalidOperationException("Cannot run this instance while it is already running");
            }

            // Gets the graphics device manager
            graphicsDeviceManager = Services.GetService<IGraphicsDeviceManager>();
            if (graphicsDeviceManager == null)
            {
                throw new InvalidOperationException("No GraphicsDeviceManager found");
            }

#if XENKO_PLATFORM_WINDOWS_DESKTOP && XENKO_GRAPHICS_API_VULKAN
            try {
                // fix scaling on Windows 8.1+
                SetProcessDpiAwareness(2);
            } catch(Exception e) { } // don't break if we are on windows 8 or lower
#endif

#if XENKO_GRAPHICS_API_VULKAN
            // get the resolution now, so we can create our window with the right settings right from the start
            GraphicsDeviceManager gdm = graphicsDeviceManager as GraphicsDeviceManager;
            GetDefaultSettings(out gdm.preferredBackBufferWidth, out gdm.preferredBackBufferHeight, out bool fullScreen, out float fov, out int index, (gameContext as GameContextSDL)?.Control ?? null);
            gdm.IsFullScreen = fullScreen;
            // Gets the GameWindow Context
            Context = gameContext ?? GameContextFactory.NewDefaultGameContext(gdm.preferredBackBufferWidth, gdm.preferredBackBufferHeight, fullScreen, index);
            PrepareContext(fov);
#else
            Context = gameContext ?? GameContextFactory.NewDefaultGameContext();
            PrepareContext();            
#endif

            try
            {
                // TODO temporary workaround as the engine doesn't support yet resize
                var graphicsDeviceManagerImpl = (GraphicsDeviceManager)graphicsDeviceManager;
                Context.RequestedWidth = graphicsDeviceManagerImpl.PreferredBackBufferWidth;
                Context.RequestedHeight = graphicsDeviceManagerImpl.PreferredBackBufferHeight;
                Context.RequestedBackBufferFormat = graphicsDeviceManagerImpl.PreferredBackBufferFormat;
                Context.RequestedDepthStencilFormat = graphicsDeviceManagerImpl.PreferredDepthStencilFormat;
                Context.RequestedGraphicsProfile = graphicsDeviceManagerImpl.PreferredGraphicsProfile;
                Context.DeviceCreationFlags = graphicsDeviceManagerImpl.DeviceCreationFlags;

                gamePlatform.Run(Context);

                EndRun();
            }
            finally
            {
                IsRunning = false;
            }
        }

        /// <summary>
        /// Creates or updates <see cref="Context"/> before window and device are created.
        /// </summary>
        protected virtual void PrepareContext(float overridefov = -1f)
        {
            // Content manager
            Content = new ContentManager(Services);
            Services.AddService<IContentManager>(Content);
            Services.AddService(Content);
        }

        /// <summary>
        /// Prevents calls to Draw until the next Update.
        /// </summary>
        public void SuppressDraw()
        {
            suppressDraw = true;
        }

        /// <summary>
        /// Updates the game's clock and calls Update and Draw.
        /// </summary>
        public void Tick()
        {
            lock (TickLock)
            {
                TickInternal();
            }
        }

        internal static bool ShouldPresent = false, PauseRendering = false;
        private void TickInternal()
        {
            try
            {
                // If this instance is existing, then don't make any further update/draw
                if (isExiting)
                    return;

                // If this instance is not active, sleep for an inactive sleep time
                if (!IsActive)
                {
                    Utilities.Sleep(InactiveSleepTime);
                    return;
                }

                // Update the timer
                timer.Tick();

                // Update the playTimer timer
                playTimer.Tick();

                // Measure updateTimer
                updateTimer.Reset();

                var elapsedAdjustedTime = timer.ElapsedTimeWithPause;

                if (forceElapsedTimeToZero)
                {
                    elapsedAdjustedTime = TimeSpan.Zero;
                    forceElapsedTimeToZero = false;
                }

                if (elapsedAdjustedTime > maximumElapsedTime)
                {
                    elapsedAdjustedTime = maximumElapsedTime;
                }

                bool suppressNextDraw = true;
                int updateCount = 1;
                var singleFrameElapsedTime = elapsedAdjustedTime;
                var drawLag = 0L;

                if (IsFixedTimeStep)
                {
                    // If the rounded TargetElapsedTime is equivalent to current ElapsedAdjustedTime
                    // then make ElapsedAdjustedTime = TargetElapsedTime. We take the same internal rules as XNA
                    if (Math.Abs(elapsedAdjustedTime.Ticks - TargetElapsedTime.Ticks) < (TargetElapsedTime.Ticks >> 6)) {
                        elapsedAdjustedTime = TargetElapsedTime;
                    }

                    // Update the accumulated time
                    accumulatedElapsedGameTime += elapsedAdjustedTime;

                    // Calculate the number of update to issue
                    if (ForceOneUpdatePerDraw) {
                        updateCount = 1;
                    } else {
                        updateCount = (int)(accumulatedElapsedGameTime.Ticks / TargetElapsedTime.Ticks);
                    }

                    if (IsDrawDesynchronized) {
                        drawLag = accumulatedElapsedGameTime.Ticks % TargetElapsedTime.Ticks;
                        suppressNextDraw = false;
                    } else if (updateCount == 0) {
                        // If there is no need for update, then exit
                        return;
                    }

                    // Calculate a moving average on updateCount
                    lastUpdateCount[nextLastUpdateCountIndex] = updateCount;
                    float updateCountMean = 0;
                    for (int i = 0; i < lastUpdateCount.Length; i++) {
                        updateCountMean += lastUpdateCount[i];
                    }

                    updateCountMean /= lastUpdateCount.Length;
                    nextLastUpdateCountIndex = (nextLastUpdateCountIndex + 1) % lastUpdateCount.Length;

                    // We are going to call Update updateCount times, so we can substract this from accumulated elapsed game time
                    accumulatedElapsedGameTime = new TimeSpan(accumulatedElapsedGameTime.Ticks - (updateCount * TargetElapsedTime.Ticks));
                    singleFrameElapsedTime = TargetElapsedTime;
                }
                else
                {
                    Array.Clear(lastUpdateCount, 0, lastUpdateCount.Length);
                    nextLastUpdateCountIndex = 0;
                }

                bool beginDrawSuccessful = false;
                try
                {
                    beginDrawSuccessful = BeginDraw();

                    // Reset the time of the next frame
                    for (lastFrameElapsedGameTime = TimeSpan.Zero; updateCount > 0 && !isExiting; updateCount--)
                    {
                        updateTime.Update(totalUpdateTime, singleFrameElapsedTime, true);
                        try
                        {
                            UpdateAndProfile(updateTime);
                            if (EarlyExit)
                                return;

                            // If there is no exception, then we can draw the frame
                            suppressNextDraw &= suppressDraw;
                            suppressDraw = false;
                        }
                        finally
                        {
                            lastFrameElapsedGameTime += singleFrameElapsedTime;
                            totalUpdateTime += singleFrameElapsedTime;
                        }
                    }

                    // End measuring update time
                    updateTimer.Tick();

                    // Update game time just before calling draw
                    //updateTime.Update(totalUpdateTime, singleFrameElapsedTime, singleFrameUpdateTime, drawRunningSlowly, true);

                    if (!suppressNextDraw)
                    {
                        totalDrawTime = TimeSpan.FromTicks(totalUpdateTime.Ticks + drawLag);
                        DrawInterpolationFactor = drawLag / (float)TargetElapsedTime.Ticks;
                        DrawFrame();
                    }
                }
                finally
                {
                    if (beginDrawSuccessful)
                    {
                        using (Profiler.Begin(GameProfilingKeys.GameEndDraw))
                        {
                            bool presenting = ShouldPresent && !PauseRendering;
                            EndDraw(presenting);

                            if (!presenting || TreatNotFocusedLikeMinimized && gamePlatform.MainWindow.Focused == false)
	                            MinimizedMinimumUpdateRate.Throttle(out _);
	                        else
	                            WindowMinimumUpdateRate.Throttle(out _);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error("Unexpected exception", ex);
                throw;
            }
        }

        #endregion

        #region Methods

        /// <summary>
        /// Starts the drawing of a frame. This method is followed by calls to Draw and EndDraw.
        /// </summary>
        /// <returns><c>true</c> to continue drawing, false to not call <see cref="Draw"/> and <see cref="EndDraw"/></returns>
        protected virtual bool BeginDraw()
        {
            beginDrawOk = false;

            if ((graphicsDeviceManager != null) && !graphicsDeviceManager.BeginDraw())
            {
                return false;
            }

            // Setup default command list
            if (GraphicsContext == null)
            {
                GraphicsContext = new GraphicsContext(GraphicsDevice);
                Services.AddService(GraphicsContext);
            }
            else
            {
                // Reset allocator
                GraphicsContext.ResourceGroupAllocator.Reset(GraphicsContext.CommandList);
                GraphicsContext.CommandList.Reset();
            }

            beginDrawOk = true;

            // Clear states
            GraphicsContext.CommandList.ClearState();

            // Perform begin of frame presenter operations
            if (GraphicsDevice.Presenter != null)
            {
                GraphicsContext.CommandList.ResourceBarrierTransition(GraphicsDevice.Presenter.DepthStencilBuffer, GraphicsResourceState.DepthWrite);
                GraphicsContext.CommandList.ResourceBarrierTransition(GraphicsDevice.Presenter.BackBuffer, GraphicsResourceState.RenderTarget);

                GraphicsDevice.Presenter.BeginDraw(GraphicsContext.CommandList);
            }

            return true;
        }

        /// <summary>
        /// Called after all components are initialized but before the first update in the game loop.
        /// </summary>
        protected virtual void BeginRun()
        {
        }

        protected override void Destroy()
        {
            base.Destroy();

            lock (this)
            {
                if (Window != null && Window.IsActivated) // force the window to be in an correct state during destroy (Deactivated events are sometimes dropped on windows)
                    Window.OnPause();

                var array = new IGameSystemBase[GameSystems.Count];
                GameSystems.CopyTo(array, 0);
                for (int i = 0; i < array.Length; i++)
                {
                    var disposable = array[i] as IDisposable;
                    if (disposable != null)
                    {
                        disposable.Dispose();
                    }
                }

                // Reset graphics context
                GraphicsContext = null;

                var disposableGraphicsManager = graphicsDeviceManager as IDisposable;
                if (disposableGraphicsManager != null)
                {
                    disposableGraphicsManager.Dispose();
                }

                DisposeGraphicsDeviceEvents();

                if (gamePlatform != null)
                {
                    gamePlatform.Release();
                }
            }
        }

        /// <summary>
        /// Reference page contains code sample.
        /// </summary>
        /// <param name="gameTime">
        /// Time passed since the last call to Draw.
        /// </param>
        protected virtual void Draw(GameTime gameTime)
        {
            GameSystems.Draw(gameTime);

            // Make sure that the render target is set back to the back buffer
            // From a user perspective this is better. From an internal point of view,
            // this code is already present in GraphicsDeviceManager.BeginDraw()
            // but due to the fact that a GameSystem can modify the state of GraphicsDevice
            // we need to restore the default render targets
            // TODO: Check how we can handle this more cleanly
            if (GraphicsDevice != null && GraphicsDevice.Presenter.BackBuffer != null)
            {
                GraphicsContext.CommandList.SetRenderTargetAndViewport(GraphicsDevice.Presenter.DepthStencilBuffer, GraphicsDevice.Presenter.BackBuffer);
            }
        }

        /// <summary>Ends the drawing of a frame. This method is preceeded by calls to Draw and BeginDraw.</summary>
        protected virtual void EndDraw(bool present)
        {
            if (beginDrawOk)
            {
                if (GraphicsDevice.Presenter != null)
                {
                    // Perform end of frame presenter operations
                    GraphicsDevice.Presenter.EndDraw(GraphicsContext.CommandList, present);

                    GraphicsContext.CommandList.ResourceBarrierTransition(GraphicsDevice.Presenter.BackBuffer, GraphicsResourceState.Present);
                }

                GraphicsContext.ResourceGroupAllocator.Flush();

                // Close command list
                GraphicsContext.CommandList.Flush();

                // Present (if necessary)
                graphicsDeviceManager.EndDraw(present);

                beginDrawOk = false;
            }
        }

        /// <summary>Called after the game loop has stopped running before exiting.</summary>
        protected virtual void EndRun()
        {
        }

        /// <summary>
        /// Stores a reference to the main GameBase thread
        /// </summary>
        public static Thread RenderingThread { get; private set; }

        /// <summary>Called after the Game is created, but before GraphicsDevice is available and before LoadContent(). Reference page contains code sample.</summary>
        protected virtual void Initialize()
        {
            RenderingThread = Thread.CurrentThread;

            GameSystems.Initialize();
        }

        internal virtual void LoadContentInternal()
        {
            GameSystems.LoadContent();
        }

        internal bool IsExiting()
        {
            return isExiting;
        }

        /// <summary>
        /// Raises the Activated event. Override this method to add code to handle when the game gains focus.
        /// </summary>
        /// <param name="sender">The Game.</param>
        /// <param name="args">Arguments for the Activated event.</param>
        protected virtual void OnActivated(object sender, EventArgs args)
        {
            Activated?.Invoke(this, args);
        }

        /// <summary>
        /// Raises the Deactivated event. Override this method to add code to handle when the game loses focus.
        /// </summary>
        /// <param name="sender">The Game.</param>
        /// <param name="args">Arguments for the Deactivated event.</param>
        protected virtual void OnDeactivated(object sender, EventArgs args)
        {
            Deactivated?.Invoke(this, args);
        }

        /// <summary>
        /// Raises an Exiting event. Override this method to add code to handle when the game is exiting.
        /// </summary>
        /// <param name="sender">The Game.</param>
        /// <param name="args">Arguments for the Exiting event.</param>
        protected virtual void OnExiting(object sender, EventArgs args)
        {
            Exiting?.Invoke(this, args);
        }

        protected virtual void OnWindowCreated() {
            WindowCreated?.Invoke(this, EventArgs.Empty);

            // make sure we render frames faster than vysnc if we are in vulkan
            int vsyncWiggleRoom = GraphicsDevice.Platform == GraphicsPlatform.Vulkan ? 1 : 0;
            // If we still have default values, let's set these based on SDL refresh rate (if we can)
            if (gamePlatform.MainWindow is GameWindowSDL) {
                if (TargetElapsedTime == defaultTimeSpan) {
                    ((GameWindowSDL)gamePlatform.MainWindow).GetDisplayInformation(out int width, out int height, out int refresh_rate);
                    TargetElapsedTime = TimeSpan.FromTicks(TimeSpan.TicksPerSecond / (refresh_rate + vsyncWiggleRoom));
                }
                if (WindowMinimumUpdateRate.MinimumElapsedTime == defaultTimeSpan) WindowMinimumUpdateRate.MinimumElapsedTime = TargetElapsedTime;
            } else if (TargetElapsedTime == defaultTimeSpan) {
                TargetElapsedTime = TimeSpan.FromTicks(TimeSpan.TicksPerSecond / (60 + vsyncWiggleRoom)); // target elapsed time is by default 60Hz
            }
        }

        private void GamePlatformOnWindowCreated(object sender, EventArgs eventArgs)
        {
            IsMouseVisible = true;
            OnWindowCreated();
        }

        /// <summary>
        /// This is used to display an error message if there is no suitable graphics device or sound card.
        /// </summary>
        /// <param name="exception">The exception to display.</param>
        /// <returns>The <see cref="bool" />.</returns>
        protected virtual bool ShowMissingRequirementMessage(Exception exception)
        {
            return true;
        }

        /// <summary>
        /// Called when graphics resources need to be unloaded. Override this method to unload any game-specific graphics resources.
        /// </summary>
        protected virtual void UnloadContent()
        {
            GameSystems.UnloadContent();
        }

        /// <summary>
        /// Reference page contains links to related conceptual articles.
        /// </summary>
        /// <param name="gameTime">
        /// Time passed since the last call to Update.
        /// </param>
        protected virtual void Update(GameTime gameTime)
        {
            GameSystems.Update(gameTime);
        }

        private void UpdateAndProfile(GameTime gameTime)
        {
            updateTimer.Reset();
            using (Profiler.Begin(GameProfilingKeys.GameUpdate))
            {
                Update(gameTime);
            }
            updateTimer.Tick();
        }

        private void GamePlatform_Activated(object sender, EventArgs e)
        {
            if (!IsActive)
            {
                IsActive = true;
                OnActivated(this, EventArgs.Empty);
            }
        }

        private void GamePlatform_Deactivated(object sender, EventArgs e)
        {
            if (IsActive)
            {
                IsActive = false;
                OnDeactivated(this, EventArgs.Empty);
            }
        }

        private void GamePlatform_Exiting(object sender, EventArgs e)
        {
            OnExiting(this, EventArgs.Empty);
        }

        private void DrawFrame()
        {
            if (SlowDownDrawCalls && (UpdateTime.FrameCount & 1) == 1) // skip the draw call about one frame over two.
                return;

            try
            {
                if (!isExiting && GameSystems.IsFirstUpdateDone && (!Window.IsMinimized || DrawEvenMinimized))
                {
                    DrawTime.Factor = UpdateTime.Factor;
                    drawTime.Update(totalDrawTime, lastFrameElapsedGameTime, true);

                    var profilingDraw = Profiler.Begin(GameProfilingKeys.GameDrawFPS);
                    var profiler = Profiler.Begin(GameProfilingKeys.GameDraw);

                    GraphicsDevice.FrameTriangleCount = 0;
                    GraphicsDevice.FrameDrawCalls = 0;

                    Draw(drawTime);

                    profiler.End("Triangle count: {0}", GraphicsDevice.FrameTriangleCount);
                    profilingDraw.End("Frame = {0}, Update = {1:0.000}ms, Draw = {2:0.000}ms, FPS = {3:0.00}", drawTime.FrameCount, updateTime.TimePerFrame.TotalMilliseconds, drawTime.TimePerFrame.TotalMilliseconds, drawTime.FramePerSecond);
                }
            }
            finally
            {
                lastFrameElapsedGameTime = TimeSpan.Zero;
            }
        }

        private void SetupGraphicsDeviceEvents()
        {
            // Find the IGraphicsDeviceSerive.
            graphicsDeviceService = Services.GetService<IGraphicsDeviceService>();

            // If there is no graphics device service, don't go further as the whole Game would not work
            if (graphicsDeviceService == null)
            {
                throw new InvalidOperationException("Unable to create a IGraphicsDeviceService");
            }

            if (graphicsDeviceService.GraphicsDevice == null)
            {
                throw new InvalidOperationException("Unable to find a GraphicsDevice instance");
            }

            resumeManager = new ResumeManager(Services);

            GraphicsDevice = graphicsDeviceService.GraphicsDevice;
            graphicsDeviceService.DeviceCreated += GraphicsDeviceService_DeviceCreated;
            graphicsDeviceService.DeviceResetting += GraphicsDeviceService_DeviceResetting;
            graphicsDeviceService.DeviceReset += GraphicsDeviceService_DeviceReset;
            graphicsDeviceService.DeviceDisposing += GraphicsDeviceService_DeviceDisposing;
        }

        private void DisposeGraphicsDeviceEvents()
        {
            if (graphicsDeviceService != null)
            {
                graphicsDeviceService.DeviceCreated -= GraphicsDeviceService_DeviceCreated;
                graphicsDeviceService.DeviceResetting -= GraphicsDeviceService_DeviceResetting;
                graphicsDeviceService.DeviceReset -= GraphicsDeviceService_DeviceReset;
                graphicsDeviceService.DeviceDisposing -= GraphicsDeviceService_DeviceDisposing;
                GraphicsDevice = null;
            }
        }

        private void GraphicsDeviceService_DeviceCreated(object sender, EventArgs e)
        {
            GraphicsDevice = graphicsDeviceService.GraphicsDevice;

            if (GameSystems.State != GameSystemState.ContentLoaded)
            {
                LoadContentInternal();
            }
        }

        private void GraphicsDeviceService_DeviceDisposing(object sender, EventArgs e)
        {
            // TODO: Unload all assets
            //Content.UnloadAll();

            if (GameSystems.State == GameSystemState.ContentLoaded)
            {
                UnloadContent();
            }

            resumeManager.OnDestroyed();

            GraphicsDevice = null;
        }

        private void GraphicsDeviceService_DeviceReset(object sender, EventArgs e)
        {
            // TODO: ResumeManager?
            //throw new NotImplementedException();
            resumeManager.OnReload();
            resumeManager.OnRecreate();
        }

        private void GraphicsDeviceService_DeviceResetting(object sender, EventArgs e)
        {
            // TODO: ResumeManager?
            //throw new NotImplementedException();
            resumeManager.OnDestroyed();
        }

        #endregion
    }
}
