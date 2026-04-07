using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Media;
using Effector;
using Mono.Cecil;
using Mono.Cecil.Cil;
using SkiaSharp;

namespace Effector.Build.Tasks;

internal sealed class AvaloniaAssemblyPatcher
{
    private static MethodInfo GetEffectorRuntimeMethod(string name, params Type[] parameterTypes) =>
        typeof(EffectorRuntime).GetMethod(
            name,
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
            binder: null,
            types: parameterTypes,
            modifiers: null)
        ?? throw new MissingMethodException(typeof(EffectorRuntime).FullName, name);

    public AvaloniaAssemblyPatchResult Patch(string assemblyPath, AvaloniaPatchAssemblyKind kind, string supportedVersion)
    {
        var result = new AvaloniaAssemblyPatchResult(assemblyPath, kind);
        if (string.IsNullOrWhiteSpace(assemblyPath) || !File.Exists(assemblyPath))
        {
            return result;
        }

        var scan = new AvaloniaPatchMetadataScanner().Scan(assemblyPath, supportedVersion, kind);
        result.AssemblyVersion = scan.AssemblyVersion;

        if (!scan.IsSupportedVersion)
        {
            result.Errors.Add(
                $"[Effector.Build] {scan.AssemblyName} version {scan.AssemblyVersion} is not supported. Expected {supportedVersion}.");
            return result;
        }

        if (scan.MissingRequirements.Count > 0)
        {
            result.Errors.Add(
                $"[Effector.Build] {scan.AssemblyName} did not contain the expected patch targets: {string.Join(", ", scan.MissingRequirements)}.");
            return result;
        }

        if (scan.IsAlreadyPatched)
        {
            result.AlreadyPatched = true;
            return result;
        }

        var pdbPath = Path.ChangeExtension(assemblyPath, ".pdb");
        var readSymbols = File.Exists(pdbPath);
        var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(Path.GetDirectoryName(assemblyPath)!);
        resolver.AddSearchDirectory(Path.GetDirectoryName(typeof(EffectorRuntime).Assembly.Location)!);
        resolver.AddSearchDirectory(Path.GetDirectoryName(typeof(SKCanvas).Assembly.Location)!);
        resolver.AddSearchDirectory(Path.GetDirectoryName(typeof(Effect).Assembly.Location)!);

        var readerParameters = new ReaderParameters
        {
            AssemblyResolver = resolver,
            ReadSymbols = readSymbols,
            InMemory = true,
            SymbolReaderProvider = readSymbols ? new PortablePdbReaderProvider() : null
        };

        using var assembly = AssemblyDefinition.ReadAssembly(assemblyPath, readerParameters);
        if (kind == AvaloniaPatchAssemblyKind.Base)
        {
            PatchAvaloniaBase(assembly.MainModule);
        }
        else
        {
            PatchAvaloniaSkia(assembly.MainModule);
        }

        var writerParameters = new WriterParameters
        {
            WriteSymbols = readSymbols,
            SymbolWriterProvider = readSymbols ? new PortablePdbWriterProvider() : null
        };

        var tempAssemblyPath = assemblyPath + ".effector.tmp";
        var tempPdbPath = Path.ChangeExtension(tempAssemblyPath, ".pdb");

        try
        {
            if (File.Exists(tempAssemblyPath))
            {
                File.Delete(tempAssemblyPath);
            }

            if (File.Exists(tempPdbPath))
            {
                File.Delete(tempPdbPath);
            }

            assembly.Write(tempAssemblyPath, writerParameters);
            if (File.Exists(assemblyPath))
            {
                File.Delete(assemblyPath);
            }
            File.Move(tempAssemblyPath, assemblyPath);

            if (readSymbols && File.Exists(tempPdbPath))
            {
                if (File.Exists(pdbPath))
                {
                    File.Delete(pdbPath);
                }

                File.Move(tempPdbPath, pdbPath);
            }
        }
        finally
        {
            if (File.Exists(tempAssemblyPath))
            {
                File.Delete(tempAssemblyPath);
            }

            if (File.Exists(tempPdbPath))
            {
                File.Delete(tempPdbPath);
            }
        }

        result.Patched = true;
        return result;
    }

