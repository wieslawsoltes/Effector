using System;
using Avalonia.Media;

namespace Effector.Runtime.Tests;

internal static class EffectTestHelpers
{
    public static IEffect AsEffect(object effect) =>
        effect as IEffect ?? throw new InvalidCastException(
            $"Expected woven Avalonia effect, but received {effect.GetType().FullName}. " +
            $"BaseType={effect.GetType().BaseType?.FullName}. " +
            $"EffectAssembly={effect.GetType().Assembly.Location}. " +
            $"BaseAssembly={effect.GetType().BaseType?.Assembly.Location}. " +
            $"IEffectAssembly={typeof(IEffect).Assembly.Location}.");

    public static IEffect? AsNullableEffect(object? effect) =>
        effect is null ? null : AsEffect(effect);

    public static bool HasEffectType(object? effect, params Type[] effectTypes)
    {
        var effectType = effect?.GetType();
        if (effectType is null)
        {
            return false;
        }

        foreach (var candidateType in effectTypes)
        {
            if (effectType == candidateType)
            {
                return true;
            }
        }

        return false;
    }
}
