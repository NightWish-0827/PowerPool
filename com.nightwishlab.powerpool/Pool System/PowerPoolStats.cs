using System;

namespace PowerPool.API
{
    public readonly struct PowerPoolStats
    {
        public readonly int PrefabKey;
        public readonly string PrefabName;
        public readonly string ItemTypeName;

        public readonly int TotalCreated;
        public readonly int InPool;
        public readonly int ActiveEstimated;

        public readonly int DestroyedInPool;
        public readonly int UnexpectedDestroyedWhileRented;
        public readonly int DestroyedOnOverflowReturn;

        public readonly int RentCalls;
        public readonly int ReturnCalls;

        public readonly int Capacity;
        public readonly int Expansions;

        public readonly int MaxPoolSize;
        public readonly PowerPoolOverflowPolicy OverflowPolicy;

        public readonly bool IsDisposed;

        internal PowerPoolStats(
            int prefabKey,
            string prefabName,
            Type itemType,
            int totalCreated,
            int inPool,
            int destroyedInPool,
            int unexpectedDestroyedWhileRented,
            int destroyedOnOverflowReturn,
            int rentCalls,
            int returnCalls,
            int capacity,
            int expansions,
            int maxPoolSize,
            PowerPoolOverflowPolicy overflowPolicy,
            bool isDisposed)
        {
            PrefabKey = prefabKey;
            PrefabName = prefabName;
            ItemTypeName = itemType?.Name ?? "<unknown>";

            TotalCreated = totalCreated;
            InPool = inPool;

            int alive = totalCreated - destroyedInPool - unexpectedDestroyedWhileRented;
            if (alive < 0) alive = 0;

            int active = alive - inPool;
            ActiveEstimated = active < 0 ? 0 : active;

            DestroyedInPool = destroyedInPool;
            UnexpectedDestroyedWhileRented = unexpectedDestroyedWhileRented;
            DestroyedOnOverflowReturn = destroyedOnOverflowReturn;

            RentCalls = rentCalls;
            ReturnCalls = returnCalls;

            Capacity = capacity;
            Expansions = expansions;

            MaxPoolSize = maxPoolSize;
            OverflowPolicy = overflowPolicy;

            IsDisposed = isDisposed;
        }
    }
}
