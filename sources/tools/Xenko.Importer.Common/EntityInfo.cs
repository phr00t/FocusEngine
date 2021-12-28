using System.Collections.Generic;
using Xenko.Assets.Materials;

namespace Xenko.Importer.Common
{
    public class EntityInfo
    {
        public List<string> TextureDependencies;
        public Dictionary<string, MaterialAsset> Materials;
        public List<string> AnimationNodes;
        public List<MeshParameters> Models;
        public List<NodeInfo> Nodes;
    }
}
