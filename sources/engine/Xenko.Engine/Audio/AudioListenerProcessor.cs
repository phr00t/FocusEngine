// Copyright (c) Xenko contributors (https://xenko.com) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System.Collections.Generic;
using System.Linq;
using Xenko.Core.Mathematics;
using Xenko.Engine;
using Xenko.Rendering;

namespace Xenko.Audio
{
    /// <summary>
    /// Processor in charge of creating and updating the <see cref="AudioListener"/> data associated to the scene <see cref="AudioListenerComponent"/>s.
    /// </summary>
    /// <remarks>
    /// The processor updates only <see cref="AudioListener"/> associated to <see cref="AudioListenerComponent"/>s 
    /// The processor is subscribing to the <see cref="audioSystem"/> <see cref="AudioListenerComponent"/> collection events to be informed of required <see cref="AudioEmitter"/> updates.
    /// When a <see cref="AudioListenerComponent"/> is added to the <see cref="audioSystem"/>, the processor set the associated <see cref="AudioEmitter"/>.
    /// When a <see cref="AudioListenerComponent"/> is removed from the entity system, 
    /// the processor set the <see cref="AudioEmitter"/> reference of the <see cref="AudioSystem"/> to null 
    /// but do not remove the <see cref="AudioListenerComponent"/> from its collection.
    /// </remarks>
    public class AudioListenerProcessor : EntityProcessor<AudioListenerComponent>
    {
        /// <summary>
        /// Reference to the <see cref="AudioSystem"/> of the game instance.
        /// </summary>
        private AudioSystem audioSystem;

        /// <summary>
        /// Create a new instance of AudioListenerProcessor.
        /// </summary>
        public AudioListenerProcessor()
            : base(typeof(AudioListenerComponent))
        {
        }

        protected internal override void OnSystemAdd()
        {
            audioSystem = Services.GetService<AudioSystem>();
        }

        protected internal override void OnSystemRemove()
        {
        }

        protected override void OnEntityComponentAdding(Entity entity, AudioListenerComponent component, AudioListenerComponent data)
        {
            if (!entity.Transform.IsMovingInsideRootScene)
                audioSystem.primaryTransform = entity.Transform;
        }

        protected override void OnEntityComponentRemoved(Entity entity, AudioListenerComponent component, AudioListenerComponent data)
        {
            if (!entity.Transform.IsMovingInsideRootScene)
                audioSystem.primaryTransform = null;
        }

        public override void Draw(RenderContext context)
        {

        }
    }
}
