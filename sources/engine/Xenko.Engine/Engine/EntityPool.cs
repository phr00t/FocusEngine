using System.Collections.Concurrent;
using System.Collections.Generic;
using Xenko.Core.Mathematics;
using Xenko.Engine;

/// <summary>
/// Pool for recycling entities
/// </summary>
public static class EntityPool {

    // You can avoid resizing of the Stack's internal array by
    // setting this to a number equal to or greater to what you
    // expect most of your pool sizes to be.
    // Note, you can also use Preload() to set the initial size
    // of a pool -- this can be handy if only some of your pools
    // are going to be exceptionally large (for example, your bullets.)
    const int DEFAULT_POOL_SIZE = 3;

    /// <summary>
    /// The Pool class represents the pool for a particular prefab.
    /// </summary>
    public class Pool {
        // We append an id to the name of anything we instantiate.
        // This is purely cosmetic.
        int nextId = 1;

        // The structure containing our inactive objects.
        // Why a Stack and not a List? Because we'll never need to
        // pluck an object from the start or middle of the array.
        // We'll always just grab the last one, which eliminates
        // any need to shuffle the objects around in memory.
        Stack<Entity> inactive;

        // The prefab that we are pooling
        Entity prefab;

        /// <summary>
        /// prepare a pool for Entity recycling
        /// </summary>
        public Pool(Entity prefab, int initialQty) {
            this.prefab = prefab;
            // If Stack uses a linked list internally, then this
            // whole initialQty thing is a placebo that we could
            // strip out for more minimal code.
            inactive = new Stack<Entity>(initialQty);
        }

        /// <summary>
        /// Spawn an object from our pool
        /// </summary>
        public Entity Spawn(Vector3? pos = null, Quaternion? rot = null) {
            Entity obj = null;

            lock (inactive)
            {
                if (inactive.Count > 0)
                    obj = inactive.Pop();
            }

            if (obj == null) {
                // We don't have an object in our pool, so we
                // instantiate a whole new object.
                obj = prefab.Clone();
                obj.Name = prefab.Name + " (" + (nextId++) + ")";
                // Add a PoolMember component so we know what pool
                // we belong to.
                obj.UsingPool = new PoolMember();
                obj.UsingPool.myPool = this;
            } else if (obj.UsingPool == null || obj.UsingPool.active || obj.Scene != null) {
                // something weird happened...
                // it isn't really inactive...
                // Just get another one, then...
                return Spawn(pos, rot);
            }

            obj.UsingPool.active = true;
            obj.Transform.Position = pos ?? Vector3.Zero;
            obj.Transform.Rotation = rot ?? Quaternion.Identity;

            return obj;
        }

        // Return an object to the inactive pool.
        public void ReturnToPool(Entity obj, ref bool active) {
            if (Xenko.Engine.SceneSystem.DoNotDisposeOnNextRemoval) return;
            if (active)
            {
                lock (inactive)
                {
                    inactive.Push(obj);
                }
            }
            obj.Scene = null;
            active = false;
        }
    }

    /// <summary>
    /// Added to freshly instantiated objects, so we can link back
    /// to the correct pool on despawn.
    /// </summary>
    public class PoolMember {
        public Pool myPool;
        public bool active;
    }

    // All of our pools
    static ConcurrentDictionary<Entity, Pool> pools = new ConcurrentDictionary<Entity, Pool>();

    /// <summary>
    /// Init our dictionary.
    /// </summary>
    static void Init(Entity prefab = null, int qty = DEFAULT_POOL_SIZE) {
        if (prefab != null && pools.ContainsKey(prefab) == false) {
            pools[prefab] = new Pool(prefab, qty);
        }
    }

    /// <summary>
    /// If you want to preload a few copies of an object at the start
    /// of a scene, you can use this. Really not needed unless you're
    /// going to go from zero instances to 10+ very quickly.
    /// Could technically be optimized more, but in practice the
    /// Spawn/Despawn sequence is going to be pretty darn quick and
    /// this avoids code duplication.
    /// </summary>
    static public void Preload(Entity prefab, int qty = DEFAULT_POOL_SIZE) {
        if (qty <= 0) return;

        Init(prefab, qty);
        
        // Make an array to grab the objects we're about to pre-spawn.
        Entity[] obs = new Entity[qty];
        for (int i = 0; i < qty; i++) {
            obs[i] = Spawn(prefab);
        }

        // Now despawn them all.
        for (int i = 0; i < qty; i++) {
            PoolMember pm = obs[i].UsingPool;
            pm.myPool.ReturnToPool(obs[i], ref pm.active);
        }
    }

    private static Entity blankPrefab;

    /// <summary>
    /// Attempts to return an entity to a pool, which includes removing it from any scene
    /// </summary>
    /// <param name="e">Entity to try and return to a pool it came from</param>
    /// <returns>True if entity was in a pool to return to</returns>
    static public bool ReturnToPool(Entity e)
    {
        if (e == null || e.UsingPool == null ||
            e.UsingPool.myPool == null || e.UsingPool.active == false)
            return false;

        e.UsingPool.myPool.ReturnToPool(e, ref e.UsingPool.active);

        return true;
    }

    /// <summary>
    /// spawn an empty Entity from a pool
    /// </summary>
    static public Entity SpawnEmpty(Vector3? pos = null, Quaternion? rot = null) {
        if (blankPrefab == null) blankPrefab = new Entity("OriginalEmpty");
        Init(blankPrefab, DEFAULT_POOL_SIZE);
        return pools[blankPrefab].Spawn(pos, rot);
    }

    /// <summary>
    /// Spawns a copy of the specified prefab (instantiating one if required).
    /// </summary>
    static public Entity Spawn(Entity prefab, Vector3? pos = null, Quaternion? rot = null) {
        Init(prefab, DEFAULT_POOL_SIZE);
        return pools[prefab].Spawn(pos, rot);
    }
}
