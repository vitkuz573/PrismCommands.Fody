using Fody;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;

public class ModuleWeaver : BaseModuleWeaver
{
    private const string DelegateCommandAttributeName = "DelegateCommandAttribute";
    private const string DelegateCommandTypeName = "Prism.Commands.DelegateCommand";
    private const string CommandBackingFieldNameFormat = "<{0}Command>k__BackingField";
    private const string GetCommandMethodNameFormat = "get_{0}Command";
    private const string CommandMethodNameFormat = "{0}Command";
    private const string SystemRuntimeAssemblyName = "System.Runtime";
    private const string PrismAssemblyName = "Prism";

    private TypeReference _delegateCommandType;

    public override IEnumerable<string> GetAssembliesForScanning()
    {
        yield return PrismAssemblyName;
    }

    public override void Execute()
    {
        _delegateCommandType = GetDelegateCommandType();

        foreach (var method in ModuleDefinition.Types.SelectMany(type => type.Methods.Where(m => m.CustomAttributes.Any(a => a.AttributeType.Name == DelegateCommandAttributeName)).ToList()))
        {
            RemoveDelegateCommandAttribute(method);

            var commandField = CreateBackingFieldForCommand(method);
            ImportAttributesForBackingField(commandField);
            AddBackingFieldToType(method.DeclaringType, commandField);

            var delegateCommandCtor = FindDelegateCommandConstructor();
            var commandProperty = CreateCommandProperty(method, commandField);

            UpdateConstructor(method.DeclaringType, method, commandField, delegateCommandCtor);

            method.DeclaringType.Properties.Add(commandProperty);
            method.DeclaringType.Methods.Add(commandProperty.GetMethod);
            MakeMethodPrivate(method);
        }
    }

    private void RemoveDelegateCommandAttribute(MethodDefinition method)
    {
        var attribute = method.CustomAttributes.FirstOrDefault(a => a.AttributeType.Name == DelegateCommandAttributeName) ?? throw new WeavingException($"Method '{method.Name}' does not have a '{DelegateCommandAttributeName}' attribute.");
        method.CustomAttributes.Remove(attribute);
    }

    private FieldDefinition CreateBackingFieldForCommand(MethodDefinition method)
    {
        var commandFieldName = string.Format(CommandBackingFieldNameFormat, method.Name);
        var commandFieldType = _delegateCommandType;

        return new FieldDefinition(commandFieldName, FieldAttributes.Private | FieldAttributes.InitOnly, commandFieldType);
    }

    private void ImportAttributesForBackingField(FieldDefinition commandField)
    {
        var compilerGeneratedAttributeType = ImportTypeFromAssembly(typeof(CompilerGeneratedAttribute), SystemRuntimeAssemblyName);
        var debuggerBrowsableAttributeType = ImportTypeFromAssembly(typeof(DebuggerBrowsableAttribute), SystemRuntimeAssemblyName);
        var debuggerBrowsableStateType = ImportTypeFromAssembly(typeof(DebuggerBrowsableState), SystemRuntimeAssemblyName);

        var compilerGeneratedAttributeCtor = ModuleDefinition.ImportReference(compilerGeneratedAttributeType.Resolve().GetConstructors().First());
        var debuggerBrowsableAttributeCtor = ModuleDefinition.ImportReference(debuggerBrowsableAttributeType.Resolve().GetConstructors().First());

        var debuggerBrowsableStateNever = new CustomAttributeArgument(debuggerBrowsableStateType, DebuggerBrowsableState.Never);
        var debuggerBrowsableAttribute = new CustomAttribute(debuggerBrowsableAttributeCtor);
        debuggerBrowsableAttribute.ConstructorArguments.Add(debuggerBrowsableStateNever);

        commandField.CustomAttributes.Add(new CustomAttribute(compilerGeneratedAttributeCtor));
        commandField.CustomAttributes.Add(debuggerBrowsableAttribute);
    }

    private void AddBackingFieldToType(TypeDefinition type, FieldDefinition commandField)
    {
        type.Fields.Add(commandField);
    }

    private MethodDefinition FindDelegateCommandConstructor()
    {
        return _delegateCommandType.Resolve().Methods.FirstOrDefault(m => m.IsConstructor && m.Parameters.Count == 1 && m.Parameters[0].ParameterType.FullName == typeof(Action).FullName) ?? throw new WeavingException("Unable to find DelegateCommand constructor with a single parameter of type Action.");
    }

