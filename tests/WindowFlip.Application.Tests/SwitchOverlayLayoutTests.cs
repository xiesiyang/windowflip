using System.Drawing;
using WindowFlip.Presentation;
using Xunit;

namespace WindowFlip.Application.Tests;

public sealed class SwitchOverlayLayoutTests
{
    [Fact]
    public void PreviewSize_IsOneAndAHalfTimesLarger()
    {
        Assert.Equal(new Size(194 * 3 / 2, 118 * 3 / 2), SwitchOverlay.LogicalPreviewSize);
    }

    [Fact]
    public void CalculateCardGrid_WrapsAllWindowsAcrossRows()
    {
        SwitchOverlay.CardGridLayout layout = SwitchOverlay.CalculateCardGrid(
            windowCount: 8,
            workingAreaWidth: 1280,
            scale: 1.0f);

        Assert.Equal(3, layout.Columns);
        Assert.Equal(3, layout.Rows);
        Assert.True(layout.Columns * layout.Rows >= 8);
    }

    [Fact]
    public void CalculateCardGrid_UsesDpiScaledDimensions()
    {
        SwitchOverlay.CardGridLayout layout = SwitchOverlay.CalculateCardGrid(
            windowCount: 8,
            workingAreaWidth: 1920,
            scale: 1.5f);

        Assert.Equal(3, layout.Columns);
        Assert.Equal(3, layout.Rows);
        Assert.Equal(new Size(1464, 1206), layout.Size);
    }
}
