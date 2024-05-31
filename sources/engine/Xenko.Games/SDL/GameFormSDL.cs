// Copyright (c) Xenko contributors (https://xenko.com) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

#if XENKO_UI_SDL
using System;
using SDL2;
using Xenko.Core.Mathematics;
using Xenko.Graphics.SDL;

namespace Xenko.Games
{
    /// <summary>
    /// Default Rendering Form on SDL based applications.
    /// </summary>
    public class GameFormSDL : Window
    {
#region Initialization
        /// <summary>
        /// Initializes a new instance of the <see cref="GameForm"/> class.
        /// </summary>
        public GameFormSDL(int width, int height, bool fullscreen, int displayindex) : this(GameContext.ProductName, width, height, fullscreen, displayindex)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="GameForm"/> class.
        /// </summary>
        /// <param name="text">The text.</param>
        public GameFormSDL(string text, int width, int height, bool fullscreen, int displayindex) : base(text, width, height, fullscreen, displayindex)
        {
            ResizeBeginActions += GameForm_ResizeBeginActions;
            ResizeEndActions += GameForm_ResizeEndActions;
            ActivateActions += GameForm_ActivateActions;
            DeActivateActions += GameForm_DeActivateActions;
            previousWindowState = FormWindowState.Normal;
            MinimizedActions += GameForm_MinimizedActions;
            MaximizedActions += GameForm_MaximizedActions;
            RestoredActions += GameForm_RestoredActions;
            FocusLostActions += GameForm_FocusEvent;
            FocusGainedActions += GameForm_FocusEvent;
        }
#endregion

#region Events
        /// <summary>
        /// Occurs when [app activated].
        /// </summary>
        public event EventHandler<EventArgs> AppActivated;

        /// <summary>
        /// Occurs when [app deactivated].
        /// </summary>
        public event EventHandler<EventArgs> AppDeactivated;

        /// <summary>
        /// Occurs when [pause rendering].
        /// </summary>
        public event EventHandler<EventArgs> PauseRendering;

        /// <summary>
        /// Occurs when [resume rendering].
        /// </summary>
        public event EventHandler<EventArgs> ResumeRendering;

        /// <summary>
        /// Occurs when [user resized].
        /// </summary>
        public event EventHandler<EventArgs> UserResized;
#endregion

#region Implementation
        // TODO: The code below is taken from GameForm.cs of the Windows Desktop implementation. This needs reviewing
        private Size2 cachedSize;
        private FormWindowState previousWindowState;
        //private DisplayMonitor monitor;
        private bool isUserResizing;

        private void GameForm_FocusEvent(SDL.SDL_WindowEvent e)
        {
            if (e.data1 == 0) return; // make no change

            GameBase.PauseRendering = e.data1 == 2;
        }

        private void GameForm_MinimizedActions(SDL.SDL_WindowEvent e)
        {
            previousWindowState = FormWindowState.Minimized;
            GameBase.PauseRendering = true;
            PauseRendering?.Invoke(this, EventArgs.Empty);
        }

        private void GameForm_MaximizedActions(SDL.SDL_WindowEvent e)
        {
            if (previousWindowState == FormWindowState.Minimized)
            {
                GameBase.PauseRendering = false;
                ResumeRendering?.Invoke(this, EventArgs.Empty);
            }

            previousWindowState = FormWindowState.Maximized;

            UserResized?.Invoke(this, EventArgs.Empty);
            //UpdateScreen();
            cachedSize = Size;
        }

        private void GameForm_RestoredActions(SDL.SDL_WindowEvent e)
        {
            if (previousWindowState == FormWindowState.Minimized)
            {
                GameBase.PauseRendering = false;
                ResumeRendering?.Invoke(this, EventArgs.Empty);
            }
            previousWindowState = FormWindowState.Normal;
        }

        private void GameForm_DeActivateActions(SDL.SDL_WindowEvent e)
        {
            AppDeactivated?.Invoke(this, EventArgs.Empty);
        }

        private void GameForm_ActivateActions(SDL.SDL_WindowEvent e)
        {
            AppActivated?.Invoke(this, EventArgs.Empty);
        }

        private void GameForm_ResizeBeginActions(SDL.SDL_WindowEvent e)
        {
            if (Graphics.GraphicsDevice.Platform == Graphics.GraphicsPlatform.Vulkan && OriginalSize.HasValue)
                // resizing not supported, return to original resolution
                Size = cachedSize = OriginalSize.Value;
            else
            {
                isUserResizing = true;
                PauseRendering?.Invoke(this, EventArgs.Empty);
                cachedSize = Size;
            }
        }

        private void GameForm_ResizeEndActions(SDL.SDL_WindowEvent e)
        {
            if (Graphics.GraphicsDevice.Platform == Graphics.GraphicsPlatform.Vulkan && OriginalSize.HasValue)
            {
                // resizing not supported, return to original resolution
                Size = cachedSize = OriginalSize.Value;
            }
            else
            {
                if (isUserResizing && cachedSize.Equals(Size))
                {
                    UserResized?.Invoke(this, EventArgs.Empty);
                    // UpdateScreen();
                }

                isUserResizing = false;
                GameBase.PauseRendering = false;
                ResumeRendering?.Invoke(this, EventArgs.Empty);
            }
        }
#endregion
    }
}
#endif
