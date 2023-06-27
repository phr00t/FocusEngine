using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using Xenko.Core;
using Xenko.Core.Serialization.Contents;
using Xenko.Engine;
using Xenko.Games;
using Xenko.Rendering;
using Xenko.Rendering.Lights;
using Xenko.Rendering.ProceduralModels;

namespace Xenko.ShaderBuilderHelper
{
    /// <summary>
    /// Helper class to build shaders for a game by loading materials and introducing them to different lights.
    /// </summary>
    public class SceneBuilder
    {
        public static List<Material> GetAllMaterials(string contentURLbase) 
        {
            Game curGame = ServiceRegistry.instance.GetService<IGame>() as Game;
            ContentManager cm = curGame.Content;
            string[] materials = cm.AllURLs(contentURLbase);
            ConcurrentQueue<Material> materialList = new ConcurrentQueue<Material>();
            Xenko.Core.Threading.Dispatcher.For(0, materials.Length, (i) =>
            {
                try
                {
                    materialList.Enqueue(cm.Load<Material>(materials[i]));
                }
                catch (Exception e)
                {
                    // probably not a material, skip
                }
            });

            List<Material> ms = new List<Material>();
            while (materialList.TryDequeue(out Material material))
                ms.Add(material);

            return ms;
        }

        public static Scene GetShaderBuilderScene(List<Material> materials_to_build)
        {
            Scene scene = new Scene();

            // setup camera
            CameraComponent camera_component = new CameraComponent();
            Entity camEntity = new Entity("Camera");
            camEntity.Add(camera_component);
            scene.Entities.Add(camEntity);
            Model basemodel = new Model();
            CubeProceduralModel cube = new CubeProceduralModel();
            cube.Generate(ServiceRegistry.instance, basemodel);
            camEntity.Transform.Position.Z = 3f;

            // setup materials
            foreach (Material material in materials_to_build)
            {
                Entity e = new Entity();
                ModelComponent mc = e.GetOrCreate<ModelComponent>();
                Model m = new Model();
                m.Materials.Add(material);
                m.Meshes.Add(basemodel.Meshes[0]);
                mc.Model = m;
                e.Add(mc);
                scene.Entities.Add(e);
            }

            // setup basic lighting base
            Entity lightBase = new Entity("lightbase");
            scene.Entities.Add(lightBase);

            return scene;
        }

        public static void SetLightingSituation(Scene shader_build_scene, bool AmbientLight, bool DirectionalLight, int PointLights, int SpotLights, bool useShadows)
        {
            Entity lightbase = shader_build_scene.FindChild("lightbase");
            lightbase.Transform.Children.Clear();
           
            if (AmbientLight)
            {
                Entity al = new Entity("AmbientLight");
                LightComponent alc = new LightComponent();
                alc.Type = new LightPoint();
                al.Add(alc);
                al.Transform.Parent = lightbase.Transform;
            }

            if (DirectionalLight)
            {
                Entity al = new Entity("DirectionalLight");
                LightComponent alc = new LightComponent();
                LightDirectional ld = new LightDirectional();
                ld.Shadow.Enabled = useShadows;
                alc.Type = ld;
                al.Add(alc);
                al.Transform.Parent = lightbase.Transform;
            }

            for (int i=0; i<PointLights; i++)
            {
                Entity al = new Entity("PointLight " + i);
                LightComponent alc = new LightComponent();
                LightPoint ld = new LightPoint();
                ld.Shadow.Enabled = useShadows;
                alc.Type = ld;
                al.Add(alc);
                al.Transform.Parent = lightbase.Transform;
                al.Transform.Position.Z = 1f;
            }

            for (int i = 0; i < SpotLights; i++)
            {
                Entity al = new Entity("SpotLight " + i);
                LightComponent alc = new LightComponent();
                LightSpot ld = new LightSpot();
                ld.Shadow.Enabled = useShadows;
                alc.Type = ld;
                al.Add(alc);
                al.Transform.Parent = lightbase.Transform;
                al.Transform.Position.Z = 1f;
                al.Transform.Rotation = Core.Mathematics.Quaternion.Identity;
            }
        }
    }
}
