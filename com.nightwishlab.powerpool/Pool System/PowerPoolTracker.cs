using System;
using PowerPool.API;
using UnityEngine;

namespace PowerPool.Core
{
    [DisallowMultipleComponent]
    [AddComponentMenu("")] // Prevent users from manually adding it in the editor
    internal sealed class PowerPoolTracker : MonoBehaviour
    {
        internal bool IsInPool { get; set; } = false;
        
        // Generation (Generation). For zombie reference prevention
        internal int Version { get; set; } = 0; 
        
        // Cleanables cached once when an object is created
        internal IPoolCleanable[] Cleanables { get; set; }
        
        internal Action<PowerPoolTracker> OnUnexpectedDestroyed;

        private void OnDestroy()
        {
            // Warning handling when an object is destroyed due to scene transition or other reasons without being returned to the pool
            if (!IsInPool)
            {
                OnUnexpectedDestroyed?.Invoke(this);
            }
        }
    }
}