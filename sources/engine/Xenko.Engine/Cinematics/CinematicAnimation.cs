using System;
using System.Collections.Generic;
using System.Text;
using Xenko.Core.Mathematics;
using Xenko.Engine;

namespace Xenko.Cinematics
{
    /// <summary>
    /// What kind of action is a given CinematicAction?
    /// </summary>
    public enum ACTION_TYPE
    {
        SET_POSITION,
        SET_ROTATION,
        SET_SCALE,
        LINEAR_MOVE,
        LINEAR_ROTATE,
        LINEAR_SCALE,
        DELEGATE,
        APPLY_VELOCITY,
        APPLY_SPIN,
        SMOOTH_MOVE,
        SMOOTH_ROTATE,
        SMOOTH_SCALE
    };

    /// <summary>
    /// An action to be stored and played in a CinematicAnimation
    /// </summary>
    public class CinematicAction : IComparable<CinematicAction>
    {
        public ACTION_TYPE Type;
        public object argument0, argument1;
        public TransformComponent target;
        public float startTime, endTime;
        public Action<ActionInfo> method;
        public bool relativeStartArgument;

        public int CompareTo(CinematicAction other)
        {
            if (startTime == other.startTime)
                return 0;

            return startTime.CompareTo(other.startTime);
        }
    }

    /// <summary>
    /// Arguments provided in an action
    /// </summary>
    public struct ActionInfo
    {
        public float PercentProgress;
        public float AnimationTimeElapsed;
    }

    /// <summary>
    /// Simple cinematic system to animate TransformComponents, run methods etc.
    /// </summary>
    public class CinematicAnimation
    {
        /// <summary>
        /// Should we play this when Play is called? Also gets set to True at the end of the Cinematic
        /// </summary>
        public bool Paused = false;

        /// <summary>
        /// Does this animation loop?
        /// </summary>
        public bool Looping = false;

        /// <summary>
        /// What time are we at in the animation?
        /// </summary>
        public float CurrentTime { get; private set; }

        /// <summary>
        /// All actions added to this CinematicAction. If you change this directly, you'll have to Reset() the animation to sort it in
        /// </summary>
        public List<CinematicAction> AllActions { get; private set; } = new List<CinematicAction>();

        /// <summary>
        /// Actions remaining to be played, sorted internally by start time
        /// </summary>
        private List<CinematicAction> RemainingActions = new List<CinematicAction>();

        /// <summary>
        /// Returns true if this cinematic is finished
        /// </summary>
        public bool IsDone => RemainingActions.Count == 0;

        /// <summary>
        /// Returns true if there are things to be played when Play is called
        /// </summary>
        public bool CanBePlayed => !IsDone && !Paused;

        /// <summary>
        /// Creates a CinematicAnimation for population and playing
        /// </summary>
        /// <param name="initialize_as_paused">Should we have this Animation Paused to start with?</param>
        public CinematicAnimation(bool initialize_as_paused = false)
        {
            Paused = initialize_as_paused;
        }

        /// <summary>
        /// Plays the CinematicAnimation, if it isn't paused
        /// </summary>
        /// <param name="time_delta">Amount of time to progress the animation</param>
        public void Play(float time_delta)
        {
            if (Paused) return;
            if (Looping && RemainingActions.Count == 0 && AllActions.Count > 0) Reset();
            CurrentTime += time_delta;
            for (int i = 0; i < RemainingActions.Count; i++)
            {
                CinematicAction ca = RemainingActions[i];
                // are we doing this now!?
                if (ca.startTime <= CurrentTime)
                {
                    // yes!
                    PerformAction(ca, time_delta);
                    // remove this action?
                    if (ca.endTime <= CurrentTime)
                    {
                        if (ca.relativeStartArgument) ca.argument0 = null;
                        RemainingActions.RemoveAt(i);
                        i--;
                    }
                }
                else break; // since we are sorted, once we are past CurrentTime, everything else will be too
            }
        }

        /// <summary>
        /// Make an action for this CinematicAnimation at the given time.
        /// </summary>
        /// <param name="type">What kind of action is this?</param>
        /// <param name="target">The TransformComponent to act on</param>
        /// <param name="endArgument">Primary argument for the action, like Vector3 for SET_POSITION</param>
        /// <param name="startTime">What time to do this action?</param>
        /// <param name="startArgument">Where should this action start from? null if use whatever we are currently at</param>
        /// <param name="endTime">When to end this action?</param>
        public void AddAction(ACTION_TYPE type, TransformComponent target, object endArgument, float startTime, object startArgument = null, float endTime = 0f)
        {
            CinematicAction ca = new CinematicAction()
            {
                Type = type,
                target = target,
                argument0 = startArgument,
                argument1 = endArgument,
                startTime = startTime,
                endTime = endTime,
                relativeStartArgument = startArgument == null
            };
            AllActions.Add(ca);
            if (CurrentTime <= startTime)
            {
                RemainingActions.Add(ca);
                RemainingActions.Sort();
            }
        }

        /// <summary>
        /// Add a method to be run at the given time. ActionInfo is constructed, set and passed as an argument to the method
        /// </summary>
        /// <param name="method">Action to be executed</param>
        /// <param name="startTime">When to execute the action</param>
        /// <param name="endTime">Keep running the action until this time, default is 0 which means always just run this once</param>
        public void AddMethod(Action<ActionInfo> method, float startTime, float endTime = 0f)
        {
            CinematicAction ca = new CinematicAction()
            {
                Type = ACTION_TYPE.DELEGATE,
                method = method,
                startTime = startTime,
                endTime = endTime
            };
            AllActions.Add(ca);
            if (CurrentTime <= startTime)
            {
                RemainingActions.Add(ca);
                RemainingActions.Sort();
            }
        }

