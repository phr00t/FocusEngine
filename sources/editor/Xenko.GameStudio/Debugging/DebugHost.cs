// Copyright (c) Xenko contributors (https://xenko.com) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System;
using System.Diagnostics;
using System.ServiceModel;

using Xenko.Core.Diagnostics;
using Xenko.Core.VisualStudio;
using Xenko.Debugger.Target;

namespace Xenko.GameStudio.Debugging
{
    /// <summary>
    /// Controls a <see cref="GameDebuggerHost"/>, the spawned process and its IPC communication.
    /// </summary>
    class DebugHost : IDisposable
    {
        public ServiceHost ServiceHost { get; private set; }
        public GameDebuggerHost GameHost { get; private set; }

        public void Start(string workingDirectory, Process debuggerProcess, LoggerResult logger)
        {
            var gameHostAssembly = typeof(GameDebuggerTarget).Assembly.Location;
        }

        public void Stop()
        {
            if (ServiceHost != null)
            {
                ServiceHost.Abort();
                ServiceHost = null;
            }
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
