// Copyright (c) Xenko contributors (https://xenko.com) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.
#if XENKO_UI_SDL
using Xenko.Core;
using Xenko.Graphics.SDL;

namespace Xenko.Games
{
    /// <summary>
    /// A <see cref="GameContext"/> to use for rendering to an existing SDL Window.
    /// </summary>
    public class GameContextSDL : GameContextWindows<Window>
    {
        static GameContextSDL()
        {
            // Preload proper SDL native library (depending on CPU type)
            NativeLibraryHelper.PreloadLibrary("SDL2.dll", typeof(Window));
        }

        /// <inheritDoc/>
        public GameContextSDL(Window control, int requestedWidth = 1280, int requestedHeight = 720, bool fullscreen = false)
            : base(control ?? new GameFormSDL(requestedWidth, requestedHeight, fullscreen), requestedWidth, requestedHeight, fullscreen) 
        {
            ContextType = AppContextType.DesktopSDL;
        }
    }
}
#endif