    private static void PatchAvaloniaBase(ModuleDefinition module)
    {
        var effectExtensions = GetType(module, "Avalonia.Media.EffectExtensions");
        var effectType = GetType(module, "Avalonia.Media.Effect");
        var effectTransitionType = GetType(module, "Avalonia.Animation.EffectTransition");
        var effectAnimatorType = GetType(module, "Avalonia.Animation.Animators.EffectAnimator");

        RewriteStaticSingleArgumentMethod(
            module,
            GetMethod(effectExtensions, "GetEffectOutputPadding", 1),
            typeof(EffectorRuntime).GetMethod(nameof(EffectorRuntime.GetEffectOutputPaddingPatched))!);
        RewriteStaticSingleArgumentMethod(
            module,
            GetMethod(effectExtensions, "ToImmutable", 1),
            typeof(EffectorRuntime).GetMethod(nameof(EffectorRuntime.ToImmutablePatched))!);
        RewriteStaticSingleArgumentMethod(
            module,
            GetMethod(effectType, "Parse", 1),
            typeof(EffectorRuntime).GetMethod(nameof(EffectorRuntime.ParseEffectPatched))!);

        InjectEffectTransitionPrefix(module, GetMethod(effectTransitionType, "DoTransition", 3));
        InjectEffectAnimatorApplyPrefix(module, GetMethod(effectAnimatorType, "Apply", 6));
        RewriteEffectAnimatorInterpolate(module, GetMethod(effectAnimatorType, "Interpolate", 3));
        // Do not rewrite ServerCompositionVisual.PushEffect. The patched body destabilizes the
        // desktop JIT on macOS arm64 and can crash the sample app in the render loop.
    }

    private static void PatchAvaloniaSkia(ModuleDefinition module)
    {
        var drawingContextType = GetType(module, "Avalonia.Skia.DrawingContextImpl");
        var currentOpacityField = GetField(drawingContextType, "_currentOpacity");
        var useOpacitySaveLayerField = GetField(drawingContextType, "_useOpacitySaveLayer");
        var baseCanvasField = GetField(drawingContextType, "_baseCanvas", "<Canvas>k__BackingField");
        var baseSurfaceField = GetField(drawingContextType, "_baseSurface", "<Surface>k__BackingField");
        var currentTransformField = GetField(drawingContextType, "_currentTransform");
        var postTransformField = GetField(drawingContextType, "_postTransform");

        RewriteDrawingContextCreateEffect(
            module,
            GetMethod(drawingContextType, "CreateEffect", 1),
            currentOpacityField,
            useOpacitySaveLayerField);
        RewriteDrawingContextPushEffect(
            module,
            GetMethod(drawingContextType, "PushEffect", 2));
        RewriteDrawingContextPopEffect(
            module,
            GetMethod(drawingContextType, "PopEffect", 0));
        RewriteCanvasGetter(
            module,
            GetMethod(drawingContextType, "get_Canvas", 0),
            baseCanvasField);
        RewriteSurfaceGetter(
            module,
            GetMethod(drawingContextType, "get_Surface", 0),
            baseSurfaceField);
        RewriteTransformSetter(
            module,
            GetMethod(drawingContextType, "set_Transform", 1),
            currentTransformField,
            postTransformField);
    }

