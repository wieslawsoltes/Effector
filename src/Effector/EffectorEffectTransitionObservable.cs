using System;
using Avalonia.Animation.Easings;
using Avalonia.Media;

namespace Effector;

internal sealed class EffectorEffectTransitionObservable : IObservable<IEffect?>, IObserver<double>, IDisposable
{
    private readonly IObservable<double> _progress;
    private readonly IEasing _easing;
    private readonly IEffect? _from;
    private readonly IEffect? _to;
    private IDisposable? _subscription;
    private IObserver<IEffect?>? _observer;

    public EffectorEffectTransitionObservable(
        IObservable<double> progress,
        IEasing easing,
        IEffect? from,
        IEffect? to)
    {
        _progress = progress ?? throw new ArgumentNullException(nameof(progress));
        _easing = easing ?? throw new ArgumentNullException(nameof(easing));
        _from = from;
        _to = to;
    }

    public IDisposable Subscribe(IObserver<IEffect?> observer)
    {
        if (observer is null)
        {
            throw new ArgumentNullException(nameof(observer));
        }

        if (_observer is not null)
        {
            throw new InvalidOperationException("This observable only supports a single subscriber.");
        }

        _observer = observer;
        _subscription = _progress.Subscribe(this);
        return this;
    }

    void IObserver<double>.OnCompleted()
    {
        var observer = _observer;
        Dispose();
        observer?.OnCompleted();
    }

    void IObserver<double>.OnError(Exception error)
    {
        var observer = _observer;
        Dispose();
        observer?.OnError(error);
    }

    void IObserver<double>.OnNext(double value)
    {
        var observer = _observer;
        if (observer is null)
        {
            return;
        }

        var eased = _easing.Ease(value);
        observer.OnNext(EffectorRuntime.InterpolateOrStep(eased, _from, _to));
    }

    public void Dispose()
    {
        _subscription?.Dispose();
        _subscription = null;
        _observer = null;
    }
}
