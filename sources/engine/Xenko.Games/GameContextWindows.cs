// Copyright (c) Xenko contributors (https://xenko.com) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.
namespace Xenko.Games
{
    /// <summary>
    /// Common ancestor to all game contexts on the Windows platform.
    /// </summary>
    /// <typeparam name="TK"></typeparam>
    public abstract class GameContextWindows<TK> : GameContext<TK>
    {
        /// <inheritDoc/>
        protected GameContextWindows(TK control, int requestedWidth = 1280, int requestedHeight = 720, bool fullscreen = false)
            : base(control, requestedWidth, requestedHeight, fullscreen)
        {
        }
    }
}
