using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.VisualTree;
using Effector.FilterEffects;
using Effector.Sample.App;
using SkiaSharp;
using Xunit;

namespace Effector.Runtime.Tests;

public sealed class FilterEffectCoverageTests
{
    private static readonly HeadlessUnitTestSession Session = HeadlessUnitTestSession.StartNew(typeof(EffectorHeadlessTestAppBuilder));
    private static readonly FilterImageSource SharedImageSource = FilterImageSource.FromPicture(CreatePictureSource());

    public static IEnumerable<object[]> SvgPrimitiveNames()
    {
        yield return new object[] { "feBlend" };
        yield return new object[] { "feColorMatrix" };
        yield return new object[] { "feComponentTransfer" };
        yield return new object[] { "feComposite" };
        yield return new object[] { "feConvolveMatrix" };
        yield return new object[] { "feDiffuseLighting" };
        yield return new object[] { "feDisplacementMap" };
        yield return new object[] { "feFlood" };
        yield return new object[] { "feGaussianBlur" };
        yield return new object[] { "feImage" };
        yield return new object[] { "feMerge" };
        yield return new object[] { "feMorphology" };
        yield return new object[] { "feOffset" };
        yield return new object[] { "feSpecularLighting" };
        yield return new object[] { "feTile" };
        yield return new object[] { "feTurbulence" };
    }

    public static IEnumerable<object[]> SvgSectionNames()
    {
        foreach (var primitive in SvgPrimitiveNames())
        {
            yield return new object[] { "SVG " + (string)primitive[0] };
        }
    }

    public static IEnumerable<object[]> SvgVisibleAfterSectionNames()
    {
        yield return new object[] { "SVG feBlend" };
        yield return new object[] { "SVG feComposite" };
        yield return new object[] { "SVG feFlood" };
        yield return new object[] { "SVG feTile" };
        yield return new object[] { "SVG feTurbulence" };
    }

    public static IEnumerable<object[]> GeneratedPaintPrimitiveGraphs()
    {
        yield return new object[]
        {
            "feFlood",
            new FilterPrimitiveCollection(
                new FloodPrimitive(Color.Parse("#0A84FF"), opacity: 0.86d))
        };

        yield return new object[]
        {
            "feTurbulence",
            new FilterPrimitiveCollection(
                new TurbulencePrimitive(
                    baseFrequencyX: 0.045d,
                    baseFrequencyY: 0.028d,
                    numOctaves: 3,
                    seed: 9d,
                    type: FilterTurbulenceType.FractalNoise,
                    stitchTiles: FilterStitchType.Stitch))
        };
    }

    public static IEnumerable<object[]> GeneratedWindowPrimitiveGraphs()
    {
        yield return new object[]
        {
            "feFlood",
            new FilterPrimitiveCollection(
                new FloodPrimitive(Color.Parse("#0A84FF"), opacity: 0.86d))
        };

        yield return new object[]
        {
            "feTurbulence",
            new FilterPrimitiveCollection(
                new TurbulencePrimitive(
                    baseFrequencyX: 0.045d,
                    baseFrequencyY: 0.028d,
                    numOctaves: 3,
                    seed: 9d,
                    type: FilterTurbulenceType.FractalNoise,
                    stitchTiles: FilterStitchType.Stitch))
        };

        yield return new object[]
        {
            "feImage",
            new FilterPrimitiveCollection(
                new ImagePrimitive(
                    SharedImageSource,
                    FilterAspectRatio.Default))
        };
    }

    [Theory]
    [MemberData(nameof(SvgPrimitiveNames))]
    public void FilterEffect_Renders_For_Each_Svg11_Primitive(string primitiveName)
    {
        using var baseline = RenderBaselineBitmap(240, 160);
        using var filter = CreateFilter(
            CreatePrimitiveGraph(primitiveName),
            new Rect(0d, 0d, 240d, 160d));
        using var effected = ApplyEffectFilterViaSaveLayer(240, 160, filter!, DrawSampleSource);

        Assert.NotEqual(HashBitmap(baseline), HashBitmap(effected));
        Assert.True(ContainsVisiblePixels(effected));
    }

    [Fact]
    public async Task MainWindow_Builds_All_Svg_Filter_Gallery_Sections()
    {
        await WithSampleEnvironmentAsync(
            ("EFFECTOR_SAMPLE_DISABLE_FEATURE_ANIMATIONS", "1"),
            ("EFFECTOR_SAMPLE_HIDE_FEATURE_ROWS", null),
            ("EFFECTOR_SAMPLE_LIMIT_SECTIONS", null),
            async () =>
            {
                await Session.Dispatch(() =>
                {
                    var window = new MainWindow
                    {
                        Width = 1280d,
                        Height = 900d
                    };

                    window.Show();
                    window.UpdateLayout();

                    var sectionTags = window.GetVisualDescendants()
                        .OfType<Border>()
                        .Select(static border => border.Tag as string)
                        .Where(static tag => !string.IsNullOrWhiteSpace(tag) && tag!.EndsWith("::Section", StringComparison.Ordinal))
                        .ToArray()!;

                    var svgSections = sectionTags
                        .Where(static tag => tag!.StartsWith("SVG fe", StringComparison.Ordinal))
                        .ToArray();

                    Assert.Equal(16, svgSections.Length);
                    Assert.Contains("SVG feImage::Section", svgSections);
                    Assert.Contains("SVG feTurbulence::Section", svgSections);
                    Assert.Contains("SVG feSpecularLighting::Section", svgSections);
                }, CancellationToken.None);
            });
    }

    [Fact]
    public async Task MainWindow_FilterGallery_Renders_NonBlank_Frame()
    {
        await WithSampleEnvironmentAsync(
            ("EFFECTOR_SAMPLE_DISABLE_FEATURE_ANIMATIONS", "1"),
            ("EFFECTOR_SAMPLE_HIDE_FEATURE_ROWS", null),
            ("EFFECTOR_SAMPLE_LIMIT_SECTIONS", "10"),
            async () =>
            {
                await Session.Dispatch(() =>
                {
                    var window = new MainWindow
                    {
                        Width = 1600d,
                        Height = 1200d
                    };

                    window.Show();
                    window.UpdateLayout();

                    using var frame = window.CaptureRenderedFrame();
                    Assert.NotNull(frame);
                    Assert.True(ContainsFrameVariance(frame!, minimumVariantPixels: 1200));
                }, CancellationToken.None);
            });
    }

    [Fact]
    public async Task MainWindow_XamlPath_HeroOnly_Renders_NonBlank_Frame()
    {
        await WithSampleEnvironmentAsync(
            ("EFFECTOR_SAMPLE_HEADLESS_SAFE_MODE", null),
            ("EFFECTOR_SAMPLE_HIDE_FEATURE_ROWS", "1"),
            ("EFFECTOR_SAMPLE_LIMIT_SECTIONS", "0"),
            () => AssertMainWindowFrameIsNonBlankAsync(1400d, 980d));
    }

    [Fact]
    public async Task MainWindow_XamlPath_FeaturerowsWithoutGallery_Renders_NonBlank_Frame()
    {
        await WithSampleEnvironmentAsync(
            ("EFFECTOR_SAMPLE_HEADLESS_SAFE_MODE", null),
            ("EFFECTOR_SAMPLE_HIDE_FEATURE_ROWS", null),
            ("EFFECTOR_SAMPLE_LIMIT_SECTIONS", "0"),
            () => AssertMainWindowFrameIsNonBlankAsync(1400d, 980d));
    }

