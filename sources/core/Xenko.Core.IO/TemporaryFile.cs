// Copyright (c) Xenko contributors (https://xenko.com) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.
using System;
using System.IO;

namespace Xenko.Core.IO
{
    public partial class TemporaryFile : IDisposable
    {
        private bool isDisposed;
        private string path;

        public TemporaryFile()
        {
            path = VirtualFileSystem.GetTempFileName();
        }

        public string Path
        {
            get { return path; }
        }

        ~TemporaryFile()
        {
            Dispose(false);
        }

        public void Dispose()
        {
            Dispose(false);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing)
        {
            if (!isDisposed)
            {
                isDisposed = true;
                TryDelete();
            }
        }

        private void TryDelete()
        {
            try
            {
                VirtualFileSystem.FileDelete(path);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
