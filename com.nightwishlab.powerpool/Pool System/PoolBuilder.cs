using System;
using System.Runtime.CompilerServices;
using PowerPool.Core;
using UnityEngine;

namespace PowerPool.API
{
    // A builder that forces it to exist only on the stack to prevent GC allocation by blocking.
    public ref struct PoolBuilder<T> where T : Component
    {
        private readonly PowerPoolCore<T> _core;
        private Vector3? _position;
        private Quaternion? _rotation;
        private Transform _parent;
        private Action<T> _onInitialize;
        private object _state;
        private Action<T, object> _onInitializeWithState;
        private bool _activate;

        internal PoolBuilder(PowerPoolCore<T> core)
        {
            _core = core;
            _position = null;
            _rotation = null;
            _parent = null;
            _onInitialize = null;
            _state = null;
            _onInitializeWithState = null;
            _activate = true;
        }

        public PoolBuilder<T> At(Vector3 position)
        {
            _position = position;
            return this;
        }

        public PoolBuilder<T> At(Vector3 position, Quaternion rotation)
        {
            _position = position;
            _rotation = rotation;
            return this;
        }

        public PoolBuilder<T> Under(Transform parent)
        {
            _parent = parent;
            return this;
        }

        public PoolBuilder<T> With(Action<T> onInitialize)
        {
            _onInitialize = onInitialize;
            return this;
        }

        /// <summary>
        /// Advanced path: separate the state object and the initialization callback.
        /// To use without capturing (closures), it is recommended to use static lambdas.
        /// For example: .WithState(ctx, static (inst, s) => inst.Init((MyCtx)s))
        /// </summary>
        public PoolBuilder<T> WithState(object state, Action<T, object> onInitialize)
        {
            _state = state;
            _onInitializeWithState = onInitialize;
            return this;
        }

        public PoolBuilder<T> Inactive()
        {
            _activate = false;
            return this;
        }

        public PoolBuilder<T> Active()
        {
            _activate = true;
            return this;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public PoolHandle<T> Rent()
        {
            var handle = _core.Rent();
            T instance = handle.UnsafeValue; 

            if (_parent != null)
            {
                instance.transform.SetParent(_parent, false);
            }
            
            if (_position.HasValue && _rotation.HasValue)
            {
                instance.transform.SetPositionAndRotation(_position.Value, _rotation.Value);
            }
            else if (_position.HasValue)
            {
                instance.transform.position = _position.Value;
            }

            // Inject external data and state
            _onInitialize?.Invoke(instance);
            _onInitializeWithState?.Invoke(instance, _state);

            // Ensure final activation after all transform operations and data injection are complete
            if (_activate)
            {
                instance.gameObject.SetActive(true);
            }

            return handle;
        }
    }
}