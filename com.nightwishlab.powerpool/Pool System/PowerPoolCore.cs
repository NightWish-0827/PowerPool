using System;
using System.Runtime.CompilerServices;
using PowerPool.API;
using UnityEngine;

namespace PowerPool.Core
{
    public sealed class PowerPoolCore<T> : IPowerPoolCore where T : Component
    {
        private T[] _items;
        private PowerPoolTracker[] _trackers;
        private int _count;
        private readonly T _prefab;
        private readonly Transform _poolRoot;
        private readonly int _prefabKey;
        private readonly string _prefabName;
        private readonly int _maxPoolSize;
        private readonly PowerPoolOverflowPolicy _overflowPolicy;

        private bool _disposed;

        private int _createdCount;
        private int _destroyedInPoolCount;
        private int _unexpectedDestroyedWhileRentedCount;
        private int _destroyedOnOverflowReturnCount;
        private int _rentCalls;
        private int _returnCalls;
        private int _expansions;

        public PowerPoolCore(
            T prefab,
            int initialCapacity,
            Transform poolRoot = null,
            int maxPoolSize = 0,
            PowerPoolOverflowPolicy overflowPolicy = PowerPoolOverflowPolicy.Expand)
        {
            _prefab = prefab;
            _prefabKey = prefab != null ? prefab.gameObject.GetInstanceID() : 0;
            _prefabName = prefab != null ? prefab.name : "<null>";
            _maxPoolSize = maxPoolSize < 0 ? 0 : maxPoolSize;
            _overflowPolicy = overflowPolicy;
            if (initialCapacity < 0) initialCapacity = 0;
            _items = new T[initialCapacity];
            _trackers = new PowerPoolTracker[initialCapacity];
            _count = 0;
            _poolRoot = poolRoot;

            Prewarm(initialCapacity);
        }

        public int PrefabKey => _prefabKey;
        public string PrefabName => _prefabName;
        public Type ItemType => typeof(T);
        public bool IsDisposed => _disposed;

        public PowerPoolStats GetStats() =>
            new PowerPoolStats(
                _prefabKey,
                _prefabName,
                typeof(T),
                _createdCount,
                _count,
                _destroyedInPoolCount,
                _unexpectedDestroyedWhileRentedCount,
                _destroyedOnOverflowReturnCount,
                _rentCalls,
                _returnCalls,
                _items?.Length ?? 0,
                _expansions,
                _maxPoolSize,
                _overflowPolicy,
                _disposed);

        private void Prewarm(int amount)
        {
            for (int i = 0; i < amount; i++)
            {
                T instance = UnityEngine.Object.Instantiate(_prefab, _poolRoot);
                PowerPoolTracker tracker = instance.gameObject.AddComponent<PowerPoolTracker>();
                tracker.IsInPool = true;
                tracker.OnUnexpectedDestroyed = HandleGhostObject;

                // Cache the cleanup targets by traversing the hierarchy at creation time
                tracker.Cleanables = instance.GetComponentsInChildren<IPoolCleanable>(true);

                if (tracker.Cleanables != null)
                {
                    for (int j = 0; j < tracker.Cleanables.Length; j++)
                    {
                        tracker.Cleanables[j].OnReturnToPool();
                    }
                }

                instance.gameObject.SetActive(false);
                
                _items[_count] = instance;
                _trackers[_count] = tracker;
                _count++;
                _createdCount++;
            }
        }

        /// <summary>
        /// Preload additional objects into the pool. (Additional created objects are immediately disabled and entered into the pool)
        /// </summary>
        public void PrewarmAdditional(int amount)
        {
            if (amount <= 0) return;
            if (_disposed) return;

            int target = _count + amount;
            EnsureCapacity(target);
            Prewarm(amount);
        }

        /// <summary>
        /// Ensure the capacity of the internal stack array. (No object creation)
        /// </summary>
        public void EnsureCapacity(int capacity)
        {
            if (capacity <= _items.Length) return;
            if (_disposed) return;
            if (_maxPoolSize > 0 && capacity > _maxPoolSize) capacity = _maxPoolSize;
            if (capacity <= _items.Length) return;

            int newSize = _items.Length == 0 ? 4 : _items.Length;
            while (newSize < capacity) newSize *= 2;

            Array.Resize(ref _items, newSize);
            Array.Resize(ref _trackers, newSize);
            _expansions++;
        }

        public void ClearPooled(bool destroyPooled)
        {
            if (_count <= 0)
            {
                _count = 0;
                return;
            }

            if (destroyPooled)
            {
                for (int i = 0; i < _count; i++)
                {
                    var item = _items[i];
                    if (item != null)
                    {
                        UnityEngine.Object.Destroy(item.gameObject);
                        _destroyedInPoolCount++;
                    }
                    else
                    {
                        _destroyedInPoolCount++;
                    }

                    _items[i] = null;
                    _trackers[i] = null;
                }
            }
            else
            {
                Array.Clear(_items, 0, _count);
                Array.Clear(_trackers, 0, _count);
            }

            _count = 0;
        }