    [Fact]
    public async Task MainWindow_XamlPath_NonFilterGallery_Renders_NonBlank_Frame()
    {
        await WithSampleEnvironmentAsync(
            ("EFFECTOR_SAMPLE_HEADLESS_SAFE_MODE", null),
            ("EFFECTOR_SAMPLE_HIDE_FEATURE_ROWS", "1"),
            ("EFFECTOR_SAMPLE_LIMIT_SECTIONS", "17"),
            () => AssertMainWindowFrameIsNonBlankAsync(1400d, 980d));
    }

    [Fact]
    public async Task MainWindow_InlineSvgXamlPreview_Compiles_To_Expected_Filter_Graph()
    {
        await WithSampleEnvironmentAsync(
            ("EFFECTOR_SAMPLE_HEADLESS_SAFE_MODE", null),
            ("EFFECTOR_SAMPLE_HIDE_FEATURE_ROWS", null),
            ("EFFECTOR_SAMPLE_LIMIT_SECTIONS", "0"),
            async () =>
            {
                await Session.Dispatch(() =>
                {
                    var window = new MainWindow
                    {
                        Width = 1400d,
                        Height = 980d
                    };

                    window.Show();
                    window.UpdateLayout();

                    var previewSurface = window.GetVisualDescendants()
                        .OfType<Grid>()
                        .FirstOrDefault(static candidate => candidate.Name == "InlineFilterEffectPreviewSurface");
                    Assert.NotNull(previewSurface);

                    var effect = Assert.IsAssignableFrom<FilterEffect>(previewSurface!.Effect);
                    Assert.Equal(5, effect.Primitives.Count);
                    Assert.IsType<GaussianBlurPrimitive>(effect.Primitives[0]);
                    Assert.IsType<OffsetPrimitive>(effect.Primitives[1]);
                    Assert.IsType<FloodPrimitive>(effect.Primitives[2]);
                    Assert.IsType<CompositePrimitive>(effect.Primitives[3]);
                    Assert.IsType<MergePrimitive>(effect.Primitives[4]);
                }, CancellationToken.None);
            });
    }

    [Fact]
    public async Task MainWindow_InlineSvgXamlPreview_Differs_From_Baseline()
    {
        await WithSampleEnvironmentAsync(
            async () =>
            {
                await Session.Dispatch(() =>
                {
                    var window = new MainWindow
                    {
                        Width = 1400d,
                        Height = 980d
                    };

                    window.Show();
                    window.UpdateLayout();

                    var previewSurface = window.GetVisualDescendants()
                        .OfType<Grid>()
                        .FirstOrDefault(static candidate => candidate.Name == "InlineFilterEffectPreviewSurface");
                    Assert.NotNull(previewSurface);
                    Assert.NotNull(previewSurface!.Effect);

                    var origin = previewSurface.TranslatePoint(default, window);
                    Assert.True(origin.HasValue);

                    var previewRect = InflateRect(
                        new SKRectI(
                            (int)Math.Floor(origin!.Value.X),
                            (int)Math.Floor(origin.Value.Y),
                            (int)Math.Ceiling(origin.Value.X + previewSurface.Bounds.Width),
                            (int)Math.Ceiling(origin.Value.Y + previewSurface.Bounds.Height)),
                        18,
                        (int)Math.Ceiling(window.Bounds.Width),
                        (int)Math.Ceiling(window.Bounds.Height));

                    using var effectedFrame = window.CaptureRenderedFrame();
                    Assert.NotNull(effectedFrame);

                    var originalEffect = previewSurface.Effect;
                    previewSurface.Effect = null;
                    window.UpdateLayout();

                    using var baselineFrame = window.CaptureRenderedFrame();
                    Assert.NotNull(baselineFrame);

                    using var effectedBitmap = DecodeBitmap(effectedFrame!);
                    using var baselineBitmap = DecodeBitmap(baselineFrame!);

                    var changedPixels = CountDifferentPixelsInsideRect(
                        baselineBitmap,
                        effectedBitmap,
                        previewRect,
                        step: 2,
                        tolerance: 8);

                    previewSurface.Effect = originalEffect;

                    Assert.True(
                        changedPixels >= 240,
                        $"Expected the inline SVG-style XAML preview to differ from baseline, but only found {changedPixels} sampled changed pixels.");
                }, CancellationToken.None);
            },
            ("EFFECTOR_SAMPLE_HEADLESS_SAFE_MODE", null),
            ("EFFECTOR_SAMPLE_HIDE_FEATURE_ROWS", null),
            ("EFFECTOR_SAMPLE_LIMIT_SECTIONS", "0"),
            ("EFFECTOR_SAMPLE_DISABLE_FEATURE_ANIMATIONS", "1"));
    }

    [Fact]
    public async Task MainWindow_XamlPath_FirstFilterGallerySection_Renders_NonBlank_Frame()
    {
        await WithSampleEnvironmentAsync(
            ("EFFECTOR_SAMPLE_HEADLESS_SAFE_MODE", null),
            ("EFFECTOR_SAMPLE_HIDE_FEATURE_ROWS", "1"),
            ("EFFECTOR_SAMPLE_LIMIT_SECTIONS", "18"),
            () => AssertMainWindowFrameIsNonBlankAsync(1400d, 980d));
    }

    [Theory]
    [MemberData(nameof(GeneratedPaintPrimitiveGraphs))]
    public void GeneratedPaintPrimitives_Render_Inside_Translated_Target_Bounds(string primitiveName, FilterPrimitiveCollection primitives)
    {
        const int contentWidth = 96;
        const int contentHeight = 72;
        const int translateX = 148;
        const int translateY = 91;

        using var filter = CreateFilter(
            primitives,
            new Rect(0d, 0d, contentWidth, contentHeight));
        using var effected = ApplyEffectFilterViaTranslatedSaveLayer(
            420,
            260,
            translateX,
            translateY,
            contentWidth,
            contentHeight,
            filter!,
            DrawBoundsProbeSource);

        var visibleBounds = GetVisiblePixelBounds(effected);
        Assert.True(visibleBounds.HasValue, $"{primitiveName} produced no visible pixels.");
        Assert.InRange(visibleBounds.Value.Left, translateX - 2, translateX + 2);
        Assert.InRange(visibleBounds.Value.Top, translateY - 2, translateY + 2);
        Assert.InRange(visibleBounds.Value.Right, translateX + contentWidth - 2, translateX + contentWidth + 2);
        Assert.InRange(visibleBounds.Value.Bottom, translateY + contentHeight - 2, translateY + contentHeight + 2);
    }

    [Theory]
    [MemberData(nameof(GeneratedPaintPrimitiveGraphs))]
    public void GeneratedPaintPrimitives_Remain_Renderable_After_Gc(string primitiveName, FilterPrimitiveCollection primitives)
    {
        using var filter = CreateFilter(
            primitives,
            new Rect(0d, 0d, 96d, 72d));

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        using var effected = ApplyEffectFilterViaSaveLayer(140, 100, filter!, DrawBoundsProbeSource);
        Assert.True(ContainsVisiblePixels(effected), $"{primitiveName} stopped rendering after GC.");
    }

