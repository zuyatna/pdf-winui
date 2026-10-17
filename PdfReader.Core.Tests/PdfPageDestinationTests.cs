namespace PdfReader.Core.Tests;

[TestClass]
public sealed class PdfPageDestinationTests
{
    [TestMethod]
    [DataRow(792.0, 0.5, 1000.0 + 250.0 + 396.0 - 24.0)]
    [DataRow(1584.0, 0.5, 1000.0 + 250.0 + 792.0 - 24.0)]
    public void VerticalOffsetUsesActualPagePositionAndCurrentZoom(double pageHeight, double y, double expected)
    {
        var destination = new PdfPageDestination(3, NormalizedY: y);
        Assert.AreEqual(expected, destination.GetVerticalOffset(1000, 250, pageHeight, 5000), 0.001);
    }

    [TestMethod]
    public void PageOnlyDestinationUsesPageTopAndScrollRangeIsClamped()
    {
        Assert.AreEqual(1250, new PdfPageDestination(3).GetVerticalOffset(1000, 250, 792, 5000));
        Assert.AreEqual(1500, new PdfPageDestination(3, NormalizedY: 1).GetVerticalOffset(1000, 250, 792, 1500));
        Assert.AreEqual(0, new PdfPageDestination(0, NormalizedY: 0).GetVerticalOffset(0, 0, 792, 5000));
    }

    [TestMethod]
    public void HorizontalDestinationUsesMeasuredPageAndRetainsOffsetWhenUnspecified()
    {
        Assert.AreEqual(876.0, new PdfPageDestination(1, NormalizedX: 0.5).GetHorizontalOffset(500, 100, 600, 2000));
        Assert.AreEqual(0.0, new PdfPageDestination(1, NormalizedX: 0).GetHorizontalOffset(0, 100, 600, 0));
        Assert.IsNull(new PdfPageDestination(1).GetHorizontalOffset(500, 100, 600, 2000));
    }
}
