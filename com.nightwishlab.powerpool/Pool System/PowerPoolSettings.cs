namespace PowerPool.API
{
    public enum PowerPoolOverflowPolicy
    {
        Expand = 0,
        DestroyOnReturn = 1,
    }

    public enum InvalidHandlePolicy
    {
        Silent = 0,
        Warn = 1,
        Error = 2,
        Throw = 3,
    }

    /// <summary>
    /// Control the behavior policy of PowerPool (Debug/Release).
    /// </summary>
    public static class PowerPoolSettings
    {
        /// <summary>
        /// Register/Spawn when there is no separate specification, the initial capacity of the Lazy created pool.
        /// </summary>
        public static int DefaultLazyInitialCapacity = 10;

        /// <summary>
        /// The maximum number that can be stored inside the pool. 0 or less means unlimited.
        /// </summary>
        public static int DefaultMaxPoolSize = 0;

        /// <summary>
        /// Policy for handling when returning from a full pool storage state.
        /// </summary>
        public static PowerPoolOverflowPolicy DefaultOverflowPolicy = PowerPoolOverflowPolicy.Expand;

        /// <summary>
        /// (Debug/Editor recommended) Force/guide the use of ref overload when returning the handle.
        /// When true, PowerPool.Return(handle) calls are processed according to the policy as Warn/Error/Throw.
        /// </summary>
        public static bool RequireRefReturnInDebug = false;

        /// <summary>
        /// Policy for invalid handle return (double return/zombie/default handle, etc.).
        /// </summary>
        public static InvalidHandlePolicy InvalidReturnPolicy = InvalidHandlePolicy.Error;

        /// <summary>
        /// Whether to print a warning for objects destroyed without going through the pool (destroyed while rented).
        /// </summary>
        public static bool LogUnexpectedDestroyed = true;
    }
}
