// Copyright (c) Xenko contributors (https://xenko.com) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;

using Xenko.Core;
using Xenko.Core.Annotations;
using Xenko.Core.Collections;
using Xenko.Core.Mathematics;
using Xenko.Core.Serialization;
using Xenko.Core.Serialization.Contents;
using Xenko.Graphics;
using Xenko.Graphics.Data;

using Buffer = Xenko.Graphics.Buffer;

namespace Xenko.Rendering
{
    /// <summary>
    /// Collection of <see cref="Mesh"/>, each one usually being a different LOD of the same Model.
    /// The effect system will select the appropriate LOD depending on distance, current pass, and other effect-specific requirements.
    /// </summary>
    [ReferenceSerializer, DataSerializerGlobal(typeof(ReferenceSerializer<Model>), Profile = "Content")]
    [ContentSerializer(typeof(DataContentSerializer<Model>))]
    [DataContract]
    public class Model : IEnumerable
    {
        private List<Mesh> meshes = new List<Mesh>();
        private readonly List<MaterialInstance> materials = new List<MaterialInstance>();

        /// <summary>
        /// This really has no use, but required for serialization backwards compatibility.
        /// </summary>
        [MemberCollection(NotNullItems = true)]
        public IList<Model> Children { get; set; }

        /// <summary>
        /// Gets the materials.
        /// </summary>
        /// <value>
        /// The materials.
        /// </value>
        [MemberCollection(NotNullItems = true)]
        public List<MaterialInstance> Materials
        {
            get { return materials; }
        }

        /// <summary>
        /// Gets the meshes.
        /// </summary>
        /// <value>
        /// The meshes.
        /// </value>
        [MemberCollection(NotNullItems = true)]
        public List<Mesh> Meshes
        {
            get { return meshes; }
            set { meshes = value; }
        }

        /// <summary>
        /// Gets or sets the hierarchy definition, which describes nodes name, default transformation and hierarchical parent.
        /// </summary>
        /// <value>
        /// The hierarchy, which describes nodes name, default transformation and hierarchical parent.
        /// </value>
        public Skeleton Skeleton { get; set; }

        /// <summary>
        /// Gets or sets the bounding box encompassing all the <see cref="Meshes"/> (not including animation).
        /// </summary>
        /// <value>
        /// The bounding box.
        /// </value>
        public BoundingBox BoundingBox { get; set; }

        /// <summary>
        /// Gets or sets the bounding sphere encompassing all the <see cref="Meshes"/> (not including animation).
        /// </summary>
        /// <value>The bounding sphere.</value>
        public BoundingSphere BoundingSphere { get; set; }

        /// <summary>
        /// Adjust smallness used for automatic culling. Can be useful for long and skinny things (lower number) or things you want to show more (higher number)
        /// </summary>
        public float SmallFactorMultiplierOverride { get; set; } = 1f;

        /// <summary>
        /// Adds the specified mesh (for collection Initializers).
        /// </summary>
        /// <param name="mesh">The mesh.</param>
        public void Add(Mesh mesh)
        {
            if (mesh != null) Meshes.Add(mesh);
        }

        /// <summary>
        /// Merges models
        /// </summary>
        /// <param name="model">Model to merge</param>
        /// <param name="updateBoundingBox">Should we update the bounding box to include both models?</param>
        /// <param name="shiftMeshMaterialIndicies">Should we shift mesh material indicies to align with existing materials in the model?</param>
        public void Add(Model model, bool updateBoundingBox = true, bool shiftMeshMaterialIndicies = false)
        {
            if (model != null)
            {
                int indexShift = shiftMeshMaterialIndicies ? materials.Count : 0;

                for (int i=0; i<model.meshes.Count; i++)
                {
                    Mesh m = model.meshes[i];
                    m.MaterialIndex += indexShift;
                    Add(m);
                }
                for (int i=0; i<model.materials.Count; i++)
                {
                    Add(model.materials[i]);
                }

                if (Skeleton == null) Skeleton = model.Skeleton;
                if (updateBoundingBox) UpdateBoundingBox();
            }
        }

        /// <summary>
        /// Adds the specified material (for collection Initializers).
        /// </summary>
        /// <param name="material">The mesh.</param>
        public void Add(MaterialInstance material)
        {
            if (material != null) Materials.Add(material);
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return meshes.Cast<object>().Concat(materials).GetEnumerator();
        }

        /// <summary>
        /// Takes all meshes and updates my bounding box accordingly
        /// </summary>
        public void UpdateBoundingBox()
        {
            //handle boundng box/sphere for whole model
            BoundingBox bb = new BoundingBox(new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity),
                                             new Vector3(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity));

            for (int i = 0; i < meshes.Count; i++)
            {
                Vector3 max = meshes[i].BoundingBox.Maximum;
                Vector3 min = meshes[i].BoundingBox.Minimum;

                // update bounding box?
                if (max.X > bb.Maximum.X) bb.Maximum.X = max.X;
                if (max.Y > bb.Maximum.Y) bb.Maximum.Y = max.Y;
                if (max.Z > bb.Maximum.Z) bb.Maximum.Z = max.Z;
                if (min.X < bb.Minimum.X) bb.Minimum.X = min.X;
                if (min.Y < bb.Minimum.Y) bb.Minimum.Y = min.Y;
                if (min.Z < bb.Minimum.Z) bb.Minimum.Z = min.Z;
            }

            BoundingBox = bb;
        }

        /// <summary>
        /// Create a clone with its own ParameterCollection.
        /// It allows reuse of a single Model for multiple ModelComponent.
        /// </summary>
        public Model Instantiate()
        {
            var result = new Model();

            foreach (var mesh in Meshes)
            {
                var meshCopy = new Mesh(mesh);
                result.Meshes.Add(meshCopy);
            }

            result.Skeleton = Skeleton;
            result.BoundingBox = BoundingBox;

            return result;
        }
    }
}
