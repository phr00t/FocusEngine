![Focus Engine](https://i.imgur.com/OjANvN9.png)
=======

Welcome to the Focus Engine source code repository!

Focus is an open-source C# game engine for realistic rendering and VR based off of Xenko/Stride. You'll still see "Xenko" in many places.
The engine is highly modular and aims at giving game makers more flexibility in their development.
Focus comes with an editor that allows you create and manage the content of your games or applications in a visual and intuitive way.

![Focus Game Studio](https://doc.stride3d.net/latest/en/manual/get-started/media/game-editor-scene.jpg)

To learn more about Stride3D, visit [stride3d.net](https://stride3d.net/).

## Why this fork?

My games require the engine to be developed at a faster pace than Stride. I'm in need of fixes, new features and better performance. These changes will not be supported by the core team, and the absolute most recent changes may not be fully stable. However, you may find them very helpful, and in some cases, essential to projects.

## Any games made with this engine?

Yes, and it is free for you to try! Works on all operating systems and supports VR in Windows: https://store.steampowered.com/app/1256380/FPS_Infinite/

Other paid games:

8089: https://store.steampowered.com/app/1593280/8089_The_Next_Action_RPG/

Spermination: https://store.steampowered.com/app/2001910/Spermination_Cream_of_the_Crop/

PerformVR: https://store.steampowered.com/app/1868400/PerformVR/

## What is different?

Most of Focus is similar to Stride and there shouldn't be any loss of functionality over the original. Changes are focused on fixes, performance improvements and new features. However, I do not maintain different languages, Android support or the Launcher. The following is a rough list of "major" changes, but might not accurately reflect the current state of differences (since both githubs are moving targets which are hopefully improving):

* Virtual Reality & OpenXR: frame rate management, resolution detection, Vulkan support, and automatic UI interaction are some of the VR improvements you'll get "out of the box". Pretty much just need to enable OpenXR in your Graphics Compositor's Forward Renderer and you'll be good to go. Tracking hands is much easier, as you can simply select which hand to track right from GameStudio. Support for multiple forward renderers in VR, with post processing. See https://github.com/phr00t/FOVTester2 for a super simple example of how easy a VR project is.
* Vulkan: Focus primarily uses Vulkan, which has been significantly overhauled to provide more performance you'd expect from the newer API. Vulkan works on Linux too. Vulkan tries to work on Mac OSX via MoltenVK, but compatibility issues have arisen that are hard to troublesoot. Stride/Xenko doesn't work either on Mac OSX as of this writing. DirectX is deprecated and unsupported on this fork.
* BepuPhysics2 for faster Physics: Focus has an additional physics library integrated, which is much faster, has an easier API, multithreaded and pure C#. It isn't integrated with GameStudio though, like Bullet physics is. See https://github.com/phr00t/FocusEngine/tree/master/sources/engine/Xenko.Physics/Bepu. If you decide to still use Bullet, this fork can handle Bullet running in another thread with interpolation.
* API Ease: TransformComponents have nice shortcuts like WorldPosition and WorldRotation. There are also other very useful shortcuts, like Material.Clone to easily clone materials.
* Lots of bugfixes: Lots of issues, and even GameStudio crashes and project corruption, have been fixed/improved in this fork. Some specific examples is crashes with particle colors or rendering 3D text from multiple cameras.
* GlobalSoundManager: easily play all sound effects for your whole project from a single static class, which handles loading and pooling sound instances automatically (even asynchronously). If you use positional sounds, make sure you call UpdatePlayingSoundPositions every frame! See https://github.com/phr00t/FocusEngine/blob/master/sources/engine/Xenko.Engine/Engine/GlobalSoundManager.cs
* CinematicAction: Simple system for performing cinematic actions on objects and calling functions at certain times. Can build a simple timeline for things to move, rotate and execute. See https://github.com/phr00t/FocusEngine/blob/master/sources/engine/Xenko.Engine/Cinematics/CinematicAnimation.cs
* EntityPool: Makes it really easy to reuse entities and prefabs. This can save lots of memory and processing, instead of recreating things that come and go (like enemies or projectiles). See https://github.com/phr00t/FocusEngine/blob/master/sources/engine/Xenko.Engine/Engine/EntityPool.cs
* UI improvements: View frustum is implemented in this fork, so UI elements outside of view won't be drawn for performance reasons. ScrollViewers can work with mouse wheels out of the box. Easily get UI elements to access in code from a Page using GatherUIDictionary. Easily make Lists and Pulldown selection boxes using GridList and PulldownList (not integrated with GameStudio yet, though). Overall UI performance is greatly improved, too!
* Particle System improvements: Colored particles work, which is pretty important! Also added EmitSpecificParticle to a ParticleEmitter, so you can emit individual particles at certain position, speeds and colors (like using EmitParams in Unity).
* Better UI Editor: Selecting things in the editor works more intuitively, like hidden things are skipped and smaller things are easier to click. Hold Shift to disable snapping etc.
* UI Text features: vertically align text or use \<color> tags to dynamically change text colors. Use \<br> tags to have multiline text set straight from GameStudio. Need text shadows, outlines or bevels? Precompile a font (right click it in the asset view) that has a Glyph Margin > 0, which will generate a PNG with room to edit in effects right into the glyphs.
* ModelBatcher: Easily make batched models using lots of individual models (think grass and rocks for your whole terrain batched into one draw call and entity). See https://github.com/phr00t/FocusEngine/blob/master/sources/engine/Xenko.Engine/Engine/ModelBatcher.cs -- if you have moving things that want to be batched, look into BatchedMeshDraw
* Level of detail system: in GameSettings -> Rendering, you can set a value for things to cull when they are really small on the screen. Separate value can be used for culling shadows, too. Individual models can have "Small Factor Adjustment" values that make them more important (or less important) to make them cull easier/harder.
* Able to set Entities (via the TransformComponent) as "static" so complex matrix calculations can be skipped for them every frame. If you have lots of objects that don't move in a scene, this can provide a significant CPU boost.
* Much more control over the depth buffer: objects, even transparent ones and UI components, provide options for how they interacts with the depth buffer.
* More Post Processing Effects: Virtual Reality Field-of-View reducing filter for motion sickness out of the box.
* Non-post processing based fog: GlobalFog which is drawn at the material-level to avoid problems with transparency, see https://github.com/phr00t/FocusEngine/blob/master/sources/engine/Xenko.Rendering/Rendering/Materials/GlobalFog.cs
* Easy setting game resolution: Game.SetDefaultSettings(width, height, fullscreen) and Game.OverrideDefaultSettings to set and save resolution of your game.
* Easy generating procedural meshes: StagedMeshDraw takes a list of verticies and indicies, no "buffer binding" or "GraphicsDevice" needed. Also will actually upload the mesh when it tries to get rendered automatically, saving time and resources if the mesh doesn't actually ever get viewed.
* Less likely to lose work: files are not actually deleted from GameStudio, just moved to the Recylce Bin. If you mess up a prefab or entity in a scene, or if you notice corruption in your project, select Help -> Restore Scene/Prefabs to return your scene and prefab files to the last time to opened your project.
* Performance: lots of tweaks have been made throughout the engine to maximize performance. This includes reducing locks and enumeration reduction, for example. GameStudio editor itself runs much smoother and can handle multiple tabs much better.
* Easy adding/removing entities from the scene: Just do myEntity.Scene = myScene (to add it) or myEntity.Scene = null (to remove it).
* Includes dfkeenan's toolkit designed for this fork (from https://github.com/dfkeenan/XenkoToolkit). May need to add the Toolkit Nuget package to use.
* Takes good things from many different Xenko/Stride forks, including the original branch when it gets updated. I don't get everything, as I focus on things that are more apparently beneficial to seasoned and commercial PC developers. I exclude tutorials, samples, non-PC platforms, launcher updates, internal naming conventions, building refactors etc. which I don't maintain.
* Probably lots of other stuff: haven't kept that great of track of improvements, I usually fix things as needed and keep moving forward!

## OK, show me something neat!

![Lots of Boxes](https://i.imgur.com/E9J0skw.png)

This is 6700+ physical boxes being rendered @ 50fps, which may not be the fastest thing out there, but it is pretty good! It is using BepuPhysics + Vulkan + https://github.com/phr00t/FocusEngine/blob/master/sources/engine/Xenko.Engine/Engine/Batching/BatchedMeshDraw.cs to draw all these boxes with 1 material, 1 mesh & 1 draw call. BatchedMeshDraw allows custom UV offsets per copy, hence why you see some boxes with different textures. Neat!

![Steam Deck Support](https://i.imgur.com/c5XmyGp.jpg)

FPS Infinite, powered by Focus Engine, running on the Steam Deck! Should work out-of-the-box when publishing Linux executables.

## What is worse in this fork?

Android/mobile support, different languages, and Universal Windows Platform support. I also work very little with DirectX, which is maintained just for the editor. Some changes I make to improve Vulkan might cause a (hopefully minor) bug in the DirectX API, which will be of low priority to fix. GPU instancing isn't implemented on Vulkan yet (although you may find the ModelBatcher & BatchedMeshDraw covers this in most cases).

Documentation is worse in this fork, as I don't have time to go back and properly document the new features I add. The list above isn't updated often enough to showcase the latest improvements. I try to comment new additions, but would require looking at the source to see them. Stride documentation is mostly still applicable, but there may be better ways to do things in this fork (VR for example).

Community support doesn't exist for this fork. The community usually revolves around the original Stride.

You can now export games and projects targeting .NET 6 & Visual Studio 2022 is now supported. However, GameStudio (the editor) is still running on .NET Framework 4.8 (not a big deal, in my humble opinion).

Creating templates with this fork is semi broken (you'll get an error, but it still gets created). Just browse for it next time you open Focus. There is an issue for it on the issues tab.

## Why don't you merge all these improvements back into Stride?

The Stride team doesn't really want most of my work, or they don't think these improvements are necessary. I've made decisions without their input to save time, so taking my work now would take significant review and likely significant rewrites to adhere to their opinions and standards. I stand by my code and its quality (otherwise I wouldn't trust making commercial games with it), but the Stride team would rather do it their way (if and when they ever get to it). Sometimes, some improvements do make it into Stride in some form, like the Fog and Outline post processing filters. Recently, I kickstarted bringing OpenXR support into Stride (but not all of my VR improvements), and they seem to be getting it to work with DirectX. Fully merging would take much more work than us "spare time devs" have. If you want to use my improvements, I only can recommend using (and building off of) my fork.

## License

Focus is covered by [MIT](LICENSE.md), unless stated otherwise (i.e. for some files that are copied from other projects).

You can find the list of third party projects [here](THIRD%20PARTY.md).

## Documentation

Find explanations and information about Xenko:
* [Stride Manual](https://doc.stride3d.net/latest/en/manual/index.html)
* [API Reference](https://doc.stride3d.net/latest/api/index.html)

## Community

Ask for help or report issues:
* [Chat with the community on Discord](https://discord.gg/k563cUH)
* [Report engine issues](https://github.com/phr00t/xenko/issues)

## Building from source

### Prerequisites

1. [Git](https://git-scm.com/downloads) (recent version that includes LFS, or install [Git LFS](https://git-lfs.github.com/) separately).
2. [Visual Studio 2022](https://www.visualstudio.com/downloads/) (Use v17.2.7, MSBuild broken with v17.3) with the following workloads:
  * `.NET desktop development` with `.NET Framework 4.8 targeting pack`
  * `Desktop development with C++` with `Windows 10 SDK (latest)`, and `VC++ 2022 latest v143 tools` or later (both should be enabled by default)
  * .NET Core 6 Runtime should automatically be supported and configured to develop for
3. [FBX SDK 2019.0 VS2015](https://www.autodesk.com/developer-network/platform-technologies/fbx-sdk-2019-0)

### Build & Use Focus

1. Clone Focus: `git clone https://github.com/phr00t/FocusEngine.git`
2. Run `<FocusDir>\build\Xenko.PCPlatforms.bat`, which starts Visual Studio.
3. Select building for "Release" for the best performance and compatibility.
4. After building, find the GameStudio executable and run it (probably `<FocusDir>\sources\editor\Xenko.GameStudio\bin\Release\net48`).
5. Some templates are outdated, especially the VR one... an empty game is your best bet.
6. Use Focus Engine's GameStudio alongside Visual Studio for building the project (you can open projects from GameStudio into Visual Studio with a toolbar icon at the top)
7. If using Bepu physics, make sure to add a reference to `BepuPhysics.dll` in `<FocusDir>\deps\bepuphysics2`
