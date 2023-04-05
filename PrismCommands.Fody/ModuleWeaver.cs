using Fody;
using Mono.Cecil;
using Mono.Cecil.Cil;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using Mono.Cecil.Rocks;

public class ModuleWeaver : BaseModuleWeaver
{
    private TypeReference _delegateCommandType;

    public override IEnumerable<string> GetAssembliesForScanning()
    {
        yield return "Prism";
    }

    public override void Execute()
    {
        _delegateCommandType = GetDelegateCommandType();

        foreach (var method in ModuleDefinition.Types.SelectMany(type => type.Methods.Where(m => m.CustomAttributes.Any(a => a.AttributeType.Name == "DelegateCommandAttribute")).ToList()))
        {
            ReplaceMethodWithCommandProperty(method);
        }
    }

    private void ReplaceMethodWithCommandProperty(MethodDefinition method)
    {
        const string attributeName = "DelegateCommandAttribute";
        var attribute = method.CustomAttributes.FirstOrDefault(a => a.AttributeType.Name == attributeName) ?? throw new WeavingException($"Method '{method.Name}' does not have a '{attributeName}' attribute.");
        method.CustomAttributes.Remove(attribute);

        var commandFieldName = $"<{method.Name}Command>k__BackingField";
        var commandFieldType = _delegateCommandType;
        
        var compilerGeneratedAttributeType = ImportTypeFromAssembly(typeof(CompilerGeneratedAttribute), "System.Runtime");
        var debuggerBrowsableAttributeType = ImportTypeFromAssembly(typeof(DebuggerBrowsableAttribute), "System.Runtime");
        var debuggerBrowsableStateType = ImportTypeFromAssembly(typeof(DebuggerBrowsableState), "System.Runtime");
        
        var compilerGeneratedAttributeCtor = ModuleDefinition.ImportReference(compilerGeneratedAttributeType.Resolve().GetConstructors().First());
        var debuggerBrowsableAttributeCtor = ModuleDefinition.ImportReference(debuggerBrowsableAttributeType.Resolve().GetConstructors().First());

        var debuggerBrowsableStateNever = new CustomAttributeArgument(debuggerBrowsableStateType, DebuggerBrowsableState.Never);
        var debuggerBrowsableAttribute = new CustomAttribute(debuggerBrowsableAttributeCtor);
        debuggerBrowsableAttribute.ConstructorArguments.Add(debuggerBrowsableStateNever);

        var commandField = new FieldDefinition(commandFieldName, FieldAttributes.Private | FieldAttributes.InitOnly, commandFieldType);
        commandField.CustomAttributes.Add(new CustomAttribute(compilerGeneratedAttributeCtor));
        commandField.CustomAttributes.Add(debuggerBrowsableAttribute);
        method.DeclaringType.Fields.Add(commandField);

        var delegateCommandCtor = _delegateCommandType.Resolve().Methods.FirstOrDefault(m => m.IsConstructor && m.Parameters.Count == 1 && m.Parameters[0].ParameterType.FullName == typeof(Action).FullName) ?? throw new WeavingException("Unable to find DelegateCommand constructor with a single parameter of type Action.");
        var commandProperty = new PropertyDefinition(method.Name + "Command", PropertyAttributes.None, commandFieldType)
        {
            GetMethod = new MethodDefinition($"get_{method.Name}Command", MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig, commandFieldType)
        };

        var ctor = method.DeclaringType.GetConstructors().FirstOrDefault() ?? throw new WeavingException("Unable to find default constructor in the type.");

        var actionType = ImportTypeFromAssembly(typeof(Action), "System.Runtime");
        var actionConstructorInfo = actionType.Resolve().GetConstructors().FirstOrDefault(c => c.Parameters.Count == 2 && c.Parameters[0].ParameterType.MetadataType == MetadataType.Object && c.Parameters[1].ParameterType.MetadataType == MetadataType.IntPtr);
        var actionConstructor = ModuleDefinition.ImportReference(actionConstructorInfo);

        var ilCtor = ctor.Body.GetILProcessor();
        var lastRetInstruction = ctor.Body.Instructions.LastOrDefault(i => i.OpCode == OpCodes.Ret);

        if (lastRetInstruction == null)
        {
            throw new WeavingException("Constructor does not have a return instruction (ret).");
        }
        
        ilCtor.InsertBefore(lastRetInstruction, ilCtor.Create(OpCodes.Nop));
        ilCtor.InsertBefore(lastRetInstruction, ilCtor.Create(OpCodes.Ldarg_0));
        ilCtor.InsertBefore(lastRetInstruction, ilCtor.Create(OpCodes.Ldarg_0));
        ilCtor.InsertBefore(lastRetInstruction, ilCtor.Create(OpCodes.Ldftn, method));
        ilCtor.InsertBefore(lastRetInstruction, ilCtor.Create(OpCodes.Newobj, actionConstructor));
        ilCtor.InsertBefore(lastRetInstruction, ilCtor.Create(OpCodes.Newobj, ModuleDefinition.ImportReference(delegateCommandCtor)));
        ilCtor.InsertBefore(lastRetInstruction, ilCtor.Create(OpCodes.Stfld, commandField));

        var il = commandProperty.GetMethod.Body.GetILProcessor();
        il.Emit(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldfld, commandField);
        il.Emit(OpCodes.Ret);

        commandProperty.GetMethod.CustomAttributes.Add(new CustomAttribute(compilerGeneratedAttributeCtor));

        method.DeclaringType.Properties.Add(commandProperty);
        method.DeclaringType.Methods.Add(commandProperty.GetMethod);
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
        const string assemblyName = "Prism";

        var assembly = ModuleDefinition.AssemblyResolver.Resolve(new AssemblyNameReference(assemblyName, null)) ?? throw new WeavingException($"Unable to find assembly '{assemblyName}'.");
        var module = assembly.MainModule;
        var type = module.GetType("Prism.Commands.DelegateCommand");

        return type == null
            ? throw new WeavingException($"Unable to find type 'Prism.Commands.DelegateCommand' in assembly '{assemblyName}'.")
            : ModuleDefinition.ImportReference(type);
    }
}
