// Copyright (c) Xenko contributors (https://xenko.com) and Silicon Studio Corp. (https://www.siliconstudio.co.jp)
// Distributed under the MIT license. See the LICENSE.md file in the project root for more information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xenko.Audio;
using Xenko.Core;
using Xenko.Core.Mathematics;
using Xenko.Core.Threading;
using Xenko.Games;

namespace Xenko.Engine
{
    /// <summary>
    /// Efficiently plays and manages sound effects for an entire project
    /// </summary>
    public sealed class GlobalSoundManager
    {
        /// <summary>
        /// How far can sounds be heard?
        /// </summary>
        public static float MaxSoundDistance = 48f;

        /// <summary>
        /// How many sounds can overlap with eachother?
        /// </summary>
        public static int MaxSameSoundOverlaps = 8;

        /// <summary>
        /// Master volume
        /// </summary>
        public static float MasterVolume = 1f;

        /// <summary>
        /// Volume to apply to streamed audio (e.g. music)
        /// </summary>
        public static float StreamedVolume = 1f;

        /// <summary>
        /// Volume to apply to non-streamed audio (e.g. effects)
        /// </summary>
        public static float NonStreamedVolume = 1f;

        /// <summary>
        /// Sounds really close to the listener can cause jarring pan issues, so this will scoot sounds away to this distance. 0 to disable.
        /// </summary>
        public static float MinimumDistanceToListener = 1f;

        /// <summary>
        /// If a looped sound is out of range, play it anyway? You want it played if it will come near the player at some point. Defaults to true. Set to false to reduce overlapping sound usage. ignoreDistanceCheck will override this if set on function calls.
        /// </summary>
        public static bool PlayOutofRangeLoopedSounds = true;

        /// <summary>
        /// Different policies for replacing existing sounds when new ones want to play (and we ran out of overlapping sounds)
        /// </summary>
        public enum REPLACE_SOUND_POLICY
        {
            NO_REPLACING,
            REPLACE_ANY,
            REPLACE_UNLOOPED_ONLY
        }

        /// <summary>
        /// If you want a sound URL to have its own replacement policy, set it in this dictionary
        /// </summary>
        public static Dictionary<string, REPLACE_SOUND_POLICY> ReplaceSoundOverrides = new Dictionary<string, REPLACE_SOUND_POLICY>();

        /// <summary>
        /// If we have hit the overlap max for a new sound that wants to play, should we stop the oldest one and replace it? Defaults to REPLACE_OLDEST_UNLOOPED_ONLY
        /// </summary>
        public static REPLACE_SOUND_POLICY DefaultReplacementPolicy = REPLACE_SOUND_POLICY.REPLACE_UNLOOPED_ONLY;

        public static SoundInstance PlayCentralSound(string url, float pitch = 1f, float volume = 1f, float pan = 0f, bool looped = false)
        {
            SoundInstance s = getFreeInstance(url, false);
            if (s != null)
            {
                s.Pitch = pitch < 0f ? RandomPitch() : pitch;
                s.Volume = volume * MasterVolume * (s.DynamicSoundSource == null ? NonStreamedVolume : StreamedVolume);
                s.IsLooping = looped;
                s.Pan = pan;
                if (s.IsSpatialized) s.Apply3D(AudioEngine.DefaultListener.Position);
                s.Play();
            }
            return s;
        }

        private static Vector3 ProcessMinDistPosition(Vector3 desiredPos)
        {
            if (MinimumDistanceToListener <= 0f) return desiredPos;
            Vector3 diff = desiredPos - AudioEngine.DefaultListener.Position;
            float dist = diff.Length();
            if (dist < MinimumDistanceToListener)
            {
                if (dist <= float.Epsilon)
                    diff = AudioEngine.DefaultListener.Forward;
                else
                    diff.Normalize();

                return diff * MinimumDistanceToListener + desiredPos;
            }
            else return desiredPos;
        }

        public static SoundInstance PlayPositionSound(string url, Vector3 position, float pitch = 1f, float volume = 1f, float distanceScale = 1f, bool looped = false, bool ignoreDistanceCheck = false)
        {
            if (ignoreDistanceCheck == false && MaxSoundDistance > 0f && (!PlayOutofRangeLoopedSounds || !looped))
            {
                float sqrDist = ((position - AudioEngine.DefaultListener.Position) * distanceScale).LengthSquared();
                if (sqrDist >= MaxSoundDistance * MaxSoundDistance) return null;
            }
            SoundInstance s = getFreeInstance(url, true);
            if (s == null) return null;
            s.Pitch = pitch < 0f ? RandomPitch() : pitch;
            s.Volume = volume * MasterVolume * (s.DynamicSoundSource == null ? NonStreamedVolume : StreamedVolume);
            s.IsLooping = looped;
            s.Pan = 0f;
            s.DistanceScale = distanceScale;
            s.Apply3D(ProcessMinDistPosition(position), null, null);
            s.Play();
            return s;
        }

