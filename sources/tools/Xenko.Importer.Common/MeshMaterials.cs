using System;
using System.Collections.Generic;
using Xenko.Assets.Materials;

namespace Xenko.Importer.Common
{
    public class MeshMaterials
    {
        public Dictionary<string, MaterialAsset> Materials;
	    public List<MeshParameters> Models;
	    public List<String> BoneNodes;
    }
}
