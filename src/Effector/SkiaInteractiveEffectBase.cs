using Avalonia.Input;

namespace Effector;

public abstract class SkiaInteractiveEffectBase : SkiaEffectBase, ISkiaInputEffectHandler
{
    public virtual void OnAttached(SkiaEffectHostContext context)
    {
    }

    public virtual void OnDetached(SkiaEffectHostContext context)
    {
    }

    public virtual void OnHostBoundsChanged(SkiaEffectHostContext context)
    {
    }

    public virtual void OnPointerEntered(SkiaEffectHostContext context, PointerEventArgs e)
    {
    }

    public virtual void OnPointerExited(SkiaEffectHostContext context, PointerEventArgs e)
    {
    }

    public virtual void OnPointerMoved(SkiaEffectHostContext context, PointerEventArgs e)
    {
    }

    public virtual void OnPointerPressed(SkiaEffectHostContext context, PointerPressedEventArgs e)
    {
    }

    public virtual void OnPointerReleased(SkiaEffectHostContext context, PointerReleasedEventArgs e)
    {
    }

    public virtual void OnPointerCaptureLost(SkiaEffectHostContext context, PointerCaptureLostEventArgs e)
    {
    }

    public virtual void OnPointerWheelChanged(SkiaEffectHostContext context, PointerWheelEventArgs e)
    {
    }
}
