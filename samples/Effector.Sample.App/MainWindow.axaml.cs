using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Effector.FilterEffects;
using Effector.Sample.Effects;

namespace Effector.Sample.App;

public partial class MainWindow : Window
{
    private const double CompactHeroBreakpoint = 1180d;
    private const double StackedFeatureRowBreakpoint = 1240d;
    private const double StackedSectionPreviewBreakpoint = 1320d;
    private const double StackedPreviewContentBreakpoint = 920d;

    private readonly WriteableBitmap _sharedBitmap;
    private readonly List<Grid> _sectionPreviewGrids = new();
    private readonly List<Grid> _previewContentGrids = new();
    private CancellationTokenSource _featureAnimationCts = new();
    private ScrollViewer? _headlessRootScrollViewer;
    private StackPanel? _headlessCodeGalleryHost;

    public MainWindow()
    {
        DataContext = new MainWindowViewModel();
        if (IsEnvironmentSwitchEnabled("EFFECTOR_SAMPLE_HEADLESS_SAFE_MODE"))
        {
            InitializeHeadlessSafeWindow();
        }
        else
        {
            InitializeComponent();
            ApplyHeadlessTestMode();
        }

        _sharedBitmap = CreateSharedBitmap(420, 240);
        BuildGallery();
        ConfigureScrollBehavior();
        ApplyResponsiveLayout(Width);

        Opened += OnOpened;
        Closed += OnClosed;
        SizeChanged += OnSizeChanged;
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void InitializeHeadlessSafeWindow()
    {
        Width = 1400d;
        Height = 980d;
        MinWidth = 640d;
        MinHeight = 480d;
        Title = "Effector Gallery";
        Background = Brushes.White;
        ExtendClientAreaToDecorationsHint = false;
        ExtendClientAreaChromeHints = ExtendClientAreaChromeHints.Default;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.None };
        BuildHeadlessSafeShell();
    }

    private void ApplyHeadlessTestMode()
    {
        if (!IsEnvironmentSwitchEnabled("EFFECTOR_SAMPLE_HIDE_FEATURE_ROWS"))
        {
            return;
        }

        RemoveFeatureRow("FeatureRowOneGrid");
        RemoveFeatureRow("FeatureRowTwoGrid");
    }

    private void RemoveFeatureRow(string name)
    {
        if (FindNamedControl<Grid>(name) is not { Parent: Panel parent } grid)
        {
            return;
        }

        grid.IsVisible = false;
        parent.Children.Remove(grid);
    }

    private void BuildHeadlessSafeShell()
    {
        var titleBlock = new TextBlock
        {
            Text = "Effector Gallery",
            FontSize = 28,
            FontWeight = FontWeight.SemiBold,
            Foreground = new SolidColorBrush(Color.Parse("#1E2A2D"))
        };

        var subtitleBlock = new TextBlock
        {
            Text = "Headless-safe gallery shell for runtime render verification.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.Parse("#55666B"))
        };

        var galleryHost = new StackPanel
        {
            Spacing = 18
        };

        var shell = new StackPanel
        {
            Margin = new Thickness(24),
            Spacing = 18,
            Children =
            {
                new Border
                {
                    Padding = new Thickness(20),
                    CornerRadius = new CornerRadius(20),
                    Background = new SolidColorBrush(Color.Parse("#FFFDFCFA")),
                    BorderBrush = new SolidColorBrush(Color.Parse("#22003845")),
                    BorderThickness = new Thickness(1),
                    Child = new StackPanel
                    {
                        Spacing = 10,
                        Children =
                        {
                            titleBlock,
                            subtitleBlock
                        }
                    }
                },
                new Border
                {
                    Padding = new Thickness(20),
                    CornerRadius = new CornerRadius(20),
                    Background = new SolidColorBrush(Color.Parse("#FFFDFCFA")),
                    BorderBrush = new SolidColorBrush(Color.Parse("#22003845")),
                    BorderThickness = new Thickness(1),
                    Child = galleryHost
                }
            }
        };

        _headlessRootScrollViewer = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = shell
        };

        _headlessCodeGalleryHost = galleryHost;
        Content = _headlessRootScrollViewer;
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        InitializeInlineXamlFilterEffectPreview();

        if (!IsEnvironmentSwitchEnabled("EFFECTOR_SAMPLE_DISABLE_FEATURE_ANIMATIONS"))
        {
            StartFeatureAnimations();
        }

        ApplyResponsiveLayout(Bounds.Width > 0d ? Bounds.Width : Width);

