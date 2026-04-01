using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Media;
using Effector;
using Mono.Cecil;
using Mono.Cecil.Cil;
using SkiaSharp;

namespace Effector.Build.Tasks;

internal sealed class EffectorWeaver
{
    private const string SkiaSourceCaptureValueFactoryInterfaceName = "Effector.ISkiaSourceCaptureValueFactory";
    private const string SkiaSourceCaptureValueFactoryMethodName = "RequiresSourceCapture";

    private sealed class EffectDefinitionModel
    {
        public EffectDefinitionModel(
            TypeDefinition effectType,
            TypeDefinition factoryType,
            MethodDefinition effectParameterlessConstructor,
            MethodDefinition factoryParameterlessConstructor,
            CustomAttribute effectAttribute,
            IReadOnlyList<PropertyDefinition> properties,
            bool supportsShaderFactory,
            bool supportsValueFactory,
            bool supportsSourceCaptureValueFactory,
            bool supportsShaderValueFactory)
        {
            EffectType = effectType;
            FactoryType = factoryType;
            EffectParameterlessConstructor = effectParameterlessConstructor;
            FactoryParameterlessConstructor = factoryParameterlessConstructor;
            EffectAttribute = effectAttribute;
            Properties = properties;
            SupportsShaderFactory = supportsShaderFactory;
            SupportsValueFactory = supportsValueFactory;
            SupportsSourceCaptureValueFactory = supportsSourceCaptureValueFactory;
            SupportsShaderValueFactory = supportsShaderValueFactory;
        }

        public TypeDefinition EffectType { get; }

        public TypeDefinition FactoryType { get; }

        public MethodDefinition EffectParameterlessConstructor { get; }

        public MethodDefinition FactoryParameterlessConstructor { get; }

        public CustomAttribute EffectAttribute { get; }

        public IReadOnlyList<PropertyDefinition> Properties { get; }

        public bool SupportsShaderFactory { get; }

        public bool SupportsValueFactory { get; }

        public bool SupportsSourceCaptureValueFactory { get; }

        public bool SupportsShaderValueFactory { get; }
    }

    public EffectorWeaverResult Rewrite(EffectorWeaverConfiguration configuration)
    {
        var result = new EffectorWeaverResult();

        if (string.IsNullOrWhiteSpace(configuration.AssemblyPath) || !File.Exists(configuration.AssemblyPath))
        {
            result.Errors.Add("Assembly to weave was not found.");
            return result;
        }

        var scan = new EffectorMetadataScanner().Scan(configuration.AssemblyPath);
        result.InspectedTypeCount = scan.InspectedTypeCount;
        result.CandidateCount = scan.Candidates.Count;

        if (scan.Candidates.Count == 0)
        {
            return result;
        }

        var resolver = CreateResolver(configuration);
        var pdbPath = Path.ChangeExtension(configuration.AssemblyPath, ".pdb");
        var readSymbols = File.Exists(pdbPath);
        var readerParameters = new ReaderParameters
        {
            AssemblyResolver = resolver,
            ReadSymbols = readSymbols,
            InMemory = true,
            SymbolReaderProvider = readSymbols ? new PortablePdbReaderProvider() : null
        };

        using var assembly = AssemblyDefinition.ReadAssembly(configuration.AssemblyPath, readerParameters);
        var module = assembly.MainModule;

        ValidateAvaloniaReference(module, configuration, result);
        if (result.Errors.Count > 0 && configuration.Strict)
        {
            return result;
        }

        var registrationMethods = new List<MethodDefinition>();

        foreach (var candidate in scan.Candidates)
        {
            if (module.LookupToken(candidate.MetadataToken) is not TypeDefinition effectType)
            {
                result.Warnings.Add($"[Effector.Build] Candidate '{candidate.FullName}' could not be resolved from metadata token 0x{candidate.MetadataToken:X8}.");
                continue;
            }

            if (!TryBuildModel(module, effectType, configuration, result, out var model))
            {
                continue;
            }

            if (IsAlreadyWoven(module, model))
            {
                if (configuration.Verbose)
                {
                    result.Warnings.Add($"[Effector.Build] Effect type '{model.EffectType.FullName}' was already woven; skipping.");
                }

                continue;
            }

            var immutableType = CreateImmutableType(module, model);
            var helperType = CreateHelperType(module, model, immutableType);
            var registerMethod = AddRegistrationMethod(module, model, immutableType, helperType);
            AddImmutableEquality(module, immutableType, helperType);
            registrationMethods.Add(registerMethod);
            result.RewrittenEffectCount++;
        }

        if (result.Errors.Count > 0 && configuration.Strict)
        {
            return result;
        }

        if (registrationMethods.Count == 0)
        {
            return result;
        }

        InjectModuleInitializer(module, registrationMethods);

        var writerParameters = new WriterParameters
        {
            WriteSymbols = readSymbols,
            SymbolWriterProvider = readSymbols ? new PortablePdbWriterProvider() : null
        };
        assembly.Write(configuration.AssemblyPath, writerParameters);
        return result;
    }

    private static DefaultAssemblyResolver CreateResolver(EffectorWeaverConfiguration configuration)
    {
        var resolver = new DefaultAssemblyResolver();
        var searchDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Path.GetDirectoryName(configuration.AssemblyPath) ?? string.Empty,
            configuration.ProjectDirectory
        };

        foreach (var referencePath in configuration.ReferencePaths)
        {
            if (string.IsNullOrWhiteSpace(referencePath))
            {
                continue;
            }

            var directory = Path.GetDirectoryName(referencePath);

            if (!string.IsNullOrWhiteSpace(directory))
            {
                searchDirectories.Add(directory);
            }
        }

