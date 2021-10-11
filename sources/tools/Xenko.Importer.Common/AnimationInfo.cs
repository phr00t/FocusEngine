using System;
using System.Collections.Generic;
using Xenko.Animations;

namespace Xenko.Importer.Common
{
    public class AnimationInfo
    {
        public TimeSpan Duration;
        public Dictionary<string, AnimationClip> AnimationClips = new Dictionary<string, AnimationClip>();
    }
}
