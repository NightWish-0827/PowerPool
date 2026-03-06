using System.Collections.Generic;
using PowerPool.API;
using PowerPool.Core;
using UnityEngine;
using PowerPoolApi = global::PowerPool.API.PowerPool;

namespace PowerPool.Examples
{
    /// <summary>
    /// - manually register/prewarm
    /// - rent with Spawn(Builder)
    /// - Save PoolHandle and return manually after that
    /// - check stats
    ///
    /// Usage:
    /// - Create an empty object in the scene and attach this component to it.
    /// - Assign the prefab with PowerPoolExampleItem to the `prefab` field.
    /// - Play and:
    ///   - Space: Spawn
    ///   - Backspace: Return the last one
    ///   - R: Return all
    ///   - S: Check stats
    /// </summary>
    public sealed class PowerPoolExample_PrewarmAndManualReturn : MonoBehaviour
    {
        [Header("Pool Target")]
        [SerializeField] private PowerPoolExampleItem prefab;
        [SerializeField] private Transform poolRoot;

        [Header("Config")]
        [Min(0)]
        [SerializeField] private int initialCapacity = 16;
        [Min(0)]
        [SerializeField] private int maxPoolSize = 0; // 0 or less = unlimited
        [SerializeField] private PowerPoolOverflowPolicy overflowPolicy = PowerPoolOverflowPolicy.Expand;

        [Header("Spawn")]
        [SerializeField] private Transform spawnOrigin;
        [SerializeField] private float spawnRadius = 1.5f;
        [SerializeField] private float impulse = 6f;

        private readonly List<PoolHandle<PowerPoolExampleItem>> _active = new List<PoolHandle<PowerPoolExampleItem>>(128);

        private void Awake()
        {
            if (prefab == null)
            {
                Debug.LogError("[PowerPool Example] prefab is empty.");
                enabled = false;
                return;
            }

            PowerPoolApi.Register(prefab, initialCapacity, poolRoot, maxPoolSize, overflowPolicy);
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.Space))
            {
                SpawnOne();
            }

            if (Input.GetKeyDown(KeyCode.Backspace))
            {
                ReturnLast();
            }

            if (Input.GetKeyDown(KeyCode.R))
            {
                ReturnAll();
            }

            if (Input.GetKeyDown(KeyCode.S))
            {
                LogStats();
            }
        }

        private void OnDisable()
        {
            ReturnAll();
        }

        private void SpawnOne()
        {
            Vector3 origin = spawnOrigin != null ? spawnOrigin.position : transform.position;
            Vector3 offset = Random.insideUnitSphere;
            offset.y = 0f;
            offset = offset.normalized * (spawnRadius <= 0f ? 0f : Random.Range(0f, spawnRadius));

            Vector3 pos = origin + offset;
            Quaternion rot = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
            Color color = Color.HSVToRGB(Random.value, 0.8f, 1f);
            Vector3 impulseVec = rot * Vector3.forward * impulse;

            // ref struct Builder is not saveable/fieldable → Rent() immediately after chaining
            var handle = PowerPoolApi.Spawn(prefab)
                .At(pos, rot)
                .Under(null)
                .WithState(
                    state: new SpawnState(color, impulseVec),
                    onInitialize: static (item, s) =>
                    {
                        var st = (SpawnState)s;
                        item.Init(st.Color, st.Impulse);
                    })
                .Rent();

            _active.Add(handle);
        }

        private void ReturnLast()
        {
            int last = _active.Count - 1;
            if (last < 0) return;

            var handle = _active[last];
            _active.RemoveAt(last);

            // ref return is the safest in debug to prevent zombie/double return
            PowerPoolApi.Return(ref handle);
        }

        private void ReturnAll()
        {
            for (int i = _active.Count - 1; i >= 0; i--)
            {
                var handle = _active[i];
                PowerPoolApi.Return(ref handle);
            }
            _active.Clear();
        }

        private void LogStats()
        {
            if (!PowerPoolApi.TryGetStats(prefab, out var stats))
            {
                Debug.Log("[PowerPool Example] No pool stats yet.");
                return;
            }

            Debug.Log(
                $"[PowerPool Example] {stats.PrefabName}<{stats.ItemTypeName}> " +
                $"Created={stats.TotalCreated}, InPool={stats.InPool}, Active~={stats.ActiveEstimated}, " +
                $"Rent={stats.RentCalls}, Return={stats.ReturnCalls}, Capacity={stats.Capacity}, Expansions={stats.Expansions}, " +
                $"Overflow={stats.OverflowPolicy}, Max={stats.MaxPoolSize}, Disposed={stats.IsDisposed}");
        }

        private readonly struct SpawnState
        {
            public readonly Color Color;
            public readonly Vector3 Impulse;

            public SpawnState(Color color, Vector3 impulse)
            {
                Color = color;
                Impulse = impulse;
            }
        }
    }
}