        if (IsEnvironmentSwitchEnabled("EFFECTOR_SAMPLE_AUTO_SCROLL_TO_FIRST_SHADER"))
        {
            Dispatcher.UIThread.Post(ScrollToFirstShaderPreview, DispatcherPriority.Background);
        }
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        SizeChanged -= OnSizeChanged;
        _featureAnimationCts.Cancel();
        _featureAnimationCts.Dispose();
        _sharedBitmap.Dispose();
    }

    private void OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        ApplyResponsiveLayout(e.NewSize.Width);
    }

    private void ScrollToFirstShaderPreview()
    {
        var target = this.GetVisualDescendants()
            .OfType<Grid>()
            .FirstOrDefault(static grid => HasEffectType(
                grid.Effect,
                typeof(ScanlineShaderEffect),
                typeof(GridShaderEffect),
                typeof(SpotlightShaderEffect)));
        target?.BringIntoView();
    }

    private static bool HasEffectType(object? effect, params Type[] effectTypes)
    {
        var effectType = effect?.GetType();
        if (effectType is null)
        {
            return false;
        }

        foreach (var candidateType in effectTypes)
        {
            if (effectType == candidateType)
            {
                return true;
            }
        }

        return false;
    }

    private static IEffect? AsAvaloniaEffect(object? effect) => effect as IEffect;

    private static bool IsEnvironmentSwitchEnabled(string variableName)
    {
        var value = Environment.GetEnvironmentVariable(variableName);
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        value = value.Trim();
        return value.Equals("1", StringComparison.Ordinal) ||
               value.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("on", StringComparison.OrdinalIgnoreCase) ||
               bool.TryParse(value, out var enabled) && enabled;
    }

    private static bool TryGetSectionLimit(out int limit)
    {
        limit = 0;
        var value = Environment.GetEnvironmentVariable("EFFECTOR_SAMPLE_LIMIT_SECTIONS");
        return !string.IsNullOrWhiteSpace(value) &&
               int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out limit) &&
               limit >= 0;
    }

    private static bool TryGetSectionFilter(out string filter)
    {
        filter = Environment.GetEnvironmentVariable("EFFECTOR_SAMPLE_SECTION_FILTER")?.Trim() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(filter);
    }

    private void BuildGallery()
    {
        var host = _headlessCodeGalleryHost ?? FindNamedControl<StackPanel>("CodeGalleryHost");
        if (host is null)
        {
            return;
        }

        var definitions = CreateDefinitions();
        if (TryGetSectionLimit(out var limit))
        {
            definitions = definitions.Take(limit).ToArray();
        }

        if (TryGetSectionFilter(out var sectionFilter))
        {
            definitions = definitions
                .Where(definition => definition.Name.Contains(sectionFilter, StringComparison.OrdinalIgnoreCase))
                .ToArray();
        }

        foreach (var definition in definitions)
        {
            host.Children.Add(BuildSection(definition));
        }
    }

    private void ConfigureScrollBehavior()
    {
        var scrollViewer = _headlessRootScrollViewer ?? FindNamedControl<ScrollViewer>("RootScrollViewer");
        if (scrollViewer is not null)
        {
            scrollViewer.AddHandler(
                InputElement.PointerWheelChangedEvent,
                OnRootScrollViewerPointerWheelChanged,
                RoutingStrategies.Bubble,
                handledEventsToo: true);
        }
    }

    private void StartFeatureAnimations()
    {
        _featureAnimationCts.Cancel();
        _featureAnimationCts.Dispose();
        _featureAnimationCts = new CancellationTokenSource();

        if (FindNamedControl<Border>("AnimatedEffectPreview") is { } preview)
        {
            _ = RunAnimatedEffectPreviewAsync(preview, _featureAnimationCts.Token);
        }
    }

    private T? FindNamedControl<T>(string name)
        where T : Control
    {
        try
        {
            var named = this.FindControl<T>(name);
            if (named is not null)
            {
                return named;
            }
        }
        catch (InvalidOperationException)
        {
            // Headless and dynamically rebuilt shells can lack a parent name scope.
        }

        return this.GetVisualDescendants()
            .OfType<T>()
            .FirstOrDefault(candidate => string.Equals(candidate.Name, name, StringComparison.Ordinal));
    }

    private static async Task RunAnimatedEffectPreviewAsync(Border preview, CancellationToken cancellationToken)
    {
        var animation = new Animation
        {
            Duration = TimeSpan.FromSeconds(3.2d),
            IterationCount = new IterationCount(2ul),
            PlaybackDirection = PlaybackDirection.Alternate,
            FillMode = FillMode.Both,
            Easing = new SineEaseInOut(),
            Children =
            {
                new KeyFrame
                {
                    Cue = new Cue(0d),
                    Setters =
                    {
                        new Setter
                        {
                            Property = Visual.EffectProperty,
                            Value = new GlowEffect
                            {
                                BlurRadius = 4d,
                                Intensity = 0.18d,
                                Color = Color.Parse("#7AD9FF")
                            }
                        }
                    }
                },
                new KeyFrame
                {
                    Cue = new Cue(0.5d),
                    Setters =
                    {
                        new Setter
                        {
                            Property = Visual.EffectProperty,
                            Value = new GlowEffect
                            {
                                BlurRadius = 20d,
                                Intensity = 0.95d,
                                Color = Color.Parse("#FFD54A")
                            }
                        }
                    }
                },
                new KeyFrame
                {
                    Cue = new Cue(1d),
                    Setters =
                    {
                        new Setter
                        {
                            Property = Visual.EffectProperty,
                            Value = new GlowEffect
                            {
                                BlurRadius = 10d,
                                Intensity = 0.55d,
                                Color = Color.Parse("#EB6A8E")
                            }
                        }
                    }
                }
            }
        };

        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await animation.RunAsync(preview, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void ApplyResponsiveLayout(double width)
    {
        if (_headlessRootScrollViewer is not null)
        {
            var headlessStackSectionPreviews = width < StackedSectionPreviewBreakpoint;
            foreach (var sectionPreviewGrid in _sectionPreviewGrids)
            {
                ApplyTwoColumnGridLayout(sectionPreviewGrid, headlessStackSectionPreviews, "*,*", 16d);
            }

            var headlessStackPreviewContent = width < StackedPreviewContentBreakpoint;
            foreach (var previewContentGrid in _previewContentGrids)
            {
                ApplyTwoColumnGridLayout(previewContentGrid, headlessStackPreviewContent, "1.15*,0.85*", 14d);
            }

            return;
        }

        ApplyHeroLayout(width < CompactHeroBreakpoint);

        if (this.FindControl<Grid>("FeatureRowOneGrid") is { } featureRowOne)
        {
            ApplyTwoColumnGridLayout(featureRowOne, width < StackedFeatureRowBreakpoint, "*,*", 24d);
        }

        if (this.FindControl<Grid>("FeatureRowTwoGrid") is { } featureRowTwo)
        {
            ApplyTwoColumnGridLayout(featureRowTwo, width < StackedFeatureRowBreakpoint, "*,*", 24d);
        }

        var stackSectionPreviews = width < StackedSectionPreviewBreakpoint;
        foreach (var sectionPreviewGrid in _sectionPreviewGrids)
        {
            ApplyTwoColumnGridLayout(sectionPreviewGrid, stackSectionPreviews, "*,*", 16d);
        }

        var stackPreviewContent = width < StackedPreviewContentBreakpoint;
        foreach (var previewContentGrid in _previewContentGrids)
        {
            ApplyTwoColumnGridLayout(previewContentGrid, stackPreviewContent, "1.15*,0.85*", 14d);
        }
    }

    private void ApplyHeroLayout(bool compact)
    {
        if (this.FindControl<Grid>("HeroContentGrid") is not { Children.Count: >= 2 } heroGrid)
        {
            return;
        }

        if (compact)
        {
            heroGrid.ColumnDefinitions = new ColumnDefinitions("1*");
            heroGrid.RowDefinitions = new RowDefinitions("Auto,Auto");
            heroGrid.ColumnSpacing = 0d;
            heroGrid.RowSpacing = 18d;
            Grid.SetColumn(heroGrid.Children[0], 0);
            Grid.SetRow(heroGrid.Children[0], 0);
            Grid.SetColumn(heroGrid.Children[1], 0);
            Grid.SetRow(heroGrid.Children[1], 1);
            return;
        }

        heroGrid.ColumnDefinitions = new ColumnDefinitions("2.2*,1*");
        heroGrid.RowDefinitions = new RowDefinitions("Auto");
        heroGrid.ColumnSpacing = 24d;
        heroGrid.RowSpacing = 0d;
        Grid.SetColumn(heroGrid.Children[0], 0);
        Grid.SetRow(heroGrid.Children[0], 0);
        Grid.SetColumn(heroGrid.Children[1], 1);
        Grid.SetRow(heroGrid.Children[1], 0);
    }

    private static void ApplyTwoColumnGridLayout(Grid grid, bool stacked, string expandedColumns, double spacing)
    {
        if (grid.Children.Count < 2)
        {
            return;
        }

        if (stacked)
        {
            grid.ColumnDefinitions = new ColumnDefinitions("1*");
            grid.RowDefinitions = new RowDefinitions("Auto,Auto");
            grid.ColumnSpacing = 0d;
            grid.RowSpacing = spacing;
            Grid.SetColumn(grid.Children[0], 0);
            Grid.SetRow(grid.Children[0], 0);
            Grid.SetColumn(grid.Children[1], 0);
            Grid.SetRow(grid.Children[1], 1);
            return;
        }

        grid.ColumnDefinitions = new ColumnDefinitions(expandedColumns);
        grid.RowDefinitions = new RowDefinitions("Auto");
        grid.ColumnSpacing = spacing;
        grid.RowSpacing = 0d;
        Grid.SetColumn(grid.Children[0], 0);
        Grid.SetRow(grid.Children[0], 0);
        Grid.SetColumn(grid.Children[1], 1);
        Grid.SetRow(grid.Children[1], 0);
    }

    private void OnRootScrollViewerPointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (e.Handled ||
            sender is not ScrollViewer scrollViewer ||
            e.Source is not Visual sourceVisual ||
            sourceVisual.FindAncestorOfType<Slider>(includeSelf: true) is null)
        {
            return;
        }

        var maxOffsetY = Math.Max(0d, scrollViewer.Extent.Height - scrollViewer.Viewport.Height);
        if (maxOffsetY <= 0d)
        {
            return;
        }

        var nextOffsetY = Math.Clamp(scrollViewer.Offset.Y - (e.Delta.Y * 56d), 0d, maxOffsetY);
        if (Math.Abs(nextOffsetY - scrollViewer.Offset.Y) < 0.01d)
        {
            return;
        }

        scrollViewer.Offset = new Vector(scrollViewer.Offset.X, nextOffsetY);
        e.Handled = true;
    }

    private IEnumerable<EffectSectionDefinition> CreateDefinitions()
    {
        yield return CreateTintDefinition();
        yield return CreatePixelateDefinition();
        yield return CreateGrayscaleDefinition();
        yield return CreateSepiaDefinition();
        yield return CreateSaturationDefinition();
        yield return CreateBrightnessContrastDefinition();
        yield return CreateInvertDefinition();
        yield return CreateGlowDefinition();
        yield return CreateSharpenDefinition();
        yield return CreateEdgeDetectDefinition();
        yield return CreateScanlineShaderDefinition();
        yield return CreateGridShaderDefinition();
        yield return CreateSpotlightShaderDefinition();
        yield return CreatePointerSpotlightShaderDefinition();
        yield return CreateReactiveGridShaderDefinition();
        yield return CreateWaterRippleShaderDefinition();
        yield return CreateBurningFlameShaderDefinition();

        foreach (var definition in CreateFilterEffectDefinitions())
        {
            yield return definition;
        }
    }

    private EffectSectionDefinition CreateTintDefinition()
    {
        var effect = new TintEffect();
        return new EffectSectionDefinition(
            "Tint",
            "Colorize content without flattening the underlying layout and typography.",
            effect,
            """
            <Border.Effect>
              <effects:TintEffect Color="#0F9D8E" Strength="0.55" />
            </Border.Effect>
            """,
            panel =>
            {
                AddSlider(panel, "Strength", 0d, 1d, effect.Strength, v => effect.Strength = v);
                AddColorSwatches(
                    panel,
                    "Tint",
                    new[]
                    {
                        ("Marine", Color.Parse("#0F9D8E")),
                        ("Coral", Color.Parse("#E96943")),
                        ("Gold", Color.Parse("#D5A021")),
                        ("Ink", Color.Parse("#4256A4"))
                    },
                    color => effect.Color = color);
            });
    }

    private EffectSectionDefinition CreatePixelateDefinition()
    {
        var effect = new PixelateEffect();
        return new EffectSectionDefinition(
            "Pixelate",
            "Break the preview into chunky cells that are useful for redaction, lo-fi art, or preview masking.",
            effect,
            """
            <Border.Effect>
              <effects:PixelateEffect CellSize="12" />
            </Border.Effect>
            """,
            panel => AddSlider(panel, "Cell Size", 1d, 36d, effect.CellSize, v => effect.CellSize = v, format: "0"));
    }

    private EffectSectionDefinition CreateGrayscaleDefinition()
    {
        var effect = new GrayscaleEffect();
        return new EffectSectionDefinition(
            "Grayscale",
            "Desaturate the scene progressively to emphasize structure, contrast, and layout.",
            effect,
            """
            <Border.Effect>
              <effects:GrayscaleEffect Amount="1" />
            </Border.Effect>
            """,
            panel => AddSlider(panel, "Amount", 0d, 1d, effect.Amount, v => effect.Amount = v));
    }

    private EffectSectionDefinition CreateSepiaDefinition()
    {
        var effect = new SepiaEffect();
        return new EffectSectionDefinition(
            "Sepia",
            "Push the preview toward a warm editorial palette with adjustable intensity.",
            effect,
            """
            <Border.Effect>
              <effects:SepiaEffect Amount="0.85" />
            </Border.Effect>
            """,
            panel => AddSlider(panel, "Amount", 0d, 1d, effect.Amount, v => effect.Amount = v));
    }

    private EffectSectionDefinition CreateSaturationDefinition()
    {
        var effect = new SaturationEffect();
        return new EffectSectionDefinition(
            "Saturation",
            "Pull color down for restraint or push it up for punchier UI and illustration work.",
            effect,
            """
            <Border.Effect>
              <effects:SaturationEffect Saturation="1.4" />
            </Border.Effect>
            """,
            panel => AddSlider(panel, "Saturation", 0d, 2.5d, effect.Saturation, v => effect.Saturation = v));
    }

    private EffectSectionDefinition CreateBrightnessContrastDefinition()
    {
        var effect = new BrightnessContrastEffect();
        return new EffectSectionDefinition(
            "Brightness + Contrast",
            "Tune punch and exposure independently for denser or softer visual treatment.",
            effect,
            """
            <Border.Effect>
              <effects:BrightnessContrastEffect Brightness="0.1" Contrast="1.25" />
            </Border.Effect>
            """,
            panel =>
            {
                AddSlider(panel, "Brightness", -1d, 1d, effect.Brightness, v => effect.Brightness = v);
                AddSlider(panel, "Contrast", 0d, 2.5d, effect.Contrast, v => effect.Contrast = v);
            });
    }

    private EffectSectionDefinition CreateInvertDefinition()
    {
        var effect = new InvertEffect();
        return new EffectSectionDefinition(
            "Invert",
            "Flip the palette for high-contrast inspection or poster-like treatments.",
            effect,
            """
            <Border.Effect>
              <effects:InvertEffect Amount="1" />
            </Border.Effect>
            """,
            panel => AddSlider(panel, "Amount", 0d, 1d, effect.Amount, v => effect.Amount = v));
    }

    private EffectSectionDefinition CreateGlowDefinition()
    {
        var effect = new GlowEffect();
        return new EffectSectionDefinition(
            "Glow",
            "Wrap the scene in a blurred colored halo that expands beyond the original bounds.",
            effect,
            """
            <Border.Effect>
              <effects:GlowEffect Color="#FFD54A" BlurRadius="12" Intensity="0.9" />
            </Border.Effect>
            """,
            panel =>
            {
                AddSlider(panel, "Blur Radius", 0d, 24d, effect.BlurRadius, v => effect.BlurRadius = v, format: "0");
                AddSlider(panel, "Intensity", 0d, 1d, effect.Intensity, v => effect.Intensity = v);
                AddColorSwatches(
                    panel,
                    "Glow",
                    new[]
                    {
                        ("Sun", Color.Parse("#FFD54A")),
                        ("Mint", Color.Parse("#1DC98A")),
                        ("Rose", Color.Parse("#EB6A8E")),
                        ("Sky", Color.Parse("#5CB2FF"))
                    },
                    color => effect.Color = color);
            });
    }

    private EffectSectionDefinition CreateSharpenDefinition()
    {
        var effect = new SharpenEffect();
        return new EffectSectionDefinition(
            "Sharpen",
            "Add edge definition back into the preview with a simple convolution kernel.",
            effect,
            """
            <Border.Effect>
              <effects:SharpenEffect Strength="1.1" />
            </Border.Effect>
            """,
            panel => AddSlider(panel, "Strength", 0d, 2d, effect.Strength, v => effect.Strength = v));
    }

    private EffectSectionDefinition CreateEdgeDetectDefinition()
    {
        var effect = new EdgeDetectEffect();
        return new EffectSectionDefinition(
            "Edge Detect",
            "Pull out structural edges for inspection, stylization, or posterized rendering.",
            effect,
            """
            <Border.Effect>
              <effects:EdgeDetectEffect Strength="1" />
            </Border.Effect>
            """,
            panel => AddSlider(panel, "Strength", 0d, 2d, effect.Strength, v => effect.Strength = v));
    }

    private EffectSectionDefinition CreateScanlineShaderDefinition()
    {
        var effect = new ScanlineShaderEffect();
        return new EffectSectionDefinition(
            "Shader Scanlines",
            "A runtime SKSL overlay pass that lays in broadcast-style scanlines without leaving the normal Visual.Effect model.",
            effect,
            """
            <Border.Effect>
              <effects:ScanlineShaderEffect Spacing="8" Strength="0.28" />
            </Border.Effect>
            """,
            panel =>
            {
                AddSlider(panel, "Spacing", 2d, 24d, effect.Spacing, v => effect.Spacing = v, format: "0");
                AddSlider(panel, "Strength", 0d, 0.8d, effect.Strength, v => effect.Strength = v);
            });
    }

    private EffectSectionDefinition CreateGridShaderDefinition()
    {
        var effect = new GridShaderEffect();
        return new EffectSectionDefinition(
            "Shader Grid Overlay",
            "Project a procedural runtime grid over the rendered content for HUD, debug, or sci-fi presentation treatments.",
            effect,
            """
            <Border.Effect>
              <effects:GridShaderEffect CellSize="22" Strength="0.24" Color="#3FC3FF" />
            </Border.Effect>
            """,
            panel =>
            {
                AddSlider(panel, "Cell Size", 8d, 64d, effect.CellSize, v => effect.CellSize = v, format: "0");
                AddSlider(panel, "Strength", 0d, 0.8d, effect.Strength, v => effect.Strength = v);
                AddColorSwatches(
                    panel,
                    "Grid",
                    new[]
                    {
                        ("Cyan", Color.Parse("#3FC3FF")),
                        ("Lime", Color.Parse("#74D64F")),
                        ("Amber", Color.Parse("#FFB347")),
                        ("Rose", Color.Parse("#F06B92"))
                    },
                    color => effect.Color = color);
            });
    }

    private EffectSectionDefinition CreateSpotlightShaderDefinition()
    {
        var effect = new SpotlightShaderEffect();
        return new EffectSectionDefinition(
            "Shader Spotlight",
            "Add a procedural spotlight wash using a runtime shader and screen blending to emphasize a region of the preview.",
            effect,
            """
            <Border.Effect>
              <effects:SpotlightShaderEffect CenterX="0.55" CenterY="0.4" Radius="0.42" Strength="0.35" Color="#FFD26B" />
            </Border.Effect>
            """,
            panel =>
            {
                AddSlider(panel, "Center X", 0d, 1d, effect.CenterX, v => effect.CenterX = v);
                AddSlider(panel, "Center Y", 0d, 1d, effect.CenterY, v => effect.CenterY = v);
                AddSlider(panel, "Radius", 0.05d, 1d, effect.Radius, v => effect.Radius = v);
                AddSlider(panel, "Strength", 0d, 1d, effect.Strength, v => effect.Strength = v);
                AddColorSwatches(
                    panel,
                    "Spotlight",
                    new[]
                    {
                        ("Gold", Color.Parse("#FFD26B")),
                        ("Ice", Color.Parse("#B9ECFF")),
                        ("Mint", Color.Parse("#9DFFC2")),
                        ("Rose", Color.Parse("#FF9EC8"))
                    },
                    color => effect.Color = color);
            });
    }

    private EffectSectionDefinition CreatePointerSpotlightShaderDefinition()
    {
        var effect = new PointerSpotlightShaderEffect();
        return new EffectSectionDefinition(
            "Pointer Spotlight Shader",
            "Move, press, and release over the preview to drive shader uniforms from live pointer state without leaving Visual.Effect.",
            effect,
            """
            <Border.Effect>
              <effects:PointerSpotlightShaderEffect Radius="0.24" Strength="0.28" PressBoost="0.42" Color="#FFD26B" />
            </Border.Effect>
            """,
            panel =>
            {
                AddSlider(panel, "Radius", 0.05d, 0.65d, effect.Radius, v => effect.Radius = v);
                AddSlider(panel, "Strength", 0d, 1d, effect.Strength, v => effect.Strength = v);
                AddSlider(panel, "Press Boost", 0d, 1d, effect.PressBoost, v => effect.PressBoost = v);
                AddColorSwatches(
                    panel,
                    "Pointer Spotlight",
                    new[]
                    {
                        ("Gold", Color.Parse("#FFD26B")),
                        ("Ice", Color.Parse("#B9ECFF")),
                        ("Mint", Color.Parse("#9DFFC2")),
                        ("Rose", Color.Parse("#FF9EC8"))
                    },
                    color => effect.Color = color);
            });
    }

    private EffectSectionDefinition CreateReactiveGridShaderDefinition()
    {
        var effect = new ReactiveGridShaderEffect();
        return new EffectSectionDefinition(
            "Reactive Grid Shader",
            "A procedural grid overlay that intensifies around the pointer and boosts further while pressed.",
            effect,
            """
            <Border.Effect>
              <effects:ReactiveGridShaderEffect CellSize="22" Strength="0.24" PressBoost="0.36" Color="#64D6FF" />
            </Border.Effect>
            """,
            panel =>
            {
                AddSlider(panel, "Cell Size", 8d, 64d, effect.CellSize, v => effect.CellSize = v, format: "0");
                AddSlider(panel, "Strength", 0d, 1d, effect.Strength, v => effect.Strength = v);
                AddSlider(panel, "Press Boost", 0d, 1d, effect.PressBoost, v => effect.PressBoost = v);
                AddColorSwatches(
                    panel,
                    "Reactive Grid",
                    new[]
                    {
                        ("Cyan", Color.Parse("#64D6FF")),
                        ("Lime", Color.Parse("#8AE06A")),
                        ("Amber", Color.Parse("#FFB347")),
                        ("Rose", Color.Parse("#F06B92"))
                    },
                    color => effect.Color = color);
            });
    }

    private EffectSectionDefinition CreateWaterRippleShaderDefinition()
    {
        var effect = new WaterRippleShaderEffect();
        return new EffectSectionDefinition(
            "Water Ripple Review Cue",
            "Press and drag through the preview to drop ripple markers over flood maps, shoreline photography, or touchscreen wayfinding surfaces while keeping the underlying scene readable.",
            effect,
            """
            <Border.Effect>
              <effects:WaterRippleShaderEffect Distortion="12" MaxRadius="0.72" RingWidth="0.065" TintStrength="0.18" Color="#7FD6FF" />
            </Border.Effect>
            """,
            panel =>
            {
                AddSlider(panel, "Distortion", 0d, 18d, effect.Distortion, v => effect.Distortion = v);
                AddSlider(panel, "Reach", 0.2d, 0.95d, effect.MaxRadius, v => effect.MaxRadius = v);
                AddSlider(panel, "Ring Width", 0.02d, 0.18d, effect.RingWidth, v => effect.RingWidth = v);
                AddSlider(panel, "Tint Strength", 0d, 0.42d, effect.TintStrength, v => effect.TintStrength = v);
                AddColorSwatches(
                    panel,
                    "Water Ripple",
                    new[]
                    {
                        ("Lagoon", Color.Parse("#7FD6FF")),
                        ("Storm", Color.Parse("#86A9FF")),
                        ("Foam", Color.Parse("#A8F1E7")),
                        ("Alert", Color.Parse("#FFB36B"))
                    },
                    color => effect.Color = color);
            });
    }

    private EffectSectionDefinition CreateBurningFlameShaderDefinition()
    {
        var effect = new BurningFlameShaderEffect();
        return new EffectSectionDefinition(
            "Burning Action Button",
            "Click the action button to ignite a slow burn across its face. This is useful for short-lived armed, destructive, or high-attention states where you want urgency without opening a modal.",
            effect,
            """
            <Button.Effect>
              <effects:BurningFlameShaderEffect FlameHeight="0.72" Distortion="8" GlowStrength="0.58" SmokeStrength="0.24" CoreColor="#FFD36B" EmberColor="#FF5B1F" />
            </Button.Effect>
            """,
            panel =>
            {
                AddSlider(panel, "Flame Height", 0.18d, 0.95d, effect.FlameHeight, v => effect.FlameHeight = v);
                AddSlider(panel, "Distortion", 0d, 18d, effect.Distortion, v => effect.Distortion = v, format: "0.0");
                AddSlider(panel, "Glow", 0d, 1d, effect.GlowStrength, v => effect.GlowStrength = v);
                AddSlider(panel, "Smoke", 0d, 0.8d, effect.SmokeStrength, v => effect.SmokeStrength = v);
                AddColorSwatches(
                    panel,
                    "Core",
                    new[]
                    {
                        ("Gold", Color.Parse("#FFD36B")),
                        ("White", Color.Parse("#FFF0B0")),
                        ("Rose", Color.Parse("#FFB36B")),
                        ("Blue", Color.Parse("#8DDCFF"))
                    },
                    color => effect.CoreColor = color);
                AddColorSwatches(
                    panel,
                    "Ember",
                    new[]
                    {
                        ("Ember", Color.Parse("#FF5B1F")),
                        ("Crimson", Color.Parse("#E53E3E")),
                        ("Copper", Color.Parse("#C96A1E")),
                        ("Violet", Color.Parse("#9B4DFF"))
                    },
                    color => effect.EmberColor = color);
            },
            BuildBurningButtonPreview);
    }

    private Border BuildSection(EffectSectionDefinition definition)
    {
        var container = new Border
        {
            Padding = new Thickness(18),
            CornerRadius = new CornerRadius(22),
            Background = new SolidColorBrush(Color.Parse("#FFFDFCFA")),
            BorderBrush = new SolidColorBrush(Color.Parse("#22003845")),
            BorderThickness = new Thickness(1),
            Tag = definition.Name + "::Section"
        };

        var root = new StackPanel
        {
            Spacing = 14
        };

        root.Children.Add(new TextBlock
        {
            Text = definition.Name,
            FontSize = 24,
            FontWeight = FontWeight.SemiBold,
            Foreground = new SolidColorBrush(Color.Parse("#1E2A2D"))
        });

        root.Children.Add(new TextBlock
        {
            Text = definition.Description,
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.Parse("#55666B"))
        });

        var previews = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*"),
            ColumnSpacing = 16
        };
        _sectionPreviewGrids.Add(previews);
        previews.Children.Add(CreatePreviewTile(definition.Name, "Before", null, definition.BuildPreviewContent));
        previews.Children.Add(CreatePreviewTile(definition.Name, "After", definition.Effect, definition.BuildPreviewContent).WithColumn(1));
        root.Children.Add(previews);

        var controlsHost = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            ItemWidth = double.NaN,
            Margin = new Thickness(0, 2, 0, 0)
        };
        definition.BuildControls(controlsHost);
        root.Children.Add(controlsHost);

        root.Children.Add(new Border
        {
            Background = new SolidColorBrush(Color.Parse("#FFF7F4EE")),
            Padding = new Thickness(14),
            CornerRadius = new CornerRadius(16),
            Child = new TextBlock
            {
                Text = definition.XamlExample,
                FontFamily = new FontFamily("Cascadia Code, Consolas"),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Color.Parse("#4E5963"))
            }
        });

        container.Child = root;
        return container;
    }

    private Border CreatePreviewTile(string sectionName, string title, object? effect, Func<object?, Control>? buildPreviewContent = null)
    {
        var outer = new Border
        {
            Padding = new Thickness(16),
            CornerRadius = new CornerRadius(20),
            Background = new SolidColorBrush(Color.Parse("#FFF8F6F1")),
            ClipToBounds = true,
            Tag = sectionName + "::" + title + "::Tile"
        };

        var root = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto"),
            RowSpacing = 14
        };

        root.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 18,
            FontWeight = FontWeight.SemiBold,
            Foreground = new SolidColorBrush(Color.Parse("#223033"))
        });

        var previewContent = buildPreviewContent?.Invoke(effect) ?? CreateDefaultPreviewContent(effect);
        previewContent.Tag = sectionName + "::" + title;
        root.Children.Add(previewContent.WithRow(1));
        outer.Child = root;
        return outer;
    }

    private Control CreateDefaultPreviewContent(object? effect)
    {
        var preview = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("1.15*,0.85*"),
            ColumnSpacing = 14
        };
        _previewContentGrids.Add(preview);

        preview.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(16),
            ClipToBounds = true,
            Child = BuildImagePanel()
        });

        preview.Children.Add(BuildUiPanel().WithColumn(1));
        preview.RowDefinitions = new RowDefinitions("Auto");

        if (AsAvaloniaEffect(effect) is { } avaloniaEffect)
        {
            preview.Effect = avaloniaEffect;
        }

        return preview;
    }

    private Control BuildImagePanel()
    {
        var grid = new Grid();
        grid.Children.Add(new Image
        {
            Source = _sharedBitmap,
            Stretch = Stretch.UniformToFill,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        });

        grid.Children.Add(new Border
        {
            Margin = new Thickness(16),
            Width = 74,
            Height = 74,
            CornerRadius = new CornerRadius(999),
            Background = new SolidColorBrush(Color.Parse("#55FFFFFF")),
            BorderBrush = new SolidColorBrush(Color.Parse("#44FFFFFF")),
            BorderThickness = new Thickness(2),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top
        });

        grid.Children.Add(new Border
        {
            Margin = new Thickness(16),
            Padding = new Thickness(10, 6),
            CornerRadius = new CornerRadius(999),
            Background = new SolidColorBrush(Color.Parse("#AA12232A")),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Bottom,
            Child = new TextBlock
            {
                Text = "bitmap + vectors",
                Foreground = Brushes.White,
                FontSize = 12,
                FontWeight = FontWeight.SemiBold
            }
        });

        return grid;
    }

    private Control BuildUiPanel()
    {
        var root = new StackPanel
        {
            Spacing = 12
        };

        root.Children.Add(new Border
        {
            Padding = new Thickness(14),
            CornerRadius = new CornerRadius(14),
            Background = new SolidColorBrush(Color.Parse("#FFF9FCFF")),
            Child = new StackPanel
            {
                Spacing = 6,
                Children =
                {
                    new TextBlock
                    {
                        Text = "Project Atlas",
                        FontSize = 18,
                        FontWeight = FontWeight.SemiBold,
                        Foreground = new SolidColorBrush(Color.Parse("#223033"))
                    },
                    new TextBlock
                    {
                        Text = "Mixed preview content so each effect hits images, typography, and small UI surfaces at once.",
                        TextWrapping = TextWrapping.Wrap,
                        Foreground = new SolidColorBrush(Color.Parse("#5D6C72"))
                    }
                }
            }
        });

        var chips = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            ItemSpacing = 8,
            LineSpacing = 8
        };
        chips.Children.Add(CreateChip("preview"));
        chips.Children.Add(CreateChip("controls"));
        chips.Children.Add(CreateChip("weaving"));
        root.Children.Add(chips);

        root.Children.Add(CreateStatRow("Latency", "14 ms", "#0E9E88"));
        root.Children.Add(CreateStatRow("Coverage", "SVG 1.1 + core", "#C9781D"));
        root.Children.Add(CreateStatRow("Renderer", "Skia", "#2D6CC1"));

        return root;
    }

    private Control BuildBurningButtonPreview(object? effect)
    {
        var root = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto"),
            RowSpacing = 14
        };

        root.Children.Add(new Border
        {
            Padding = new Thickness(12, 8),
            CornerRadius = new CornerRadius(999),
            HorizontalAlignment = HorizontalAlignment.Left,
            Background = new SolidColorBrush(Color.Parse("#1A7C2D12")),
            Child = new TextBlock
            {
                Text = "click to ignite",
                Foreground = new SolidColorBrush(Color.Parse("#9C4A19")),
                FontWeight = FontWeight.SemiBold
            }
        });

        root.Children.Add(new TextBlock
        {
            Text = "Transient burn communicates that a destructive or irreversible action is armed and still cooling down.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.Parse("#5A666B"))
        }.WithRow(1));

        var button = new Button
        {
            Width = 240,
            Height = 74,
            HorizontalAlignment = HorizontalAlignment.Left,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(18, 12),
            Background = new SolidColorBrush(Color.Parse("#1E2328")),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.Parse("#364148")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(18),
            Effect = AsAvaloniaEffect(effect),
            Content = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                Children =
                {
                    new StackPanel
                    {
                        Spacing = 4,
                        Children =
                        {
                            new TextBlock
                            {
                                Text = "Deploy Emergency Patch",
                                FontSize = 16,
                                FontWeight = FontWeight.SemiBold
                            },
                            new TextBlock
                            {
                                Text = "Payment recovery · production lane",
                                Foreground = new SolidColorBrush(Color.Parse("#B8C6CF")),
                                FontSize = 12
                            }
                        }
                    },
                    new Border
                    {
                        Width = 34,
                        Height = 34,
                        CornerRadius = new CornerRadius(999),
                        Background = new SolidColorBrush(Color.Parse("#22394247")),
                        Child = new TextBlock
                        {
                            Text = "!",
                            HorizontalAlignment = HorizontalAlignment.Center,
                            VerticalAlignment = VerticalAlignment.Center,
                            FontWeight = FontWeight.Bold,
                            Foreground = new SolidColorBrush(Color.Parse("#FFD36B"))
                        }
                    }.WithColumn(1)
                }
            }
        };

        root.Children.Add(new Border
        {
            Padding = new Thickness(20),
            CornerRadius = new CornerRadius(20),
            Background = new SolidColorBrush(Color.Parse("#FFF9F4EA")),
            Child = new StackPanel
            {
                Spacing = 12,
                Children =
                {
                    button,
                    new WrapPanel
                    {
                        Orientation = Orientation.Horizontal,
                        ItemSpacing = 8,
                        LineSpacing = 8,
                        Children =
                        {
                            CreateChip("armed state"),
                            CreateChip("high attention"),
                            CreateChip("real button")
                        }
                    }
                }
            }
        }.WithRow(2));

        return root;
    }

    private static Border CreateChip(string text) =>
        new()
        {
            Padding = new Thickness(10, 4),
            CornerRadius = new CornerRadius(999),
            Background = new SolidColorBrush(Color.Parse("#FFF2ECE1")),
            Child = new TextBlock
            {
                Text = text,
                Foreground = new SolidColorBrush(Color.Parse("#685A48"))
            }
        };

    private static Border CreateStatRow(string label, string value, string accentHex)
    {
        var accent = new SolidColorBrush(Color.Parse(accentHex));
        return new Border
        {
            Padding = new Thickness(12),
            CornerRadius = new CornerRadius(14),
            Background = new SolidColorBrush(Color.Parse("#FFFFFFFF")),
            Child = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                Children =
                {
                    new TextBlock
                    {
                        Text = label,
                        VerticalAlignment = VerticalAlignment.Center,
                        Foreground = new SolidColorBrush(Color.Parse("#5B6B70"))
                    },
                    new TextBlock
                    {
                        Text = value,
                        FontWeight = FontWeight.SemiBold,
                        VerticalAlignment = VerticalAlignment.Center,
                        Foreground = accent
                    }.WithColumn(1)
                }
            }
        };
    }

    private static void AddSlider(Panel host, string label, double minimum, double maximum, double value, Action<double> setter, string format = "0.00")
    {
        var valueText = new TextBlock
        {
            Text = value.ToString(format, CultureInfo.InvariantCulture),
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.Parse("#6A757B"))
        };

        var slider = new Slider
        {
            Minimum = minimum,
            Maximum = maximum,
            Value = value,
            Width = 220
        };
        slider.PropertyChanged += (_, args) =>
        {
            if (args.Property == RangeBase.ValueProperty)
            {
                var current = slider.Value;
                valueText.Text = current.ToString(format, CultureInfo.InvariantCulture);
                setter(current);
            }
        };

        host.Children.Add(new Border
        {
            Margin = new Thickness(0, 0, 12, 12),
            Padding = new Thickness(12),
            CornerRadius = new CornerRadius(16),
            Background = new SolidColorBrush(Color.Parse("#FFF8F3EA")),
            Child = new StackPanel
            {
                Spacing = 6,
                Children =
                {
                    new TextBlock
                    {
                        Text = label,
                        FontWeight = FontWeight.SemiBold,
                        Foreground = new SolidColorBrush(Color.Parse("#2B3A3E"))
                    },
                    slider,
                    valueText
                }
            }
        });
    }

    private static void AddColorSwatches(Panel host, string label, IReadOnlyList<(string Name, Color Color)> options, Action<Color> setter)
    {
        var swatches = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            ItemSpacing = 8,
            LineSpacing = 8
        };

        foreach (var option in options)
        {
            var button = new Button
            {
                Padding = new Thickness(10, 6),
                Background = new SolidColorBrush(option.Color),
                Foreground = GetRelativeLuminance(option.Color) > 0.55d ? Brushes.Black : Brushes.White,
                Content = option.Name
            };
            button.Click += (_, _) => setter(option.Color);
            swatches.Children.Add(button);
        }

        host.Children.Add(new Border
        {
            Margin = new Thickness(0, 0, 12, 12),
            Padding = new Thickness(12),
            CornerRadius = new CornerRadius(16),
            Background = new SolidColorBrush(Color.Parse("#FFF8F3EA")),
            Child = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    new TextBlock
                    {
                        Text = label,
                        FontWeight = FontWeight.SemiBold,
                        Foreground = new SolidColorBrush(Color.Parse("#2B3A3E"))
                    },
                    swatches
                }
            }
        });
    }

    private static WriteableBitmap CreateSharedBitmap(int width, int height)
    {
        var bitmap = new WriteableBitmap(
            new PixelSize(width, height),
            new Vector(96, 96),
            PixelFormat.Bgra8888,
            AlphaFormat.Unpremul);

        using var framebuffer = bitmap.Lock();
        var buffer = new byte[framebuffer.RowBytes * framebuffer.Size.Height];

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var index = (y * framebuffer.RowBytes) + (x * 4);
                var fx = x / (double)(width - 1);
                var fy = y / (double)(height - 1);

                var red = (byte)(40 + (190 * fx));
                var green = (byte)(70 + (120 * (1d - fy)));
                var blue = (byte)(120 + (110 * (1d - Math.Abs((fx - fy) * 0.75d))));

                if ((x - (width * 0.72d)) * (x - (width * 0.72d)) + (y - (height * 0.25d)) * (y - (height * 0.25d)) < 1500)
                {
                    red = 248;
                    green = 201;
                    blue = 93;
                }

                if (x > width * 0.12d && x < width * 0.48d && y > height * 0.55d && y < height * 0.88d)
                {
                    red = 245;
                    green = 113;
                    blue = 78;
                }

                if (Math.Abs((y - (height * 0.55d)) - ((x - (width * 0.1d)) * 0.35d)) < 4)
                {
                    red = 255;
                    green = 255;
                    blue = 255;
                }

                buffer[index] = blue;
                buffer[index + 1] = green;
                buffer[index + 2] = red;
                buffer[index + 3] = 255;
            }
        }

        Marshal.Copy(buffer, 0, framebuffer.Address, buffer.Length);
        return bitmap;
    }

    private sealed class EffectSectionDefinition
    {
        public EffectSectionDefinition(
            string name,
            string description,
            object effect,
            string xamlExample,
            Action<Panel> buildControls,
            Func<object?, Control>? buildPreviewContent = null)
        {
            Name = name;
            Description = description;
            Effect = effect;
            XamlExample = xamlExample;
            BuildControls = buildControls;
            BuildPreviewContent = buildPreviewContent;
        }

        public string Name { get; }

        public string Description { get; }

        public object Effect { get; }

        public string XamlExample { get; }

        public Action<Panel> BuildControls { get; }

        public Func<object?, Control>? BuildPreviewContent { get; }
    }

    private static double GetRelativeLuminance(Color color)
    {
        static double Linearize(byte component)
        {
            var channel = component / 255d;
            return channel <= 0.03928d
                ? channel / 12.92d
                : Math.Pow((channel + 0.055d) / 1.055d, 2.4d);
        }

        return (0.2126d * Linearize(color.R)) +
               (0.7152d * Linearize(color.G)) +
               (0.0722d * Linearize(color.B));
    }
}

internal static class ControlPositionExtensions
{
    public static T WithColumn<T>(this T control, int column)
        where T : Control
    {
        Grid.SetColumn(control, column);
        return control;
    }

    public static T WithRow<T>(this T control, int row)
        where T : Control
    {
        Grid.SetRow(control, row);
        return control;
    }
}
