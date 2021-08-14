// Copyright (c) Xenko contributors (https://xenko.com) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using Xenko.Core;

namespace Xenko.Navigation
{
    internal static class NativeInvoke
    {
#if XENKO_PLATFORM_IOS
        internal const string Library = "__Internal";
#else
        internal const string Library = "libxenkonavigation";
#endif

        internal static void PreLoad()
        {
            NativeLibraryHelper.PreloadLibrary("libxenkonavigation", typeof(NativeInvoke));
        }

        static NativeInvoke()
        {
            PreLoad();
        }
    }
}