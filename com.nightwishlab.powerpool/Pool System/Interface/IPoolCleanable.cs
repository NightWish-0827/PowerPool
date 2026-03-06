namespace PowerPool.API
{
    /// <summary>
    /// Objects returned to the pool should be cleaned up.
    /// </summary>
    public interface IPoolCleanable
    {
        void OnReturnToPool();
    }
}