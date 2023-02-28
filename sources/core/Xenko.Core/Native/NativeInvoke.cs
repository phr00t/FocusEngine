// Copyright (c) Xenko contributors (https://xenko.com) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security;
#if XENKO_PLATFORM_IOS
using ObjCRuntime;
#endif

namespace Xenko.Core.Native
{
    public static class NativeInvoke
    {
#if XENKO_PLATFORM_IOS
        internal const string Library = "__Internal";
        internal const string LibraryName = "libcore.so";
#else
        internal const string Library = "libcore";
#if XENKO_PLATFORM_WINDOWS
        internal const string LibraryName = "libcore.dll";
#else
        internal const string LibraryName = "libcore.so";
#endif
#endif

        static NativeInvoke()
        {
            NativeLibrary.PreloadLibrary(LibraryName, typeof(NativeInvoke));
        }
    }
}