        public void DisposePool(bool destroyPooled)
        {
            if (_disposed) return;
            _disposed = true;
            ClearPooled(destroyPooled);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public PoolHandle<T> Rent()
        {
            if (_disposed)
            {
                throw new InvalidOperationException($"[PowerPool] The pool ({_prefabName}) has already been disposed. Rent is not possible after Unregister/Dispose.");
            }

            while (_count > 0)
            {
                _count--;
                T item = _items[_count];
                PowerPoolTracker tracker = _trackers[_count];

                if (item == null || item.gameObject == null)
                {
                    Debug.LogWarning("[PowerPool] A destroyed object was found in the pool and is being disposed.");
                    _destroyedInPoolCount++;
                    continue; 
                }

                tracker.IsInPool = false;
                tracker.Version++; 
                
                // Note: Here we do not call SetActive(true) and pass it to the Builder.
                _rentCalls++;
                return new PoolHandle<T>(item, tracker, this);
            }

            // When additional allocation is made
            T newInstance = UnityEngine.Object.Instantiate(_prefab, _poolRoot);
            PowerPoolTracker newTracker = newInstance.gameObject.AddComponent<PowerPoolTracker>();
            newTracker.IsInPool = false;
            newTracker.Version = 1; 
            newTracker.OnUnexpectedDestroyed = HandleGhostObject;
            newTracker.Cleanables = newInstance.GetComponentsInChildren<IPoolCleanable>(true);
            
            // Note: The additional allocated object is also passed in a disabled state.
            newInstance.gameObject.SetActive(false);
            _createdCount++;
            _rentCalls++;
            return new PoolHandle<T>(newInstance, newTracker, this);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Return(PoolHandle<T> handle)
        {
            if (_disposed)
            {
                // Objects returned to a disposed pool are safely cleaned up and destroyed.
                if (!handle.IsValid)
                {
                    HandleInvalidReturn(handle);
                    return;
                }

                var trackerDisposed = handle.Tracker;
                if (trackerDisposed?.Cleanables != null)
                {
                    for (int i = 0; i < trackerDisposed.Cleanables.Length; i++)
                    {
                        trackerDisposed.Cleanables[i].OnReturnToPool();
                    }
                }

                var itemDisposed = handle.UnsafeValue;
                if (itemDisposed != null)
                {
                    UnityEngine.Object.Destroy(itemDisposed.gameObject);
                }

                _returnCalls++;
                return;
            }

            if (!handle.IsValid)
            {
                HandleInvalidReturn(handle);
                return;
            }

            T item = handle.UnsafeValue;
            PowerPoolTracker tracker = handle.Tracker; // Remove GetComponent overhead

            // Execute the state initialization logic
            if (tracker.Cleanables != null)
            {
                for (int i = 0; i < tracker.Cleanables.Length; i++)
                {
                    tracker.Cleanables[i].OnReturnToPool();
                }
            }

            tracker.IsInPool = true;
            tracker.Version++; 
            
            item.gameObject.SetActive(false);

            if (_count == _items.Length)
            {
                    // Policy handling when returning from a full pool storage state
                if (_overflowPolicy == PowerPoolOverflowPolicy.DestroyOnReturn)
                {
                    UnityEngine.Object.Destroy(item.gameObject);
                    _destroyedOnOverflowReturnCount++;
                    _returnCalls++;
                    return;
                }

                // Expand: Expand the array when needed (but the maxPoolSize is not exceeded)
                int current = _items.Length;
                int next = current == 0 ? 4 : current * 2;
                if (_maxPoolSize > 0 && next > _maxPoolSize) next = _maxPoolSize;

                // If the maxPoolSize is reached, but the pool is full (=next==current), expansion is not possible → destroy the returned object
                if (next <= current)
                {
                    UnityEngine.Object.Destroy(item.gameObject);
                    _destroyedOnOverflowReturnCount++;
                    _returnCalls++;
                    return;
                }

                Array.Resize(ref _items, next);
                Array.Resize(ref _trackers, next);
                _expansions++;
            }

            _items[_count] = item;
            _trackers[_count] = tracker;
            _count++;
            _returnCalls++;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void HandleInvalidReturn(PoolHandle<T> handle)
        {
            PowerPoolTracker tracker = handle.Tracker;

            string reason;
            if (tracker == null)
            {
                reason = "Tracker not found (default handle or external manipulation)";
            }
            else if (tracker.IsInPool)
            {
                reason = "Double return (already in the pool)";
            }
            else if (tracker.Version != handle.IssuedVersion)
            {
                reason = $"Zombie/Stale handle (HandleVersion={handle.IssuedVersion}, CurrentVersion={tracker.Version})";
            }
            else
            {
                reason = "Validation failed (unknown state)";
            }

            string msg = $"[PowerPool] Invalid or already returned handle. ({reason})";

            switch (PowerPoolSettings.InvalidReturnPolicy)
            {
                case InvalidHandlePolicy.Silent:
                    return;
                case InvalidHandlePolicy.Warn:
                    Debug.LogWarning(msg);
                    return;
                case InvalidHandlePolicy.Error:
                    Debug.LogError(msg);
                    return;
                case InvalidHandlePolicy.Throw:
                    throw new InvalidOperationException(msg);
            }
        }

        private void HandleGhostObject(PowerPoolTracker tracker)
        {
            if (PowerPoolSettings.LogUnexpectedDestroyed)
            {
                Debug.LogWarning("[PowerPool] An object was forcibly destroyed without going through the pool. Be careful about memory leaks/reference leaks.");
            }
            _unexpectedDestroyedWhileRentedCount++;
        }
    }
}