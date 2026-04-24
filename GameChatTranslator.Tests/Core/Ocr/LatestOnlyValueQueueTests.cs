using GameTranslator;
using Xunit;

namespace GameChatTranslator.Tests
{
    public class LatestOnlyValueQueueTests
    {
        [Fact]
        public void Enqueue_MultipleValues_KeepsLatestOnly()
        {
            var queue = new LatestOnlyValueQueue<int>();

            queue.Enqueue(1);
            queue.Enqueue(2);
            queue.Enqueue(3);

            Assert.True(queue.TryDequeue(out int value));
            Assert.Equal(3, value);
            Assert.False(queue.HasPending);
        }

        [Fact]
        public void TryDequeue_EmptyQueue_ReturnsFalse()
        {
            var queue = new LatestOnlyValueQueue<string>();

            Assert.False(queue.TryDequeue(out string value));
            Assert.Null(value);
        }

        [Fact]
        public void Clear_RemovesPendingValue()
        {
            var queue = new LatestOnlyValueQueue<string>();
            queue.Enqueue("pending");

            queue.Clear();

            Assert.False(queue.HasPending);
            Assert.False(queue.TryDequeue(out string value));
            Assert.Null(value);
        }
    }
}
