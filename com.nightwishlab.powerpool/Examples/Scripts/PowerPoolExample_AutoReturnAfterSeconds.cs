using System.Collections;
using System.Collections.Generic;
using PowerPool.API;
using PowerPool.Core;
using UnityEngine;
using PowerPoolApi = global::PowerPool.API.PowerPool;

namespace PowerPool.Examples
{
    /// <summary>
    /// - Spawn periodically
    /// - Each Spawn is automatically returned after n seconds
    ///
    /// Points:
    /// - To safely use `PowerPool.Return(ref handle)` in coroutines/delayed return,
    ///   store the handle in the "ticket" object.
    /// </summary>
    public sealed class PowerPoolExample_AutoReturnAfterSeconds : MonoBehaviour
    {
        [Header("Pool Target")]
        [SerializeField] private PowerPoolExampleItem prefab;
        [SerializeField] private Transform poolRoot;

        [Header("Prewarm (optional)")]
        [Min(0)]
        [SerializeField] private int initialCapacity = 32;

        [Header("Spawner")]
        [Min(0f)]
        [SerializeField] private float spawnInterval = 0.1f;
        [Min(0)]
        [SerializeField] private int maxConcurrent = 200;
        [Min(0f)]
        [SerializeField] private float lifeSeconds = 2f;

        [Header("Placement")]
        [SerializeField] private Transform spawnOrigin;
        [SerializeField] private float spawnRadius = 5f;

        private readonly List<Ticket> _tickets = new List<Ticket>(256);
        private Coroutine _loop;

        private void Awake()
        {
            if (prefab == null)
            {
                Debug.LogError("[PowerPool Example] prefab is empty.");
                enabled = false;
                return;
            }

            // Registration can be skipped, but Lazy Init is done in Spawn(), but for this example, it is explicitly prewarmed.
            PowerPoolApi.Register(prefab, initialCapacity, poolRoot);
        }

        private void OnEnable()
        {
            _loop = StartCoroutine(SpawnLoop());
        }

        private void OnDisable()
        {
            if (_loop != null)
            {
                StopCoroutine(_loop);
                _loop = null;
            }

            // Even if it is disabled in the middle, all remaining handles are returned
            for (int i = _tickets.Count - 1; i >= 0; i--)
            {
                var t = _tickets[i];
                if (t != null)
                {
                    t.CancelAndReturn();
                }
            }
            _tickets.Clear();
        }

        private IEnumerator SpawnLoop()
        {
            var wait = spawnInterval > 0f ? new WaitForSeconds(spawnInterval) : null;

            while (enabled)
            {
                if (maxConcurrent <= 0 || _tickets.Count < maxConcurrent)
                {
                    SpawnOne(lifeSeconds);
                }

                if (wait != null) yield return wait;
                else yield return null;
            }
        }

        private void SpawnOne(float life)
        {
            Vector3 origin = spawnOrigin != null ? spawnOrigin.position : transform.position;
            Vector3 pos = origin + Random.insideUnitSphere * (spawnRadius <= 0f ? 0f : spawnRadius);
            pos.y = origin.y;
            Quaternion rot = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);

            var ticket = new Ticket(this);
            ticket.Handle = PowerPoolApi.Spawn(prefab)
                .At(pos, rot)
                .With(static item =>
                {
                    // Example: The initialization when returning is handled in IPoolCleanable, so here is only the minimum processing
                    // (if you want, you can also inject external data like item.Init(...))
                })
                .Rent();

            _tickets.Add(ticket);
            ticket.Routine = StartCoroutine(AutoReturn(ticket, life));
        }

        private IEnumerator AutoReturn(Ticket ticket, float seconds)
        {
            if (seconds > 0f) yield return new WaitForSeconds(seconds);
            else yield return null;

            if (ticket != null)
            {
                ticket.Routine = null;
                ticket.ReturnAndRemove();
            }
        }

        private void RemoveTicket(Ticket ticket)
        {
            int idx = _tickets.IndexOf(ticket);
            if (idx >= 0) _tickets.RemoveAt(idx);
        }

        private sealed class Ticket
        {
            private readonly PowerPoolExample_AutoReturnAfterSeconds _owner;

            public PoolHandle<PowerPoolExampleItem> Handle;
            public Coroutine Routine;

            public Ticket(PowerPoolExample_AutoReturnAfterSeconds owner)
            {
                _owner = owner;
                Handle = default;
                Routine = null;
            }

            public void CancelAndReturn()
            {
                if (Routine != null)
                {
                    _owner.StopCoroutine(Routine);
                    Routine = null;
                }

                // ref return is immediately invalidated on the caller side (=default), making debugging easier.
                PowerPoolApi.Return(ref Handle);
                _owner.RemoveTicket(this);
            }

            public void ReturnAndRemove()
            {
                PowerPoolApi.Return(ref Handle);
                _owner.RemoveTicket(this);
            }
        }
    }
}

