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
        private object currentSelection;

        /// <summary>
        /// Toggle an option on the list
        /// </summary>
        /// <param name="value">What option to select</param>
        /// <param name="toggleState"></param>
        /// <param name="deselectOthers">if true, deselect others</param>
        /// <param name="triggerSelectedAction">if true, trigger the EntrySelectedAction for this value if found</param>
        public override void Select(object value, ToggleState toggleState = ToggleState.Checked, bool deselectOthers = false, bool triggerSelectedAction = false)
        {
            if (toggleState == ToggleState.Checked) currentSelection = value;
            base.Select(value, toggleState, deselectOthers, triggerSelectedAction);
            RebuildVisualList();
        }

        /// <summary>
        /// What is selected right now?
        /// </summary>
        /// <returns>Selected option, null otherwise</returns>
        public override object GetSelection()
        {
            return currentSelection;
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

        /// <summary>
        /// Add an entry to the pulldown list
        /// </summary>
        /// <param name="displayName">What should be displayed for this entry?</param>
        /// <param name="value">Is there an object to assign to this entry? If null, the name as a string will be used</param>
        /// <param name="rebuildVisualListAfter">Rebuild the list visually now, or not?</param>
        /// <returns>Returns the UIElement for the added entry</returns>
        public override UIElement AddEntry(string displayName, object value = null, bool rebuildVisualListAfter = true)
        {
            if (currentSelection == null) currentSelection = value;
            ButtonBase added = base.AddEntry(displayName, value, rebuildVisualListAfter) as ButtonBase;
            added.Click -= toggleChanger;
            added.Click += toggleChanger;
            return added;
        }

        /// <summary>
        /// Get all entries of the pulldown
        /// </summary>
        /// <param name="onlySelected">If true, returns a list of just the current selection</param>
        /// <returns>List of entries</returns>
        public override List<object> GetEntries(bool onlySelected = false)
        {
            if (onlySelected == false)
                return base.GetEntries(onlySelected);

            if (currentSelection == null)
                return new List<object>();

            return new List<object>() { currentSelection };
        }

        /// <summary>
        /// How many are currently selected? Usually 1 with a pulldown
        /// </summary>
        public override int GetSelectedCount()
        {
            return currentSelection != null ? 1 : 0;
        }

        /// <summary>
        /// Is the value currently selected?
        /// </summary>
        /// <param name="value">Value to check</param>
        /// <returns>If it is the current selection, ToggleState.Checked is returned</returns>
        public override ToggleState GetSelectedState(object value)
        {
            if (value == null) return ToggleState.Indeterminate;
            return value.Equals(currentSelection) ? ToggleState.Checked : ToggleState.UnChecked;
        }

        /// <summary>
        /// Clear the pulldown list
        /// </summary>
        /// <param name="rebuildVisualList">Visually rebuild it empty?</param>
        public override void RemoveAllEntries(bool rebuildVisualList = true)
        {
            currentSelection = null;
            base.RemoveAllEntries(rebuildVisualList);
        }

        /// <summary>
        /// Remove a specific entry
        /// </summary>
        public override bool RemoveEntry(object value, bool rebuildVisualList = true)
        {
            if (value?.Equals(currentSelection) ?? false) ResetSelected();
            return base.RemoveEntry(value, rebuildVisualList);
        }

        /// <summary>
        /// Untoggle and toggle buttons and set default selection
        /// </summary>
        /// <param name="ignoreValue">Ignore the state of a certain value?</param>
        public override void ResetSelected(object ignoreValue = null)
        {
            base.ResetSelected(ignoreValue);
            currentSelection = null;
            // just pick any selection
            foreach (var pair in entryElements)
            {
                currentSelection = pair.Key;
                break;
            }
        }

        protected override void SetInternalClickAction(ButtonBase uie, object value)
        {            
            uie.Click += delegate {
                if (_currentlyExpanded == false) return;
                base.ResetSelected(value);
                currentSelection = value;
                EntrySelectedAction?.Invoke(value);
            };
        }

        /// <summary>
        /// Update the pulldown list visually with options
        /// </summary>
        override public void RebuildVisualList()
        {
            if (UpdateEntryWidth()) RepairWidth();
            myGrid.Children.Clear();
            foreach (var uie in entryElements.OrderBy(i => GetSelectedState(i.Key)))
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
