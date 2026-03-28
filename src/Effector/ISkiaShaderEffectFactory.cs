using Avalonia.Media;

namespace Effector;

public interface ISkiaShaderEffectFactory<in TEffect>
    where TEffect : class, IEffect
{
    SkiaShaderEffect? CreateShaderEffect(TEffect effect, SkiaShaderEffectContext context);
}
