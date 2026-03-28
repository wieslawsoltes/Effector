using System;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Reactive;

namespace Effector;

public abstract class SkiaEffectBase : Animatable
{
    static SkiaEffectBase()
    {
        SkiaEffectInputManager.EnsureInitialized();
    }

    protected SkiaEffectBase()
    {
    }

    protected static void AffectsRender<T>(params AvaloniaProperty[] properties)
        where T : SkiaEffectBase
    {
        if (properties is null)
        {
            throw new ArgumentNullException(nameof(properties));
        }

        var invalidateObserver = new AnonymousObserver<AvaloniaPropertyChangedEventArgs>(
            static e => (e.Sender as T)?.InvalidateEffect());

        foreach (var property in properties)
        {
            property.Changed.Subscribe(invalidateObserver);
        }
    }

    protected void InvalidateEffect()
    {
    }

    internal void RequestInvalidate()
    {
        InvalidateEffect();
    }
}
