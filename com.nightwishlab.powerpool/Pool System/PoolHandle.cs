using System;
using System.Runtime.CompilerServices;
using PowerPool.API;
using UnityEngine;

namespace PowerPool.Core
{
    /// <summary>
    /// A zero-allocation handle that prevents zombie references and supports O(1) return.
    /// </summary>
    public readonly struct PoolHandle<T> : IDisposable, IEquatable<PoolHandle<T>> where T : Component
    {
        private readonly T _instance;
        private readonly int _version;
        
        // Expose the tracker to allow immediate access without GetComponent from the core
        internal readonly PowerPoolTracker Tracker;
        
        // Expose the source pool reference to allow immediate return without dictionary lookup
        internal readonly PowerPoolCore<T> SourcePool;

        internal PoolHandle(T instance, PowerPoolTracker tracker, PowerPoolCore<T> sourcePool)
        {
            _instance = instance;
            Tracker = tracker;
            _version = tracker.Version;
            SourcePool = sourcePool;
        }

        internal int IssuedVersion
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _version;
        }

        public bool IsValid
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => Tracker != null && !Tracker.IsInPool && Tracker.Version == _version;
        }

        public T Value
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                if (!IsValid)
                {
                    throw new InvalidOperationException(
                        $"[PowerPool] Zombie reference detected! {_instance.name} has already been returned or manipulated.");
                }
                return _instance;
            }
        }

        public T UnsafeValue
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _instance;
        }

        /// <summary>
        /// Return the object to the source pool immediately via the handle.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Dispose()
        {
            // Dispose is closer to "return attempt". The validity/policy judgment is handled by SourcePool.
            if (SourcePool != null)
            {
                SourcePool.Return(this);
                return;
            }

            switch (PowerPoolSettings.InvalidReturnPolicy)
            {
                case InvalidHandlePolicy.Silent:
                    return;
                case InvalidHandlePolicy.Warn:
                    Debug.LogWarning("[PowerPool] The handle has no default/source pool information. (return ignored)");
                    return;
                case InvalidHandlePolicy.Error:
                    Debug.LogError("[PowerPool] The handle has no default/source pool information. (return ignored)");
                    return;
                case InvalidHandlePolicy.Throw:
                    throw new InvalidOperationException("[PowerPool] The handle has no default/source pool information.");
            }
        }

        /// <summary>
        /// A more explicit alias for Dispose(). It works the same as Dispose() but has a more explicit name.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Return() => Dispose();

        public bool Equals(PoolHandle<T> other) => 
            _instance == other._instance && _version == other._version;
    }
}