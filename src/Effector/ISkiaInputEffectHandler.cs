using Avalonia.Input;

namespace Effector;

public interface ISkiaInputEffectHandler
{
    void OnAttached(SkiaEffectHostContext context);

    void OnDetached(SkiaEffectHostContext context);

    void OnHostBoundsChanged(SkiaEffectHostContext context);

    void OnPointerEntered(SkiaEffectHostContext context, PointerEventArgs e);

    void OnPointerExited(SkiaEffectHostContext context, PointerEventArgs e);

    void OnPointerMoved(SkiaEffectHostContext context, PointerEventArgs e);

    void OnPointerPressed(SkiaEffectHostContext context, PointerPressedEventArgs e);

    void OnPointerReleased(SkiaEffectHostContext context, PointerReleasedEventArgs e);

    void OnPointerCaptureLost(SkiaEffectHostContext context, PointerCaptureLostEventArgs e);

    void OnPointerWheelChanged(SkiaEffectHostContext context, PointerWheelEventArgs e);
}
