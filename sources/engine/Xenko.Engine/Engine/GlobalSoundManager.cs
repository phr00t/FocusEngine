// Copyright (c) Xenko contributors (https://xenko.com) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xenko.Audio;
using Xenko.Core;
using Xenko.Core.Mathematics;
using Xenko.Games;

namespace Xenko.Engine
{
    /// <summary>
    /// Efficiently plays and manages sound effects for an entire project
    /// </summary>
    [Display("Global Sound Manager", Expand = ExpandRule.Once)]
    [DataContract("GlobalSoundManager")]
    [ComponentOrder(7500)]
    [ComponentCategory("Audio")]
    public sealed class GlobalSoundManager : ActivableEntityComponent
    {
        [DataMember]
        public float MaxSoundDistance = 48f;

        [DataMember]
        public int MaxSameSoundOverlaps = 8;

        [DataMember]
        public float MasterVolume = 1f;

        public SoundInstance PlayCentralSound(string url, float pitch = 1f, float volume = 1f, float pan = 0f, bool looped = false)
        {
            SoundInstance s = getFreeInstance(url, false);
            if (s != null)
            {
                s.Pitch = pitch < 0f ? RandomPitch() : pitch;
                s.Volume = volume * MasterVolume;
                s.IsLooping = looped;
                s.Pan = pan;
                if (s.IsSpatialized) s.Apply3D(AudioEngine.DefaultListener.Position);
                s.Play();
            }
            return s;
        }

        public SoundInstance PlayPositionSound(string url, Vector3 position, float pitch = 1f, float volume = 1f, float distanceScale = 1f, bool looped = false)
        {
            if (MaxSoundDistance > 0f)
            {
                float sqrDist = ((position - AudioEngine.DefaultListener.Position) * distanceScale).LengthSquared();
                if (sqrDist >= MaxSoundDistance * MaxSoundDistance) return null;
            }
            SoundInstance s = getFreeInstance(url, true);
            if (s == null) return null;
            s.Pitch = pitch < 0f ? RandomPitch() : pitch;
            s.Volume = volume * MasterVolume;
            s.IsLooping = looped;
            s.Pan = 0f;
            s.Apply3D(position, null, null, distanceScale);
            s.Play();
            return s;
        }

        public SoundInstance PlayAttachedSound(string url, Entity parent, float pitch = 1f, float volume = 1f, float distanceScale = 1f, bool looped = false)
        {
            Vector3 pos = parent.Transform.WorldPosition(true);
            if (MaxSoundDistance > 0f)
            {
                float sqrDist = ((pos - AudioEngine.DefaultListener.Position) * distanceScale).LengthSquared();
                if (MaxSoundDistance > 0f && sqrDist >= MaxSoundDistance * MaxSoundDistance) return null;
            }
            SoundInstance s = getFreeInstance(url, true);
            if (s == null) return null;
            s.Pitch = pitch < 0f ? RandomPitch() : pitch;
            s.Volume = volume * MasterVolume;
            s.IsLooping = looped;
            s.Pan = 0f;
            s.Apply3D(pos, null, null, distanceScale);
            s.Play();
            var posSnd = new PositionalSound() {
                pos = pos,
                soundInstance = s,
                entity = parent,
                distance_scale = distanceScale
            };
            lock (currentAttached) {
                currentAttached.Add(posSnd);
            }
            return s;
        }

        public Task<SoundInstance> PlayCentralSoundTask(string url, float pitch = 1f, float volume = 1f, float pan = 0f, bool looped = false)
        {
            return Task.Factory.StartNew<SoundInstance>(() =>
            {
                return PlayCentralSound(url, pitch, volume, pan, looped);
            });
        }

        public Task<SoundInstance> PlayPositionSoundTask(string url, Vector3 position, float pitch = 1f, float volume = 1f, float distanceScale = 1f, bool looped = false)
        {
            return Task.Factory.StartNew<SoundInstance>(() =>
            {
                return PlayPositionSound(url, position, pitch, volume, distanceScale, looped);
            });
        }

        public Task<SoundInstance> PlayAttachedSoundTask(string url, Entity parent, float pitch = 1f, float volume = 1f, float distanceScale = 1f, bool looped = false)
        {
            return Task.Factory.StartNew<SoundInstance>(() =>
            {
                return PlayAttachedSound(url, parent, pitch, volume, distanceScale, looped);
            });
        }

