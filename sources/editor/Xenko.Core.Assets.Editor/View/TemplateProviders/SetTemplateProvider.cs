// Copyright (c) .NET Foundation and Contributors (https://dotnetfoundation.org/ & https://stride3d.net)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.
using Xenko.Core.Presentation.Quantum;
using Xenko.Core.Presentation.Quantum.View;
using Xenko.Core.Presentation.Quantum.ViewModels;

namespace Xenko.Core.Assets.Editor.View.TemplateProviders
{
    public class SetTemplateProvider : TypeMatchTemplateProvider
    {
        public override string Name => "Set";

        public override bool MatchNode(NodeViewModel node)
        {
            return node.HasSet && node.NodeValue != null;
        }
    }
}
