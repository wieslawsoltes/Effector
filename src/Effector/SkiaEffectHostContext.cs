using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace Effector;

public readonly struct SkiaEffectHostContext
{
    private readonly SkiaEffectBase _effect;

    internal SkiaEffectHostContext(SkiaEffectBase effect, Visual visual)
    {
        _effect = effect ?? throw new ArgumentNullException(nameof(effect));
        Visual = visual ?? throw new ArgumentNullException(nameof(visual));
        InputElement = visual as InputElement;
    }

    public SkiaEffectBase Effect => _effect;

    public Visual Visual { get; }

    public InputElement? InputElement { get; }

    public Rect Bounds => Visual.Bounds;

    public Size Size => Bounds.Size;

    public Rect InputBounds => ResolveInputBounds(Visual);

    public Size InputSize => InputBounds.Size;

    public bool HasInput => InputElement is not null;

    public Point GetPosition(PointerEventArgs e)
    {
        if (e is null)
        {
            throw new ArgumentNullException(nameof(e));
        }

        return e.GetPosition(Visual);
    }

    public Point GetNormalizedPosition(PointerEventArgs e, bool clamp = true) =>
        Normalize(GetPosition(e), clamp);

    public Point Normalize(Point position, bool clamp = true)
    {
        var bounds = InputBounds;
        var width = bounds.Width <= 0d ? 1d : bounds.Width;
        var height = bounds.Height <= 0d ? 1d : bounds.Height;

        var x = (position.X - bounds.X) / width;
        var y = (position.Y - bounds.Y) / height;

        if (clamp)
        {
            x = Clamp01(x);
            y = Clamp01(y);
        }

        return new Point(x, y);
    }

    public void CapturePointer(PointerEventArgs e)
    {
        if (e is null)
        {
            throw new ArgumentNullException(nameof(e));
        }

        if (InputElement is not null)
        {
            e.Pointer.Capture(InputElement);
        }
    }

    public void ReleasePointerCapture(PointerEventArgs e)
    {
        if (e is null)
        {
            throw new ArgumentNullException(nameof(e));
        }

        if (ReferenceEquals(e.Pointer.Captured, InputElement))
        {
            e.Pointer.Capture(null);
        }
    }

    public void Invalidate()
    {
        _effect.RequestInvalidate();
    }

    private static double Clamp01(double value)
    {
        if (value <= 0d)
        {
            return 0d;
        }

        if (value >= 1d)
        {
            return 1d;
        }

        return value;
    }

    private static Rect ResolveInputBounds(Visual visual)
    {
        var hostBounds = new Rect(default, visual.Bounds.Size);
        if (hostBounds.Width <= 0d || hostBounds.Height <= 0d)
        {
            return hostBounds;
        }

        var contentBounds = ResolveContentBounds(visual, visual);
        if (contentBounds is null)
        {
            return hostBounds;
        }

        var clipped = contentBounds.Value.Intersect(hostBounds);
        return clipped.Width > 0d && clipped.Height > 0d
            ? clipped
            : hostBounds;
    }

    private static Rect? ResolveContentBounds(Visual root, Visual current)
    {
        Rect? result = ShouldIncludeOwnBounds(current)
            ? TranslateBounds(root, current)
            : null;

        var hasVisualChildren = false;
        foreach (var child in current.GetVisualChildren())
        {
            hasVisualChildren = true;
            if (!child.IsVisible)
            {
                continue;
            }

            result = Union(result, ResolveContentBounds(root, child));
        }

        if (!hasVisualChildren && result is null)
        {
            result = TranslateBounds(root, current);
        }

        return result;
    }

    private static bool ShouldIncludeOwnBounds(Visual visual)
    {
        switch (visual)
        {
            case Border border:
                return border.Background is not null || border.BorderBrush is not null;
            case Panel panel:
                return panel.Background is not null;
            case Image:
            case TextBlock:
                return true;
            default:
                return false;
        }
    }

    private static Rect? TranslateBounds(Visual root, Visual visual)
    {
        var size = visual.Bounds.Size;
        if (size.Width <= 0d || size.Height <= 0d)
        {
            return null;
        }

        if (ReferenceEquals(root, visual))
        {
            return new Rect(default, size);
        }

        var topLeft = visual.TranslatePoint(default, root);
        return topLeft.HasValue
            ? new Rect(topLeft.Value, size)
            : null;
    }

    private static Rect? Union(Rect? left, Rect? right)
    {
        if (left is null)
        {
            return right;
        }

        if (right is null)
        {
            return left;
        }

        var x1 = Math.Min(left.Value.X, right.Value.X);
        var y1 = Math.Min(left.Value.Y, right.Value.Y);
        var x2 = Math.Max(left.Value.Right, right.Value.Right);
        var y2 = Math.Max(left.Value.Bottom, right.Value.Bottom);
        return new Rect(x1, y1, x2 - x1, y2 - y1);
    }
}
