using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.VisualTree;
using Effector.FilterEffects;
using SkiaSharp;

namespace Effector.Sample.App;

public partial class MainWindow
{
    private static readonly FilterImageSource SharedFilterImageSource = FilterImageSource.FromPicture(CreateFilterGalleryPicture());

    private void InitializeInlineXamlFilterEffectPreview()
    {
        var border = this.GetVisualDescendants()
            .OfType<Border>()
            .FirstOrDefault(static candidate => candidate.Name == "InlineFilterEffectBorder");
        if (border is null)
        {
            return;
        }

        var parsedBorder = AvaloniaRuntimeXamlLoader.Parse<Border>(
            """
            <Border xmlns="https://github.com/avaloniaui">
              <Border.Effect>
                <FilterEffect Padding="24" />
              </Border.Effect>
            </Border>
            """,
            typeof(FilterEffect).Assembly);

        if ((object?)parsedBorder.Effect is not FilterEffect effect)
        {
            return;
        }

        effect.Primitives = CreateInlineXamlPreviewGraph();
        border.Effect = AsAvaloniaEffect(effect);
    }

    private IEnumerable<EffectSectionDefinition> CreateFilterEffectDefinitions()
    {
        yield return CreateFilterBlendDefinition();
        yield return CreateFilterColorMatrixDefinition();
        yield return CreateFilterComponentTransferDefinition();
        yield return CreateFilterCompositeDefinition();
        yield return CreateFilterConvolveMatrixDefinition();
        yield return CreateFilterDiffuseLightingDefinition();
        yield return CreateFilterDisplacementMapDefinition();
        yield return CreateFilterFloodDefinition();
        yield return CreateFilterGaussianBlurDefinition();
        yield return CreateFilterImageDefinition();
        yield return CreateFilterMergeDefinition();
        yield return CreateFilterMorphologyDefinition();
        yield return CreateFilterOffsetDefinition();
        yield return CreateFilterSpecularLightingDefinition();
        yield return CreateFilterTileDefinition();
        yield return CreateFilterTurbulenceDefinition();
    }

    private EffectSectionDefinition CreateFilterBlendDefinition() =>
        CreateStaticFilterDefinition(
            "SVG feBlend",
            "Blend the live source graphic with a generated flood layer using the same two-input primitive shape as SVG.",
            """
            var effect = new FilterEffect
            {
                Padding = new Thickness(24),
                Primitives = new FilterPrimitiveCollection(
                    new FloodPrimitive(Color.Parse("#6C5CE7"), opacity: 0.72, result: "wash"),
                    new BlendPrimitive(
                        FilterBlendMode.SoftLight,
                        input: FilterInput.SourceGraphic,
                        input2: FilterInput.Named("wash")))
            };
            """,
            new FilterPrimitiveCollection(
                new FloodPrimitive(Color.Parse("#6C5CE7"), opacity: 0.72d, result: "wash"),
                new BlendPrimitive(
                    FilterBlendMode.SoftLight,
                    input: FilterInput.SourceGraphic,
                    input2: FilterInput.Named("wash"))));

    private EffectSectionDefinition CreateFilterColorMatrixDefinition() =>
        CreateStaticFilterDefinition(
            "SVG feColorMatrix",
            "Apply saturate, hue rotate, or full matrix transforms through the immutable filter graph model.",
            """
            var effect = new FilterEffect
            {
                Padding = new Thickness(24),
                Primitives = new FilterPrimitiveCollection(
                    new ColorMatrixPrimitive(
                        FilterColorMatrixType.HueRotate,
                        new FilterNumberCollection(120d)))
            };
            """,
            new FilterPrimitiveCollection(
                new ColorMatrixPrimitive(
                    FilterColorMatrixType.HueRotate,
                    new FilterNumberCollection(120d))));

