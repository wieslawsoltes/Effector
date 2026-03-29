#nullable enable

using System;
using System.IO;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Effector.PackageIntegration.App;
using Effector.PackageIntegration.Effects;
using SkiaSharp;
using Xunit;

namespace Effector.PackageIntegration.Tests;

internal static class IntegrationHeadlessAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<Effector.PackageIntegration.App.App>()
            .UseSkia()
            .WithInterFont()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions
            {
                UseHeadlessDrawing = false
            });
}

public sealed class PackageIntegrationTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.StartNew(typeof(IntegrationHeadlessAppBuilder));

    private static void RunOnUiThread(Action action)
    {
        Session.Dispatch(action, CancellationToken.None).GetAwaiter().GetResult();
    }

    [Fact]
    public void NuGetConsumedEffect_IsAssignableTo_IEffect()
    {
        RunOnUiThread(() =>
        {
            Assert.IsAssignableFrom<IEffect>(new PackageTintEffect());
        });
    }

    [Fact]
    public void NuGetConsumedMainWindow_Renders_And_EffectedPreview_Differs()
    {
        RunOnUiThread(() =>
        {
            var window = new MainWindow();
            window.Show();
            window.UpdateLayout();

            using var frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);

            var screenshotPath = GetScreenshotPath("package-integration-window.png");
            frame!.Save(screenshotPath);
            Assert.True(File.Exists(screenshotPath));

            var beforeHost = window.FindControl<Border>("BeforeHost");
            var afterHost = window.FindControl<Border>("AfterHost");
            Assert.NotNull(beforeHost);
            Assert.NotNull(afterHost);

            var beforeOrigin = beforeHost!.TranslatePoint(default, window);
            var afterOrigin = afterHost!.TranslatePoint(default, window);
            Assert.True(beforeOrigin.HasValue);
            Assert.True(afterOrigin.HasValue);

            var sampleOffsetX = (int)Math.Round(beforeHost.Bounds.Width * 0.33d);
            var sampleOffsetY = (int)Math.Round(beforeHost.Bounds.Height * 0.55d);

            var beforePixel = GetAverageColor(
                frame,
                (int)Math.Round(beforeOrigin!.Value.X) + sampleOffsetX,
                (int)Math.Round(beforeOrigin.Value.Y) + sampleOffsetY,
                radius: 8);
            var afterPixel = GetAverageColor(
                frame,
                (int)Math.Round(afterOrigin!.Value.X) + sampleOffsetX,
                (int)Math.Round(afterOrigin.Value.Y) + sampleOffsetY,
                radius: 8);

            var tintTarget = new SKColor(0, 194, 255);

            Assert.NotEqual(beforePixel, afterPixel);
            Assert.True(GetColorDistance(afterPixel, tintTarget) < GetColorDistance(beforePixel, tintTarget));
            Assert.True(GetColorDistance(beforePixel, afterPixel) > 25d);
        });
    }

    private static SKColor GetAverageColor(Bitmap bitmap, int centerX, int centerY, int radius)
    {
        using var stream = new MemoryStream();
        bitmap.Save(stream);
        stream.Position = 0;
        using var skBitmap = SKBitmap.Decode(stream);
        Assert.NotNull(skBitmap);
        var source = skBitmap!;

        var minX = Math.Max(0, centerX - radius);
        var maxX = Math.Min(source.Width - 1, centerX + radius);
        var minY = Math.Max(0, centerY - radius);
        var maxY = Math.Min(source.Height - 1, centerY + radius);

        long red = 0;
        long green = 0;
        long blue = 0;
        long alpha = 0;
        long count = 0;

        for (var y = minY; y <= maxY; y++)
        {
            for (var x = minX; x <= maxX; x++)
            {
                var pixel = source.GetPixel(x, y);
                red += pixel.Red;
                green += pixel.Green;
                blue += pixel.Blue;
                alpha += pixel.Alpha;
                count++;
            }
        }

        Assert.True(count > 0);
        return new SKColor(
            (byte)(red / count),
            (byte)(green / count),
            (byte)(blue / count),
            (byte)(alpha / count));
    }

    private static double GetColorDistance(SKColor left, SKColor right)
    {
        var red = left.Red - right.Red;
        var green = left.Green - right.Green;
        var blue = left.Blue - right.Blue;
        return Math.Sqrt((red * red) + (green * green) + (blue * blue));
    }

    private static string GetScreenshotPath(string fileName)
    {
        var root = Environment.GetEnvironmentVariable("AVALONIA_SCREENSHOT_DIR");
        if (string.IsNullOrWhiteSpace(root))
        {
            root = Path.Combine(AppContext.BaseDirectory, "screenshots");
        }

        Directory.CreateDirectory(root);
        return Path.Combine(root, fileName);
    }
}
