using Avalonia;
using Avalonia.Media;
using SkiaSharp;

namespace Effector;

public interface ISkiaEffectFactory<in TEffect>
    where TEffect : class, IEffect
{
    Thickness GetPadding(TEffect effect);

    SKImageFilter? CreateFilter(TEffect effect, SkiaEffectContext context);
}