    private PropertyDefinition CreateCommandProperty(MethodDefinition method, FieldDefinition commandField)
    {
        var commandProperty = new PropertyDefinition(string.Format(CommandMethodNameFormat, method.Name), PropertyAttributes.None, commandField.FieldType)
        {
            GetMethod = new MethodDefinition(string.Format(GetCommandMethodNameFormat, method.Name), MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig, commandField.FieldType)
        };

        var il = commandProperty.GetMethod.Body.GetILProcessor();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, commandField);
        il.Emit(OpCodes.Ret);

        var compilerGeneratedAttributeType = ImportTypeFromAssembly(typeof(CompilerGeneratedAttribute), SystemRuntimeAssemblyName);
        var compilerGeneratedAttributeCtor = ModuleDefinition.ImportReference(compilerGeneratedAttributeType.Resolve().GetConstructors().First());
        commandProperty.GetMethod.CustomAttributes.Add(new CustomAttribute(compilerGeneratedAttributeCtor));

        return commandProperty;
    }

    private void UpdateConstructor(TypeDefinition type, MethodDefinition method, FieldDefinition commandField, MethodDefinition delegateCommandCtor)
    {
        var ctor = type.GetConstructors().FirstOrDefault() ?? throw new WeavingException("Unable to find default constructor in the type.");

        var actionType = ImportTypeFromAssembly(typeof(Action), SystemRuntimeAssemblyName);
        var actionConstructorInfo = actionType.Resolve().GetConstructors().FirstOrDefault(c => c.Parameters.Count == 2 && c.Parameters[0].ParameterType.MetadataType == MetadataType.Object && c.Parameters[1].ParameterType.MetadataType == MetadataType.IntPtr);
        var actionConstructor = ModuleDefinition.ImportReference(actionConstructorInfo);

        var ilCtor = ctor.Body.GetILProcessor();
        var lastRetInstruction = ctor.Body.Instructions.LastOrDefault(i => i.OpCode == OpCodes.Ret) ?? throw new WeavingException("Constructor does not have a return instruction (ret).");

        ilCtor.InsertBefore(lastRetInstruction, ilCtor.Create(OpCodes.Nop));
        ilCtor.InsertBefore(lastRetInstruction, ilCtor.Create(OpCodes.Ldarg_0));
        ilCtor.InsertBefore(lastRetInstruction, ilCtor.Create(OpCodes.Ldarg_0));
        ilCtor.InsertBefore(lastRetInstruction, ilCtor.Create(OpCodes.Ldftn, method));
        ilCtor.InsertBefore(lastRetInstruction, ilCtor.Create(OpCodes.Newobj, actionConstructor));
        ilCtor.InsertBefore(lastRetInstruction, ilCtor.Create(OpCodes.Newobj, ModuleDefinition.ImportReference(delegateCommandCtor)));
        ilCtor.InsertBefore(lastRetInstruction, ilCtor.Create(OpCodes.Stfld, commandField));
    }

    private void MakeMethodPrivate(MethodDefinition method)
    {
        method.IsPrivate = true;
    }

    private TypeReference ImportTypeFromAssembly(Type type, string assemblyName)
    {
        var assembly = ModuleDefinition.AssemblyResolver.Resolve(new AssemblyNameReference(assemblyName, null)) ?? throw new WeavingException($"Unable to find assembly '{assemblyName}'.");
        var module = assembly.MainModule;
        var typeDefinition = module.GetType(type.FullName);

        return typeDefinition == null
            ? throw new WeavingException($"Unable to find type '{type.FullName}' in assembly '{assemblyName}'.")
            : ModuleDefinition.ImportReference(typeDefinition);
    }

    private TypeReference GetDelegateCommandType()
    {
        var assembly = ModuleDefinition.AssemblyResolver.Resolve(new AssemblyNameReference(PrismAssemblyName, null)) ?? throw new WeavingException($"Unable to find assembly '{PrismAssemblyName}'.");
        var module = assembly.MainModule;
        var type = module.GetType(DelegateCommandTypeName);

        return type == null
            ? throw new WeavingException($"Unable to find type '{DelegateCommandTypeName}' in assembly '{PrismAssemblyName}'.")
            : ModuleDefinition.ImportReference(type);
    }
}