        public void UpdatePlayingSoundPositions(float? overrideTimePerFrame = null)
        {
            for (int i = 0; i < currentAttached.Count; i++)
            {
                PositionalSound ps = currentAttached[i];
                if (ps.entity.Scene == null)
                {
                    ps.soundInstance.Stop();
                    currentAttached.RemoveAt(i);
                    i--;
                    continue;
                }
                else if (ps.soundInstance.PlayState == Media.PlayState.Stopped)
                {
                    currentAttached.RemoveAt(i);
                    i--;
                    continue;
                }
                Vector3 newpos = ps.entity.Transform.WorldPosition();
                float timePerFrame = overrideTimePerFrame ?? ((float)internalGame.UpdateTime.Elapsed.Ticks / TimeSpan.TicksPerSecond);
                ps.soundInstance.Apply3D(newpos, (newpos - ps.pos) / timePerFrame, null, ps.distance_scale);
                ps.pos = newpos;
            }
        }

        public void StopAllSounds()
        {
            foreach (List<SoundInstance> si in instances.Values)
            {
                for (int i = 0; i < si.Count; i++)
                {
                    si[i].Stop();
                }
            }
            lock (currentAttached)
            {
                currentAttached.Clear();
            }
        }

        public void StopSound(string url)
        {
            if (instances.TryGetValue(url, out var snds))
            {
                for (int i = 0; i < snds.Count; i++)
                {
                    snds[i].Stop();
                }
            }
        }

        public float RandomPitch(float range = 0.2f, float middle = 1f)
        {
            if (rand == null) rand = new Random(Environment.TickCount);
            return (float)rand.NextDouble() * range * 2f + middle - range;
        }

        public void Reset()
        {
            StopAllSounds();
            Sounds.Clear();
            foreach (List<SoundInstance> si in instances.Values)
            {
                for (int i = 0; i < si.Count; i++)
                {
                    si[i].Dispose();
                }
                si.Clear();
            }
            instances.Clear();
        }

        public GlobalSoundManager()
        {
            // global sound manager relies on listeners being shared, so everything can be
            // safely reused
            internalGame = ServiceRegistry.instance?.GetService<IGame>() as Game;
        }

        private ConcurrentDictionary<string, Sound> Sounds = new ConcurrentDictionary<string, Sound>();
        private ConcurrentDictionary<string, List<SoundInstance>> instances = new ConcurrentDictionary<string, List<SoundInstance>>();
        private List<PositionalSound> currentAttached = new List<PositionalSound>();
        private System.Random rand;
        private Game internalGame;

        private SoundInstance getFreeInstance(string url, bool spatialized)
        {
            if (url == null) return null;

            if (instances.TryGetValue(url, out var ins))
            {
                for (int i=0; i<ins.Count; i++)
                {
                    if (ins[i].PlayState == Media.PlayState.Stopped)
                        return ins[i];
                }

                // have we reached our max sounds though?
                if (ins.Count >= MaxSameSoundOverlaps) return null;

                // don't have a free one to play, add a new one to the list
                if (Sounds.TryGetValue(url, out var snd0))
                {
                    SoundInstance si0 = snd0.CreateInstance(AudioEngine.DefaultListener, true, false, 0f, HrtfEnvironment.Small);
                    ins.Add(si0);
                    return si0;
                }
            }

            // don't have a list for this, make one
            if (Sounds.TryGetValue(url, out var snd1))
            {
                SoundInstance si1 = snd1.CreateInstance(AudioEngine.DefaultListener, true, false, 0f, HrtfEnvironment.Small);
                List<SoundInstance> lsi1 = new List<SoundInstance>();
                lsi1.Add(si1);
                instances[url] = lsi1;
                return si1;
            }

            // this might throw an exception if you provided a bad url
            Sound snd2 = internalGame.Content.Load<Sound>(url);

            if (!snd2.Spatialized && spatialized)
                throw new InvalidOperationException("Trying to play " + url + " positionally, yet it is a non-spatialized sound!");

            SoundInstance si = snd2.CreateInstance(AudioEngine.DefaultListener, true, false, 0f, HrtfEnvironment.Small);
            List<SoundInstance> lsi = new List<SoundInstance>();
            lsi.Add(si);
            instances[url] = lsi;
            Sounds[url] = snd2;
            return si;
        }

        private class PositionalSound
        {
            public SoundInstance soundInstance;
            public Entity entity;
            public Vector3 pos;
            public float distance_scale;
        }
    }
}
