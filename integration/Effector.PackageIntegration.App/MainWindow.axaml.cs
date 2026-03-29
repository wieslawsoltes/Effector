using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Effector.PackageIntegration.Effects;

namespace Effector.PackageIntegration.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        ApplyCodeAssignedEffect();
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void ApplyCodeAssignedEffect()
    {
        if (this.FindControl<Border>("CodeAssignedBadge") is not { } badge)
        {
            return;
        }

        badge.Effect = new PackageTintEffect
        {
            TintColor = Color.Parse("#FFB11B"),
            Strength = 0.65d
        };
    }
}
