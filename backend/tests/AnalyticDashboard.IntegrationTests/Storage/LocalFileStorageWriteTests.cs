using System.Text;

using AnalyticDashboard.Infrastructure.Storage;

namespace AnalyticDashboard.IntegrationTests.Storage;

public sealed class LocalFileStorageWriteTests
{
    [Fact]
    public async Task Save_ShouldPreserveExistingFile_WhenCreateNewFails()
    {
        var storage = new LocalFileStorage();
        var directory = $"tests/storage-writes/{Guid.NewGuid():N}";
        var key = $"{directory}/existing.parquet";
        var original = "published artifact"u8.ToArray();

        try
        {
            await using var originalStream = new MemoryStream(original);
            await storage.SaveAsync(
                key,
                originalStream,
                TestContext.Current.CancellationToken
            );
            await using var replacement = new MemoryStream("replacement"u8.ToArray());

            await Assert.ThrowsAsync<IOException>(() => storage.SaveAsync(
                key,
                replacement,
                TestContext.Current.CancellationToken
            ));

            await using var saved = await storage.OpenReadAsync(
                key,
                TestContext.Current.CancellationToken
            );
            using var contents = new MemoryStream();
            await saved.CopyToAsync(
                contents,
                TestContext.Current.CancellationToken
            );
            Assert.Equal(original, contents.ToArray());
        }
        finally
        {
            await storage.DeleteDirectoryAsync(directory, CancellationToken.None);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Save_ShouldRemoveOnlyItsPartialFile_WhenCopyFailsOrIsCancelled(bool cancel)
    {
        var storage = new LocalFileStorage();
        var directory = $"tests/storage-writes/{Guid.NewGuid():N}";
        var partialKey = $"{directory}/partial.parquet";
        var publishedKey = $"{directory}/published.parquet";

        try
        {
            await using var published = new MemoryStream("published"u8.ToArray());
            await storage.SaveAsync(
                publishedKey,
                published,
                TestContext.Current.CancellationToken
            );
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(
                TestContext.Current.CancellationToken
            );
            await using var source = new FailingCopyStream(cancel ? cancellation : null);

            var exception = await Record.ExceptionAsync(() => storage.SaveAsync(
                partialKey,
                source,
                cancellation.Token
            ));

            if (cancel)
            {
                Assert.IsType<OperationCanceledException>(exception);
            }
            else
            {
                Assert.IsType<IOException>(exception);
            }

            Assert.True(source.PartialWriteCompleted);
            Assert.False(await storage.ExistsAsync(
                partialKey,
                TestContext.Current.CancellationToken
            ));

            await using var saved = await storage.OpenReadAsync(
                publishedKey,
                TestContext.Current.CancellationToken
            );
            using var reader = new StreamReader(saved, Encoding.UTF8);
            Assert.Equal(
                "published",
                await reader.ReadToEndAsync(TestContext.Current.CancellationToken)
            );
        }
        finally
        {
            await storage.DeleteDirectoryAsync(directory, CancellationToken.None);
        }
    }

    private sealed class FailingCopyStream : Stream
    {
        private readonly CancellationTokenSource? _cancellation;

        public FailingCopyStream(CancellationTokenSource? cancellation)
        {
            _cancellation = cancellation;
        }

        public bool PartialWriteCompleted { get; private set; }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override async Task CopyToAsync(
            Stream destination,
            int bufferSize,
            CancellationToken cancellationToken)
        {
            await destination.WriteAsync("partial data"u8.ToArray(), cancellationToken);
            PartialWriteCompleted = true;

            if (_cancellation is not null)
            {
                _cancellation.Cancel();
                throw new OperationCanceledException(_cancellation.Token);
            }

            throw new IOException("Simulated source read failure after writing a partial file.");
        }

        public override void Flush() => throw new NotSupportedException();

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
