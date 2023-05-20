using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using Xenko.Core.Mathematics;
using Xenko.Engine;

namespace Xenko.Physics.Bepu
{
    /// <summary>
    /// Handles requesting a raycast to be performed at a good time. Result will be populated with the latest one, when its ready.
    /// This is faster than doing an immediate Raycast and more reliable than a TryFastRaycast, but will likely delay results.
    /// </summary>
    public class BepuManagedRaycast
    {
        private class BepuRequest
        {
            public BepuHitResult res;
            public Vector3 start, end;
            public BepuPhysicsComponent skip;
            public CollisionFilterGroupFlags hits;
            public int ticks;
            public object user_data;
        }

        private ConcurrentQueue<BepuRequest> internalRequests, internalResults;

        private Action<float> internalRayAction;

        /// <summary>
        /// Pop out the oldest result from the requested queue
        /// </summary>
        /// <param name="ageMS">How old is this request in Environment.Ticks?</param>
        /// <param name="user_data">Data that was part of the request</param>
        /// <returns>null if there are no results</returns>
        public bool PopResult(out BepuHitResult result, out int ageMS, out object user_data)
        {
            if (internalResults.TryDequeue(out var res) == false)
            {
                user_data = null;   
                ageMS = int.MaxValue;
                result = default;
                return false;
            }

            ageMS = Environment.TickCount - res.ticks;
            result = res.res;
            user_data = res.user_data;
            return true;
        }

        /// <summary>
        /// Make a request and setting parameters at the same time. user_data stays with this request
        /// </summary>
        public void Request(object user_data, Vector3 start, Vector3 end, CollisionFilterGroupFlags hits = CollisionFilterGroupFlags.AllFilter, BepuPhysicsComponent skipComponent = null)
        {
            Request(new BepuRequest()
            {
                ticks = Environment.TickCount,
                end = end,
                start = start,
                hits = hits,
                skip = skipComponent,
                user_data = user_data               
            });
        }

        /// <summary>
        /// Make a request and setting parameters at the same time. user_data stays with this request
        /// </summary>
        public void Request(object user_data, Vector3 start, Vector3 direction, float length, CollisionFilterGroupFlags hits = CollisionFilterGroupFlags.AllFilter, BepuPhysicsComponent skipComponent = null)
        {
            Request(new BepuRequest()
            {
                ticks = Environment.TickCount,
                end = start + direction * length,
                start = start,
                hits = hits,
                skip = skipComponent,
                user_data = user_data
            });
        }

        private void Request(BepuRequest br)
        {
            if (internalRayAction == null)
            {
                internalRayAction = (tpf) =>
                {
                    if (internalRequests.TryDequeue(out var req))
                    {
                        req.res = BepuSimulation.instance.Raycast(req.start, req.end, req.hits, req.skip);
                        req.ticks = Environment.TickCount;
                        internalResults.Enqueue(req);
                    }
                };
            }

            internalRequests.Enqueue(br);
            BepuSimulation.instance.ActionsAfterSimulationStep.Enqueue(internalRayAction);
        }
    }
}
