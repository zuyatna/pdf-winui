using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PdfReader.Core.Tests;

[TestClass]
public sealed class WorkerLearningTests
{
    [TestMethod]
    public async Task RunAsync_ReturnsComputedValue()
    {
        // Arrange
        await using var worker = new PdfiumWorker();

        // Act
        int result = await worker.RunAsync(() => 21 * 2);

        // Assert
        Assert.AreEqual(42, result);
    }

    [TestMethod]
    public async Task RunAsync_ExecutesAllWorkOnSingleDedicatedThread()
    {
        // Arrange
        await using var worker = new PdfiumWorker();
        int callerThreadId = Environment.CurrentManagedThreadId;

        // Act
        int firstWorkerThreadId = await worker.RunAsync(() => Environment.CurrentManagedThreadId);
        int secondWorkerThreadId = await worker.RunAsync(() => Environment.CurrentManagedThreadId);

        // Assert
        Assert.AreNotEqual(callerThreadId, firstWorkerThreadId);
        Assert.AreEqual(firstWorkerThreadId, secondWorkerThreadId);
    }
}