        public static SoundInstance PlayAttachedSound(string url, Entity parent, float pitch = 1f, float volume = 1f, float distanceScale = 1f, bool looped = false, bool ignoreDistanceCheck = false)
        {
            Vector3 pos = parent.Transform.WorldPosition(true);
            if (ignoreDistanceCheck == false && MaxSoundDistance > 0f && (!PlayOutofRangeLoopedSounds || !looped))
            {
                float sqrDist = ((pos - AudioEngine.DefaultListener.Position) * distanceScale).LengthSquared();
                if (MaxSoundDistance > 0f && sqrDist >= MaxSoundDistance * MaxSoundDistance) return null;
            }
            SoundInstance s = getFreeInstance(url, true);
            if (s == null) return null;
            s.Pitch = pitch < 0f ? RandomPitch() : pitch;
            s.Volume = volume * MasterVolume * (s.DynamicSoundSource == null ? NonStreamedVolume : StreamedVolume);
            s.IsLooping = looped;
            s.Pan = 0f;
            s.DistanceScale = distanceScale;
            s.Apply3D(ProcessMinDistPosition(pos), null, null);
            s.Play();
            var posSnd = new PositionalSound() {
                oldpos = pos,
                soundInstance = s,
                entity = parent,
            };
            currentAttached.Add(posSnd);
            return s;
        }

        public static Task<SoundInstance> PlayCentralSoundTask(string url, float pitch = 1f, float volume = 1f, float pan = 0f, bool looped = false)
        {
            return Task.Factory.StartNew<SoundInstance>(() =>
            {
                return PlayCentralSound(url, pitch, volume, pan, looped);
            });
        }

        public static Task<SoundInstance> PlayPositionSoundTask(string url, Vector3 position, float pitch = 1f, float volume = 1f, float distanceScale = 1f, bool looped = false, bool ignoreDistanceCheck = false)
        {
            return Task.Factory.StartNew<SoundInstance>(() =>
            {
                return PlayPositionSound(url, position, pitch, volume, distanceScale, looped, ignoreDistanceCheck);
            });
        }

        public static Task<SoundInstance> PlayAttachedSoundTask(string url, Entity parent, float pitch = 1f, float volume = 1f, float distanceScale = 1f, bool looped = false, bool ignoreDistanceCheck = false)
        {
            return Task.Factory.StartNew<SoundInstance>(() =>
            {
                return PlayAttachedSound(url, parent, pitch, volume, distanceScale, looped, ignoreDistanceCheck);
            });
        }

        /// <summary>
        /// Call this to make sure sounds attached to things get moved (and cleaned up) properly
        /// </summary>
        /// <param name="overrideTimePerFrame"></param>
        public static void UpdatePlayingSoundPositions(float? overrideTimePerFrame = null)
        {
            foreach (PositionalSound ps in currentAttached) {
                if (ps?.entity?.Scene == null)
                {
                    ps?.soundInstance?.Stop();
                    currentAttached.TryRemove(ps);
                    continue;
                }
                else if (ps.soundInstance.PlayState == Media.PlayState.Stopped)
                {
                    currentAttached.TryRemove(ps);
                    continue;
                }
                Vector3 newpos = ps.entity.Transform.WorldPosition();
                float timePerFrame = overrideTimePerFrame ?? ((float)internalGame.UpdateTime.Elapsed.Ticks / TimeSpan.TicksPerSecond);
                ps.soundInstance.Apply3D(ProcessMinDistPosition(newpos), (newpos - ps.oldpos) / timePerFrame, null);
                ps.oldpos = newpos;
            }
        }

        public static void StopAllSounds()
        {
            foreach (List<SoundInstance> si in instances.Values)
            {
                for (int i = 0; i < si.Count; i++)
                {
                    si[i].Stop();
                }
            }
            currentAttached.Clear();
        }

        /// <summary>
        /// Returns all of the paused sounds for easy resuming ones you want
        /// Make sure you either stop these if not resuming them, or resume them
        /// </summary>
        public static List<SoundInstance> PauseLoopedSounds()
        {
            List<SoundInstance> paused = new List<SoundInstance>();
            foreach (List<SoundInstance> si in instances.Values)
            {
                for (int i = 0; i < si.Count; i++)
                {
                    if (si[i].IsLooping && si[i].PlayState == Media.PlayState.Playing)
                    {
                        si[i].Pause();
                        paused.Add(si[i]);
                    }
                }
            }
            return paused;
        }

