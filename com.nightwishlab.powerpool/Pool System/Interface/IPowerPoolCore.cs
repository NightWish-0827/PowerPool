using System;

namespace PowerPool.API
{
    internal interface IPowerPoolCore
    {
        int PrefabKey { get; }
        string PrefabName { get; }
        Type ItemType { get; }

        bool IsDisposed { get; }

        PowerPoolStats GetStats();

        void ClearPooled(bool destroyPooled);
        void DisposePool(bool destroyPooled);

        void EnsureCapacity(int capacity);
        void PrewarmAdditional(int amount);
    }
}
