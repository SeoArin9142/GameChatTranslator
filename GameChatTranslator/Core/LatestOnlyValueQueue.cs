namespace GameTranslator
{
    internal sealed class LatestOnlyValueQueue<T>
    {
        private bool hasPending;
        private T latestValue;

        public bool HasPending => hasPending;

        public void Enqueue(T value)
        {
            latestValue = value;
            hasPending = true;
        }

        public bool TryDequeue(out T value)
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

        public void Clear()
        {
            hasPending = false;
            latestValue = default;
        }
    }
}
