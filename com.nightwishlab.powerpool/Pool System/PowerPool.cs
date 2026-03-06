using System.Collections.Generic;
using UnityEngine;
using PowerPool.Core;

namespace PowerPool.API
{
    public static class PowerPool
    {
        // Use the InstanceID of the prefab as the key to perfectly support the separation of the same type and different prefabs
        private static readonly Dictionary<int, IPowerPoolCore> _pools = new Dictionary<int, IPowerPoolCore>();

        private static PowerPoolCore<T> GetCoreOrThrow<T>(int key, IPowerPoolCore core) where T : Component
        {
            if (core is PowerPoolCore<T> typed) return typed;
            throw new System.InvalidOperationException(
                $"[PowerPool] A different type of pool access was detected for the same prefab ({core.PrefabName}). " +
                $"(ExistingType={core.ItemType?.Name}, RequestedType={typeof(T).Name})");
        }

        /// <summary>
        /// Register or prewarm the pool manually.
        /// </summary>
        public static void Register<T>(T prefab, int initialCapacity, Transform poolRoot = null) where T : Component
        {
            int key = prefab.gameObject.GetInstanceID();
            if (!_pools.ContainsKey(key))
            {
                var core = new PowerPoolCore<T>(
                    prefab,
                    initialCapacity,
                    poolRoot,
                    PowerPoolSettings.DefaultMaxPoolSize,
                    PowerPoolSettings.DefaultOverflowPolicy);
                _pools.Add(key, core);
            }
            else
            {
                Debug.LogWarning($"[PowerPool] The pool for the prefab ({prefab.name}) is already registered.");
            }
        }

        public static void Register<T>(
            T prefab,
            int initialCapacity,
            Transform poolRoot,
            int maxPoolSize,
            PowerPoolOverflowPolicy overflowPolicy = PowerPoolOverflowPolicy.Expand) where T : Component
        {
            int key = prefab.gameObject.GetInstanceID();
            if (!_pools.ContainsKey(key))
            {
                var core = new PowerPoolCore<T>(prefab, initialCapacity, poolRoot, maxPoolSize, overflowPolicy);
                _pools.Add(key, core);
            }
            else
            {
                Debug.LogWarning($"[PowerPool] The pool for the prefab ({prefab.name}) is already registered.");
            }
        }

        /// <summary>
        /// The entry point for the Fluent Builder.
        /// </summary>
        public static PoolBuilder<T> Spawn<T>(T prefab) where T : Component
        {
            int key = prefab.gameObject.GetInstanceID();
            
            if (!_pools.TryGetValue(key, out var coreObj))
            {
                Debug.Log($"[PowerPool] The pool for the prefab ({prefab.name}) is not registered, so it is automatically created (Lazy Init)");
                var newCore = new PowerPoolCore<T>(
                    prefab,
                    PowerPoolSettings.DefaultLazyInitialCapacity,
                    null,
                    PowerPoolSettings.DefaultMaxPoolSize,
                    PowerPoolSettings.DefaultOverflowPolicy);
                _pools.Add(key, newCore);
                return new PoolBuilder<T>(newCore);
            }

            return new PoolBuilder<T>(GetCoreOrThrow<T>(key, coreObj));
        }

        /// <summary>
        /// Explicit return (Return) API.
        /// Internally calls Handle.Dispose() to return the object to the pool via O(1) search.
        /// </summary>
        public static void Return<T>(PoolHandle<T> handle) where T : Component
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (PowerPoolSettings.RequireRefReturnInDebug)
            {
                const string msg = "[PowerPool] (Debug) Return is recommended/forced to use PowerPool.Return(ref handle). " +
                                   "Value copy handles remain in the caller scope, making double return/misuse debugging difficult.";

                switch (PowerPoolSettings.InvalidReturnPolicy)
                {
                    case InvalidHandlePolicy.Silent:
                        break;
                    case InvalidHandlePolicy.Warn:
                        Debug.LogWarning(msg);
                        break;
                    case InvalidHandlePolicy.Error:
                        Debug.LogError(msg);
                        break;
                    case InvalidHandlePolicy.Throw:
                        throw new System.InvalidOperationException(msg);
                }
            }
#endif
            handle.Dispose();
        }

        /// <summary>
        /// Immediately invalidate the handle on the caller side after return (default). (useful for zombie/double return debugging)
        /// </summary>
        public static void Return<T>(ref PoolHandle<T> handle) where T : Component
        {
            handle.Dispose();
            handle = default;
        }

