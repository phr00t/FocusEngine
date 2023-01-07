// Copyright (c) Xenko contributors (https://xenko.com) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.
using Xenko.Core.Assets.Editor.View.Behaviors;
using Xceed.Wpf.AvalonDock.Layout;

namespace Xenko.GameStudio
{
    /// <summary>
    /// An implementation of the <see cref="ActivateOnLogBehavior{T}"/> for the <see cref="LayoutAnchorable"/> control.
    /// </summary>
    public class LayoutAnchorableActivateOnLogBehavior : ActivateOnLogBehavior<LayoutAnchorable>
    {
        protected override void Activate()
        {
            AssociatedObject.Show();
            try
            {
                AssociatedObject.IsSelected = true;
            } catch { } // sometimes this crashes..?
            AssociatedObject.IsActive = true;       // This ensures this 'tab' is the selected one in a tab group
        }
    }
}