        private float Sigmoid(double value)
        {
            return 1.0f / (1.0f + (float)Math.Exp(-value));
        }

        [ThreadStatic]
        private static ActionInfo tempArguments;

        private void PerformAction(CinematicAction ca, float delta_time)
        {
            // wait, are we just an action to call here?
            float totalTimeOfAction = ca.endTime - ca.startTime;
            float positionInAction = totalTimeOfAction > 0f ? (CurrentTime - ca.startTime) / totalTimeOfAction : 1f;
            if (positionInAction > 1f) positionInAction = 1f;
            switch (ca.Type)
            {
                case ACTION_TYPE.DELEGATE:
                    tempArguments.AnimationTimeElapsed = delta_time;
                    tempArguments.PercentProgress = positionInAction;
                    ca.method(tempArguments);
                    break;
                case ACTION_TYPE.SET_POSITION:
                    ca.target.Position = (Vector3)ca.argument1;
                    break;
                case ACTION_TYPE.SET_ROTATION:
                    ca.target.Rotation = (Quaternion)ca.argument1;
                    break;
                case ACTION_TYPE.SET_SCALE:
                    ca.target.Scale = (Vector3)ca.argument1;
                    break;
                case ACTION_TYPE.LINEAR_MOVE:
                    if (ca.argument0 == null) ca.argument0 = ca.target.Position;
                    ca.target.Position = Vector3.Lerp((Vector3)ca.argument0, (Vector3)ca.argument1, positionInAction);
                    break;
                case ACTION_TYPE.LINEAR_ROTATE:
                    if (ca.argument0 == null) ca.argument0 = ca.target.Rotation;
                    ca.target.Rotation = Quaternion.Lerp((Quaternion)ca.argument0, (Quaternion)ca.argument1, positionInAction);
                    break;
                case ACTION_TYPE.LINEAR_SCALE:
                    if (ca.argument0 == null) ca.argument0 = ca.target.Scale;
                    ca.target.Scale = Vector3.Lerp((Vector3)ca.argument0, (Vector3)ca.argument1, positionInAction);
                    break;
                case ACTION_TYPE.APPLY_VELOCITY:
                    ca.target.Position += (Vector3)(ca.argument1) * delta_time;
                    break;
                case ACTION_TYPE.APPLY_SPIN:
                    ca.target.Rotation *= Quaternion.Lerp(Quaternion.Identity, (Quaternion)ca.argument1, delta_time);
                    break;
                case ACTION_TYPE.SMOOTH_MOVE:
                    if (ca.argument0 == null) ca.argument0 = ca.target.Position;
                    ca.target.Position = positionInAction < 1f ? Vector3.Lerp((Vector3)ca.argument0, (Vector3)ca.argument1, Sigmoid(positionInAction * 15.0 - 7.0)) : (Vector3)ca.argument1;
                    break;
                case ACTION_TYPE.SMOOTH_ROTATE:
                    if (ca.argument0 == null) ca.argument0 = ca.target.Rotation;
                    ca.target.Rotation = positionInAction < 1f ? Quaternion.Lerp((Quaternion)ca.argument0, (Quaternion)ca.argument1, Sigmoid(positionInAction * 15.0 - 7.0)) : (Quaternion)ca.argument1;
                    break;
                case ACTION_TYPE.SMOOTH_SCALE:
                    if (ca.argument0 == null) ca.argument0 = ca.target.Scale;
                    ca.target.Scale = positionInAction < 1f ? Vector3.Lerp((Vector3)ca.argument0, (Vector3)ca.argument1, Sigmoid(positionInAction * 15.0 - 7.0)) : (Vector3)ca.argument1;
                    break;
            }
        }

        /// <summary>
        /// Calculates and provides the total length of this cinematic animation
        /// </summary>
        public float Length
        {
            get
            {
                float len = 0f;
                for (int i=0; i<AllActions.Count; i++)
                {
                    var action = AllActions[i];
                    float time = Math.Max(action.startTime, action.endTime);
                    if (time > len) len = time;
                }
                return len;
            }
        }

        /// <summary>
        /// Simple shortcut to immediately play the remaining animation to the end
        /// </summary>
        public void PlayToEnd()
        {
            float timeToPlay = Length - CurrentTime;
            if (timeToPlay > 0f)
                Play(Length - CurrentTime);
        }

        /// <summary>
        /// Sets the animation to a certain time by resetting the whole animation, then jumping to this time
        /// </summary>
        /// <param name="time">What time to set the animation to</param>
        public void SetTime(float time)
        {
            Reset();
            bool wasPaused = Paused;
            CurrentTime = time;
            Paused = false;
            Play(0f);
            Paused = wasPaused;
        }

        /// <summary>
        /// Sets the animation back to the start for replaying
        /// </summary>
        public void Reset()
        {
            CurrentTime = 0f;
            RemainingActions.Clear();
            for (int i = 0; i < AllActions.Count; i++)
            {
                CinematicAction ca = AllActions[i];
                if (ca.relativeStartArgument) ca.argument0 = null;
                RemainingActions.Add(ca);
            }
            RemainingActions.Sort();
        }
    }
}
