namespace Effector;

public interface ISkiaShaderEffectValueFactory
{
    SkiaShaderEffect? CreateShaderEffect(object[] values, SkiaShaderEffectContext context);
}
