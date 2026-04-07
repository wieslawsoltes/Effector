using System;
using System.Collections.Generic;
using System.IO;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace Effector.Build.Tasks;

internal enum AvaloniaPatchAssemblyKind
{
    Base,
    Skia
}

internal sealed class AvaloniaPatchScanRequirement
{
    public AvaloniaPatchScanRequirement(string typeFullName, string methodName, int parameterCount)
    {
        TypeFullName = typeFullName;
        MethodName = methodName;
        ParameterCount = parameterCount;
    }

    public string TypeFullName { get; }

    public string MethodName { get; }

    public int ParameterCount { get; }
}

internal sealed class AvaloniaPatchScanResult
{
    public AvaloniaPatchScanResult(
        string assemblyName,
        string assemblyVersion,
        bool isSupportedVersion,
        bool isAlreadyPatched,
        IReadOnlyList<string> missingRequirements)
    {
        AssemblyName = assemblyName;
        AssemblyVersion = assemblyVersion;
        IsSupportedVersion = isSupportedVersion;
        IsAlreadyPatched = isAlreadyPatched;
        MissingRequirements = missingRequirements;
    }

    public string AssemblyName { get; }

    public string AssemblyVersion { get; }

    public bool IsSupportedVersion { get; }

    public bool IsAlreadyPatched { get; }

    public IReadOnlyList<string> MissingRequirements { get; }
}

internal sealed class AvaloniaPatchMetadataScanner
{
    private const string EffectorRuntimeTypeName = "Effector.EffectorRuntime";

