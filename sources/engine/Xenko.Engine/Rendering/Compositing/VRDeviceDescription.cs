// Copyright (c) Xenko contributors (https://xenko.com) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.
using System.Collections.Generic;
using System.ComponentModel;
using Xenko.Core;
using Xenko.Rendering.Images;
using Xenko.VirtualReality;

namespace Xenko.Rendering.Compositing
{
    [DataContract]
    public class VRDeviceDescription
    {
        // defaults to OpenXR, the only supported VR API
        [DataMember(10)]
        public VRApi Api { get; set; } = VRApi.OpenXR;

        [DataMember(20)]
        public float ResolutionScale { get; set; } = 1f;
    }
}