    private static void RewriteStaticSingleArgumentMethod(ModuleDefinition module, MethodDefinition method, MethodInfo runtimeMethod)
    {
        method.Body.Instructions.Clear();
        method.Body.ExceptionHandlers.Clear();
        method.Body.Variables.Clear();
        method.Body.InitLocals = false;

        var il = method.Body.GetILProcessor();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, module.ImportReference(runtimeMethod));
        il.Emit(OpCodes.Ret);
    }

    private static void InjectEffectTransitionPrefix(ModuleDefinition module, MethodDefinition method)
    {
        var observableType = module.ImportReference(typeof(IObservable<IEffect?>));
        var easingProperty = typeof(Animation).Assembly
            .GetType("Avalonia.Animation.TransitionBase")?
            .GetProperty("Easing", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new MissingMemberException("Avalonia.Animation.TransitionBase", "Easing");
        var createObservable = module.ImportReference(typeof(EffectorRuntime).GetMethod(nameof(EffectorRuntime.TryCreateTransitionObservable))!);
        var easingGetter = module.ImportReference(easingProperty.GetMethod!);

        method.Body.InitLocals = true;
        var observableLocal = new VariableDefinition(observableType);
        method.Body.Variables.Add(observableLocal);

        var il = method.Body.GetILProcessor();
        var originalFirst = method.Body.Instructions.First();
        var continueOriginal = il.Create(OpCodes.Nop);

        il.InsertBefore(originalFirst, il.Create(OpCodes.Ldarg_1));
        il.InsertBefore(originalFirst, il.Create(OpCodes.Ldarg_0));
        il.InsertBefore(originalFirst, il.Create(OpCodes.Call, easingGetter));
        il.InsertBefore(originalFirst, il.Create(OpCodes.Ldarg_2));
        il.InsertBefore(originalFirst, il.Create(OpCodes.Ldarg_3));
        il.InsertBefore(originalFirst, il.Create(OpCodes.Ldloca_S, observableLocal));
        il.InsertBefore(originalFirst, il.Create(OpCodes.Call, createObservable));
        il.InsertBefore(originalFirst, il.Create(OpCodes.Brfalse_S, continueOriginal));
        il.InsertBefore(originalFirst, il.Create(OpCodes.Ldloc, observableLocal));
        il.InsertBefore(originalFirst, il.Create(OpCodes.Ret));
        il.InsertBefore(originalFirst, continueOriginal);
    }

    private static void InjectEffectAnimatorApplyPrefix(ModuleDefinition module, MethodDefinition method)
    {
        var disposableType = module.ImportReference(typeof(IDisposable));
        var tryApply = module.ImportReference(typeof(EffectorRuntime).GetMethod(nameof(EffectorRuntime.TryApplyCustomEffectAnimator))!);

        method.Body.InitLocals = true;
        var disposableLocal = new VariableDefinition(disposableType);
        method.Body.Variables.Add(disposableLocal);

        var il = method.Body.GetILProcessor();
        var originalFirst = method.Body.Instructions.First();
        var continueOriginal = il.Create(OpCodes.Nop);

        il.InsertBefore(originalFirst, il.Create(OpCodes.Ldarg_0));
        il.InsertBefore(originalFirst, il.Create(OpCodes.Ldarg_1));
        il.InsertBefore(originalFirst, il.Create(OpCodes.Ldarg_2));
        il.InsertBefore(originalFirst, il.Create(OpCodes.Ldarg_3));
        il.InsertBefore(originalFirst, il.Create(OpCodes.Ldarg_S, method.Parameters[3]));
        il.InsertBefore(originalFirst, il.Create(OpCodes.Ldarg_S, method.Parameters[4]));
        il.InsertBefore(originalFirst, il.Create(OpCodes.Ldarg_S, method.Parameters[5]));
        il.InsertBefore(originalFirst, il.Create(OpCodes.Ldloca_S, disposableLocal));
        il.InsertBefore(originalFirst, il.Create(OpCodes.Call, tryApply));
        il.InsertBefore(originalFirst, il.Create(OpCodes.Brfalse_S, continueOriginal));
        il.InsertBefore(originalFirst, il.Create(OpCodes.Ldloc, disposableLocal));
        il.InsertBefore(originalFirst, il.Create(OpCodes.Ret));
        il.InsertBefore(originalFirst, continueOriginal);
    }

    private static void RewriteEffectAnimatorInterpolate(ModuleDefinition module, MethodDefinition method)
    {
        var tryInterpolate = module.ImportReference(typeof(EffectorRuntime).GetMethod(nameof(EffectorRuntime.TryInterpolateEffect))!);
        method.Body.Instructions.Clear();
        method.Body.ExceptionHandlers.Clear();
        method.Body.Variables.Clear();
        method.Body.InitLocals = true;

        var resultLocal = new VariableDefinition(module.ImportReference(typeof(IEffect)));
        method.Body.Variables.Add(resultLocal);

        var il = method.Body.GetILProcessor();
        var fallback = il.Create(OpCodes.Nop);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Ldloca_S, resultLocal);
        il.Emit(OpCodes.Call, tryInterpolate);
        il.Emit(OpCodes.Brfalse_S, fallback);
        il.Emit(OpCodes.Ldloc, resultLocal);
        il.Emit(OpCodes.Ret);

        il.Append(fallback);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldc_R8, 0.5d);
        il.Emit(OpCodes.Clt);
        var returnOld = il.Create(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Brtrue_S, returnOld);
        il.Emit(OpCodes.Ldarg_3);
        il.Emit(OpCodes.Ret);
        il.Append(returnOld);
        il.Emit(OpCodes.Ret);
    }

    private static void RewriteServerCompositionPushEffect(ModuleDefinition module, MethodDefinition method)
    {
        method.Body.Instructions.Clear();
        method.Body.ExceptionHandlers.Clear();
        method.Body.Variables.Clear();
        method.Body.InitLocals = false;

        var serverCompositionVisualType = method.DeclaringType;
        var effectProperty = serverCompositionVisualType.Properties.Single(static property => property.Name == "Effect").GetMethod
            ?? throw new MissingMethodException(serverCompositionVisualType.FullName, "get_Effect");
        var getEffectBounds = GetMethod(serverCompositionVisualType, "GetEffectBounds", 0);
        var boundsToRect = getEffectBounds.ReturnType.Resolve()?.Methods.FirstOrDefault(candidate => candidate.Name == "ToRect" && candidate.Parameters.Count == 0)
            ?? throw new MissingMethodException(getEffectBounds.ReturnType.FullName, "ToRect");
        var transformProperty = method.Parameters[0].ParameterType.Resolve()?.Properties.Single(static property => property.Name == "Transform")
            ?? throw new MissingMemberException(method.Parameters[0].ParameterType.FullName, "Transform");
        var pushEffectMethod = GetMethod(method.Parameters[0].ParameterType.Resolve()!, "PushEffect", 2);
        var recordMethod = module.ImportReference(typeof(EffectorRuntime).GetMethod(nameof(EffectorRuntime.RecordRenderThreadEffect))!);
        var matrixIdentityGetter = module.ImportReference(typeof(Matrix).GetProperty(nameof(Matrix.Identity), BindingFlags.Static | BindingFlags.Public)!.GetMethod!);
        var effectLocal = new VariableDefinition(module.ImportReference(typeof(IEffect)));
        var clipLocal = new VariableDefinition(module.ImportReference(typeof(Rect)));
        var oldMatrixLocal = new VariableDefinition(module.ImportReference(typeof(Matrix)));

        method.Body.InitLocals = true;
        method.Body.Variables.Add(effectLocal);
        method.Body.Variables.Add(clipLocal);
        method.Body.Variables.Add(oldMatrixLocal);

        var il = method.Body.GetILProcessor();
        var returnFalse = il.Create(OpCodes.Ldc_I4_0);
        var returnTrue = il.Create(OpCodes.Ldc_I4_1);
        var continueIfEffect = il.Create(OpCodes.Nop);
        var continueIfClip = il.Create(OpCodes.Nop);
        var nullableRectCtor = module.ImportReference(typeof(Nullable<Rect>).GetConstructor(new[] { typeof(Rect) })!);
        var widthGetter = module.ImportReference(typeof(Rect).GetProperty(nameof(Rect.Width))!.GetMethod!);
        var heightGetter = module.ImportReference(typeof(Rect).GetProperty(nameof(Rect.Height))!.GetMethod!);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, module.ImportReference(effectProperty));
        il.Emit(OpCodes.Stloc, effectLocal);
        il.Emit(OpCodes.Ldloc, effectLocal);
        il.Emit(OpCodes.Brtrue_S, continueIfEffect);
        il.Emit(OpCodes.Br_S, returnFalse);

        il.Append(continueIfEffect);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, getEffectBounds);
        il.Emit(OpCodes.Call, module.ImportReference(boundsToRect));
        il.Emit(OpCodes.Stloc, clipLocal);
        il.Emit(OpCodes.Ldloca_S, clipLocal);
        il.Emit(OpCodes.Call, widthGetter);
        il.Emit(OpCodes.Ldc_R8, 0d);
        il.Emit(OpCodes.Cgt);
        il.Emit(OpCodes.Brfalse_S, returnFalse);
        il.Emit(OpCodes.Ldloca_S, clipLocal);
        il.Emit(OpCodes.Call, heightGetter);
        il.Emit(OpCodes.Ldc_R8, 0d);
        il.Emit(OpCodes.Cgt);
        il.Emit(OpCodes.Brtrue_S, continueIfClip);
        il.Emit(OpCodes.Br_S, returnFalse);

        il.Append(continueIfClip);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, module.ImportReference(transformProperty.GetMethod!));
        il.Emit(OpCodes.Stloc, oldMatrixLocal);

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Call, matrixIdentityGetter);
        il.Emit(OpCodes.Call, module.ImportReference(transformProperty.SetMethod!));

        il.Emit(OpCodes.Ldloc, effectLocal);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloc, clipLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, recordMethod);

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloc, clipLocal);
        il.Emit(OpCodes.Newobj, nullableRectCtor);
        il.Emit(OpCodes.Ldloc, effectLocal);
        il.Emit(OpCodes.Callvirt, module.ImportReference(pushEffectMethod));

        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldloc, oldMatrixLocal);
        il.Emit(OpCodes.Call, module.ImportReference(transformProperty.SetMethod!));
        il.Emit(OpCodes.Br_S, returnTrue);

        il.Append(returnFalse);
        il.Emit(OpCodes.Ret);
        il.Append(returnTrue);
        il.Emit(OpCodes.Ret);
    }

    private static void RewriteDrawingContextCreateEffect(
        ModuleDefinition module,
        MethodDefinition method,
        FieldDefinition currentOpacityField,
        FieldDefinition useOpacitySaveLayerField)
    {
        method.Body.Instructions.Clear();
        method.Body.ExceptionHandlers.Clear();
        method.Body.Variables.Clear();
        method.Body.InitLocals = false;

        var il = method.Body.GetILProcessor();
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, currentOpacityField);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, useOpacitySaveLayerField);
        il.Emit(OpCodes.Call, module.ImportReference(GetEffectorRuntimeMethod(
            nameof(EffectorRuntime.CreateEffectPatched),
            typeof(IEffect),
            typeof(double),
            typeof(bool))));
        il.Emit(OpCodes.Ret);
    }

    private static void RewriteDrawingContextPushEffect(ModuleDefinition module, MethodDefinition method)
    {
        method.Body.Instructions.Clear();
        method.Body.ExceptionHandlers.Clear();
        method.Body.Variables.Clear();
        method.Body.InitLocals = true;

        var skImageFilterType = module.ImportReference(typeof(SKImageFilter));
        var filterLocal = new VariableDefinition(skImageFilterType);
        method.Body.Variables.Add(filterLocal);

        var checkLease = GetMethod(method.DeclaringType, "CheckLease", 0);
        var createEffect = GetMethod(method.DeclaringType, "CreateEffect", 1);
        var getCanvas = GetMethod(method.DeclaringType, "get_Canvas", 0);
        var saveLayerNoRect = module.ImportReference(typeof(SKCanvas).GetMethods().Single(candidate =>
            candidate.Name == nameof(SKCanvas.SaveLayer) &&
            candidate.GetParameters().Length == 1 &&
            candidate.GetParameters()[0].ParameterType == typeof(SKPaint)));
        var saveLayerWithRect = module.ImportReference(typeof(SKCanvas).GetMethods().Single(candidate =>
            candidate.Name == nameof(SKCanvas.SaveLayer) &&
            candidate.GetParameters().Length == 2 &&
            candidate.GetParameters()[0].ParameterType == typeof(SKRect) &&
            candidate.GetParameters()[1].ParameterType == typeof(SKPaint)));
        var toSkRect = module.ImportReference(typeof(EffectorRuntime).GetMethod(nameof(EffectorRuntime.ToSKRectPatched))!);
        var beginShader = module.ImportReference(typeof(EffectorRuntime).GetMethod(nameof(EffectorRuntime.TryBeginShaderEffectPatched))!);
        var nullableRectHasValue = module.ImportReference(typeof(Nullable<Rect>).GetProperty(nameof(Nullable<Rect>.HasValue))!.GetMethod!);
        var nullableRectValue = module.ImportReference(typeof(Nullable<Rect>).GetProperty(nameof(Nullable<Rect>.Value))!.GetMethod!);
        var imageFilterProperty = module.ImportReference(typeof(SKPaint).GetProperty(nameof(SKPaint.ImageFilter))!.SetMethod!);
        var paintCtor = module.ImportReference(typeof(SKPaint).GetConstructor(Type.EmptyTypes)!);
        var paintDispose = module.ImportReference(typeof(SKPaint).GetMethod(nameof(SKPaint.Dispose))!);
        var paintLocal = new VariableDefinition(module.ImportReference(typeof(SKPaint)));
        method.Body.Variables.Add(paintLocal);

        var afterShader = Instruction.Create(OpCodes.Nop);
        var noClip = Instruction.Create(OpCodes.Nop);
        var afterUsing = Instruction.Create(OpCodes.Nop);
        var skipFilterDispose = Instruction.Create(OpCodes.Nop);

        var il = method.Body.GetILProcessor();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, checkLease);

        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Call, beginShader);
        il.Emit(OpCodes.Brfalse_S, afterShader);
        il.Emit(OpCodes.Ret);

        il.Append(afterShader);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_2);
        il.Emit(OpCodes.Call, createEffect);
        il.Emit(OpCodes.Stloc, filterLocal);

        il.Emit(OpCodes.Newobj, paintCtor);
        il.Emit(OpCodes.Stloc, paintLocal);

        il.Emit(OpCodes.Ldloc, paintLocal);
        il.Emit(OpCodes.Ldloc, filterLocal);
        il.Emit(OpCodes.Callvirt, imageFilterProperty);

        il.Emit(OpCodes.Ldarga_S, method.Parameters[0]);
        il.Emit(OpCodes.Call, nullableRectHasValue);
        il.Emit(OpCodes.Brfalse_S, noClip);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, getCanvas);
        il.Emit(OpCodes.Ldarga_S, method.Parameters[0]);
        il.Emit(OpCodes.Call, nullableRectValue);
        il.Emit(OpCodes.Call, toSkRect);
        il.Emit(OpCodes.Ldloc, paintLocal);
        il.Emit(OpCodes.Callvirt, saveLayerWithRect);
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Br_S, afterUsing);

        il.Append(noClip);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, getCanvas);
        il.Emit(OpCodes.Ldloc, paintLocal);
        il.Emit(OpCodes.Callvirt, saveLayerNoRect);
        il.Emit(OpCodes.Pop);

        il.Append(afterUsing);
        il.Emit(OpCodes.Ldloc, paintLocal);
        il.Emit(OpCodes.Callvirt, paintDispose);

        il.Emit(OpCodes.Ldloc, filterLocal);
        il.Emit(OpCodes.Brfalse_S, skipFilterDispose);
        il.Emit(OpCodes.Ldloc, filterLocal);
        il.Emit(OpCodes.Callvirt, module.ImportReference(typeof(IDisposable).GetMethod(nameof(IDisposable.Dispose))!));
        il.Append(skipFilterDispose);

        il.Emit(OpCodes.Ret);
    }

    private static void RewriteDrawingContextPopEffect(ModuleDefinition module, MethodDefinition method)
    {
        method.Body.Instructions.Clear();
        method.Body.ExceptionHandlers.Clear();
        method.Body.Variables.Clear();
        method.Body.InitLocals = false;

        var checkLease = GetMethod(method.DeclaringType, "CheckLease", 0);
        var restoreCanvas = GetMethod(method.DeclaringType, "RestoreCanvas", 0);
        var endShader = module.ImportReference(typeof(EffectorRuntime).GetMethod(nameof(EffectorRuntime.TryEndShaderEffectPatched))!);

        var il = method.Body.GetILProcessor();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, checkLease);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, endShader);
        var restoreBuiltIn = il.Create(OpCodes.Nop);
        il.Emit(OpCodes.Brfalse_S, restoreBuiltIn);
        il.Emit(OpCodes.Ret);

        il.Append(restoreBuiltIn);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, restoreCanvas);
        il.Emit(OpCodes.Ret);
    }

    private static void RewriteCanvasGetter(ModuleDefinition module, MethodDefinition method, FieldDefinition baseCanvasField)
    {
        method.Body.Instructions.Clear();
        method.Body.ExceptionHandlers.Clear();
        method.Body.Variables.Clear();
        method.Body.InitLocals = true;

        var canvasLocal = new VariableDefinition(module.ImportReference(typeof(SKCanvas)));
        method.Body.Variables.Add(canvasLocal);

        var il = method.Body.GetILProcessor();
        var fallback = il.Create(OpCodes.Nop);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloca_S, canvasLocal);
        il.Emit(OpCodes.Call, module.ImportReference(typeof(EffectorRuntime).GetMethod(nameof(EffectorRuntime.TryGetActiveShaderCanvas))!));
        il.Emit(OpCodes.Brfalse_S, fallback);
        il.Emit(OpCodes.Ldloc, canvasLocal);
        il.Emit(OpCodes.Ret);
        il.Append(fallback);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, baseCanvasField);
        il.Emit(OpCodes.Ret);
    }

    private static void RewriteSurfaceGetter(ModuleDefinition module, MethodDefinition method, FieldDefinition baseSurfaceField)
    {
        method.Body.Instructions.Clear();
        method.Body.ExceptionHandlers.Clear();
        method.Body.Variables.Clear();
        method.Body.InitLocals = true;

        var surfaceLocal = new VariableDefinition(module.ImportReference(typeof(SKSurface)));
        method.Body.Variables.Add(surfaceLocal);

        var il = method.Body.GetILProcessor();
        var fallback = il.Create(OpCodes.Nop);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloca_S, surfaceLocal);
        il.Emit(OpCodes.Call, module.ImportReference(typeof(EffectorRuntime).GetMethod(nameof(EffectorRuntime.TryGetActiveShaderSurface))!));
        il.Emit(OpCodes.Brfalse_S, fallback);
        il.Emit(OpCodes.Ldloc, surfaceLocal);
        il.Emit(OpCodes.Ret);
        il.Append(fallback);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, baseSurfaceField);
        il.Emit(OpCodes.Ret);
    }

    private static void RewriteTransformSetter(
        ModuleDefinition module,
        MethodDefinition method,
        FieldDefinition currentTransformField,
        FieldDefinition postTransformField)
    {
        method.Body.Instructions.Clear();
        method.Body.ExceptionHandlers.Clear();
        method.Body.Variables.Clear();
        method.Body.InitLocals = true;

        var checkLease = GetMethod(method.DeclaringType, "CheckLease", 0);
        var canvasGetter = GetMethod(method.DeclaringType, "get_Canvas", 0);
        var skiaSharpExtensions = GetType(module, "Avalonia.Skia.SkiaSharpExtensions");
        var toSkMatrix = GetMethod(skiaSharpExtensions, "ToSKMatrix", 1);
        var matrixMultiply = module.ImportReference(typeof(Matrix).GetMethod("op_Multiply", BindingFlags.Static | BindingFlags.Public)!);
        var nullableMatrixCtor = module.ImportReference(typeof(Nullable<Matrix>).GetConstructor(new[] { typeof(Matrix) })!);
        var nullableMatrixHasValue = module.ImportReference(typeof(Nullable<Matrix>).GetProperty(nameof(Nullable<Matrix>.HasValue))!.GetMethod!);
        var nullableMatrixValue = module.ImportReference(typeof(Nullable<Matrix>).GetProperty(nameof(Nullable<Matrix>.Value))!.GetMethod!);
        var setMatrix = module.ImportReference(typeof(SKCanvas).GetMethod(nameof(SKCanvas.SetMatrix), new[] { typeof(SKMatrix) })!);
        var adjustShaderTransform = module.ImportReference(typeof(EffectorRuntime).GetMethod(nameof(EffectorRuntime.AdjustTransformForActiveShaderFrame))!);
        var effectiveTransformLocal = new VariableDefinition(module.ImportReference(typeof(Matrix)));
        method.Body.Variables.Add(effectiveTransformLocal);

        var il = method.Body.GetILProcessor();
        var continueSet = il.Create(OpCodes.Nop);
        var skipPostTransform = il.Create(OpCodes.Nop);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, checkLease);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, currentTransformField);
        il.Emit(OpCodes.Box, module.ImportReference(currentTransformField.FieldType));
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Box, module.ImportReference(typeof(Matrix)));
        il.Emit(OpCodes.Call, module.ImportReference(typeof(object).GetMethod(nameof(object.Equals), new[] { typeof(object), typeof(object) })!));
        il.Emit(OpCodes.Brfalse_S, continueSet);
        il.Emit(OpCodes.Ret);

        il.Append(continueSet);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Newobj, nullableMatrixCtor);
        il.Emit(OpCodes.Stfld, currentTransformField);
        il.Emit(OpCodes.Ldarg_1);
        il.Emit(OpCodes.Stloc, effectiveTransformLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldflda, postTransformField);
        il.Emit(OpCodes.Call, nullableMatrixHasValue);
        il.Emit(OpCodes.Brfalse_S, skipPostTransform);
        il.Emit(OpCodes.Ldloc, effectiveTransformLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldflda, postTransformField);
        il.Emit(OpCodes.Call, nullableMatrixValue);
        il.Emit(OpCodes.Call, matrixMultiply);
        il.Emit(OpCodes.Stloc, effectiveTransformLocal);
        il.Append(skipPostTransform);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldloc, effectiveTransformLocal);
        il.Emit(OpCodes.Call, adjustShaderTransform);
        il.Emit(OpCodes.Stloc, effectiveTransformLocal);
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Call, canvasGetter);
        il.Emit(OpCodes.Ldloc, effectiveTransformLocal);
        il.Emit(OpCodes.Call, module.ImportReference(toSkMatrix));
        il.Emit(OpCodes.Callvirt, setMatrix);
        il.Emit(OpCodes.Ret);
    }

    private static TypeDefinition GetType(ModuleDefinition module, string fullName) =>
        module.Types.FirstOrDefault(type => type.FullName == fullName)
        ?? throw new InvalidOperationException($"Type '{fullName}' was not found in '{module.Assembly.Name.Name}'.");

    private static MethodDefinition GetMethod(TypeDefinition type, string name, int parameterCount) =>
        type.Methods.FirstOrDefault(method => method.Name == name && method.Parameters.Count == parameterCount)
        ?? throw new InvalidOperationException($"Method '{type.FullName}::{name}/{parameterCount}' was not found.");

    private static FieldDefinition GetField(TypeDefinition type, params string[] names) =>
        type.Fields.FirstOrDefault(field => names.Contains(field.Name, StringComparer.Ordinal))
        ?? throw new InvalidOperationException($"Field '{type.FullName}::{string.Join("' or '", names)}' was not found.");
}

internal sealed class AvaloniaAssemblyPatchResult
{
    public AvaloniaAssemblyPatchResult(string assemblyPath, AvaloniaPatchAssemblyKind kind)
    {
        AssemblyPath = assemblyPath;
        Kind = kind;
    }

    public string AssemblyPath { get; }

    public AvaloniaPatchAssemblyKind Kind { get; }

    public string AssemblyVersion { get; set; } = string.Empty;

    public bool Patched { get; set; }

    public bool AlreadyPatched { get; set; }

    public List<string> Warnings { get; } = new();

    public List<string> Errors { get; } = new();
}
