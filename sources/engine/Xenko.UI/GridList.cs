using System;
using System.Collections.Generic;
using System.Linq;
using Xenko.Engine;
using Xenko.UI.Controls;
using Xenko.UI.Panels;

namespace Xenko.UI
{
    /// <summary>
    /// Helper class to generate clickable lists within a Grid, given a UILibrary to use for each entry 
    /// </summary>
    public class GridList
    {
        private Grid myGrid;
        private UILibrary template;
        private Dictionary<object, Dictionary<string, UIElement>> entryElements = new Dictionary<object, Dictionary<string, UIElement>>();
        private string templateName;
        private float entryHeight;

        /// <summary>
        /// Sort alphabetically?
        /// </summary>
        public bool AlphabeticalSort = false;

        /// <summary>
        /// Maximum number of ToggleButtons that can be checked
        /// </summary>
        public int MaxCheckedAllowed = 1;

        /// <summary>
        /// Action to take when a button is clicked or ToggleButton is checked, argument is the value of the entry
        /// </summary>
        public Action<object> EntrySelectedAction = (value) => { };

        /// <summary>
        /// Make a GridList for the given Grid
        /// </summary>
        /// <param name="grid">What grid will the list be in?</param>
        /// <param name="entryTemplate">What template to use for entries?</param>
        /// <param name="templateRootName">Optional specified name for the root UIElement in the entryTemplate</param>
        public GridList(Grid grid, UILibrary entryTemplate, string templateRootName = null)
        {
            myGrid = grid;
            template = entryTemplate;
            if (templateRootName == null)
            {
                foreach (string name in entryTemplate.UIElements.Keys)
                {
                    // just grab the first name
                    templateName = name;
                    break;
                }
            }
            else templateName = templateRootName;
            entryHeight = entryTemplate.UIElements[templateName].Height;
        }

        /// <summary>
        /// How many toggle buttons are checked?
        /// </summary>
        public int GetCheckedCount()
        {
            int cc = 0;
            foreach (Dictionary<string, UIElement> elements in entryElements.Values)
            {
                foreach (UIElement uie in elements.Values)
                {
                    if (uie is ToggleButton tb && tb.State == ToggleState.Checked) cc++;
                }
            }
            return cc;
        }

        /// <summary>
        /// Clear all checked ToggleButtons
        /// </summary>
        public void ResetChecked(object ignoreValue = null)
        {
            foreach (var pair in entryElements)
            {
                if (pair.Key == ignoreValue) continue;
                foreach (UIElement uie in pair.Value.Values)
                {
                    if (uie is ToggleButton tb) tb.State = ToggleState.UnChecked;
                }
            }
        }

        /// <summary>
        /// Gets the entry display name for a value
        /// </summary>
        /// <param name="value">value of entry to look for</param>
        /// <returns>null if it couldn't be found</returns>
        public string GetDisplayName(object value)
        {
            if (entryElements.TryGetValue(value, out var uied))
            {
                foreach (UIElement uie in uied.Values)
                {
                    if (uie is TextBlock tb) return tb.Text;
                }
            }
            return null;
        }

        /// <summary>
        /// Add a list of entries to the list
        /// </summary>
        /// <param name="entries">The list, values will be the same as the display value</param>
        /// <param name="rebuildVisualListAfter">If you intend to add more lists and want to defer visually updating, set to false</param>
        public void AddEntries(List<string> entries, bool rebuildVisualListAfter = true)
        {
            for (int i = 0; i < entries.Count; i++) AddEntry(entries[i], null, false);
            if (rebuildVisualListAfter) RebuildVisualList();
        }

        /// <summary>
        /// Add a specific entry to the list
        /// </summary>
        /// <param name="displayName">What string to display for the entry</param>
        /// <param name="value">What is the underlying value of this entry, can't be duplicates</param>
        /// <param name="rebuildVisualListAfter">If you intend to add more items and want to defer visually updating, set to false</param>
        /// <returns></returns>
        public UIElement AddEntry(string displayName, object value = null, bool rebuildVisualListAfter = true)
        {
            if (value == null) value = displayName;
            UIElement newEntry = template.InstantiateElement<UIElement>(templateName);
            Dictionary<string, UIElement> allElements = newEntry.GatherUIDictionary<UIElement>();
            foreach (UIElement uie in allElements.Values)
            {
                if (uie is TextBlock tb)
                {
                    tb.Text = displayName;
                }
                else if (uie is ToggleButton tbn)
                {
                    tbn.Checked += delegate {
                        int alreadyChecked = GetCheckedCount();
                        if (alreadyChecked == 2 && MaxCheckedAllowed == 1)
                        {
                            // effectively just change our choice
                            ResetChecked(value);
                            EntrySelectedAction(value);
                        }
                        else if (alreadyChecked > MaxCheckedAllowed)
                        {
                            // too many checked
                            tbn.State = ToggleState.UnChecked;
                        }
                        else
                        {
                            // check made
                            EntrySelectedAction(value);
                        }
                    };
                }
                else if (uie is Button bn)
                {
                    bn.Click += delegate {
                        EntrySelectedAction(value);
                    };
                }
            }
            entryElements[value] = allElements;
            if (rebuildVisualListAfter) RebuildVisualList();
            return newEntry;
        }

        /// <summary>
        /// Get a dictionary of all UIElements for an entry, keys will be names of UIElements
        /// </summary>
        public Dictionary<string, UIElement> GetEntry(string value)
        {
            if (entryElements.TryGetValue(value, out var uie)) return uie;
            return null;
        }

        /// <summary>
        /// Removes an entry from the list
        /// </summary>
        /// <param name="value">value of entry to remove</param>
        /// <param name="rebuildVisualList">If you intend to remove more items and want to defer visually updating, set to false</param>
        /// <returns>true if successfully removed</returns>
        public bool RemoveEntry(string value, bool rebuildVisualList = true)
        {
            if (entryElements.Remove(value))
            {
                if (rebuildVisualList)
                    RebuildVisualList();

                return true;
            }
            return false;
        }

        /// <summary>
        /// Clears the list
        /// </summary>
        public void RemoveAllEntries(bool rebuildVisualList = true)
        {
            entryElements.Clear();
            if (rebuildVisualList)
                RebuildVisualList();
        }

        /// <summary>
        /// Get list of entries added
        /// </summary>
        /// <param name="onlyChecked">if true, only return ToggleButtons checked</param>
        /// <returns></returns>
        public List<object> GetEntries(bool onlyChecked = false)
        {
            List<object> retList = new List<object>();
            foreach (var uie in entryElements)
            {
                if (!onlyChecked)
                {
                    retList.Add(uie.Key);
                }
                else
                {
                    foreach (UIElement uiec in uie.Value.Values)
                    {
                        if (uiec is ToggleButton tb && tb.State == ToggleState.Checked)
                        {
                            retList.Add(uie.Key);
                            break;
                        }
                    }
                }
            }
            return retList;
        }

        private void AddToList(UIElement uie)
        {
            uie.Margin = new Thickness(0f, uie.Height * myGrid.Children.Count, 0f, 0f);
            myGrid.Children.Add(uie);
        }

        public void RebuildVisualList()
        {
            myGrid.Children.Clear();
            if (AlphabeticalSort)
            {
                foreach (var uie in entryElements.OrderBy(i => GetDisplayName(i.Key)))
                {
                    AddToList(uie.Value[templateName]);
                }
            }
            else
            {
                foreach (var uie in entryElements)
                {
                    AddToList(uie.Value[templateName]);
                }
            }
            myGrid.Height = entryHeight * entryElements.Count;
        }
    }
}