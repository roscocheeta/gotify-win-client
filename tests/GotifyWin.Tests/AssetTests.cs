using System;
using System.Drawing;
using System.IO;
using Xunit;

namespace GotifyWin.Tests;

public class AssetTests
{
    [Fact]
    public void AppIcon_ExistsAndHasValidMultiResolutionHeader()
    {
        var dir = AppContext.BaseDirectory;
        string? assetDir = null;
        while (dir != null)
        {
            var candidate = Path.Combine(dir, "src", "GotifyWin", "Assets");
            if (Directory.Exists(candidate))
            {
                assetDir = candidate;
                break;
            }
            dir = Path.GetDirectoryName(dir);
        }

        Assert.NotNull(assetDir);

        var icoPath = Path.Combine(assetDir, "app.ico");
        Assert.True(File.Exists(icoPath), $"app.ico should exist at {icoPath}");

        // Validate multi-resolution frames
        using var fs = File.OpenRead(icoPath);
        using var reader = new BinaryReader(fs);

        var reserved = reader.ReadUInt16();
        var type = reader.ReadUInt16();
        var count = reader.ReadUInt16();

        Assert.Equal(0, reserved);
        Assert.Equal(1, type); // 1 = Icon
        Assert.True(count >= 7, $"Expected at least 7 mipmap resolutions, got {count}");

        // Validate that System.Drawing.Icon can load it
        using var icon = new Icon(icoPath);
        Assert.True(icon.Width > 0);
        Assert.True(icon.Height > 0);

        // Validate companion PNGs exist
        var pngPath = Path.Combine(assetDir, "app.png");
        var png32Path = Path.Combine(assetDir, "app-32.png");
        Assert.True(File.Exists(pngPath), "app.png should exist");
        Assert.True(File.Exists(png32Path), "app-32.png should exist");
    }
}
