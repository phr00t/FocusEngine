// Copyright (c) Xenko contributors (https://xenko.com) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.
using System;
using System.ComponentModel;
using System.Diagnostics;

using Xenko.Core;
using Xenko.UI.Events;

namespace Xenko.UI.Controls
{
    /// <summary>
    /// Represents the base primitive for all the button-like controls
    /// </summary>
    [DataContract(nameof(ButtonBase))]
    [DataContractMetadataType(typeof(ButtonBaseMetadata))]
    [DebuggerDisplay("ButtonBase - Name={Name}")]
    [Display(category: InputCategory)]
    public abstract class ButtonBase : ContentControl
    {
        static ButtonBase()
        {
            EventManager.RegisterClassHandler(typeof(ButtonBase), ClickEvent, ClickClassHandler);
        }

        /// <summary>
        /// Create an instance of button.
        /// </summary>
        protected ButtonBase()
        {
            CanBeHitByUser = true;  // Warning: this must also match in ButtonBaseMetadata
        }

        /// <summary>
        /// Gets or sets when the Click event occurs.
        /// </summary>
        /// <userdoc>Indicates when the click event occurs.</userdoc>
        [DataMember]
        [Display(category: BehaviorCategory)]
        [DefaultValue(ClickMode.Release)]
        public ClickMode ClickMode { get; set; } = ClickMode.Release;

        /// <summary>
        /// Gets a value that indicates whether the button is currently down.
        /// </summary>
        [DataMemberIgnore]
        public virtual bool IsPressed { get; protected set; }

        /// <summary>
        /// Occurs when a <see cref="Button"/> is clicked.
        /// </summary>
        /// <remarks>A click event is bubbling</remarks>
        public event EventHandler<RoutedEventArgs> Click
        {
            add { AddHandler(ClickEvent, value); }
            remove { RemoveHandler(ClickEvent, value); }
        }

        /// <summary>
        /// Identifies the <see cref="Click"/> routed event.
        /// </summary>
        public static readonly RoutedEvent<RoutedEventArgs> ClickEvent = EventManager.RegisterRoutedEvent<RoutedEventArgs>(
            "Click",
            RoutingStrategy.Bubble,
            typeof(Button));


        protected override void OnTouchDown(TouchEventArgs args)
        {
            // only respond to left clicks
            if (args.ButtonId != 0) return;

            base.OnTouchDown(args);

            IsPressed = true;

            if (ClickMode == ClickMode.Press)
                RaiseEvent(new RoutedEventArgs(ClickEvent));
        }

        protected override void OnTouchUp(TouchEventArgs args)
        {
            // only respond to left clicks
            if (args.ButtonId != 0) return;

            base.OnTouchUp(args);

            if (IsPressed && ClickMode == ClickMode.Release)
                RaiseEvent(new RoutedEventArgs(ClickEvent));

            IsPressed = false;
        }

        protected override void OnTouchLeave(TouchEventArgs args)
        {
            base.OnTouchLeave(args);

            IsPressed = false;
        }

        /// <summary>
        /// The class handler of the event <see cref="Click"/>.
        /// This method can be overridden in inherited classes to perform actions common to all instances of a class.
        /// </summary>
        /// <param name="args">The arguments of the event</param>
        protected virtual void OnClick(RoutedEventArgs args)
        {

        }

        private static void ClickClassHandler(object sender, RoutedEventArgs args)
        {
            // only accept left clicks
            if (args is TouchEventArgs tea && tea.ButtonId != 0) return;

            var buttonBase = (ButtonBase)sender;

            buttonBase.OnClick(args);
        }

        /// <summary>
        /// Shortcut function to resize this UIElement to fit internal content (like resizing a button to fit its text)
        /// </summary>
        /// <param name="XMargin">How much X padding?</param>
        /// <param name="YMargin">How much Y padding?</param>
        public void ResizeToChild(float XMargin = 10f, float YMargin = 10f)
        {
            float maxX = 0f, maxY = 0f;
            foreach (UIElement bbc in VisualChildren)
            {
                var size = bbc is TextBlock tb ? tb.CalculateTextSize() : bbc.GetNoBullshitSize();

                if (size.X > maxX)
                    maxX = size.X;
                if (size.Y > maxY)
                    maxY = size.Y;
            }

            if (maxX > 0f && maxY > 0f)
            {
                Width = maxX + XMargin;
                Height = maxY + YMargin;
            }
        }

        private class ButtonBaseMetadata
        {
            [DefaultValue(true)]
            public bool CanBeHitByUser { get; set; }
        }
    }
}