        public static bool TryGetStats<T>(T prefab, out PowerPoolStats stats) where T : Component
        {
            int key = prefab.gameObject.GetInstanceID();
            if (_pools.TryGetValue(key, out var core))
            {
                stats = core.GetStats();
                return true;
            }

            stats = default;
            return false;
        }

        public static PowerPoolStats GetStats<T>(T prefab) where T : Component
        {
            int key = prefab.gameObject.GetInstanceID();
            if (_pools.TryGetValue(key, out var core))
            {
                return core.GetStats();
            }

            throw new System.InvalidOperationException($"[PowerPool] The pool for the prefab ({prefab.name}) does not exist.");
        }

        public static void Clear<T>(T prefab, bool destroyPooled = true) where T : Component
        {
            int key = prefab.gameObject.GetInstanceID();
            if (_pools.TryGetValue(key, out var core))
            {
                core.ClearPooled(destroyPooled);
            }
        }

        public static void ClearAll(bool destroyPooled = true)
        {
            foreach (var kv in _pools)
            {
                kv.Value.ClearPooled(destroyPooled);
            }
        }

        /// <summary>
        /// Global dispose API. Dispose all pools and remove them from the registry.
        /// Use this when developers want to explicitly dispose the pool memory in scene transitions, etc.
        /// </summary>
        public static void DisposeAll(bool destroyPooled = true)
        {
            UnregisterAll(disposePools: true, destroyPooled: destroyPooled);
        }

#if UNITY_EDITOR
        /// <summary>
        /// For editor trackers: get the Stats of all registered pools as a snapshot.
        /// (Minimize allocation by reusing List)
        /// </summary>
        public static void GetAllStats(List<PowerPoolStats> buffer)
        {
            buffer.Clear();
            if (buffer.Capacity < _pools.Count) buffer.Capacity = _pools.Count;

            foreach (var kv in _pools)
            {
                buffer.Add(kv.Value.GetStats());
            }
        }

        public static int RegisteredPoolCount => _pools.Count;

        public static bool TryClearByKey(int prefabKey, bool destroyPooled = true)
        {
            if (_pools.TryGetValue(prefabKey, out var core))
            {
                core.ClearPooled(destroyPooled);
                return true;
            }
            return false;
        }

        public static bool TryDisposeByKey(int prefabKey, bool destroyPooled = true)
        {
            if (_pools.TryGetValue(prefabKey, out var core))
            {
                core.DisposePool(destroyPooled);
                _pools.Remove(prefabKey);
                return true;
            }
            return false;
        }
#endif

        public static bool Unregister<T>(T prefab, bool disposePool = false, bool destroyPooled = true) where T : Component
        {
            int key = prefab.gameObject.GetInstanceID();
            if (_pools.TryGetValue(key, out var core))
            {
                _pools.Remove(key);
                if (disposePool)
                {
                    core.DisposePool(destroyPooled);
                }
                return true;
            }
            return false;
        }

        public static void UnregisterAll(bool disposePools = false, bool destroyPooled = true)
        {
            if (!disposePools)
            {
                _pools.Clear();
                return;
            }

            foreach (var kv in _pools)
            {
                kv.Value.DisposePool(destroyPooled);
            }
            _pools.Clear();
        }

        public static void EnsureCapacity<T>(T prefab, int capacity) where T : Component
        {
            int key = prefab.gameObject.GetInstanceID();
            if (!_pools.TryGetValue(key, out var coreObj))
            {
                var newCore = new PowerPoolCore<T>(
                    prefab,
                    0,
                    null,
                    PowerPoolSettings.DefaultMaxPoolSize,
                    PowerPoolSettings.DefaultOverflowPolicy);
                _pools.Add(key, newCore);
                newCore.EnsureCapacity(capacity);
                return;
            }

            GetCoreOrThrow<T>(key, coreObj).EnsureCapacity(capacity);
        }

        public static void Prewarm<T>(T prefab, int additional) where T : Component
        {
            int key = prefab.gameObject.GetInstanceID();
            if (!_pools.TryGetValue(key, out var coreObj))
            {
                var newCore = new PowerPoolCore<T>(
                    prefab,
                    0,
                    null,
                    PowerPoolSettings.DefaultMaxPoolSize,
                    PowerPoolSettings.DefaultOverflowPolicy);
                _pools.Add(key, newCore);
                newCore.PrewarmAdditional(additional);
                return;
            }

            GetCoreOrThrow<T>(key, coreObj).PrewarmAdditional(additional);
        }
    }
}