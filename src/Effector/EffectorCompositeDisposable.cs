using System;

namespace Effector;

internal sealed class EffectorCompositeDisposable : IDisposable
{
    private IDisposable? _first;
    private IDisposable? _second;

    public EffectorCompositeDisposable(IDisposable? first, IDisposable? second)
    {
        _first = first;
        _second = second;
    }

    public void Dispose()
    {
        var first = _first;
        var second = _second;
        _first = null;
        _second = null;

        second?.Dispose();
        first?.Dispose();
    }
}
