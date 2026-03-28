using Avalonia;
using SkiaSharp;

namespace Effector;

public interface ISkiaEffectValueFactory
{
    Thickness GetPadding(object[] values);

    SKImageFilter? CreateFilter(object[] values, SkiaEffectContext context);
}
