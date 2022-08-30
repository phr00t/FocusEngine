// Copyright (c) Xenko contributors (https://xenko.com) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System.Collections.Generic;
using Xenko.Core;
using Xenko.Core.Serialization;
using Xenko.Core.Serialization.Contents;
using Xenko.UI;

namespace Xenko.Engine
{
    /// <summary>
    /// A page containing a UI hierarchy.
    /// </summary>
    [DataContract("UIPage")]
    [ContentSerializer(typeof(DataContentSerializerWithReuse<UIPage>))]
    [ReferenceSerializer, DataSerializerGlobal(typeof(ReferenceSerializer<UIPage>), Profile = "Content")]
    public sealed class UIPage : ComponentBase
    {
        /// <summary>
        /// Gets or sets the root element of the page.
        /// </summary>
        /// <userdoc>The root element of the page.</userdoc>
        [DataMember]
        public UIElement RootElement { get; set; }

        /// <summary>
        /// Shortcut to gathering a dictionary of UIElements (keyed by name) from page of type T
        /// </summary>
        /// <typeparam name="T">Type of UIElements to gather, can be UIElement too for all</typeparam>
        /// <param name="dictionary">Dictionary to populate</param>
        /// <param name="childrenOnly">Include the root, or just the children?</param>
        public void GatherUIDictionary<T>(ref Dictionary<string, T> dictionary, bool childrenOnly = false) where T : UIElement
        {
            RootElement.GatherUIDictionary<T>(ref dictionary, childrenOnly);
        }

        /// <summary>
        /// Shortcut to finding a UIElement based on name from this page
        /// </summary>
        /// <param name="name">Name of UIElement to find</param>
        public UIElement FindName(string name)
        {
            return RootElement.FindName(name);
        }

        /// <summary>
        /// Shortcut to gathering a dictionary of UIElements from page of type T
        /// </summary>
        /// <typeparam name="T">Type of UIElements to gather, can be UIElement too for all</typeparam>
        /// <param name="childrenOnly">Include the root, or just the children?</param>
        /// <returns>Populated dictionary, keyed by name of UIElement</returns>
        public Dictionary<string, T> GatherUIDictionary<T>(bool childrenOnly = false) where T : UIElement
        {
            return RootElement.GatherUIDictionary<T>(childrenOnly);
        }
    }
}
