using Avalonia;
using Avalonia.Collections;
using Avalonia.Metadata;
using Effector;
using System.ComponentModel;
using System.Collections.Specialized;
using System.Linq;

namespace Effector.FilterEffects;

[SkiaEffect(typeof(FilterEffectFactory), RequiresSourceCapture = true)]
public class FilterEffect : SkiaEffectBase, ISupportInitialize, IAddChild, IAddChild<FilterPrimitiveXaml>
{
    public static readonly StyledProperty<FilterPrimitiveCollection> PrimitivesProperty =
        AvaloniaProperty.Register<FilterEffect, FilterPrimitiveCollection>(
            nameof(Primitives),
            FilterPrimitiveCollection.Empty);

    public static readonly StyledProperty<Thickness> PaddingProperty =
        AvaloniaProperty.Register<FilterEffect, Thickness>(nameof(Padding));

    private readonly FilterPrimitiveChildren _children = new();
    private bool _isInitializing;
    private bool _syncPending;

    static FilterEffect()
    {
        AffectsRender<FilterEffect>(PrimitivesProperty, PaddingProperty);
    }

    public FilterEffect()
    {
        _children.CollectionChanged += OnChildrenCollectionChanged;
    }

    public FilterPrimitiveCollection Primitives
    {
        get => GetValue(PrimitivesProperty);
        set => SetValue(PrimitivesProperty, value ?? FilterPrimitiveCollection.Empty);
    }

    public Thickness Padding
    {
        get => GetValue(PaddingProperty);
        set => SetValue(PaddingProperty, value);
    }

    [Content]
    public FilterPrimitiveChildren Children => _children;

    void ISupportInitialize.BeginInit()
    {
        _isInitializing = true;
        _syncPending = true;
    }

    void ISupportInitialize.EndInit()
    {
        _isInitializing = false;

        if (_syncPending)
        {
            SyncChildrenToPrimitives();
            _syncPending = false;
        }
    }

    void IAddChild<FilterPrimitiveXaml>.AddChild(FilterPrimitiveXaml child)
    {
        if (child is null)
        {
            throw new System.ArgumentNullException(nameof(child));
        }

        _children.Add(child);
    }

    void IAddChild.AddChild(object child)
    {
        if (child is FilterPrimitiveXaml primitive)
        {
            ((IAddChild<FilterPrimitiveXaml>)this).AddChild(primitive);
            return;
        }

        throw new System.InvalidOperationException($"FilterEffect content must be a {nameof(FilterPrimitiveXaml)}.");
    }

    private void OnChildrenCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            ResubscribeChildren();
        }
        else
        {
            Unsubscribe(e.OldItems);
            Subscribe(e.NewItems);
        }

        SchedulePrimitivesSync();
    }

    private void OnChildChanged(object? sender, System.EventArgs e) => SchedulePrimitivesSync();

    private void SchedulePrimitivesSync()
    {
        if (_isInitializing)
        {
            _syncPending = true;
            return;
        }

        SyncChildrenToPrimitives();
        _syncPending = false;
    }

    private void ResubscribeChildren()
    {
        foreach (var child in _children)
        {
            child.Changed -= OnChildChanged;
            child.Changed += OnChildChanged;
        }
    }

    private void Subscribe(System.Collections.IList? items)
    {
        if (items is null)
        {
            return;
        }

        foreach (var item in items.OfType<FilterPrimitiveXaml>())
        {
            item.Changed += OnChildChanged;
        }
    }

    private void Unsubscribe(System.Collections.IList? items)
    {
        if (items is null)
        {
            return;
        }

        foreach (var item in items.OfType<FilterPrimitiveXaml>())
        {
            item.Changed -= OnChildChanged;
        }
    }

    private void SyncChildrenToPrimitives()
    {
        Primitives = _children.Count == 0
            ? FilterPrimitiveCollection.Empty
            : new FilterPrimitiveCollection(_children.Select(static child => child.ToPrimitive()));
    }
}

#pragma warning disable CS8981
[UsableDuringInitialization]
public sealed class filter : FilterEffect
{
}
#pragma warning restore CS8981
