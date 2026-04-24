namespace GameTranslator
{
    internal sealed class LatestOnlyValueQueue<T>
    {
        private readonly object sync = new object();
        private bool hasPending;
        private T latestValue;

        public bool HasPending
        {
            get
            {
                lock (sync)
                {
                    return hasPending;
                }
            }
        }

        public void Enqueue(T value)
        {
            lock (sync)
            {
                latestValue = value;
                hasPending = true;
            }
        }

        public bool TryDequeue(out T value)
        {
            lock (sync)
            {
                if (hasPending)
                {
                    value = latestValue;
                    hasPending = false;
                    latestValue = default;
                    return true;
                }

                value = default;
                return false;
            }
        }

        public void Clear()
        {
            lock (sync)
            {
                hasPending = false;
                latestValue = default;
            }
        }
    }
}