    [Fact]
    public void FeImage_Renders_Inside_Translated_Target_Bounds()
    {
        const int contentWidth = 220;
        const int contentHeight = 140;
        const int translateX = 148;
        const int translateY = 91;

        using var filter = CreateFilter(
            new FilterPrimitiveCollection(
                new ImagePrimitive(
                    SharedImageSource,
                    FilterAspectRatio.Default)),
            new Rect(0d, 0d, contentWidth, contentHeight));
        using var effected = ApplyEffectFilterViaTranslatedSaveLayer(
            640,
            420,
            translateX,
            translateY,
            contentWidth,
            contentHeight,
            filter!,
            DrawImageBoundsProbeSource);

        var visibleBounds = GetVisiblePixelBounds(effected);
        Assert.True(visibleBounds.HasValue, "feImage produced no visible pixels.");
        Assert.InRange(visibleBounds.Value.Left, translateX - 2, translateX + 2);
        Assert.InRange(visibleBounds.Value.Top, translateY - 2, translateY + 2);
        Assert.InRange(visibleBounds.Value.Right, translateX + contentWidth - 2, translateX + contentWidth + 2);
        Assert.InRange(visibleBounds.Value.Bottom, translateY + contentHeight - 2, translateY + contentHeight + 2);
    }

    [Fact]
    public void FeImage_Slice_Stays_Inside_Its_Primitive_Bounds()
    {
        var cropRect = new Rect(70d, 20d, 80d, 120d);

        using var filter = CreateFilter(
            new FilterPrimitiveCollection(
                new ImagePrimitive(
                    SharedImageSource,
                    new FilterAspectRatio(FilterAspectAlignment.XMidYMid, FilterAspectMeetOrSlice.Slice),
                    cropRect: cropRect)),
            new Rect(0d, 0d, 220d, 140d));
        using var effected = ApplyEffectFilterViaSaveLayer(220, 140, filter!, DrawImageBoundsProbeSource);

        var visibleBounds = GetVisiblePixelBounds(effected);
        Assert.True(visibleBounds.HasValue, "feImage slice produced no visible pixels.");
        Assert.InRange(visibleBounds.Value.Left, (int)cropRect.X - 2, (int)cropRect.X + 2);
        Assert.InRange(visibleBounds.Value.Top, (int)cropRect.Y - 2, (int)cropRect.Y + 2);
        Assert.InRange(visibleBounds.Value.Right, (int)(cropRect.Right) - 2, (int)(cropRect.Right) + 2);
        Assert.InRange(visibleBounds.Value.Bottom, (int)(cropRect.Bottom) - 2, (int)(cropRect.Bottom) + 2);
    }