    private EffectSectionDefinition CreateFilterComponentTransferDefinition() =>
        CreateStaticFilterDefinition(
            "SVG feComponentTransfer",
            "Drive per-channel gamma, linear, discrete, and table transforms without leaving Avalonia's effect property.",
            """
            var effect = new FilterEffect
            {
                Padding = new Thickness(24),
                Primitives = new FilterPrimitiveCollection(
                    new ComponentTransferPrimitive(
                        red: new FilterComponentTransferChannel(
                            FilterComponentTransferType.Gamma,
                            amplitude: 1.1,
                            exponent: 0.75,
                            offset: 0.02),
                        green: new FilterComponentTransferChannel(
                            FilterComponentTransferType.Linear,
                            slope: 1.1,
                            intercept: 0.03),
                        blue: new FilterComponentTransferChannel(
                            FilterComponentTransferType.Table,
                            new FilterNumberCollection(0d, 0.16d, 0.82d, 1d))))
            };
            """,
            new FilterPrimitiveCollection(
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
                        new FilterNumberCollection(0d, 0.16d, 0.82d, 1d)))));

    private EffectSectionDefinition CreateFilterCompositeDefinition() =>
        CreateStaticFilterDefinition(
            "SVG feComposite",
            "Mask a flood layer with the live source alpha using the same arithmetic and Porter-Duff operator surface as SVG.",
            """
            var effect = new FilterEffect
            {
                Padding = new Thickness(24),
                Primitives = new FilterPrimitiveCollection(
                    new FloodPrimitive(Color.Parse("#FFB347"), opacity: 0.92, result: "paint"),
                    new CompositePrimitive(
                        FilterCompositeOperator.In,
                        input: FilterInput.Named("paint"),
                        input2: FilterInput.SourceAlpha))
            };
            """,
            new FilterPrimitiveCollection(
                new FloodPrimitive(Color.Parse("#FFB347"), opacity: 0.92d, result: "paint"),
                new CompositePrimitive(
                    FilterCompositeOperator.In,
                    input: FilterInput.Named("paint"),
                    input2: FilterInput.SourceAlpha)));

    private EffectSectionDefinition CreateFilterConvolveMatrixDefinition() =>
        CreateStaticFilterDefinition(
            "SVG feConvolveMatrix",
            "Run a custom convolution kernel over the source content to sharpen or stylize the preview.",
            """
            var effect = new FilterEffect
            {
                Padding = new Thickness(24),
                Primitives = new FilterPrimitiveCollection(
                    new ConvolveMatrixPrimitive(
                        orderX: 3,
                        orderY: 3,
                        kernelMatrix: new FilterNumberCollection(
                            0, -1, 0,
                            -1, 5, -1,
                            0, -1, 0)))
            };
            """,
            new FilterPrimitiveCollection(
                new ConvolveMatrixPrimitive(
                    orderX: 3,
                    orderY: 3,
                    kernelMatrix: new FilterNumberCollection(
                        0d, -1d, 0d,
                        -1d, 5d, -1d,
                        0d, -1d, 0d))));

    private EffectSectionDefinition CreateFilterDiffuseLightingDefinition() =>
        CreateStaticFilterDefinition(
            "SVG feDiffuseLighting",
            "Use the source alpha as a height map and illuminate it with a directional light to create a soft embossed result.",
            """
            var effect = new FilterEffect
            {
                Padding = new Thickness(24),
                Primitives = new FilterPrimitiveCollection(
                    new DiffuseLightingPrimitive(
                        Color.Parse("#FFF2CC"),
                        new FilterDistantLight(azimuth: 135, elevation: 42),
                        surfaceScale: 3.2,
                        diffuseConstant: 1.4,
                        input: FilterInput.SourceAlpha))
            };
            """,
            new FilterPrimitiveCollection(
                new DiffuseLightingPrimitive(
                    Color.Parse("#FFF2CC"),
                    new FilterDistantLight(azimuth: 135d, elevation: 42d),
                    surfaceScale: 3.2d,
                    diffuseConstant: 1.4d,
                    input: FilterInput.SourceAlpha)));

    private EffectSectionDefinition CreateFilterDisplacementMapDefinition() =>
        CreateStaticFilterDefinition(
            "SVG feDisplacementMap",
            "Use generated turbulence as a displacement field to warp the source graphic with SVG-style channel selectors.",
            """
            var effect = new FilterEffect
            {
                Padding = new Thickness(24),
                Primitives = new FilterPrimitiveCollection(
                    new TurbulencePrimitive(
                        baseFrequencyX: 0.035,
                        baseFrequencyY: 0.02,
                        numOctaves: 2,
                        seed: 3,
                        type: FilterTurbulenceType.Turbulence,
                        result: "noise"),
                    new DisplacementMapPrimitive(
                        scale: 18,
                        xChannelSelector: FilterChannelSelector.R,
                        yChannelSelector: FilterChannelSelector.B,
                        input: FilterInput.SourceGraphic,
                        input2: FilterInput.Named("noise")))
            };
            """,
            new FilterPrimitiveCollection(
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
                    input2: FilterInput.Named("noise"))));

    private EffectSectionDefinition CreateFilterFloodDefinition() =>
        CreateStaticFilterDefinition(
            "SVG feFlood",
            "Generate a solid flood layer directly in the effect graph, useful as a building block for composite and blend chains.",
            """
            var effect = new FilterEffect
            {
                Padding = new Thickness(24),
                Primitives = new FilterPrimitiveCollection(
                    new FloodPrimitive(Color.Parse("#0A84FF"), opacity: 0.86))
            };
            """,
            new FilterPrimitiveCollection(
                new FloodPrimitive(Color.Parse("#0A84FF"), opacity: 0.86d)),
            buildPreviewContent: BuildFloodFilterPreview);

    private EffectSectionDefinition CreateFilterGaussianBlurDefinition() =>
        CreateStaticFilterDefinition(
            "SVG feGaussianBlur",
            "Blur the live content with SVG-style standard deviation values and output padding.",
            """
            var effect = new FilterEffect
            {
                Padding = new Thickness(24),
                Primitives = new FilterPrimitiveCollection(
                    new GaussianBlurPrimitive(stdDeviationX: 6, stdDeviationY: 4))
            };
            """,
            new FilterPrimitiveCollection(
                new GaussianBlurPrimitive(stdDeviationX: 6d, stdDeviationY: 4d)));

    private EffectSectionDefinition CreateFilterImageDefinition() =>
        CreateStaticFilterDefinition(
            "SVG feImage",
            "Inject an external image source into the filter graph, which is the last missing SVG 1.1 primitive from the first pass.",
            """
            var effect = new FilterEffect
            {
                Padding = new Thickness(24),
                Primitives = new FilterPrimitiveCollection(
                    new ImagePrimitive(
                        SharedFilterImageSource,
                        FilterAspectRatio.Default))
            };
            """,
            new FilterPrimitiveCollection(
                new ImagePrimitive(
                    SharedFilterImageSource,
                    FilterAspectRatio.Default)));

    private EffectSectionDefinition CreateFilterMergeDefinition() =>
        CreateStaticFilterDefinition(
            "SVG feMerge",
            "Stack multiple intermediate results in order, similar to a list of feMergeNode children in SVG.",
            """
            var effect = new FilterEffect
            {
                Padding = new Thickness(24),
                Primitives = new FilterPrimitiveCollection(
                    new FloodPrimitive(Color.Parse("#FF7A59"), opacity: 0.28, result: "wash"),
                    new MergePrimitive(
                        new FilterInputCollection(
                            FilterInput.SourceGraphic,
                            FilterInput.Named("wash"))))
            };
            """,
            new FilterPrimitiveCollection(
                new FloodPrimitive(Color.Parse("#FF7A59"), opacity: 0.28d, result: "wash"),
                new MergePrimitive(
                    new FilterInputCollection(
                        FilterInput.SourceGraphic,
                        FilterInput.Named("wash")))));

    private EffectSectionDefinition CreateFilterMorphologyDefinition() =>
        CreateStaticFilterDefinition(
            "SVG feMorphology",
            "Erode or dilate the source to expand silhouettes and push edges outward in a mask-like way.",
            """
            var effect = new FilterEffect
            {
                Padding = new Thickness(24),
                Primitives = new FilterPrimitiveCollection(
                    new MorphologyPrimitive(
                        FilterMorphologyOperator.Dilate,
                        radiusX: 2,
                        radiusY: 2))
            };
            """,
            new FilterPrimitiveCollection(
                new MorphologyPrimitive(
                    FilterMorphologyOperator.Dilate,
                    radiusX: 2d,
                    radiusY: 2d)));

    private EffectSectionDefinition CreateFilterOffsetDefinition() =>
        CreateStaticFilterDefinition(
            "SVG feOffset",
            "Translate the filtered output without changing the underlying layout tree.",
            """
            var effect = new FilterEffect
            {
                Padding = new Thickness(32),
                Primitives = new FilterPrimitiveCollection(
                    new OffsetPrimitive(dx: 18, dy: 14))
            };
            """,
            new FilterPrimitiveCollection(
                new OffsetPrimitive(dx: 18d, dy: 14d)),
            padding: 32d);

    private EffectSectionDefinition CreateFilterSpecularLightingDefinition() =>
        CreateStaticFilterDefinition(
            "SVG feSpecularLighting",
            "Generate a sharper highlight response from the source alpha height map with a spotlight light source.",
            """
            var effect = new FilterEffect
            {
                Padding = new Thickness(24),
                Primitives = new FilterPrimitiveCollection(
                    new SpecularLightingPrimitive(
                        Color.Parse("#FFF8E1"),
                        new FilterSpotLight(
                            x: 140,
                            y: -30,
                            z: 120,
                            pointsAtX: 120,
                            pointsAtY: 70,
                            pointsAtZ: 0,
                            specularExponent: 14,
                            limitingConeAngle: 28),
                        surfaceScale: 3,
                        specularConstant: 1.2,
                        specularExponent: 18,
                        input: FilterInput.SourceAlpha))
            };
            """,
            new FilterPrimitiveCollection(
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
                    input: FilterInput.SourceAlpha)));

    private EffectSectionDefinition CreateFilterTileDefinition() =>
        CreateStaticFilterDefinition(
            "SVG feTile",
            "Repeat a subsection of the source across a larger destination rectangle to emulate SVG tiling behavior.",
            """
            var effect = new FilterEffect
            {
                Padding = new Thickness(24),
                Primitives = new FilterPrimitiveCollection(
                    new TilePrimitive(
                        sourceRect: new Rect(20, 20, 110, 70),
                        destinationRect: new Rect(0, 0, 220, 140)))
            };
            """,
            new FilterPrimitiveCollection(
                new TilePrimitive(
                    sourceRect: new Rect(20d, 20d, 110d, 70d),
                    destinationRect: new Rect(0d, 0d, 220d, 140d))),
            buildPreviewContent: BuildTileFilterPreview);

    private EffectSectionDefinition CreateFilterTurbulenceDefinition() =>
        CreateStaticFilterDefinition(
            "SVG feTurbulence",
            "Generate Perlin noise directly in the graph, useful for clouds, heat haze, water, and displacement sources.",
            """
            var effect = new FilterEffect
            {
                Padding = new Thickness(24),
                Primitives = new FilterPrimitiveCollection(
                    new TurbulencePrimitive(
                        baseFrequencyX: 0.045,
                        baseFrequencyY: 0.028,
                        numOctaves: 3,
                        seed: 9,
                        type: FilterTurbulenceType.FractalNoise,
                        stitchTiles: FilterStitchType.Stitch))
            };
            """,
            new FilterPrimitiveCollection(
                new TurbulencePrimitive(
                    baseFrequencyX: 0.045d,
                    baseFrequencyY: 0.028d,
                    numOctaves: 3,
                    seed: 9d,
                    type: FilterTurbulenceType.FractalNoise,
                    stitchTiles: FilterStitchType.Stitch)),
            buildPreviewContent: BuildTurbulenceFilterPreview);

    private Control BuildTileFilterPreview(object? effect)
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

        var badge = new Border
        {
            Width = 28d,
            Height = 28d,
            CornerRadius = new CornerRadius(999d),
            Background = new SolidColorBrush(Color.Parse("#FFFDF7EE")),
            BorderBrush = new SolidColorBrush(Color.Parse("#55FFFFFF")),
            BorderThickness = new Thickness(1d)
        };
        Canvas.SetLeft(badge, 94d);
        Canvas.SetTop(badge, 22d);

        var detail = new Border
        {
            Width = 88d,
            Height = 14d,
            CornerRadius = new CornerRadius(7d),
            Background = Brushes.White
        };
        Canvas.SetLeft(detail, 24d);
        Canvas.SetTop(detail, 92d);

        return new Border
        {
            Width = 220d,
            Height = 140d,
            Background = new SolidColorBrush(Color.Parse("#FFFF00FF")),
            Effect = AsAvaloniaEffect(effect),
            Child = new Canvas
            {
                Width = 220d,
                Height = 140d,
                Children =
                {
                    accent,
                    badge,
                    detail
                }
            }
        };
    }

    private Control BuildFloodFilterPreview(object? effect)
    {
        var title = new TextBlock
        {
            Text = "Flood Source",
            FontSize = 22d,
            FontWeight = FontWeight.SemiBold,
            Foreground = new SolidColorBrush(Color.Parse("#263238"))
        };
        Canvas.SetLeft(title, 28d);
        Canvas.SetTop(title, 28d);

        var subtitle = new TextBlock
        {
            Width = 180d,
            Text = "The After preview should become a solid SVG flood layer.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.Parse("#55666B"))
        };
        Canvas.SetLeft(subtitle, 28d);
        Canvas.SetTop(subtitle, 64d);

        var badge = new Border
        {
            Width = 82d,
            Height = 82d,
            CornerRadius = new CornerRadius(999d),
            Background = new SolidColorBrush(Color.Parse("#FFFFD27A"))
        };
        Canvas.SetLeft(badge, 212d);
        Canvas.SetTop(badge, 28d);

        var footer = new Border
        {
            Width = 154d,
            Height = 18d,
            CornerRadius = new CornerRadius(9d),
            Background = new SolidColorBrush(Color.Parse("#FF263238"))
        };
        Canvas.SetLeft(footer, 28d);
        Canvas.SetTop(footer, 166d);

        return new Border
        {
            Width = 320d,
            Height = 220d,
            CornerRadius = new CornerRadius(18d),
            ClipToBounds = true,
            Background = new SolidColorBrush(Color.Parse("#FFF3E7C5")),
            Effect = AsAvaloniaEffect(effect),
            Child = new Canvas
            {
                Width = 320d,
                Height = 220d,
                Children =
                {
                    title,
                    subtitle,
                    badge,
                    footer
                }
            }
        };
    }

    private Control BuildTurbulenceFilterPreview(object? effect)
    {
        var avaloniaEffect = AsAvaloniaEffect(effect);
        var host = new Border
        {
            Width = 320d,
            Height = 220d,
            CornerRadius = new CornerRadius(18d),
            ClipToBounds = true,
            Background = new SolidColorBrush(Color.Parse("#FFF4E8BF")),
            Effect = avaloniaEffect
        };

        if (avaloniaEffect is not null)
        {
            return host;
        }

        host.Child = new Grid
        {
            Children =
            {
                new Border
                {
                    Margin = new Thickness(18d),
                    CornerRadius = new CornerRadius(16d),
                    Background = new SolidColorBrush(Color.Parse("#66FFFFFF"))
                },
                new StackPanel
                {
                    Margin = new Thickness(28d, 26d),
                    Spacing = 10d,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = "Noise Source",
                            FontSize = 22d,
                            FontWeight = FontWeight.SemiBold,
                            Foreground = new SolidColorBrush(Color.Parse("#31443F"))
                        },
                        new TextBlock
                        {
                            Width = 188d,
                            Text = "The After preview renders generated turbulence directly from the SVG graph.",
                            TextWrapping = TextWrapping.Wrap,
                            Foreground = new SolidColorBrush(Color.Parse("#55666B"))
                        }
                    }
                }
            }
        };

        return host;
    }

    private static EffectSectionDefinition CreateStaticFilterDefinition(
        string name,
        string description,
        string codeExample,
        FilterPrimitiveCollection primitives,
        double padding = 24d,
        Func<object?, Control>? buildPreviewContent = null)
    {
        var effect = new FilterEffect
        {
            Padding = new Thickness(padding),
            Primitives = primitives
        };

        return new EffectSectionDefinition(
            name,
            description,
            effect,
            codeExample,
            static _ => { },
            buildPreviewContent);
    }

    private static FilterPrimitiveCollection CreateInlineXamlPreviewGraph()
    {
        return new FilterPrimitiveCollection(
            new GaussianBlurPrimitive(stdDeviationX: 4d, result: "blur"),
            new OffsetPrimitive(dx: 6d, dy: 4d, input: FilterInput.Named("blur"), result: "offset"),
            new FloodPrimitive(
                Color.Parse("#7A0A84FF"),
                opacity: 0.9d,
                result: "wash"),
            new CompositePrimitive(
                FilterCompositeOperator.In,
                input: FilterInput.Named("wash"),
                input2: FilterInput.Named("offset"),
                result: "shadow"),
            new MergePrimitive(
                new FilterInputCollection(
                    FilterInput.Named("shadow"),
                    FilterInput.SourceGraphic)));
    }

    private static SKPicture CreateFilterGalleryPicture()
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
                    new SKColor(31, 98, 177),
                    new SKColor(50, 168, 164),
                    new SKColor(255, 196, 87)
                },
                null,
                SKShaderTileMode.Clamp);
            canvas.DrawRoundRect(bounds, 24f, 24f, backgroundPaint);
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

        using (var panelPaint = new SKPaint { Color = new SKColor(255, 255, 255, 212), IsAntialias = true })
        {
            canvas.DrawRoundRect(SKRect.Create(16f, 18f, 84f, 40f), 14f, 14f, panelPaint);
        }

        using (var accentPaint = new SKPaint { Color = new SKColor(255, 121, 93), IsAntialias = true })
        {
            canvas.DrawRoundRect(SKRect.Create(26f, 28f, 34f, 12f), 6f, 6f, accentPaint);
            canvas.DrawRoundRect(SKRect.Create(26f, 48f, 56f, 7f), 3.5f, 3.5f, accentPaint);
        }

        return recorder.EndRecording();
    }
}