    public AvaloniaPatchScanResult Scan(string assemblyPath, string supportedVersion, AvaloniaPatchAssemblyKind kind)
    {
        using var stream = new FileStream(assemblyPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var peReader = new PEReader(stream, PEStreamOptions.Default);
        var metadataReader = peReader.GetMetadataReader();
        var assemblyDefinition = metadataReader.GetAssemblyDefinition();
        var assemblyName = metadataReader.GetString(assemblyDefinition.Name);
        var version = assemblyDefinition.Version;
        var actualVersion = $"{version.Major}.{version.Minor}.{version.Build}";
        var typeMethods = BuildTypeMethodMap(metadataReader);
        var missingRequirements = GetRequirements(kind)
            .Where(requirement => !typeMethods.TryGetValue(requirement.TypeFullName, out var methods) || !methods.Contains((requirement.MethodName, requirement.ParameterCount)))
            .Select(requirement => requirement.TypeFullName + "::" + requirement.MethodName + "/" + requirement.ParameterCount.ToString())
            .ToArray();

        return new AvaloniaPatchScanResult(
            assemblyName,
            actualVersion,
            string.Equals(actualVersion, supportedVersion, StringComparison.Ordinal),
            IsAlreadyPatched(metadataReader),
            missingRequirements);
    }

    private static Dictionary<string, HashSet<(string Name, int ParameterCount)>> BuildTypeMethodMap(MetadataReader metadataReader)
    {
        var result = new Dictionary<string, HashSet<(string Name, int ParameterCount)>>(StringComparer.Ordinal);

        foreach (var typeHandle in metadataReader.TypeDefinitions)
        {
            var typeDefinition = metadataReader.GetTypeDefinition(typeHandle);
            var typeName = GetTypeDefinitionFullName(metadataReader, typeDefinition);
            var methods = new HashSet<(string Name, int ParameterCount)>();

            foreach (var methodHandle in typeDefinition.GetMethods())
            {
                var method = metadataReader.GetMethodDefinition(methodHandle);
                var methodName = metadataReader.GetString(method.Name);
                var signature = method.DecodeSignature(Provider.Instance, genericContext: null);
                methods.Add((methodName, signature.ParameterTypes.Length));
            }

            result[typeName] = methods;
        }

        return result;
    }

    private static bool IsAlreadyPatched(MetadataReader metadataReader)
    {
        foreach (var memberHandle in metadataReader.MemberReferences)
        {
            var member = metadataReader.GetMemberReference(memberHandle);
            if (GetTypeHandleFullName(metadataReader, member.Parent) == EffectorRuntimeTypeName)
            {
                return true;
            }
        }

        return metadataReader.TypeReferences
            .Select(handle => metadataReader.GetTypeReference(handle))
            .Any(reference =>
                string.Equals(metadataReader.GetString(reference.Namespace) + "." + metadataReader.GetString(reference.Name), EffectorRuntimeTypeName, StringComparison.Ordinal));
    }

    private static IReadOnlyList<AvaloniaPatchScanRequirement> GetRequirements(AvaloniaPatchAssemblyKind kind) =>
        kind switch
        {
            AvaloniaPatchAssemblyKind.Base =>
            [
                new("Avalonia.Media.EffectExtensions", "GetEffectOutputPadding", 1),
                new("Avalonia.Media.EffectExtensions", "ToImmutable", 1),
                new("Avalonia.Media.Effect", "Parse", 1),
                new("Avalonia.Animation.EffectTransition", "DoTransition", 3),
                new("Avalonia.Animation.Animators.EffectAnimator", "Apply", 6),
                new("Avalonia.Animation.Animators.EffectAnimator", "Interpolate", 3)
            ],
            AvaloniaPatchAssemblyKind.Skia =>
            [
                new("Avalonia.Skia.DrawingContextImpl", "PushEffect", 2),
                new("Avalonia.Skia.DrawingContextImpl", "PopEffect", 0),
                new("Avalonia.Skia.DrawingContextImpl", "CreateEffect", 1),
                new("Avalonia.Skia.DrawingContextImpl", "get_Canvas", 0),
                new("Avalonia.Skia.DrawingContextImpl", "get_Surface", 0),
                new("Avalonia.Skia.DrawingContextImpl", "set_Transform", 1)
            ],
            _ => Array.Empty<AvaloniaPatchScanRequirement>()
        };

    private static string GetTypeDefinitionFullName(MetadataReader metadataReader, TypeDefinition typeDefinition)
    {
        var ns = metadataReader.GetString(typeDefinition.Namespace);
        var name = metadataReader.GetString(typeDefinition.Name);
        return string.IsNullOrEmpty(ns) ? name : ns + "." + name;
    }

    private static string? GetTypeHandleFullName(MetadataReader metadataReader, EntityHandle handle) =>
        handle.Kind switch
        {
            HandleKind.TypeReference => GetTypeReferenceFullName(metadataReader, metadataReader.GetTypeReference((TypeReferenceHandle)handle)),
            HandleKind.TypeDefinition => GetTypeDefinitionFullName(metadataReader, metadataReader.GetTypeDefinition((TypeDefinitionHandle)handle)),
            _ => null
        };

    private static string GetTypeReferenceFullName(MetadataReader metadataReader, TypeReference typeReference)
    {
        var ns = metadataReader.GetString(typeReference.Namespace);
        var name = metadataReader.GetString(typeReference.Name);
        return string.IsNullOrEmpty(ns) ? name : ns + "." + name;
    }

    private sealed class Provider : ISignatureTypeProvider<string, object?>
    {
        public static readonly Provider Instance = new();

        public string GetArrayType(string elementType, ArrayShape shape) => elementType + "[]";
        public string GetByReferenceType(string elementType) => elementType + "&";
        public string GetFunctionPointerType(MethodSignature<string> signature) => "fnptr";
        public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments) => genericType;
        public string GetGenericMethodParameter(object? genericContext, int index) => "!!" + index.ToString();
        public string GetGenericTypeParameter(object? genericContext, int index) => "!" + index.ToString();
        public string GetModifiedType(string modifierType, string unmodifiedType, bool isRequired) => unmodifiedType;
        public string GetPinnedType(string elementType) => elementType;
        public string GetPointerType(string elementType) => elementType + "*";
        public string GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode.ToString();
        public string GetSZArrayType(string elementType) => elementType + "[]";
        public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind) => GetTypeDefinitionFullName(reader, reader.GetTypeDefinition(handle));
        public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind) => GetTypeReferenceFullName(reader, reader.GetTypeReference(handle));
        public string GetTypeFromSpecification(MetadataReader reader, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind) => "spec";
        public string GetUnsupportedSignatureTypeKind(byte rawTypeKind) => "unsupported";
    }
}
