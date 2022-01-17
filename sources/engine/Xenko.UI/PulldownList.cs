using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Xenko.Core;
using Xenko.Core.Mathematics;
using Xenko.Engine;
using Xenko.Input;
using Xenko.UI.Controls;
using Xenko.UI.Panels;

namespace Xenko.UI
{
    public class PulldownList : GridList
    {
        /// <summary>
        /// Is this pulldown list expanded and showing options?
        /// </summary>
        public bool CurrentlyExpanded
        {
            get
            {
                return _currentlyExpanded;
            }
            set
            {
                if (value != _currentlyExpanded)
                {
                    _currentlyExpanded = value;
                    RebuildVisualList();
                }
            }
        }

        /// <summary>
        /// How many options to display when expanded?
        /// </summary>
        public int OptionsToShow
        {
            get
            {
                return _optionsToShow;
            }
            set
            {
                _optionsToShow = value;
                if (_optionsToShow < 1) _optionsToShow = 1;
            }
        }

        /// <summary>
        /// This can only be one
        /// </summary>
        public override int MaxCheckedAllowed
        {
            get => base.MaxCheckedAllowed;
            set
            {
                base.MaxCheckedAllowed = 1;
            }
        }

        private int _optionsToShow = 4;
        private bool _currentlyExpanded = false;
        private ScrollViewer scroll;
        private System.EventHandler<Events.RoutedEventArgs> toggleChanger;
        private Dictionary<object, string> storedOptions = new Dictionary<object, string>();
        private UIElement pulldownIndicator;

        /// <summary>
        /// Toggle an option on the list
        /// </summary>
        /// <param name="value">What option to select</param>
        /// <param name="toggleState"></param>
        /// <param name="deselectOthers">if true, deselect others</param>
        public override void Select(object value, ToggleState toggleState = ToggleState.Checked, bool deselectOthers = false)
        {
            base.Select(value, toggleState, deselectOthers);
            RebuildVisualList();
        }

        /// <summary>
        /// What is selected right now?
        /// </summary>
        /// <returns>Selected option, null otherwise</returns>
        public object GetSelection()
        {
            List<object> entries = GetEntries(true);
            if (entries == null || entries.Count == 0) return null;
            return entries[0];
        }

        /// <summary>
        /// Constructor for a pulldown list
        /// </summary>
        /// <param name="grid">Grid, which needs to be inside of a scrollviewer</param>
        /// <param name="entryTemplate">What to use for entries in the list?</param>
        /// <param name="pulldownIndicator">Optional UIElement that will be shown when not expanded, like a down arrow</param>
        /// <param name="templateRootName">Name of the UIElement to clone when making list entries, can be null to determine automatically</param>
        public PulldownList(Grid grid, UILibrary entryTemplate, UIElement pulldownIndicator = null, string templateRootName = null) : base(grid, entryTemplate, templateRootName)
        {
            toggleChanger = delegate
            {
                _currentlyExpanded = !_currentlyExpanded;
                RebuildVisualList();
            };
            scroll = grid.Parent as ScrollViewer;
            if (scroll == null) throw new ArgumentException("Grid needs a ScrollViewer as Parent");
            scroll.ScrollMode = ScrollingMode.Vertical;
            
            myGrid.Height = entryHeight;
            this.pulldownIndicator = pulldownIndicator;

            listener = new ClickHandler();
            listener.mouseOverCheck = this;

            inputManager = ServiceRegistry.instance.GetService<InputManager>();
        }

        public override UIElement AddEntry(string displayName, object value = null, bool rebuildVisualListAfter = true)
        {
            ButtonBase added = base.AddEntry(displayName, value, rebuildVisualListAfter) as ButtonBase;
            added.Click -= toggleChanger;
            added.Click += toggleChanger;
            return added;
        }

        override public void RebuildVisualList()
        {
            if (UpdateEntryWidth()) RepairWidth();
            myGrid.Children.Clear();
            foreach (var uie in entryElements.OrderBy(i => GetToggledState(i.Key)))
            {
                AddToList(uie.Value[templateName]);
            }
            if (pulldownIndicator != null) pulldownIndicator.Visibility = _currentlyExpanded ? Visibility.Hidden : Visibility.Visible;
            scroll.Height = _currentlyExpanded ? entryHeight * Math.Min(entryElements.Count, _optionsToShow + 0.5f) : entryHeight;
            myGrid.Height = _currentlyExpanded ? entryHeight * entryElements.Count : entryHeight;

            if (_currentlyExpanded)
            {
                inputManager.AddListener(listener);
            }
            else
            {
                inputManager.RemoveListener(listener);
            }

            // get a jumpstart on its rendersize, which is useful for determining what is now visible
            scroll.RenderSize = new Vector3(scroll.RenderSize.X, scroll.Height, 1f);
        }

        private InputManager inputManager;
        private ClickHandler listener;

        private class ClickHandler : IInputEventListener<PointerEvent>
        {
            public PulldownList mouseOverCheck;

            public void ProcessEvent(PointerEvent inputEvent)
            {
                if (inputEvent.EventType == PointerEventType.Pressed &&
                    mouseOverCheck.scroll.MouseOverState == MouseOverState.MouseOverNone)
                {
                    mouseOverCheck.CurrentlyExpanded = false;
                }
            }
        }
    }
}