        /// <summary>
        /// Helper function for resuming a list of paused, looped sounds usually provided from PauseLoopedSounds()
        /// </summary>
        public static void ResumeLoopedSounds(List<SoundInstance> paused_looped_sounds)
        {
            if (paused_looped_sounds == null) return;
            foreach (SoundInstance si in paused_looped_sounds)
                if (si.IsLooping && si.PlayState == Media.PlayState.Paused) si.Play();
        }

        public static void StopSound(string url)
        {
            if (instances.TryGetValue(url, out var snds))
            {
                for (int i = 0; i < snds.Count; i++)
                {
                    snds[i].Stop();
                }
            }
        }

        public static float RandomPitch(float range = 0.2f, float middle = 1f)
        {
            if (rand == null) rand = new Random(Environment.TickCount);
            return (float)rand.NextDouble() * range * 2f + middle - range;
        }

        public static void Reset()
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

        static GlobalSoundManager()
        {
            // global sound manager relies on listeners being shared, so everything can be
            // safely reused
            internalGame = ServiceRegistry.instance?.GetService<IGame>() as Game;
        }

        private static ConcurrentDictionary<string, Sound> Sounds = new ConcurrentDictionary<string, Sound>();
        private static ConcurrentDictionary<string, List<SoundInstance>> instances = new ConcurrentDictionary<string, List<SoundInstance>>();
        private static ConcurrentHashSet<PositionalSound> currentAttached = new ConcurrentHashSet<PositionalSound>();
        private static System.Random rand;
        private static Game internalGame;

        /// <summary>
        /// Pre-load a sound so it will play faster later.
        /// </summary>
        /// <param name="url">Content URL of the audio</param>
        public static void PreloadAudio(string url)
        {
            if (Sounds.TryGetValue(url, out var snd1) || instances.TryGetValue(url, out var ins))
                return; // already loaded

            // this might throw an exception if you provided a bad url
            Sound snd2 = internalGame.Content.Load<Sound>(url);

            // load this sound and prepare lists for it
            SoundInstance si = snd2.CreateInstance(AudioEngine.DefaultListener);
            List<SoundInstance> lsi = new List<SoundInstance>();
            lsi.Add(si);
            instances[url] = lsi;
            Sounds[url] = snd2;
        }

        private static SoundInstance getFreeInstance(string url, bool spatialized)
        {
            if (url == null) return null;

            if (instances.TryGetValue(url, out var ins))
            {
                if (ReplaceSoundOverrides.TryGetValue(url, out var ReplacementPolicy) == false)
                    ReplacementPolicy = DefaultReplacementPolicy;

                SoundInstance oldestSI = null;
                float replaceScore = 0f;
                for (int i=0; i<ins.Count; i++)
                {
                    var snd = ins[i];
                    if (snd.PlayState == Media.PlayState.Stopped)
                        return snd;

                    if (ReplacementPolicy != REPLACE_SOUND_POLICY.NO_REPLACING)
                    {
                        float myscore = (float)snd.Position.TotalSeconds + (snd.IsSpatialized ? (snd.SpatializedPosition - snd.Listener.Position).Length() : 0f);
                        if ((i == 0 || myscore > replaceScore) && (ReplacementPolicy == REPLACE_SOUND_POLICY.REPLACE_ANY || snd.IsLooping == false))
                        {
                            oldestSI = snd;
                            replaceScore = myscore;
                        }
                    }
                }

                // have we reached our max sounds though?
                // are we going to try and replace it?
                if (ins.Count >= MaxSameSoundOverlaps)
                {
                    if (oldestSI != null)
                    {
                        oldestSI.Stop();
                        return oldestSI;
                    }

                    // max reached and no replacement
                    return null;
                }

                // don't have a free one to play, add a new one to the list
                if (Sounds.TryGetValue(url, out var snd0))
                {
                    SoundInstance si0 = snd0.CreateInstance(AudioEngine.DefaultListener);
                    ins.Add(si0);
                    return si0;
                }
            }

            // don't have a list for this, make one
            if (Sounds.TryGetValue(url, out var snd1))
            {
                SoundInstance si1 = snd1.CreateInstance(AudioEngine.DefaultListener);
                List<SoundInstance> lsi1 = new List<SoundInstance>();
                lsi1.Add(si1);
                instances[url] = lsi1;
                return si1;
            }

            // this might throw an exception if you provided a bad url
            Sound snd2 = internalGame.Content.Load<Sound>(url);

            if (!snd2.Spatialized && spatialized)
                throw new InvalidOperationException("Trying to play " + url + " positionally, yet it is a non-spatialized sound!");

            SoundInstance si = snd2.CreateInstance(AudioEngine.DefaultListener);
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
            public Vector3 oldpos;
        }
    }
}
