﻿using Fody;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using PrismCommands.Fody.Extensions;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;

public class ModuleWeaver : BaseModuleWeaver
{
    private const string DelegateCommandAttributeName = "DelegateCommandAttribute";
    private const string CommandBackingFieldNameFormat = "<{0}Command>k__BackingField";
    private const string GetCommandMethodNameFormat = "get_{0}";
    private const string CommandMethodNameFormat = "{0}Command";

    private TypeReference _delegateCommandType;

    public override IEnumerable<string> GetAssembliesForScanning()
    {
        yield return "Prism";
    }

    public override void Execute()
    {
        _delegateCommandType = ModuleDefinition.ImportReference("Prism.Commands.DelegateCommand", "Prism");

        foreach (var method in ModuleDefinition.Types.SelectMany(type => type.Methods.Where(m => m.CustomAttributes.Any(a => a.AttributeType.Name == DelegateCommandAttributeName)).ToList()))
        {
            RemoveDelegateCommandAttribute(method);

            var commandField = CreateBackingFieldForCommand(method);
            AddAttributesToBackingField(commandField);
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
        var attribute = method.CustomAttributes.FirstOrDefault(a => a.AttributeType.Name == DelegateCommandAttributeName) ?? throw new WeavingException($"Method '{method.FullName}' does not have a '{DelegateCommandAttributeName}' attribute.");
        method.CustomAttributes.Remove(attribute);
    }

    private FieldDefinition CreateBackingFieldForCommand(MethodDefinition method)
    {
        var commandFieldName = string.Format(CommandBackingFieldNameFormat, method.Name);
        var commandFieldType = _delegateCommandType;

        return new FieldDefinition(commandFieldName, FieldAttributes.Private | FieldAttributes.InitOnly, commandFieldType);
    }

    private void AddAttributesToBackingField(FieldDefinition commandField)
    {
        commandField.AddAttribute<CompilerGeneratedAttribute>(ModuleDefinition, "System.Runtime");
        commandField.AddAttribute<DebuggerBrowsableAttribute>(ModuleDefinition, "System.Runtime", DebuggerBrowsableState.Never);
    }

    private void AddBackingFieldToType(TypeDefinition type, FieldDefinition commandField)
    {
        type.Fields.Add(commandField);
    }

    private MethodDefinition FindDelegateCommandConstructor()
    {
        var delegateCommandConstructors = _delegateCommandType.Resolve().GetConstructors();

        return delegateCommandConstructors.FirstOrDefault(m => m.Parameters.Count == 1 && m.Parameters[0].ParameterType.FullName == typeof(Action).FullName) ?? throw new WeavingException($"Unable to find DelegateCommand constructor with a single parameter of type Action. Available constructors: {string.Join(", ", delegateCommandConstructors.Select(c => c.ToString()))}");
    }

    private PropertyDefinition CreateCommandProperty(MethodDefinition method, FieldDefinition commandField)
    {
        var commandMethodName = string.Format(CommandMethodNameFormat, method.Name);
        var getCommandMethodName = string.Format(GetCommandMethodNameFormat, commandMethodName);

        var commandProperty = new PropertyDefinition(commandMethodName, PropertyAttributes.None, commandField.FieldType)
        {
            GetMethod = new MethodDefinition(getCommandMethodName, MethodAttributes.Public | MethodAttributes.SpecialName | MethodAttributes.HideBySig, commandField.FieldType)
            {
                Body =
                {
                    Instructions =
                    {
                        Instruction.Create(OpCodes.Ldarg_0),
                        Instruction.Create(OpCodes.Ldfld, commandField),
                        Instruction.Create(OpCodes.Ret)
                    }
                }
            }
        };

        commandProperty.GetMethod.AddAttribute<CompilerGeneratedAttribute>(ModuleDefinition, "System.Runtime");

        return commandProperty;
    }

    private void UpdateConstructor(TypeDefinition type, MethodDefinition method, FieldDefinition commandField, MethodDefinition delegateCommandCtor)
    {
        var ctor = type.GetConstructors().FirstOrDefault() ?? throw new WeavingException($"Unable to find default constructor in the type '{type.FullName}'.");

        var actionType = ModuleDefinition.ImportReference(typeof(Action).FullName, "System.Runtime");
        var actionConstructorInfo = actionType.Resolve().GetConstructors().FirstOrDefault(c => c.Parameters.Count == 2 && c.Parameters[0].ParameterType.MetadataType == MetadataType.Object && c.Parameters[1].ParameterType.MetadataType == MetadataType.IntPtr) ?? throw new WeavingException($"Unable to find Action constructor with two parameters in the type '{actionType.FullName}'.");
        var actionConstructor = ModuleDefinition.ImportReference(actionConstructorInfo);

        var ilCtor = ctor.Body.GetILProcessor();
        var lastRetInstruction = ctor.Body.Instructions.LastOrDefault(i => i.OpCode == OpCodes.Ret) ?? throw new WeavingException($"Constructor '{ctor.FullName}' does not have a return instruction (ret).");

        var instructions = new[]
        {
            Instruction.Create(OpCodes.Nop),
            Instruction.Create(OpCodes.Ldarg_0),
            Instruction.Create(OpCodes.Ldarg_0),
            Instruction.Create(OpCodes.Ldftn, method),
            Instruction.Create(OpCodes.Newobj, actionConstructor),
            Instruction.Create(OpCodes.Newobj, ModuleDefinition.ImportReference(delegateCommandCtor)),
            Instruction.Create(OpCodes.Stfld, commandField)
        };

        foreach (var instruction in instructions)
        {
            ilCtor.InsertBefore(lastRetInstruction, instruction);
        }
    }

    private void MakeMethodPrivate(MethodDefinition method)
    {
        method.IsPrivate = true;
    }
}