    [Theory]
    [MemberData(nameof(GeneratedWindowPrimitiveGraphs))]
    public async Task GeneratedSourcePrimitives_DoNot_Render_At_Window_Origin_When_Host_Is_Translated(
        string primitiveName,
        FilterPrimitiveCollection primitives)
    {
        await Session.Dispatch(() =>
        {
            var effect = new FilterEffect
            {
                Padding = new Thickness(24d),
                Primitives = primitives
            };

            var host = CreateTranslatedEffectHost(effect);
            var root = new Canvas
            {
                Width = 640d,
                Height = 420d,
                Background = Brushes.White,
                Children =
                {
                    host
                }
            };
            Canvas.SetLeft(host, 148d);
            Canvas.SetTop(host, 91d);

            var window = new Window
            {
                Width = 640d,
                Height = 420d,
                Background = Brushes.White,
                Content = root
            };

            window.Show();
            window.UpdateLayout();

            using var frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);

            var leakedPixel = GetBitmapPixel(frame!, 20, 20);
            Assert.True(
                leakedPixel.Red > 240 && leakedPixel.Green > 240 && leakedPixel.Blue > 240,
                $"Expected white background near window origin for {primitiveName}, but sampled {leakedPixel.Red},{leakedPixel.Green},{leakedPixel.Blue}.");

            var hostOrigin = host.TranslatePoint(new Point(0d, 0d), window);
            Assert.True(hostOrigin.HasValue);

            var insidePixel = GetBitmapPixel(
                frame,
                (int)Math.Round(hostOrigin!.Value.X + 44d),
                (int)Math.Round(hostOrigin.Value.Y + 36d));
            Assert.False(
                IsNearColor(insidePixel, new SKColor(255, 0, 255)),
                $"Expected {primitiveName} output inside host bounds, but sampled the original magenta source {insidePixel.Red},{insidePixel.Green},{insidePixel.Blue}.");
        }, CancellationToken.None);
    }

    [Theory]
    [MemberData(nameof(SvgSectionNames))]
    public async Task SvgFilterSections_DoNot_Modify_Pixels_Outside_The_After_Tile_When_Scrolled(string sectionName)
    {
        await WithSampleEnvironmentAsync(
            ("EFFECTOR_SAMPLE_HIDE_FEATURE_ROWS", "1"),
            ("EFFECTOR_SAMPLE_DISABLE_FEATURE_ANIMATIONS", "1"),
            ("EFFECTOR_SAMPLE_LIMIT_SECTIONS", null),
            async () =>
            {
                await Session.Dispatch(() =>
                {
                    var window = new MainWindow
                    {
                        Width = 1600d,
                        Height = 1200d
                    };

                    window.Show();
                    window.UpdateLayout();

                    var section = window.GetVisualDescendants()
                        .OfType<Border>()
                        .FirstOrDefault(candidate => Equals(candidate.Tag, sectionName + "::Section"));
                    Assert.NotNull(section);

                    section!.BringIntoView();
                    window.UpdateLayout();

                    var afterPreview = window.GetVisualDescendants()
                        .OfType<Control>()
                        .FirstOrDefault(candidate => Equals(candidate.Tag, sectionName + "::After"));
                    Assert.NotNull(afterPreview);

                    var afterTile = window.GetVisualDescendants()
                        .OfType<Border>()
                        .FirstOrDefault(candidate => Equals(candidate.Tag, sectionName + "::After::Tile"));
                    Assert.NotNull(afterTile);

                    var tileOrigin = afterTile!.TranslatePoint(default, window);
                    Assert.True(tileOrigin.HasValue);

                    var keepRect = InflateRect(
                        new SKRectI(
                            (int)Math.Floor(tileOrigin!.Value.X),
                            (int)Math.Floor(tileOrigin.Value.Y),
                            (int)Math.Ceiling(tileOrigin.Value.X + afterTile.Bounds.Width),
                            (int)Math.Ceiling(tileOrigin.Value.Y + afterTile.Bounds.Height)),
                        6,
                        (int)Math.Ceiling(window.Bounds.Width),
                        (int)Math.Ceiling(window.Bounds.Height));

                    using var effectedFrame = window.CaptureRenderedFrame();
                    Assert.NotNull(effectedFrame);

                    var originalEffect = afterPreview!.Effect;
                    afterPreview.Effect = null;
                    window.UpdateLayout();

                    using var baselineFrame = window.CaptureRenderedFrame();
                    Assert.NotNull(baselineFrame);

                    using var effectedBitmap = DecodeBitmap(effectedFrame!);
                    using var baselineBitmap = DecodeBitmap(baselineFrame!);

                    var outsideDiffPixels = CountDifferentPixelsOutsideRect(
                        baselineBitmap,
                        effectedBitmap,
                        keepRect,
                        step: 2,
                        tolerance: 8);

                    afterPreview.Effect = originalEffect;

                    Assert.True(
                        outsideDiffPixels <= 24,
                        $"{sectionName} changed {outsideDiffPixels} sampled pixels outside its After tile bounds.");
                }, CancellationToken.None);
            });
    }

    [Theory]
    [MemberData(nameof(SvgVisibleAfterSectionNames))]
    public async Task SvgSection_After_Preview_Renders_Visible_Content(string sectionName)
    {
        await WithSampleEnvironmentAsync(
            ("EFFECTOR_SAMPLE_HIDE_FEATURE_ROWS", "1"),
            ("EFFECTOR_SAMPLE_DISABLE_FEATURE_ANIMATIONS", "1"),
            ("EFFECTOR_SAMPLE_LIMIT_SECTIONS", null),
            async () =>
            {
                await Session.Dispatch(() =>
                {
                    var window = new MainWindow
                    {
                        Width = 1600d,
                        Height = 1200d
                    };

                    window.Show();
                    window.UpdateLayout();

                    var section = window.GetVisualDescendants()
                        .OfType<Border>()
                        .FirstOrDefault(candidate => Equals(candidate.Tag, sectionName + "::Section"));
                    Assert.NotNull(section);

                    section!.BringIntoView();
                    window.UpdateLayout();

                    var afterPreview = window.GetVisualDescendants()
                        .OfType<Control>()
                        .FirstOrDefault(candidate => Equals(candidate.Tag, sectionName + "::After"));
                    Assert.NotNull(afterPreview);

                    var previewOrigin = afterPreview!.TranslatePoint(default, window);
                    Assert.True(previewOrigin.HasValue);

                    var previewRect = ShrinkRect(
                        new SKRectI(
                            (int)Math.Floor(previewOrigin!.Value.X),
                            (int)Math.Floor(previewOrigin.Value.Y),
                            (int)Math.Ceiling(previewOrigin.Value.X + afterPreview.Bounds.Width),
                            (int)Math.Ceiling(previewOrigin.Value.Y + afterPreview.Bounds.Height)),
                        8);

                    using var frame = window.CaptureRenderedFrame();
                    Assert.NotNull(frame);

                    using var bitmap = DecodeBitmap(frame!);
                    var visiblePixels = CountPixelsDifferentFromColorInsideRect(
                        bitmap,
                        previewRect,
                        new SKColor(248, 246, 241),
                        step: 2,
                        tolerance: 10);

                    Assert.True(
                        visiblePixels >= 900,
                        $"Expected {sectionName} After preview to contain visible content, but only found {visiblePixels} sampled non-background pixels.");

                    if (string.Equals(sectionName, "SVG feFlood", StringComparison.Ordinal))
                    {
                        var centerPixel = bitmap.GetPixel(
                            (previewRect.Left + previewRect.Right) / 2,
                            (previewRect.Top + previewRect.Bottom) / 2);
                        Assert.True(
                            centerPixel.Blue >= 220 &&
                            centerPixel.Green >= 110 &&
                            centerPixel.Red <= 90,
                            $"Expected {sectionName} After preview to show the configured flood color, but sampled {centerPixel.Red},{centerPixel.Green},{centerPixel.Blue}.");
                    }
                }, CancellationToken.None);
            });
    }

    [Theory]
    [MemberData(nameof(SvgSectionNames))]
    public async Task SvgSection_After_Preview_Differs_From_Baseline(string sectionName)
    {
        await WithSampleEnvironmentAsync(
            ("EFFECTOR_SAMPLE_HIDE_FEATURE_ROWS", "1"),
            ("EFFECTOR_SAMPLE_DISABLE_FEATURE_ANIMATIONS", "1"),
            ("EFFECTOR_SAMPLE_LIMIT_SECTIONS", null),
            async () =>
            {
                await Session.Dispatch(() =>
                {
                    var window = new MainWindow
                    {
                        Width = 1600d,
                        Height = 1200d
                    };

                    window.Show();
                    window.UpdateLayout();

                    var section = window.GetVisualDescendants()
                        .OfType<Border>()
                        .FirstOrDefault(candidate => Equals(candidate.Tag, sectionName + "::Section"));
                    Assert.NotNull(section);

                    section!.BringIntoView();
                    window.UpdateLayout();

                    var afterPreview = window.GetVisualDescendants()
                        .OfType<Control>()
                        .FirstOrDefault(candidate => Equals(candidate.Tag, sectionName + "::After"));
                    Assert.NotNull(afterPreview);
                    Assert.NotNull(afterPreview!.Effect);

                    var previewOrigin = afterPreview.TranslatePoint(default, window);
                    Assert.True(previewOrigin.HasValue);

                    var previewRect = ShrinkRect(
                        new SKRectI(
                            (int)Math.Floor(previewOrigin!.Value.X),
                            (int)Math.Floor(previewOrigin.Value.Y),
                            (int)Math.Ceiling(previewOrigin.Value.X + afterPreview.Bounds.Width),
                            (int)Math.Ceiling(previewOrigin.Value.Y + afterPreview.Bounds.Height)),
                        8);

                    using var effectedFrame = window.CaptureRenderedFrame();
                    Assert.NotNull(effectedFrame);

                    var originalEffect = afterPreview.Effect;
                    afterPreview.Effect = null;
                    window.UpdateLayout();

                    using var baselineFrame = window.CaptureRenderedFrame();
                    Assert.NotNull(baselineFrame);

                    using var effectedBitmap = DecodeBitmap(effectedFrame!);
                    using var baselineBitmap = DecodeBitmap(baselineFrame!);

                    var changedPixels = CountDifferentPixelsInsideRect(
                        baselineBitmap,
                        effectedBitmap,
                        previewRect,
                        step: 2,
                        tolerance: 8);

                    afterPreview.Effect = originalEffect;

                    Assert.True(
                        changedPixels >= 120,
                        $"Expected {sectionName} After preview to differ from the baseline, but only found {changedPixels} sampled changed pixels.");
                }, CancellationToken.None);
            });
    }

    [Fact]
    public async Task FeTile_Renders_On_Simple_Window_Host()
    {
        await Session.Dispatch(() =>
        {
            var effect = new FilterEffect
            {
                Padding = new Thickness(24d),
                Primitives = new FilterPrimitiveCollection(
                    new TilePrimitive(
                        sourceRect: new Rect(20d, 20d, 110d, 70d),
                        destinationRect: new Rect(0d, 0d, 220d, 140d)))
            };

            var host = CreateTranslatedEffectHost(effect);
            var root = new Canvas
            {
                Width = 320d,
                Height = 240d,
                Background = Brushes.White,
                Children =
                {
                    host
                }
            };
            Canvas.SetLeft(host, 48d);
            Canvas.SetTop(host, 32d);

            var window = new Window
            {
                Width = 320d,
                Height = 240d,
                Background = Brushes.White,
                Content = root
            };

            window.Show();
            window.UpdateLayout();

            using var frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);

            using var bitmap = DecodeBitmap(frame!);
            var hostRect = new SKRectI(48, 32, 48 + 220, 32 + 140);
            var visiblePixels = CountPixelsDifferentFromColorInsideRect(
                bitmap,
                hostRect,
                new SKColor(255, 255, 255),
                step: 2,
                tolerance: 10);

            Assert.True(visiblePixels >= 600, $"Expected feTile to render on a simple host, but only found {visiblePixels} sampled non-white pixels.");

            var repeatedPixel = bitmap.GetPixel(48 + 140, 32 + 44);
            Assert.False(
                IsNearColor(repeatedPixel, new SKColor(255, 0, 255), tolerance: 14),
                $"Expected feTile to repeat the sampled source motif into the second tile, but sampled {repeatedPixel.Red},{repeatedPixel.Green},{repeatedPixel.Blue}.");
        }, CancellationToken.None);
    }

    [Fact]
    public async Task FeFlood_Renders_Configured_Color_On_Simple_Window_Host()
    {
        await Session.Dispatch(() =>
        {
            var effect = new FilterEffect
            {
                Padding = new Thickness(24d),
                Primitives = new FilterPrimitiveCollection(
                    new FloodPrimitive(Color.Parse("#0A84FF"), opacity: 1d))
            };

            var host = CreateTranslatedEffectHost(effect);
            var window = new Window
            {
                Width = 320d,
                Height = 240d,
                Background = Brushes.White,
                Content = host
            };

            window.Show();
            window.UpdateLayout();

            using var frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);

            using var bitmap = DecodeBitmap(frame!);
            var floodedPixel = bitmap.GetPixel(110, 70);
            Assert.True(
                IsNearColor(floodedPixel, new SKColor(10, 132, 255), tolerance: 18),
                $"Expected feFlood to render the configured flood color, but sampled {floodedPixel.Red},{floodedPixel.Green},{floodedPixel.Blue}.");
        }, CancellationToken.None);
    }

    [Fact]
    public async Task FeComposite_Renders_Configured_Color_On_Simple_Window_Host()
    {
        await Session.Dispatch(() =>
        {
            var effect = new FilterEffect
            {
                Padding = new Thickness(24d),
                Primitives = new FilterPrimitiveCollection(
                    new FloodPrimitive(Color.Parse("#FFB347"), opacity: 0.92d, result: "paint"),
                    new CompositePrimitive(
                        FilterCompositeOperator.In,
                        input: FilterInput.Named("paint"),
                        input2: FilterInput.SourceAlpha))
            };

            var host = CreateTranslatedEffectHost(effect);
            var window = new Window
            {
                Width = 320d,
                Height = 240d,
                Background = Brushes.White,
                Content = host
            };

            window.Show();
            window.UpdateLayout();

            using var frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);

            using var bitmap = DecodeBitmap(frame!);
            var compositePixel = bitmap.GetPixel(110, 70);
            Assert.True(
                IsNearColor(compositePixel, new SKColor(255, 179, 71), tolerance: 22),
                $"Expected feComposite to render the configured flood color through SourceAlpha, but sampled {compositePixel.Red},{compositePixel.Green},{compositePixel.Blue}.");
        }, CancellationToken.None);
    }

    [Theory]
    [MemberData(nameof(GeneratedPaintPrimitiveGraphs))]
    public void GeneratedPaintPrimitives_Render_With_Explicit_RenderThreadBounds_Without_HostVisual(
        string primitiveName,
        FilterPrimitiveCollection primitives)
    {
        const int contentWidth = 96;
        const int contentHeight = 72;
        const int renderX = 148;
        const int renderY = 91;
        const int padding = 24;

        var effect = RunOnUiThread(() => EffectExtensions.ToImmutable(EffectTestHelpers.AsEffect(new FilterEffect
        {
            Padding = new Thickness(padding),
            Primitives = primitives
        })));

        var renderBounds = new Rect(
            renderX,
            renderY,
            contentWidth + (padding * 2),
            contentHeight + (padding * 2));

        using var filter = EffectorRuntime.CreateEffectPatched(effect, 1d, useOpacitySaveLayer: false, renderBounds);
        Assert.NotNull(filter);

        using var effected = ApplyEffectFilterViaSceneSaveLayer(
            420,
            260,
            SKRect.Create(renderX, renderY, (float)renderBounds.Width, (float)renderBounds.Height),
            filter!,
            canvas => DrawBoundsProbeSourceAt(canvas, renderX + padding, renderY + padding, contentWidth, contentHeight));

        var visibleBounds = GetVisiblePixelBounds(effected);
        Assert.True(visibleBounds.HasValue, $"{primitiveName} produced no visible pixels when created from render-thread bounds.");
        Assert.InRange(visibleBounds.Value.Left, renderX + padding - 2, renderX + padding + 2);
        Assert.InRange(visibleBounds.Value.Top, renderY + padding - 2, renderY + padding + 2);
        Assert.InRange(visibleBounds.Value.Right, renderX + padding + contentWidth - 2, renderX + padding + contentWidth + 2);
        Assert.InRange(visibleBounds.Value.Bottom, renderY + padding + contentHeight - 2, renderY + padding + contentHeight + 2);
    }

    [Fact]
    public void FeComposite_Renders_With_Explicit_RenderThreadBounds_Without_HostVisual()
    {
        const int contentWidth = 220;
        const int contentHeight = 140;
        const int renderX = 148;
        const int renderY = 91;
        const int padding = 24;

        var effect = RunOnUiThread(() => EffectExtensions.ToImmutable(EffectTestHelpers.AsEffect(new FilterEffect
        {
            Padding = new Thickness(padding),
            Primitives = new FilterPrimitiveCollection(
                new FloodPrimitive(Color.Parse("#FFB347"), opacity: 0.92d, result: "paint"),
                new CompositePrimitive(
                    FilterCompositeOperator.In,
                    input: FilterInput.Named("paint"),
                    input2: FilterInput.SourceAlpha))
        })));

        var renderBounds = new Rect(
            renderX,
            renderY,
            contentWidth + (padding * 2),
            contentHeight + (padding * 2));

        using var filter = EffectorRuntime.CreateEffectPatched(effect, 1d, useOpacitySaveLayer: false, renderBounds);
        Assert.NotNull(filter);

        using var effected = ApplyEffectFilterViaSceneSaveLayer(
            640,
            420,
            SKRect.Create(renderX, renderY, (float)renderBounds.Width, (float)renderBounds.Height),
            filter!,
            canvas => DrawImageBoundsProbeSourceAt(canvas, renderX + padding, renderY + padding, contentWidth, contentHeight));

        var compositePixel = effected.GetPixel(renderX + padding + (contentWidth / 2), renderY + padding + (contentHeight / 2));
        Assert.True(
            IsNearColor(compositePixel, new SKColor(255, 179, 71), tolerance: 22),
            $"Expected feComposite to render from explicit render-thread bounds, but sampled {compositePixel.Red},{compositePixel.Green},{compositePixel.Blue}.");
    }

    [Theory]
    [MemberData(nameof(GeneratedWindowPrimitiveGraphs))]
    public async Task GeneratedSourcePrimitives_Can_Create_Filter_Off_Ui_Thread_Using_Cached_Host_Bounds(
        string primitiveName,
        FilterPrimitiveCollection primitives)
    {
        Window? window = null;
        IEffect? frozenEffect = null;

        try
        {
            await Session.Dispatch(() =>
            {
                var mutableEffect = new FilterEffect
                {
                    Padding = new Thickness(24d),
                    Primitives = primitives
                };

                var host = CreateTranslatedEffectHost(mutableEffect);
                var root = new Canvas
                {
                    Width = 640d,
                    Height = 420d,
                    Background = Brushes.White,
                    Children =
                    {
                        host
                    }
                };
                Canvas.SetLeft(host, 148d);
                Canvas.SetTop(host, 91d);

                window = new Window
                {
                    Width = 640d,
                    Height = 420d,
                    Background = Brushes.White,
                    Content = root
                };

                window.Show();
                window.UpdateLayout();
                frozenEffect = EffectorRuntime.FreezeRegisteredEffect(mutableEffect);
            }, CancellationToken.None);

            Assert.NotNull(frozenEffect);

            var failure = await Task.Run(() =>
            {
                try
                {
                    using var filter = EffectorRuntime.CreateEffectPatched(frozenEffect!, 1d, useOpacitySaveLayer: false);
                    Assert.NotNull(filter);
                    return (Exception?)null;
                }
                catch (Exception ex)
                {
                    return ex;
                }
            });

            Assert.True(
                failure is null,
                $"{primitiveName} should create its filter off the UI thread using cached host bounds, but failed with: {failure}");
        }
        finally
        {
            if (window is not null)
            {
                await Session.Dispatch(window.Close, CancellationToken.None);
            }
        }
    }

    private static FilterPrimitiveCollection CreatePrimitiveGraph(string primitiveName)
    {
        return primitiveName switch
        {
            "feBlend" => new FilterPrimitiveCollection(
                new FloodPrimitive(Color.Parse("#6C5CE7"), opacity: 0.72d, result: "wash"),
                new BlendPrimitive(
                    FilterBlendMode.SoftLight,
                    input: FilterInput.SourceGraphic,
                    input2: FilterInput.Named("wash"))),
            "feColorMatrix" => new FilterPrimitiveCollection(
                new ColorMatrixPrimitive(
                    FilterColorMatrixType.HueRotate,
                    new FilterNumberCollection(120d))),
            "feComponentTransfer" => new FilterPrimitiveCollection(
                new ComponentTransferPrimitive(
                    red: new FilterComponentTransferChannel(
                        FilterComponentTransferType.Gamma,
                        amplitude: 1.1d,
                        exponent: 0.75d,
                        offset: 0.02d),
                    green: new FilterComponentTransferChannel(
                        FilterComponentTransferType.Linear,
                        slope: 1.1d,
                        intercept: 0.03d),
                    blue: new FilterComponentTransferChannel(
                        FilterComponentTransferType.Table,
                        new FilterNumberCollection(0d, 0.16d, 0.82d, 1d)))),
            "feComposite" => new FilterPrimitiveCollection(
                new FloodPrimitive(Color.Parse("#FFB347"), opacity: 0.92d, result: "paint"),
                new CompositePrimitive(
                    FilterCompositeOperator.In,
                    input: FilterInput.Named("paint"),
                    input2: FilterInput.SourceAlpha)),
            "feConvolveMatrix" => new FilterPrimitiveCollection(
                new ConvolveMatrixPrimitive(
                    orderX: 3,
                    orderY: 3,
                    kernelMatrix: new FilterNumberCollection(
                        0d, -1d, 0d,
                        -1d, 5d, -1d,
                        0d, -1d, 0d))),
            "feDiffuseLighting" => new FilterPrimitiveCollection(
                new DiffuseLightingPrimitive(
                    Color.Parse("#FFF2CC"),
                    new FilterDistantLight(azimuth: 135d, elevation: 42d),
                    surfaceScale: 3.2d,
                    diffuseConstant: 1.4d,
                    input: FilterInput.SourceAlpha)),
            "feDisplacementMap" => new FilterPrimitiveCollection(
                new TurbulencePrimitive(
                    baseFrequencyX: 0.035d,
                    baseFrequencyY: 0.02d,
                    numOctaves: 2,
                    seed: 3d,
                    type: FilterTurbulenceType.Turbulence,
                    result: "noise"),
                new DisplacementMapPrimitive(
                    scale: 18d,
                    xChannelSelector: FilterChannelSelector.R,
                    yChannelSelector: FilterChannelSelector.B,
                    input: FilterInput.SourceGraphic,
                    input2: FilterInput.Named("noise"))),
            "feFlood" => new FilterPrimitiveCollection(
                new FloodPrimitive(Color.Parse("#0A84FF"), opacity: 0.86d)),
            "feGaussianBlur" => new FilterPrimitiveCollection(
                new GaussianBlurPrimitive(stdDeviationX: 6d, stdDeviationY: 4d)),
            "feImage" => new FilterPrimitiveCollection(
                new ImagePrimitive(
                    SharedImageSource,
                    FilterAspectRatio.Default)),
            "feMerge" => new FilterPrimitiveCollection(
                new FloodPrimitive(Color.Parse("#FF7A59"), opacity: 0.28d, result: "wash"),
                new MergePrimitive(
                    new FilterInputCollection(
                        FilterInput.SourceGraphic,
                        FilterInput.Named("wash")))),
            "feMorphology" => new FilterPrimitiveCollection(
                new MorphologyPrimitive(
                    FilterMorphologyOperator.Dilate,
                    radiusX: 2d,
                    radiusY: 2d)),
            "feOffset" => new FilterPrimitiveCollection(
                new OffsetPrimitive(dx: 18d, dy: 14d)),
            "feSpecularLighting" => new FilterPrimitiveCollection(
                new SpecularLightingPrimitive(
                    Color.Parse("#FFF8E1"),
                    new FilterSpotLight(
                        x: 140d,
                        y: -30d,
                        z: 120d,
                        pointsAtX: 120d,
                        pointsAtY: 70d,
                        pointsAtZ: 0d,
                        specularExponent: 14d,
                        limitingConeAngle: 28d),
                    surfaceScale: 3d,
                    specularConstant: 1.2d,
                    specularExponent: 18d,
                    input: FilterInput.SourceAlpha)),
            "feTile" => new FilterPrimitiveCollection(
                new TilePrimitive(
                    sourceRect: new Rect(20d, 20d, 110d, 70d),
                    destinationRect: new Rect(0d, 0d, 240d, 160d))),
            "feTurbulence" => new FilterPrimitiveCollection(
                new TurbulencePrimitive(
                    baseFrequencyX: 0.045d,
                    baseFrequencyY: 0.028d,
                    numOctaves: 3,
                    seed: 9d,
                    type: FilterTurbulenceType.FractalNoise,
                    stitchTiles: FilterStitchType.Stitch)),
            _ => throw new ArgumentOutOfRangeException(nameof(primitiveName), primitiveName, null)
        };
    }

    private static SKBitmap RenderBaselineBitmap(int width, int height)
    {
        var bitmap = new SKBitmap(width, height);
        using var surface = SKSurface.Create(new SKImageInfo(width, height));
        DrawSampleSource(surface.Canvas);
        surface.Canvas.Flush();
        using var snapshot = surface.Snapshot();
        snapshot.ReadPixels(bitmap.Info, bitmap.GetPixels(), bitmap.RowBytes, 0, 0);
        return bitmap;
    }

    private static SKBitmap ApplyEffectFilterViaSaveLayer(int width, int height, SKImageFilter filter, Action<SKCanvas> drawSource)
    {
        var bitmap = new SKBitmap(width, height);
        using var surface = SKSurface.Create(new SKImageInfo(width, height));
        using var paint = new SKPaint { ImageFilter = filter };
        surface.Canvas.SaveLayer(paint);
        drawSource(surface.Canvas);
        surface.Canvas.Restore();
        surface.Canvas.Flush();
        using var filteredImage = surface.Snapshot();
        filteredImage.ReadPixels(bitmap.Info, bitmap.GetPixels(), bitmap.RowBytes, 0, 0);
        return bitmap;
    }

    private static SKBitmap ApplyEffectFilterViaTranslatedSaveLayer(
        int width,
        int height,
        float translateX,
        float translateY,
        float layerWidth,
        float layerHeight,
        SKImageFilter filter,
        Action<SKCanvas> drawSource)
    {
        var bitmap = new SKBitmap(width, height);
        using var surface = SKSurface.Create(new SKImageInfo(width, height));
        using var paint = new SKPaint { ImageFilter = filter };
        surface.Canvas.Clear(SKColors.Transparent);
        surface.Canvas.Save();
        surface.Canvas.Translate(translateX, translateY);
        surface.Canvas.SaveLayer(SKRect.Create(0f, 0f, layerWidth, layerHeight), paint);
        drawSource(surface.Canvas);
        surface.Canvas.Restore();
        surface.Canvas.Restore();
        surface.Canvas.Flush();
        using var filteredImage = surface.Snapshot();
        filteredImage.ReadPixels(bitmap.Info, bitmap.GetPixels(), bitmap.RowBytes, 0, 0);
        return bitmap;
    }

    private static SKBitmap ApplyEffectFilterViaSceneSaveLayer(
        int width,
        int height,
        SKRect layerRect,
        SKImageFilter filter,
        Action<SKCanvas> drawSource)
    {
        var bitmap = new SKBitmap(width, height);
        using var surface = SKSurface.Create(new SKImageInfo(width, height));
        using var paint = new SKPaint { ImageFilter = filter };
        surface.Canvas.Clear(SKColors.Transparent);
        surface.Canvas.SaveLayer(layerRect, paint);
        drawSource(surface.Canvas);
        surface.Canvas.Restore();
        surface.Canvas.Flush();
        using var filteredImage = surface.Snapshot();
        filteredImage.ReadPixels(bitmap.Info, bitmap.GetPixels(), bitmap.RowBytes, 0, 0);
        return bitmap;
    }

    private static void DrawSampleSource(SKCanvas canvas)
    {
        canvas.Clear(new SKColor(246, 244, 238));

        using var cardPaint = new SKPaint
        {
            Color = new SKColor(255, 255, 255),
            IsAntialias = true
        };
        canvas.DrawRoundRect(SKRect.Create(18f, 16f, 204f, 128f), 22f, 22f, cardPaint);

        using var headerPaint = new SKPaint
        {
            Color = new SKColor(49, 110, 196),
            IsAntialias = true
        };
        canvas.DrawRoundRect(SKRect.Create(34f, 30f, 118f, 28f), 12f, 12f, headerPaint);

        using var accentPaint = new SKPaint
        {
            Color = new SKColor(255, 136, 90),
            IsAntialias = true
        };
        canvas.DrawCircle(182f, 52f, 20f, accentPaint);
        canvas.DrawRoundRect(SKRect.Create(42f, 82f, 62f, 40f), 14f, 14f, accentPaint);

        using var detailPaint = new SKPaint
        {
            Color = new SKColor(60, 72, 92),
            IsAntialias = true
        };
        canvas.DrawRoundRect(SKRect.Create(118f, 90f, 78f, 12f), 6f, 6f, detailPaint);
        canvas.DrawRoundRect(SKRect.Create(118f, 110f, 54f, 10f), 5f, 5f, detailPaint);
    }

    private static void DrawBoundsProbeSource(SKCanvas canvas)
    {
        using var paint = new SKPaint
        {
            Color = new SKColor(24, 110, 242, 255),
            IsAntialias = true
        };
        canvas.DrawRoundRect(SKRect.Create(0f, 0f, 96f, 72f), 14f, 14f, paint);
    }

    private static void DrawBoundsProbeSourceAt(SKCanvas canvas, float x, float y, float width, float height)
    {
        using var paint = new SKPaint
        {
            Color = new SKColor(24, 110, 242, 255),
            IsAntialias = true
        };
        canvas.DrawRoundRect(SKRect.Create(x, y, width, height), 14f, 14f, paint);
    }

    private static void DrawImageBoundsProbeSource(SKCanvas canvas)
    {
        using var paint = new SKPaint
        {
            Color = new SKColor(24, 110, 242, 255),
            IsAntialias = true
        };
        canvas.DrawRect(SKRect.Create(0f, 0f, 220f, 140f), paint);
    }

    private static void DrawImageBoundsProbeSourceAt(SKCanvas canvas, float x, float y, float width, float height)
    {
        using var paint = new SKPaint
        {
            Color = new SKColor(24, 110, 242, 255),
            IsAntialias = true
        };
        canvas.DrawRect(SKRect.Create(x, y, width, height), paint);
    }

    private static string HashBitmap(SKBitmap bitmap)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return Convert.ToHexString(SHA256.HashData(data.ToArray()));
    }

    private static bool ContainsVisiblePixels(SKBitmap bitmap)
    {
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                if (bitmap.GetPixel(x, y).Alpha > 0)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static SKRectI? GetVisiblePixelBounds(SKBitmap bitmap)
    {
        var minX = bitmap.Width;
        var minY = bitmap.Height;
        var maxX = -1;
        var maxY = -1;

        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                if (bitmap.GetPixel(x, y).Alpha <= 0)
                {
                    continue;
                }

                if (x < minX)
                {
                    minX = x;
                }

                if (y < minY)
                {
                    minY = y;
                }

                if (x > maxX)
                {
                    maxX = x;
                }

                if (y > maxY)
                {
                    maxY = y;
                }
            }
        }

        if (maxX < minX || maxY < minY)
        {
            return null;
        }

        return new SKRectI(minX, minY, maxX + 1, maxY + 1);
    }

    private static SKColor GetBitmapPixel(Avalonia.Media.Imaging.Bitmap bitmap, int x, int y)
    {
        using var stream = new MemoryStream();
        bitmap.Save(stream);
        stream.Position = 0;
        using var skBitmap = SKBitmap.Decode(stream);
        Assert.NotNull(skBitmap);
        return skBitmap!.GetPixel(x, y);
    }

    private static SKBitmap DecodeBitmap(Avalonia.Media.Imaging.Bitmap bitmap)
    {
        using var stream = new MemoryStream();
        bitmap.Save(stream);
        stream.Position = 0;
        var skBitmap = SKBitmap.Decode(stream);
        Assert.NotNull(skBitmap);
        return skBitmap!;
    }

    private static Border CreateTranslatedEffectHost(object effect)
    {
        var accent = new Border
        {
            Width = 74d,
            Height = 52d,
            CornerRadius = new CornerRadius(14d),
            Background = new SolidColorBrush(Color.Parse("#FFFF8A56"))
        };
        Canvas.SetLeft(accent, 18d);
        Canvas.SetTop(accent, 18d);

        var detail = new Border
        {
            Width = 88d,
            Height = 14d,
            CornerRadius = new CornerRadius(7d),
            Background = Brushes.White
        };
        Canvas.SetLeft(detail, 110d);
        Canvas.SetTop(detail, 96d);

        return new Border
        {
            Width = 220d,
            Height = 140d,
            Background = new SolidColorBrush(Color.Parse("#FFFF00FF")),
            Effect = EffectTestHelpers.AsEffect(effect),
            Child = new Canvas
            {
                Width = 220d,
                Height = 140d,
                Children =
                {
                    accent,
                    detail
                }
            }
        };
    }

    private static bool ContainsFrameVariance(Avalonia.Media.Imaging.Bitmap bitmap, int minimumVariantPixels)
    {
        using var skBitmap = DecodeBitmap(bitmap);

        var background = skBitmap!.GetPixel(4, 4);
        var variantPixels = 0;
        for (var y = 0; y < skBitmap.Height; y += 3)
        {
            for (var x = 0; x < skBitmap.Width; x += 3)
            {
                if (IsNearColor(skBitmap.GetPixel(x, y), background, tolerance: 6))
                {
                    continue;
                }

                variantPixels++;
                if (variantPixels >= minimumVariantPixels)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static int CountDifferentPixelsOutsideRect(
        SKBitmap expected,
        SKBitmap actual,
        SKRectI keepRect,
        int step,
        byte tolerance)
    {
        Assert.Equal(expected.Width, actual.Width);
        Assert.Equal(expected.Height, actual.Height);

        var differentPixels = 0;
        for (var y = 0; y < actual.Height; y += step)
        {
            for (var x = 0; x < actual.Width; x += step)
            {
                if (x >= keepRect.Left && x < keepRect.Right && y >= keepRect.Top && y < keepRect.Bottom)
                {
                    continue;
                }

                if (IsNearColor(expected.GetPixel(x, y), actual.GetPixel(x, y), tolerance))
                {
                    continue;
                }

                differentPixels++;
            }
        }

        return differentPixels;
    }

    private static int CountDifferentPixelsInsideRect(
        SKBitmap expected,
        SKBitmap actual,
        SKRectI rect,
        int step,
        byte tolerance)
    {
        Assert.Equal(expected.Width, actual.Width);
        Assert.Equal(expected.Height, actual.Height);

        var differentPixels = 0;
        for (var y = Math.Max(0, rect.Top); y < Math.Min(actual.Height, rect.Bottom); y += step)
        {
            for (var x = Math.Max(0, rect.Left); x < Math.Min(actual.Width, rect.Right); x += step)
            {
                if (IsNearColor(expected.GetPixel(x, y), actual.GetPixel(x, y), tolerance))
                {
                    continue;
                }

                differentPixels++;
            }
        }

        return differentPixels;
    }

    private static SKRectI InflateRect(SKRectI rect, int padding, int maxWidth, int maxHeight) =>
        new(
            Math.Max(0, rect.Left - padding),
            Math.Max(0, rect.Top - padding),
            Math.Min(maxWidth, rect.Right + padding),
            Math.Min(maxHeight, rect.Bottom + padding));

    private static SKRectI ShrinkRect(SKRectI rect, int inset) =>
        new(
            rect.Left + inset,
            rect.Top + inset,
            Math.Max(rect.Left + inset + 1, rect.Right - inset),
            Math.Max(rect.Top + inset + 1, rect.Bottom - inset));

    private static int CountPixelsDifferentFromColorInsideRect(
        SKBitmap bitmap,
        SKRectI rect,
        SKColor color,
        int step,
        byte tolerance)
    {
        var differentPixels = 0;
        for (var y = Math.Max(0, rect.Top); y < Math.Min(bitmap.Height, rect.Bottom); y += step)
        {
            for (var x = Math.Max(0, rect.Left); x < Math.Min(bitmap.Width, rect.Right); x += step)
            {
                if (IsNearColor(bitmap.GetPixel(x, y), color, tolerance))
                {
                    continue;
                }

                differentPixels++;
            }
        }

        return differentPixels;
    }

    private static bool IsNearColor(SKColor actual, SKColor expected, byte tolerance = 10) =>
        Math.Abs(actual.Red - expected.Red) <= tolerance &&
        Math.Abs(actual.Green - expected.Green) <= tolerance &&
        Math.Abs(actual.Blue - expected.Blue) <= tolerance;

    private static SKImageFilter CreateFilter(FilterPrimitiveCollection primitives, Rect inputBounds) =>
        Session.Dispatch(
                () =>
                {
                    var effect = new FilterEffect
                    {
                        Padding = new Thickness(24d),
                        Primitives = primitives
                    };
                    var context = new SkiaEffectContext(1d, usesOpacitySaveLayer: false, inputBounds);

                    Assert.True(EffectorRuntime.TryCreateFilter(EffectTestHelpers.AsEffect(effect), context, out var filter));
                    Assert.NotNull(filter);
                    return filter!;
                },
                CancellationToken.None)
            .GetAwaiter()
            .GetResult();

    private static TResult RunOnUiThread<TResult>(Func<TResult> action) =>
        Session.Dispatch(action, CancellationToken.None)
            .GetAwaiter()
            .GetResult();

    private static async Task AssertMainWindowFrameIsNonBlankAsync(double width, double height)
    {
        await Session.Dispatch(() =>
        {
            var window = new MainWindow
            {
                Width = width,
                Height = height
            };

            window.Show();
            window.UpdateLayout();

            using var frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            Assert.True(ContainsFrameVariance(frame!, minimumVariantPixels: 1200));
        }, CancellationToken.None);
    }

    private static async Task WithSampleEnvironmentAsync(
        Func<Task> action,
        params (string Name, string? Value)[] variables)
    {
        if (IsHeadlessSampleWindowCoverageDisabled())
        {
            return;
        }

        var originals = new string?[variables.Length];
        for (var index = 0; index < variables.Length; index++)
        {
            originals[index] = Environment.GetEnvironmentVariable(variables[index].Name);
            SetEnvironmentVariable(variables[index].Name, variables[index].Value);
        }

        try
        {
            await action();
        }
        finally
        {
            for (var index = 0; index < variables.Length; index++)
            {
                SetEnvironmentVariable(variables[index].Name, originals[index]);
            }
        }
    }

    private static Task WithSampleEnvironmentAsync(
        (string Name, string? Value) first,
        (string Name, string? Value) second,
        (string Name, string? Value) third,
        Func<Task> action) =>
        WithSampleEnvironmentAsync(action, first, second, third);

    private static bool IsHeadlessSampleWindowCoverageDisabled()
    {
        return string.Equals(
            Environment.GetEnvironmentVariable("EFFECTOR_SKIP_HEADLESS_SAMPLE_WINDOW_TESTS"),
            "1",
            StringComparison.Ordinal);
    }

    private static void SetEnvironmentVariable(string name, string? value)
    {
        Environment.SetEnvironmentVariable(name, string.IsNullOrWhiteSpace(value) ? null : value);
    }

    private static SKPicture CreatePictureSource()
    {
        var bounds = SKRect.Create(0f, 0f, 220f, 140f);
        using var recorder = new SKPictureRecorder();
        var canvas = recorder.BeginRecording(bounds);

        using (var backgroundPaint = new SKPaint())
        {
            backgroundPaint.Shader = SKShader.CreateLinearGradient(
                new SKPoint(0f, 0f),
                new SKPoint(bounds.Right, bounds.Bottom),
                new[]
                {
                    new SKColor(56, 116, 255),
                    new SKColor(47, 204, 162),
                    new SKColor(255, 201, 110)
                },
                null,
                SKShaderTileMode.Clamp);
            canvas.DrawRoundRect(bounds, 24f, 24f, backgroundPaint);
        }

        using (var panelPaint = new SKPaint { Color = new SKColor(255, 255, 255, 216), IsAntialias = true })
        {
            canvas.DrawRoundRect(SKRect.Create(16f, 18f, 84f, 40f), 14f, 14f, panelPaint);
        }

        using (var accentPaint = new SKPaint { Color = new SKColor(255, 121, 93), IsAntialias = true })
        {
            canvas.DrawRoundRect(SKRect.Create(26f, 28f, 34f, 12f), 6f, 6f, accentPaint);
            canvas.DrawRoundRect(SKRect.Create(26f, 48f, 56f, 7f), 3.5f, 3.5f, accentPaint);
        }

        using (var sunPaint = new SKPaint { Color = new SKColor(255, 244, 184), IsAntialias = true })
        {
            canvas.DrawCircle(166f, 38f, 24f, sunPaint);
        }

        using (var mountainPaint = new SKPaint { Color = new SKColor(36, 53, 74), IsAntialias = true })
        {
            var path = new SKPath();
            path.MoveTo(0f, 118f);
            path.LineTo(54f, 76f);
            path.LineTo(94f, 104f);
            path.LineTo(138f, 62f);
            path.LineTo(196f, 118f);
            path.LineTo(220f, 118f);
            path.LineTo(220f, 140f);
            path.LineTo(0f, 140f);
            path.Close();
            canvas.DrawPath(path, mountainPaint);
        }

        return recorder.EndRecording();
    }
}
