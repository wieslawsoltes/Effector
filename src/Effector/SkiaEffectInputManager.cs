using System;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Reactive;

namespace Effector;

internal static class SkiaEffectInputManager
{
    private static readonly object Sync = new();
    private static readonly ConditionalWeakTable<Visual, SkiaEffectInputAttachment> Attachments = new();
    private static bool s_initialized;

    public static void EnsureInitialized()
    {
        lock (Sync)
        {
            if (s_initialized)
            {
                return;
            }

            s_initialized = true;
            Visual.EffectProperty.Changed.Subscribe(new AnonymousObserver<AvaloniaPropertyChangedEventArgs>(OnEffectChanged));
        }
    }

    private static void OnEffectChanged(AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Sender is not Visual visual)
        {
            return;
        }

        lock (Sync)
        {
            DetachCore(visual, e.OldValue as SkiaEffectBase);

            if (e.NewValue is SkiaEffectBase effect)
            {
                var attachment = new SkiaEffectInputAttachment(visual, effect, effect as ISkiaInputEffectHandler);
                Attachments.Remove(visual);
                Attachments.Add(visual, attachment);
            }
        }
    }

    private static void DetachCore(Visual visual, SkiaEffectBase? expectedEffect)
    {
        if (!Attachments.TryGetValue(visual, out var attachment))
        {
            return;
        }

        if (expectedEffect is not null && !ReferenceEquals(attachment.Effect, expectedEffect))
        {
            return;
        }

        Attachments.Remove(visual);
        attachment.Dispose();
    }

    private sealed class SkiaEffectInputAttachment : IDisposable
    {
        private readonly Visual _visual;
        private readonly ISkiaInputEffectHandler? _handler;
        private readonly SkiaEffectHostContext _context;
        private readonly IDisposable _boundsSubscription;
        private readonly IDisposable _renderTransformSubscription;
        private readonly IDisposable _renderTransformOriginSubscription;
        private readonly Layoutable? _layoutable;
        private Transform? _observedRenderTransform;
        private bool _disposed;

        public SkiaEffectInputAttachment(Visual visual, SkiaEffectBase effect, ISkiaInputEffectHandler? handler)
        {
            _visual = visual;
            Effect = effect;
            _handler = handler;
            _context = new SkiaEffectHostContext(effect, visual);
            _boundsSubscription = visual.GetObservable(Visual.BoundsProperty)
                .Subscribe(new AnonymousObserver<Rect>(_ => OnBoundsChanged()));
            _renderTransformSubscription = visual.GetObservable(Visual.RenderTransformProperty)
                .Subscribe(new AnonymousObserver<ITransform?>(OnRenderTransformChanged));
            _renderTransformOriginSubscription = visual.GetObservable(Visual.RenderTransformOriginProperty)
                .Subscribe(new AnonymousObserver<RelativePoint>(_ => OnBoundsChanged()));
            _layoutable = visual as Layoutable;
            AttachRenderTransformObserver(visual.RenderTransform);
            if (_layoutable is not null)
            {
                _layoutable.LayoutUpdated += LayoutUpdated;
                _layoutable.EffectiveViewportChanged += EffectiveViewportChanged;
            }

            if (handler is not null && visual is InputElement input)
            {
                input.AddHandler(InputElement.PointerEnteredEvent, PointerEntered, handledEventsToo: true);
                input.AddHandler(InputElement.PointerExitedEvent, PointerExited, handledEventsToo: true);
                input.AddHandler(InputElement.PointerMovedEvent, PointerMoved, handledEventsToo: true);
                input.AddHandler(InputElement.PointerPressedEvent, PointerPressed, handledEventsToo: true);
                input.AddHandler(InputElement.PointerReleasedEvent, PointerReleased, handledEventsToo: true);
                input.AddHandler(InputElement.PointerCaptureLostEvent, PointerCaptureLost, handledEventsToo: true);
                input.AddHandler(InputElement.PointerWheelChangedEvent, PointerWheelChanged, handledEventsToo: true);
            }

            EffectorRuntime.UpdateHostVisualBounds(effect, visual);
            _handler?.OnAttached(_context);
            _handler?.OnHostBoundsChanged(_context);
        }

        public SkiaEffectBase Effect { get; }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _boundsSubscription.Dispose();
            _renderTransformSubscription.Dispose();
            _renderTransformOriginSubscription.Dispose();
            DetachRenderTransformObserver();
            if (_layoutable is not null)
            {
                _layoutable.LayoutUpdated -= LayoutUpdated;
                _layoutable.EffectiveViewportChanged -= EffectiveViewportChanged;
            }

            if (_handler is not null && _visual is InputElement input)
            {
                input.RemoveHandler(InputElement.PointerEnteredEvent, PointerEntered);
                input.RemoveHandler(InputElement.PointerExitedEvent, PointerExited);
                input.RemoveHandler(InputElement.PointerMovedEvent, PointerMoved);
                input.RemoveHandler(InputElement.PointerPressedEvent, PointerPressed);
                input.RemoveHandler(InputElement.PointerReleasedEvent, PointerReleased);
                input.RemoveHandler(InputElement.PointerCaptureLostEvent, PointerCaptureLost);
                input.RemoveHandler(InputElement.PointerWheelChangedEvent, PointerWheelChanged);
            }

            EffectorRuntime.ClearHostVisual(Effect);
            _handler?.OnDetached(_context);
        }

        private void PointerEntered(object? sender, PointerEventArgs e)
        {
            if (!_disposed && _handler is not null)
            {
                _handler.OnPointerEntered(_context, e);
            }
        }

        private void PointerExited(object? sender, PointerEventArgs e)
        {
            if (!_disposed && _handler is not null)
            {
                _handler.OnPointerExited(_context, e);
            }
        }

        private void PointerMoved(object? sender, PointerEventArgs e)
        {
            if (!_disposed && _handler is not null)
            {
                _handler.OnPointerMoved(_context, e);
            }
        }

        private void PointerPressed(object? sender, PointerPressedEventArgs e)
        {
            if (!_disposed && _handler is not null)
            {
                _handler.OnPointerPressed(_context, e);
            }
        }

        private void PointerReleased(object? sender, PointerReleasedEventArgs e)
        {
            if (!_disposed && _handler is not null)
            {
                _handler.OnPointerReleased(_context, e);
            }
        }

        private void PointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
        {
            if (!_disposed && _handler is not null)
            {
                _handler.OnPointerCaptureLost(_context, e);
            }
        }

        private void PointerWheelChanged(object? sender, PointerWheelEventArgs e)
        {
            if (!_disposed && _handler is not null)
            {
                _handler.OnPointerWheelChanged(_context, e);
            }
        }

        private void OnRenderTransformChanged(ITransform? transform)
        {
            if (_disposed)
            {
                return;
            }

            AttachRenderTransformObserver(transform);
            OnBoundsChanged();
        }

        private void OnBoundsChanged()
        {
            if (_disposed)
            {
                return;
            }

            EffectorRuntime.UpdateHostVisualBounds(Effect, _visual);
            _handler?.OnHostBoundsChanged(_context);
        }

        private void LayoutUpdated(object? sender, EventArgs e) => OnBoundsChanged();

        private void EffectiveViewportChanged(object? sender, EffectiveViewportChangedEventArgs e) => OnBoundsChanged();

        private void AttachRenderTransformObserver(ITransform? transform)
        {
            var observedTransform = transform as Transform;
            if (ReferenceEquals(_observedRenderTransform, observedTransform))
            {
                return;
            }

            DetachRenderTransformObserver();
            _observedRenderTransform = observedTransform;
            if (_observedRenderTransform is not null)
            {
                _observedRenderTransform.Changed += RenderTransformChanged;
            }
        }

        private void DetachRenderTransformObserver()
        {
            if (_observedRenderTransform is null)
            {
                return;
            }

            _observedRenderTransform.Changed -= RenderTransformChanged;
            _observedRenderTransform = null;
        }

        private void RenderTransformChanged(object? sender, EventArgs e) => OnBoundsChanged();
    }
}
