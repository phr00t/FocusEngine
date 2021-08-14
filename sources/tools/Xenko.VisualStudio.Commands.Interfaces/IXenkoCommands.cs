// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://xenko3d.net) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.
using System;
using Xenko.VisualStudio.Commands.Shaders;

namespace Xenko.VisualStudio.Commands
{
    /// <summary>
    /// Describes xenko commands accessed by VS Package to current xenko package (so that VSPackage doesn't depend on Xenko assemblies).
    /// </summary>
    /// <remarks>
    /// WARNING: Removing any of those methods will likely break backwards compatibility!
    /// </remarks>
    public interface IXenkoCommands
    {
        byte[] GenerateShaderKeys(string inputFileName, string inputFileContent);

        RawShaderNavigationResult AnalyzeAndGoToDefinition(string projectPath, string sourceCode, RawSourceSpan span);
    }
}
