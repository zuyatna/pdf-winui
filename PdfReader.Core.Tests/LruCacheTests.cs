using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace PdfReader.Core.Tests;

[TestClass]
public sealed class LruCacheTests
{
    private sealed class DisposableEntry(string name) : IDisposable
    {
        public string Name { get; } = name;

        public bool IsDisposed { get; private set; }

        public void Dispose()
        {
            IsDisposed = true;
        }
    }

    [TestMethod]
    public void TryGetValue_ReturnsStoredItem_AndReturnsFalseOnMiss()
    {
        // Arrange
        using var cache = new LruCache<int, DisposableEntry>(maxBytes: 100);
        var entry = new DisposableEntry("page-0");

        // Act
        cache.Set(0, entry, byteSize: 40);
        bool hit = cache.TryGetValue(0, out DisposableEntry? found);
        bool miss = cache.TryGetValue(99, out DisposableEntry? missing);

        // Assert
        Assert.IsTrue(hit);
        Assert.AreSame(entry, found);
        Assert.IsFalse(miss);
        Assert.IsNull(missing);
        Assert.AreEqual(40, cache.CurrentBytes);
    }

    [TestMethod]
    public void Set_WhenExceedingByteBudget_EvictsLeastRecentlyUsedAndDisposesIt()
    {
        // Arrange
        using var cache = new LruCache<int, DisposableEntry>(maxBytes: 100);
        var first = new DisposableEntry("page-1");
        var second = new DisposableEntry("page-2");
        var third = new DisposableEntry("page-3");

        // Act
        cache.Set(1, first, byteSize: 40);
        cache.Set(2, second, byteSize: 40);
        cache.Set(3, third, byteSize: 40);

        // Assert
        Assert.IsTrue(first.IsDisposed, "Evicted oldest entry should be disposed.");
        Assert.IsFalse(second.IsDisposed);
        Assert.IsFalse(third.IsDisposed);
        Assert.IsFalse(cache.TryGetValue(1, out _));
        Assert.IsTrue(cache.TryGetValue(2, out _));
        Assert.IsTrue(cache.TryGetValue(3, out _));
        Assert.AreEqual(80, cache.CurrentBytes);
    }

    [TestMethod]
    public void TryGetValue_PromotesAccessedItem_SoLeastRecentlyUsedIsEvictedFirst()
    {
        // Arrange
        using var cache = new LruCache<int, DisposableEntry>(maxBytes: 100);
        var first = new DisposableEntry("page-1");
        var second = new DisposableEntry("page-2");
        var third = new DisposableEntry("page-3");

        cache.Set(1, first, byteSize: 40);
        cache.Set(2, second, byteSize: 40);

        // Act: touch key 1 so key 2 becomes least recently used
        Assert.IsTrue(cache.TryGetValue(1, out _));
        cache.Set(3, third, byteSize: 40);

        // Assert
        Assert.IsFalse(first.IsDisposed, "Recently accessed entry should stay cached.");
        Assert.IsTrue(second.IsDisposed, "Least recently used entry should be evicted and disposed.");
        Assert.IsTrue(cache.TryGetValue(1, out _));
        Assert.IsFalse(cache.TryGetValue(2, out _));
        Assert.IsTrue(cache.TryGetValue(3, out _));
    }

    [TestMethod]
    public void Clear_RemovesAllEntries_ResetsCurrentBytes_AndDisposesAllItems()
    {
        // Arrange
        using var cache = new LruCache<int, DisposableEntry>(maxBytes: 100);
        var first = new DisposableEntry("page-1");
        var second = new DisposableEntry("page-2");
        cache.Set(1, first, byteSize: 30);
        cache.Set(2, second, byteSize: 50);

        // Act
        cache.Clear();

        // Assert
        Assert.AreEqual(0, cache.Count);
        Assert.AreEqual(0, cache.CurrentBytes);
        Assert.IsTrue(first.IsDisposed);
        Assert.IsTrue(second.IsDisposed);
    }
}
