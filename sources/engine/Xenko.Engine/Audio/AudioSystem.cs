// Copyright (c) Xenko contributors (https://xenko.com) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System;
using Xenko.Core;
using Xenko.Core.Collections;
using Xenko.Core.Mathematics;
using Xenko.Engine;
using Xenko.Engine.Design;
using Xenko.Games;

namespace Xenko.Audio
{
    /// <summary>
    /// The Audio System.
    /// It creates an underlying instance of <see cref="AudioEngine"/>.
    /// </summary>
    public class AudioSystem : GameSystemBase, IAudioEngineProvider
    {
        private static readonly object AudioEngineStaticLock = new object();
        private static AudioEngine audioEngineSingleton;

        private SceneSystem sceneSystem;
        
        internal TransformComponent primaryTransform;

        /// <summary>
        /// Create an new instance of AudioSystem
        /// </summary>
        /// <param name="registry">The service registry in which to register the <see cref="AudioSystem"/> services</param>
        public AudioSystem(IServiceRegistry registry)
            : base(registry)
        {
            Enabled = true;
        }

        /// <summary>
        /// The underlying <see cref="AudioEngine" />.
        /// </summary>
        /// <value>The audio engine.</value>
        public AudioEngine AudioEngine { get; private set; }

        public AudioDevice RequestedAudioDevice { get; set; } = new AudioDevice();

        public override void Initialize()
        {
            base.Initialize();

            lock (AudioEngineStaticLock)
            {
                if (audioEngineSingleton == null)
                {
                    var settings = Services.GetService<IGameSettingsService>()?.Settings?.Configurations?.Get<AudioEngineSettings>();
                    audioEngineSingleton = AudioEngineFactory.NewAudioEngine(RequestedAudioDevice, settings != null && settings.HrtfSupport ? AudioLayer.DeviceFlags.Hrtf : AudioLayer.DeviceFlags.None);
                }
                else
                {
                    ((IReferencable)audioEngineSingleton).AddReference();
                }

                AudioEngine = audioEngineSingleton;
            }

            Game.Activated += OnActivated;
            Game.Deactivated += OnDeactivated;

            sceneSystem = Services.GetService<SceneSystem>();
        }

        public override void Update(GameTime gameTime)
        {
            var listener = AudioEngine.DefaultListener;
            TransformComponent tc = primaryTransform ?? sceneSystem.GraphicsCompositor?.MainCamera?.Entity?.Transform;
            if (tc != null && listener != null)
            {
                listener.WorldTransform = tc.WorldMatrix;
                var newPosition = listener.WorldTransform.TranslationVector;
                listener.Velocity = (newPosition - listener.Position) / (float)gameTime.TimePerFrame.TotalSeconds; // estimate velocity from last and new position
                listener.Position = newPosition;
                listener.Forward = -tc.Forward(true);
                listener.Up = tc.Up(true);

                listener.Update();
            }

            AudioEngine.Update();
        }

        // called on dispose
        protected override void Destroy()
        {
            Game.Activated -= OnActivated;
            Game.Deactivated -= OnDeactivated;

            base.Destroy();

            lock (AudioEngineStaticLock)
            {
                AudioEngine = null;
                var count = ((IReferencable)audioEngineSingleton).Release();
                if (count == 0)
                {
                    audioEngineSingleton = null;
                }
            }
        }

        private void OnActivated(object sender, EventArgs e)
        {
            // resume the audio
            AudioEngine.ResumeAudio();
        }

        private void OnDeactivated(object sender, EventArgs e)
        {
            // pause the audio
            AudioEngine.PauseAudio();
            AudioEngine.Update(); // force the update of the audio to pause the Musics
        }
    }
}
