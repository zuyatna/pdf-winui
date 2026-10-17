using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PdfReader.Core.Tests;

[TestClass]
public sealed class PdfiumWorkerTests
{
    [TestMethod]
    public async Task RunAsync_WithPreCanceledToken_ThrowsOperationCanceledException()
    {
        // Arrange
        await using var worker = new PdfiumWorker();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        bool executed = false;

        // Act & Assert
        await Assert.ThrowsExactlyAsync<OperationCanceledException>(async () =>
        {
            await worker.RunAsync(() =>
            {
                executed = true;
                return 1;
            }, cts.Token);
        });

        Assert.IsFalse(executed);
    }

    [TestMethod]
    public async Task RunAsync_WhenCapacityReached_AppliesBackpressureUntilSlotFrees()
    {
        // Arrange
        await using var worker = new PdfiumWorker(capacity: 2);
        using var firstJobStarted = new ManualResetEventSlim(false);
        using var releaseFirstJob = new ManualResetEventSlim(false);
        using var thirdJobEnqueued = new ManualResetEventSlim(false);

        // Act: occupy slot 1 (running on worker thread) and slot 2 (queued)
        Task<int> firstTask = worker.RunAsync(() =>
        {
            firstJobStarted.Set();
            releaseFirstJob.Wait(TimeSpan.FromSeconds(5));
            return 1;
        });

        Assert.IsTrue(firstJobStarted.Wait(TimeSpan.FromSeconds(5)));

        Task<int> secondTask = worker.RunAsync(() => 2);

        Task<int> thirdTask = Task.Run(() =>
        {
            Task<int> task = worker.RunAsync(() => 3);
            thirdJobEnqueued.Set();
            return task;
        });

        // Assert: third submission is blocked by capacity backpressure while first job is held
        Assert.IsFalse(thirdJobEnqueued.Wait(TimeSpan.FromMilliseconds(100)));

        releaseFirstJob.Set();

        Assert.IsTrue(thirdJobEnqueued.Wait(TimeSpan.FromSeconds(5)));

        int[] results = await Task.WhenAll(firstTask, secondTask, thirdTask);
        CollectionAssert.AreEqual(new[] { 1, 2, 3 }, results);
    }

    [TestMethod]
    public async Task RunAsync_AfterDisposeAsync_ThrowsObjectDisposedException()
    {
        // Arrange
        var worker = new PdfiumWorker();
        await worker.DisposeAsync();

        // Act & Assert
        await Assert.ThrowsExactlyAsync<ObjectDisposedException>(async () =>
        {
            await worker.RunAsync(() => 1);
        });
    }
}