        foreach (var directory in searchDirectories.Where(static path => !string.IsNullOrWhiteSpace(path) && Directory.Exists(path)))
        {
            resolver.AddSearchDirectory(directory);
        }

        return resolver;
    }

    private static void ValidateAvaloniaReference(ModuleDefinition module, EffectorWeaverConfiguration configuration, EffectorWeaverResult result)
    {
        var avaloniaBase = module.AssemblyReferences.FirstOrDefault(static reference => reference.Name == "Avalonia.Base");
        if (avaloniaBase is null)
        {
            result.Errors.Add("[Effector.Build] Assembly does not reference Avalonia.Base.");
            return;
        }

        var actual = $"{avaloniaBase.Version.Major}.{avaloniaBase.Version.Minor}.{avaloniaBase.Version.Build}";
        if (!string.Equals(actual, configuration.SupportedAvaloniaVersion, StringComparison.Ordinal))
        {
            result.Errors.Add($"[Effector.Build] Avalonia.Base version {actual} is not supported. Expected {configuration.SupportedAvaloniaVersion}.");
        }
    }

    private static bool TryBuildModel(
        ModuleDefinition module,
        TypeDefinition effectType,
        EffectorWeaverConfiguration configuration,
        EffectorWeaverResult result,
        out EffectDefinitionModel model)
    {
        model = default!;

        if (effectType.IsNested)
        {
            result.Errors.Add($"[Effector.Build] Effect type '{effectType.FullName}' must not be nested.");
            return false;
        }

        if (!effectType.IsClass || effectType.IsAbstract || effectType.HasGenericParameters)
        {
            result.Errors.Add($"[Effector.Build] Effect type '{effectType.FullName}' must be a non-abstract, non-generic class.");
            return false;
        }

        if (!DerivesFrom(effectType, typeof(SkiaEffectBase).FullName))
        {
            result.Errors.Add($"[Effector.Build] Effect type '{effectType.FullName}' must derive from {typeof(SkiaEffectBase).FullName}.");
            return false;
        }

        var effectCtor = effectType.Methods.FirstOrDefault(static method => method.IsConstructor && !method.IsStatic && method.Parameters.Count == 0 && !method.IsPrivate);
        if (effectCtor is null)
        {
            result.Errors.Add($"[Effector.Build] Effect type '{effectType.FullName}' must expose a non-private parameterless constructor.");
            return false;
        }

        var effectAttribute = effectType.CustomAttributes.FirstOrDefault(static attribute => attribute.AttributeType.FullName == typeof(SkiaEffectAttribute).FullName);
        if (effectAttribute is null || effectAttribute.ConstructorArguments.Count != 1 || effectAttribute.ConstructorArguments[0].Value is not TypeReference factoryTypeReference)
        {
            result.Errors.Add($"[Effector.Build] Effect type '{effectType.FullName}' must declare [SkiaEffect(typeof(FactoryType))].");
            return false;
        }

        var factoryType = factoryTypeReference.Resolve();
        if (factoryType is null)
        {
            result.Errors.Add($"[Effector.Build] Factory type '{factoryTypeReference.FullName}' for '{effectType.FullName}' could not be resolved.");
            return false;
        }

        var factoryCtor = factoryType.Methods.FirstOrDefault(static method => method.IsConstructor && !method.IsStatic && method.Parameters.Count == 0 && !method.IsPrivate);
        if (factoryCtor is null)
        {
            result.Errors.Add($"[Effector.Build] Factory type '{factoryType.FullName}' must expose a non-private parameterless constructor.");
            return false;
        }

        if (!ImplementsFactory(factoryType, effectType))
        {
            result.Errors.Add($"[Effector.Build] Factory type '{factoryType.FullName}' must implement {typeof(ISkiaEffectFactory<>).FullName}<{effectType.FullName}>.");
            return false;
        }

        var properties = effectType.Properties
            .Where(static property =>
                property.GetMethod is not null &&
                property.SetMethod is not null &&
                property.GetMethod.IsPublic &&
                property.SetMethod.IsPublic &&
                !property.GetMethod.IsStatic &&
                !property.HasParameters)
            .ToArray();

        if (properties.Any(static property => property.PropertyType.IsByReference))
        {
            result.Errors.Add($"[Effector.Build] Effect type '{effectType.FullName}' contains unsupported by-ref properties.");
            return false;
        }

        foreach (var property in properties)
        {
            if (!IsRenderThreadSafePropertyType(property.PropertyType))
            {
                result.Errors.Add(
                    $"[Effector.Build] Effect type '{effectType.FullName}' property '{property.Name}' has unsupported type '{property.PropertyType.FullName}'. " +
                    "Custom effect properties must be value types or System.String so the immutable snapshot can be used safely on the render thread.");
                return false;
            }
        }

        var supportsShaderFactory = ImplementsShaderFactory(factoryType, effectType);
        var supportsValueFactory = ImplementsInterface(factoryType, typeof(ISkiaEffectValueFactory).FullName);
        var supportsSourceCaptureValueFactory = ImplementsInterface(factoryType, SkiaSourceCaptureValueFactoryInterfaceName);
        var supportsShaderValueFactory = ImplementsInterface(factoryType, typeof(ISkiaShaderEffectValueFactory).FullName);
        if (!supportsValueFactory)
        {
            result.Errors.Add(
                $"[Effector.Build] Factory type '{factoryType.FullName}' must implement {typeof(ISkiaEffectValueFactory).FullName} so filters and padding can be created from immutable snapshots on the render thread.");
            return false;
        }

        if (supportsShaderFactory && !supportsShaderValueFactory)
        {
            result.Errors.Add(
                $"[Effector.Build] Factory type '{factoryType.FullName}' implements {typeof(ISkiaShaderEffectFactory<>).FullName} but does not implement {typeof(ISkiaShaderEffectValueFactory).FullName}. Shader effects must be creatable from immutable snapshots on the render thread.");
            return false;
        }

        model = new EffectDefinitionModel(
            effectType,
            factoryType,
            effectCtor,
            factoryCtor,
            effectAttribute,
            properties,
            supportsShaderFactory,
            supportsValueFactory,
            supportsSourceCaptureValueFactory,
            supportsShaderValueFactory);
        return true;
    }

    private static bool DerivesFrom(TypeDefinition type, string? baseTypeFullName)
    {
        var current = type;
        while (current.BaseType is not null)
        {
            if (current.BaseType.FullName == baseTypeFullName)
            {
                return true;
            }

            current = current.BaseType.Resolve();
            if (current is null)
            {
                break;
            }
        }

        return false;
    }

    private static bool IsRenderThreadSafePropertyType(TypeReference propertyType)
    {
        if (propertyType.IsValueType || propertyType.IsGenericParameter)
        {
            return true;
        }

        if (propertyType.FullName == typeof(string).FullName)
        {
            return true;
        }

        var resolved = propertyType.Resolve();
        return resolved is not null && ImplementsInterface(resolved, typeof(IEffectorImmutableValue).FullName);
    }

    private static bool ImplementsFactory(TypeDefinition factoryType, TypeDefinition effectType)
    {
        foreach (var implementedInterface in factoryType.Interfaces)
        {
            if (implementedInterface.InterfaceType is GenericInstanceType genericInterface &&
                genericInterface.ElementType.FullName == typeof(ISkiaEffectFactory<>).FullName &&
                genericInterface.GenericArguments.Count == 1 &&
                genericInterface.GenericArguments[0].FullName == effectType.FullName)
            {
                return true;
            }
        }

        return false;
    }

    private static bool ImplementsInterface(TypeDefinition type, string? fullName)
    {
        if (string.IsNullOrWhiteSpace(fullName))
        {
            return false;
        }

        foreach (var implementedInterface in type.Interfaces)
        {
            if (implementedInterface.InterfaceType.FullName == fullName)
            {
                return true;
            }
        }

        return false;
    }

    private static bool ImplementsShaderFactory(TypeDefinition factoryType, TypeDefinition effectType)
    {
        foreach (var implementedInterface in factoryType.Interfaces)
        {
            if (implementedInterface.InterfaceType is GenericInstanceType genericInterface &&
                genericInterface.ElementType.FullName == typeof(ISkiaShaderEffectFactory<>).FullName &&
                genericInterface.GenericArguments.Count == 1 &&
                genericInterface.GenericArguments[0].FullName == effectType.FullName)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsAlreadyWoven(ModuleDefinition module, EffectDefinitionModel model)
    {
        var immutableName = model.EffectType.Name + "__EffectorImmutable";
        var helperName = model.EffectType.Name + "__EffectorGenerated";

        return module.Types.Any(type =>
            type.Namespace == model.EffectType.Namespace &&
            (type.Name == immutableName || type.Name == helperName));
    }

    private static TypeDefinition CreateImmutableType(ModuleDefinition module, EffectDefinitionModel model)
    {
        var type = new TypeDefinition(
            model.EffectType.Namespace,
            model.EffectType.Name + "__EffectorImmutable",
            TypeAttributes.Class | TypeAttributes.NotPublic | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
            module.TypeSystem.Object);

        module.Types.Add(type);
        type.Interfaces.Add(new InterfaceImplementation(module.ImportReference(typeof(IImmutableEffect))));

        var fields = new List<FieldDefinition>(model.Properties.Count);
        foreach (var property in model.Properties)
        {
            var field = new FieldDefinition(
                "__" + property.Name + "BackingField",
                FieldAttributes.Private | FieldAttributes.InitOnly,
                module.ImportReference(property.PropertyType));
            type.Fields.Add(field);
            fields.Add(field);
        }

        var paddingField = new FieldDefinition(
            "__EffectorPadding",
            FieldAttributes.Private | FieldAttributes.InitOnly,
            module.ImportReference(typeof(Thickness)));
        type.Fields.Add(paddingField);
        var valuesField = new FieldDefinition(
            "__EffectorValues",
            FieldAttributes.Private | FieldAttributes.InitOnly,
            new ArrayType(module.TypeSystem.Object));
        type.Fields.Add(valuesField);

        var ctor = new MethodDefinition(
            ".ctor",
            MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName,
            module.TypeSystem.Void);

        foreach (var property in model.Properties)
        {
            ctor.Parameters.Add(new ParameterDefinition(property.Name, ParameterAttributes.None, module.ImportReference(property.PropertyType)));
        }
        ctor.Parameters.Add(new ParameterDefinition("padding", ParameterAttributes.None, module.ImportReference(typeof(Thickness))));
        ctor.Parameters.Add(new ParameterDefinition("values", ParameterAttributes.None, new ArrayType(module.TypeSystem.Object)));

        type.Methods.Add(ctor);
        var ctorIl = ctor.Body.GetILProcessor();
        ctorIl.Emit(OpCodes.Ldarg_0);
        ctorIl.Emit(OpCodes.Call, module.ImportReference(typeof(object).GetConstructor(Type.EmptyTypes)!));

        for (var index = 0; index < fields.Count; index++)
        {
            ctorIl.Emit(OpCodes.Ldarg_0);
            ctorIl.Emit(OpCodes.Ldarg, ctor.Parameters[index]);
            ctorIl.Emit(OpCodes.Stfld, fields[index]);
        }

        ctorIl.Emit(OpCodes.Ldarg_0);
        ctorIl.Emit(OpCodes.Ldarg, ctor.Parameters[ctor.Parameters.Count - 2]);
        ctorIl.Emit(OpCodes.Stfld, paddingField);

        ctorIl.Emit(OpCodes.Ldarg_0);
        ctorIl.Emit(OpCodes.Ldarg, ctor.Parameters[ctor.Parameters.Count - 1]);
        ctorIl.Emit(OpCodes.Stfld, valuesField);

        ctorIl.Emit(OpCodes.Ret);

        for (var index = 0; index < model.Properties.Count; index++)
        {
            var sourceProperty = model.Properties[index];
            var getter = new MethodDefinition(
                "get_" + sourceProperty.Name,
                MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.SpecialName,
                module.ImportReference(sourceProperty.PropertyType));
            type.Methods.Add(getter);

            var getterIl = getter.Body.GetILProcessor();
            getterIl.Emit(OpCodes.Ldarg_0);
            getterIl.Emit(OpCodes.Ldfld, fields[index]);
            getterIl.Emit(OpCodes.Ret);

            var generatedProperty = new PropertyDefinition(sourceProperty.Name, PropertyAttributes.None, module.ImportReference(sourceProperty.PropertyType))
            {
                GetMethod = getter
            };
            type.Properties.Add(generatedProperty);
        }

        var paddingGetter = new MethodDefinition(
            "GetEffectorPadding",
            MethodAttributes.Assembly | MethodAttributes.HideBySig,
            module.ImportReference(typeof(Thickness)));
        type.Methods.Add(paddingGetter);
        var paddingIl = paddingGetter.Body.GetILProcessor();
        paddingIl.Emit(OpCodes.Ldarg_0);
        paddingIl.Emit(OpCodes.Ldfld, paddingField);
        paddingIl.Emit(OpCodes.Ret);

        var valuesGetter = new MethodDefinition(
            "GetEffectorValues",
            MethodAttributes.Assembly | MethodAttributes.HideBySig,
            new ArrayType(module.TypeSystem.Object));
        type.Methods.Add(valuesGetter);
        var valuesIl = valuesGetter.Body.GetILProcessor();
        valuesIl.Emit(OpCodes.Ldarg_0);
        valuesIl.Emit(OpCodes.Ldfld, valuesField);
        valuesIl.Emit(OpCodes.Ret);

        return type;
    }

    private static TypeDefinition CreateHelperType(ModuleDefinition module, EffectDefinitionModel model, TypeDefinition immutableType)
    {
        var type = new TypeDefinition(
            model.EffectType.Namespace,
            model.EffectType.Name + "__EffectorGenerated",
            TypeAttributes.Class | TypeAttributes.NotPublic | TypeAttributes.Abstract | TypeAttributes.Sealed | TypeAttributes.BeforeFieldInit,
            module.TypeSystem.Object);

        module.Types.Add(type);

        var factoryField = new FieldDefinition("s_factory", FieldAttributes.Private | FieldAttributes.Static | FieldAttributes.InitOnly, module.ImportReference(model.FactoryType));
        type.Fields.Add(factoryField);

        var typeInitializer = new MethodDefinition(
            ".cctor",
            MethodAttributes.Private | MethodAttributes.Static | MethodAttributes.HideBySig | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName,
            module.TypeSystem.Void);
        type.Methods.Add(typeInitializer);
        var cctorIl = typeInitializer.Body.GetILProcessor();
        cctorIl.Emit(OpCodes.Newobj, module.ImportReference(model.FactoryParameterlessConstructor));
        cctorIl.Emit(OpCodes.Stsfld, factoryField);
        cctorIl.Emit(OpCodes.Ret);

        AddCreateMutableMethod(module, type, model);
        AddFreezeMethod(module, type, model, immutableType, factoryField);
        AddGetValuesMethod(module, type, model, immutableType);
        AddEqualityMethods(module, type, model, immutableType);
        AddPaddingAndFilterMethods(module, type, model, immutableType, factoryField);
        if (model.SupportsShaderFactory)
        {
            AddShaderMethod(module, type, model, factoryField);
        }

        return type;
    }

    private static MethodDefinition AddCreateMutableMethod(
        ModuleDefinition module,
        TypeDefinition helperType,
        EffectDefinitionModel model)
    {
        var method = new MethodDefinition(
            "CreateMutable",
            MethodAttributes.Assembly | MethodAttributes.Static | MethodAttributes.HideBySig,
            module.ImportReference(typeof(IEffect)));
        helperType.Methods.Add(method);

        var il = method.Body.GetILProcessor();
        il.Emit(OpCodes.Newobj, module.ImportReference(model.EffectParameterlessConstructor));
        il.Emit(OpCodes.Ret);
        return method;
    }

    private static MethodDefinition AddFreezeMethod(
        ModuleDefinition module,
        TypeDefinition helperType,
        EffectDefinitionModel model,
        TypeDefinition immutableType,
        FieldDefinition factoryField)
    {
        var method = new MethodDefinition(
            "Freeze",
            MethodAttributes.Assembly | MethodAttributes.Static | MethodAttributes.HideBySig,
            module.ImportReference(typeof(IImmutableEffect)));
        method.Parameters.Add(new ParameterDefinition("effect", ParameterAttributes.None, module.ImportReference(typeof(IEffect))));
        helperType.Methods.Add(method);

        var immutableCtor = immutableType.Methods.Single(static candidate => candidate.IsConstructor);
        var il = method.Body.GetILProcessor();
        var mutableLocal = new VariableDefinition(module.ImportReference(model.EffectType));
        var paddingLocal = new VariableDefinition(module.ImportReference(typeof(Thickness)));
        var valuesLocal = new VariableDefinition(new ArrayType(module.TypeSystem.Object));
        var valuePadding = ResolveFactoryMethod(
            model.FactoryType,
            nameof(ISkiaEffectValueFactory.GetPadding),
            1,
            module.ImportReference(typeof(object[])).FullName);
        method.Body.Variables.Add(mutableLocal);
        method.Body.Variables.Add(paddingLocal);
        method.Body.Variables.Add(valuesLocal);
        method.Body.InitLocals = true;

        var loadMutable = il.Create(OpCodes.Nop);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, module.ImportReference(immutableType));
        il.Emit(OpCodes.Brfalse_S, loadMutable);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, module.ImportReference(immutableType));
        il.Emit(OpCodes.Ret);

        il.Append(loadMutable);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, module.ImportReference(model.EffectType));
        il.Emit(OpCodes.Stloc, mutableLocal);

        il.Emit(OpCodes.Ldc_I4, model.Properties.Count);
        il.Emit(OpCodes.Newarr, module.TypeSystem.Object);
        for (var index = 0; index < model.Properties.Count; index++)
        {
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldc_I4, index);
            il.Emit(OpCodes.Ldloc, mutableLocal);
            il.Emit(OpCodes.Callvirt, module.ImportReference(model.Properties[index].GetMethod!));
            if (model.Properties[index].PropertyType.IsValueType || model.Properties[index].PropertyType.IsGenericParameter)
            {
                il.Emit(OpCodes.Box, module.ImportReference(model.Properties[index].PropertyType));
            }

            il.Emit(OpCodes.Stelem_Ref);
        }

        il.Emit(OpCodes.Stloc, valuesLocal);

        il.Emit(OpCodes.Ldsfld, factoryField);
        il.Emit(OpCodes.Ldloc, valuesLocal);
        il.Emit(OpCodes.Callvirt, module.ImportReference(valuePadding));
        il.Emit(OpCodes.Stloc, paddingLocal);

        foreach (var property in model.Properties)
        {
            il.Emit(OpCodes.Ldloc, mutableLocal);
            il.Emit(OpCodes.Callvirt, module.ImportReference(property.GetMethod!));
        }

        il.Emit(OpCodes.Ldloc, paddingLocal);
        il.Emit(OpCodes.Ldloc, valuesLocal);
        il.Emit(OpCodes.Newobj, module.ImportReference(immutableCtor));
        il.Emit(OpCodes.Ret);
        return method;
    }

    private static MethodDefinition AddGetValuesMethod(
        ModuleDefinition module,
        TypeDefinition helperType,
        EffectDefinitionModel model,
        TypeDefinition immutableType)
    {
        var method = new MethodDefinition(
            "GetValues",
            MethodAttributes.Private | MethodAttributes.Static | MethodAttributes.HideBySig,
            new ArrayType(module.TypeSystem.Object));
        method.Parameters.Add(new ParameterDefinition("effect", ParameterAttributes.None, module.ImportReference(typeof(IEffect))));
        helperType.Methods.Add(method);

        var il = method.Body.GetILProcessor();
        method.Body.InitLocals = true;
        var mutableLocal = new VariableDefinition(module.ImportReference(model.EffectType));
        method.Body.Variables.Add(mutableLocal);

        var fromMutable = il.Create(OpCodes.Nop);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, module.ImportReference(immutableType));
        il.Emit(OpCodes.Brfalse_S, fromMutable);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, module.ImportReference(immutableType));
        il.Emit(OpCodes.Call, module.ImportReference(immutableType.Methods.Single(static method => method.Name == "GetEffectorValues")));
        il.Emit(OpCodes.Ret);

        il.Append(fromMutable);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Castclass, module.ImportReference(model.EffectType));
        il.Emit(OpCodes.Stloc, mutableLocal);
        il.Emit(OpCodes.Ldc_I4, model.Properties.Count);
        il.Emit(OpCodes.Newarr, module.TypeSystem.Object);

        for (var index = 0; index < model.Properties.Count; index++)
        {
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Ldc_I4, index);
            il.Emit(OpCodes.Ldloc, mutableLocal);
            il.Emit(OpCodes.Callvirt, module.ImportReference(model.Properties[index].GetMethod!));

            if (model.Properties[index].PropertyType.IsValueType || model.Properties[index].PropertyType.IsGenericParameter)
            {
                il.Emit(OpCodes.Box, module.ImportReference(model.Properties[index].PropertyType));
            }

            il.Emit(OpCodes.Stelem_Ref);
        }

        il.Emit(OpCodes.Ret);
        return method;
    }

    private static void AddEqualityMethods(ModuleDefinition module, TypeDefinition helperType, EffectDefinitionModel model, TypeDefinition immutableType)
    {
        AddHelperEffectEquals(module, helperType, model, immutableType);
        AddHelperGetHashCode(module, helperType);
    }

    private static MethodDefinition AddHelperEffectEquals(ModuleDefinition module, TypeDefinition helperType, EffectDefinitionModel model, TypeDefinition immutableType)
    {
        var method = new MethodDefinition(
            "EffectEquals",
            MethodAttributes.Assembly | MethodAttributes.Static | MethodAttributes.HideBySig,
            module.TypeSystem.Boolean);
        method.Parameters.Add(new ParameterDefinition("left", ParameterAttributes.None, module.ImportReference(typeof(IEffect))));
        method.Parameters.Add(new ParameterDefinition("right", ParameterAttributes.None, module.ImportReference(typeof(IEffect))));
        helperType.Methods.Add(method);

        var getValues = helperType.Methods.Single(static candidate => candidate.Name == "GetValues");
        var valuesEqual = module.ImportReference(typeof(EffectorRuntime).GetMethod(nameof(EffectorRuntime.AreValuesEqual))!);
        var il = method.Body.GetILProcessor();
        var returnFalse = il.Create(OpCodes.Ldc_I4_0);
        var rightCheck = il.Create(OpCodes.Nop);
        var compare = il.Create(OpCodes.Nop);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Brfalse_S, returnFalse);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Brfalse_S, returnFalse);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, module.ImportReference(model.EffectType));
        il.Emit(OpCodes.Brtrue_S, rightCheck);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Isinst, module.ImportReference(immutableType));
        il.Emit(OpCodes.Brtrue_S, rightCheck);
        il.Emit(OpCodes.Br_S, returnFalse);

        il.Append(rightCheck);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, module.ImportReference(model.EffectType));
        il.Emit(OpCodes.Brtrue_S, compare);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Isinst, module.ImportReference(immutableType));
        il.Emit(OpCodes.Brtrue_S, compare);
        il.Emit(OpCodes.Br_S, returnFalse);

        il.Append(compare);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, getValues);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, getValues);
        il.Emit(OpCodes.Call, valuesEqual);
        il.Emit(OpCodes.Ret);

        il.Append(returnFalse);
        il.Emit(OpCodes.Ret);
        return method;
    }

    private static MethodDefinition AddHelperGetHashCode(ModuleDefinition module, TypeDefinition helperType)
    {
        var method = new MethodDefinition(
            "GetEffectHashCode",
            MethodAttributes.Assembly | MethodAttributes.Static | MethodAttributes.HideBySig,
            module.TypeSystem.Int32);
        method.Parameters.Add(new ParameterDefinition("effect", ParameterAttributes.None, module.ImportReference(typeof(IEffect))));
        helperType.Methods.Add(method);

        var getValues = helperType.Methods.Single(static candidate => candidate.Name == "GetValues");
        var combine = module.ImportReference(typeof(EffectorRuntime).GetMethod(nameof(EffectorRuntime.CombineHashCodes))!);
        var il = method.Body.GetILProcessor();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, getValues);
        il.Emit(OpCodes.Call, combine);
        il.Emit(OpCodes.Ret);
        return method;
    }

    private static void AddPaddingAndFilterMethods(
        ModuleDefinition module,
        TypeDefinition helperType,
        EffectDefinitionModel model,
        TypeDefinition immutableType,
        FieldDefinition factoryField)
    {
        var paddingMethod = new MethodDefinition(
            "GetPadding",
            MethodAttributes.Assembly | MethodAttributes.Static | MethodAttributes.HideBySig,
            module.ImportReference(typeof(Thickness)));
        paddingMethod.Parameters.Add(new ParameterDefinition("effect", ParameterAttributes.None, module.ImportReference(typeof(IEffect))));
        helperType.Methods.Add(paddingMethod);

        var getValues = helperType.Methods.Single(static candidate => candidate.Name == "GetValues");
        var valuePadding = ResolveFactoryMethod(
            model.FactoryType,
            nameof(ISkiaEffectValueFactory.GetPadding),
            1,
            module.ImportReference(typeof(object[])).FullName);
        var paddingIl = paddingMethod.Body.GetILProcessor();
        var returnFactory = paddingIl.Create(OpCodes.Nop);
        var paddingGetter = immutableType.Methods.Single(static method => method.Name == "GetEffectorPadding");

        paddingIl.Emit(OpCodes.Ldarg_0);
        paddingIl.Emit(OpCodes.Isinst, module.ImportReference(immutableType));
        paddingIl.Emit(OpCodes.Brfalse_S, returnFactory);
        paddingIl.Emit(OpCodes.Ldarg_0);
        paddingIl.Emit(OpCodes.Castclass, module.ImportReference(immutableType));
        paddingIl.Emit(OpCodes.Call, module.ImportReference(paddingGetter));
        paddingIl.Emit(OpCodes.Ret);

        paddingIl.Append(returnFactory);
        paddingIl.Emit(OpCodes.Ldsfld, factoryField);
        paddingIl.Emit(OpCodes.Ldarg_0);
        paddingIl.Emit(OpCodes.Call, getValues);
        paddingIl.Emit(OpCodes.Callvirt, module.ImportReference(valuePadding));
        paddingIl.Emit(OpCodes.Ret);

        var filterMethod = new MethodDefinition(
            "CreateFilter",
            MethodAttributes.Assembly | MethodAttributes.Static | MethodAttributes.HideBySig,
            module.ImportReference(typeof(SKImageFilter)));
        filterMethod.Parameters.Add(new ParameterDefinition("effect", ParameterAttributes.None, module.ImportReference(typeof(IEffect))));
        filterMethod.Parameters.Add(new ParameterDefinition("context", ParameterAttributes.None, module.ImportReference(typeof(SkiaEffectContext))));
        helperType.Methods.Add(filterMethod);

        var valueFilter = ResolveFactoryMethod(
            model.FactoryType,
            nameof(ISkiaEffectValueFactory.CreateFilter),
            2,
            module.ImportReference(typeof(object[])).FullName);
        var filterIl = filterMethod.Body.GetILProcessor();
        filterIl.Emit(OpCodes.Ldsfld, factoryField);
        filterIl.Emit(OpCodes.Ldarg_0);
        filterIl.Emit(OpCodes.Call, getValues);
        filterIl.Emit(OpCodes.Ldarg_1);
        filterIl.Emit(OpCodes.Callvirt, module.ImportReference(valueFilter));
        filterIl.Emit(OpCodes.Ret);

        if (!model.SupportsSourceCaptureValueFactory)
        {
            return;
        }

        var requiresCaptureMethod = new MethodDefinition(
            "RequiresSourceCapture",
            MethodAttributes.Assembly | MethodAttributes.Static | MethodAttributes.HideBySig,
            module.TypeSystem.Boolean);
        requiresCaptureMethod.Parameters.Add(new ParameterDefinition("effect", ParameterAttributes.None, module.ImportReference(typeof(IEffect))));
        helperType.Methods.Add(requiresCaptureMethod);

        var valueRequiresCapture = ResolveFactoryMethod(
            model.FactoryType,
            SkiaSourceCaptureValueFactoryMethodName,
            1,
            module.ImportReference(typeof(object[])).FullName);
        var requiresCaptureIl = requiresCaptureMethod.Body.GetILProcessor();
        requiresCaptureIl.Emit(OpCodes.Ldsfld, factoryField);
        requiresCaptureIl.Emit(OpCodes.Ldarg_0);
        requiresCaptureIl.Emit(OpCodes.Call, getValues);
        requiresCaptureIl.Emit(OpCodes.Callvirt, module.ImportReference(valueRequiresCapture));
        requiresCaptureIl.Emit(OpCodes.Ret);
    }

    private static MethodDefinition AddShaderMethod(ModuleDefinition module, TypeDefinition helperType, EffectDefinitionModel model, FieldDefinition factoryField)
    {
        var method = new MethodDefinition(
            "CreateShaderEffect",
            MethodAttributes.Assembly | MethodAttributes.Static | MethodAttributes.HideBySig,
            module.ImportReference(typeof(SkiaShaderEffect)));
        method.Parameters.Add(new ParameterDefinition("effect", ParameterAttributes.None, module.ImportReference(typeof(IEffect))));
        method.Parameters.Add(new ParameterDefinition("context", ParameterAttributes.None, module.ImportReference(typeof(SkiaShaderEffectContext))));
        helperType.Methods.Add(method);

        var valueMethod = ResolveFactoryMethod(
            model.FactoryType,
            nameof(ISkiaShaderEffectValueFactory.CreateShaderEffect),
            2,
            module.ImportReference(typeof(object[])).FullName);
        var il = method.Body.GetILProcessor();
        var getValues = helperType.Methods.Single(static candidate => candidate.Name == "GetValues");
        il.Emit(OpCodes.Ldsfld, factoryField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, getValues);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Callvirt, module.ImportReference(valueMethod));
        il.Emit(OpCodes.Ret);
        return method;
    }

    private static MethodReference ResolveFactoryMethod(TypeDefinition factoryType, string methodName, int parameterCount)
    {
        var method = factoryType.Methods.First(candidate => candidate.Name == methodName && candidate.Parameters.Count == parameterCount);
        return method;
    }

    private static MethodReference ResolveFactoryMethod(TypeDefinition factoryType, string methodName, int parameterCount, string? firstParameterType)
    {
        var method = factoryType.Methods.First(candidate =>
            candidate.Name == methodName &&
            candidate.Parameters.Count == parameterCount &&
            string.Equals(candidate.Parameters[0].ParameterType.FullName, firstParameterType, StringComparison.Ordinal));
        return method;
    }

    private static MethodDefinition AddRegistrationMethod(ModuleDefinition module, EffectDefinitionModel model, TypeDefinition immutableType, TypeDefinition helperType)
    {
        var method = new MethodDefinition(
            "RegisterEffect",
            MethodAttributes.Assembly | MethodAttributes.Static | MethodAttributes.HideBySig,
            module.TypeSystem.Void);
        helperType.Methods.Add(method);

        var register = module.ImportReference(typeof(EffectorRuntime).GetMethod(nameof(EffectorRuntime.Register))!);
        var createMutableDelegateCtor = module.ImportReference(typeof(Func<IEffect>).GetConstructors().Single());
        var freezeDelegateCtor = module.ImportReference(typeof(Func<IEffect, IImmutableEffect>).GetConstructors().Single());
        var paddingDelegateCtor = module.ImportReference(typeof(Func<IEffect, Thickness>).GetConstructors().Single());
        var filterDelegateCtor = module.ImportReference(typeof(Func<IEffect, SkiaEffectContext, SKImageFilter>).GetConstructors().Single());
        var requiresCaptureDelegateCtor = module.ImportReference(typeof(Func<IEffect, bool>).GetConstructors().Single());
        var shaderDelegateCtor = module.ImportReference(typeof(Func<IEffect, SkiaShaderEffectContext, SkiaShaderEffect>).GetConstructors().Single());
        var createMutableMethod = helperType.Methods.Single(static candidate => candidate.Name == "CreateMutable");
        var freezeMethod = helperType.Methods.Single(static candidate => candidate.Name == "Freeze");
        var paddingMethod = helperType.Methods.Single(static candidate => candidate.Name == "GetPadding");
        var filterMethod = helperType.Methods.Single(static candidate => candidate.Name == "CreateFilter");
        var requiresCaptureMethod = model.SupportsSourceCaptureValueFactory
            ? helperType.Methods.Single(static candidate => candidate.Name == "RequiresSourceCapture")
            : null;
        var shaderMethod = model.SupportsShaderFactory
            ? helperType.Methods.Single(static candidate => candidate.Name == "CreateShaderEffect")
            : null;

        var il = method.Body.GetILProcessor();
        il.Emit(OpCodes.Ldtoken, module.ImportReference(model.EffectType));
        il.Emit(OpCodes.Call, module.ImportReference(typeof(Type).GetMethod(nameof(Type.GetTypeFromHandle))!));
        il.Emit(OpCodes.Ldtoken, module.ImportReference(immutableType));
        il.Emit(OpCodes.Call, module.ImportReference(typeof(Type).GetMethod(nameof(Type.GetTypeFromHandle))!));

        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ldftn, createMutableMethod);
        il.Emit(OpCodes.Newobj, createMutableDelegateCtor);

        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ldftn, freezeMethod);
        il.Emit(OpCodes.Newobj, freezeDelegateCtor);

        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ldftn, paddingMethod);
        il.Emit(OpCodes.Newobj, paddingDelegateCtor);

        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ldftn, filterMethod);
        il.Emit(OpCodes.Newobj, filterDelegateCtor);

        if (requiresCaptureMethod is not null)
        {
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Ldftn, requiresCaptureMethod);
            il.Emit(OpCodes.Newobj, requiresCaptureDelegateCtor);
        }
        else
        {
            il.Emit(OpCodes.Ldnull);
        }

        if (shaderMethod is not null)
        {
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Ldftn, shaderMethod);
            il.Emit(OpCodes.Newobj, shaderDelegateCtor);
        }
        else
        {
            il.Emit(OpCodes.Ldnull);
        }

        il.Emit(OpCodes.Call, register);
        il.Emit(OpCodes.Ret);
        return method;
    }

    private static void AddImmutableEquality(ModuleDefinition module, TypeDefinition immutableType, TypeDefinition helperType)
    {
        var effectEquals = helperType.Methods.Single(static candidate => candidate.Name == "EffectEquals");
        var hashCode = helperType.Methods.Single(static candidate => candidate.Name == "GetEffectHashCode");
        var equatableEquals = module.ImportReference(typeof(IEquatable<IEffect>).GetMethod(nameof(IEquatable<IEffect>.Equals))!);

        var equalsMethod = new MethodDefinition(
            nameof(IEquatable<IEffect>.Equals),
            MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.NewSlot | MethodAttributes.Virtual | MethodAttributes.Final,
            module.TypeSystem.Boolean);
        equalsMethod.Parameters.Add(new ParameterDefinition("other", ParameterAttributes.None, module.ImportReference(typeof(IEffect))));
        equalsMethod.Overrides.Add(equatableEquals);
        immutableType.Methods.Add(equalsMethod);

        var equalsIl = equalsMethod.Body.GetILProcessor();
        equalsIl.Emit(OpCodes.Ldarg_0);
        equalsIl.Emit(OpCodes.Ldarg_1);
        equalsIl.Emit(OpCodes.Call, effectEquals);
        equalsIl.Emit(OpCodes.Ret);

        var objectEquals = new MethodDefinition(
            "Equals",
            MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.Virtual,
            module.TypeSystem.Boolean);
        objectEquals.Parameters.Add(new ParameterDefinition("obj", ParameterAttributes.None, module.TypeSystem.Object));
        immutableType.Methods.Add(objectEquals);

        var objectEqualsIl = objectEquals.Body.GetILProcessor();
        objectEquals.Body.InitLocals = true;
        var otherLocal = new VariableDefinition(module.ImportReference(typeof(IEffect)));
        objectEquals.Body.Variables.Add(otherLocal);
        var returnFalse = objectEqualsIl.Create(OpCodes.Ldc_I4_0);
        objectEqualsIl.Emit(OpCodes.Ldarg_1);
        objectEqualsIl.Emit(OpCodes.Isinst, module.ImportReference(typeof(IEffect)));
        objectEqualsIl.Emit(OpCodes.Stloc, otherLocal);
        objectEqualsIl.Emit(OpCodes.Ldloc, otherLocal);
        objectEqualsIl.Emit(OpCodes.Brfalse_S, returnFalse);
        objectEqualsIl.Emit(OpCodes.Ldarg_0);
        objectEqualsIl.Emit(OpCodes.Ldloc, otherLocal);
        objectEqualsIl.Emit(OpCodes.Call, equalsMethod);
        objectEqualsIl.Emit(OpCodes.Ret);
        objectEqualsIl.Append(returnFalse);
        objectEqualsIl.Emit(OpCodes.Ret);

        var getHashCode = new MethodDefinition(
            nameof(GetHashCode),
            MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.Virtual,
            module.TypeSystem.Int32);
        immutableType.Methods.Add(getHashCode);

        var hashIl = getHashCode.Body.GetILProcessor();
        hashIl.Emit(OpCodes.Ldarg_0);
        hashIl.Emit(OpCodes.Call, hashCode);
        hashIl.Emit(OpCodes.Ret);
    }

    private static void InjectModuleInitializer(ModuleDefinition module, IReadOnlyList<MethodDefinition> registrationMethods)
    {
        var moduleType = module.Types.First(static type => type.Name == "<Module>");
        var cctor = moduleType.Methods.FirstOrDefault(static method => method.IsConstructor && method.IsStatic);

        if (cctor is null)
        {
            cctor = new MethodDefinition(
                ".cctor",
                MethodAttributes.Private | MethodAttributes.Static | MethodAttributes.HideBySig | MethodAttributes.SpecialName | MethodAttributes.RTSpecialName,
                module.TypeSystem.Void);
            moduleType.Methods.Add(cctor);
            cctor.Body.GetILProcessor().Emit(OpCodes.Ret);
        }

        var il = cctor.Body.GetILProcessor();
        var first = cctor.Body.Instructions.First();
        foreach (var registrationMethod in registrationMethods)
        {
            il.InsertBefore(first, il.Create(OpCodes.Call, registrationMethod));
        }
    }
}
