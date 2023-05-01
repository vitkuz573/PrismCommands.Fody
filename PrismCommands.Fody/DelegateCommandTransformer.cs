using Fody;
using Mono.Cecil;
using Mono.Cecil.Cil;
using PrismCommands.Fody.Extensions;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Xml.Linq;

namespace PrismCommands.Fody;

public class DelegateCommandTransformer
{
    private const string CommandBackingFieldNameFormat = "<{0}>k__BackingField";
    private const string GetCommandMethodNameFormat = "get_{0}";
    private const string CommandMethodNameFormat = "{0}Command";

    public string AttributeName { get; } = "DelegateCommandAttribute";

    private readonly WeaverConfig _config;
    private readonly ConstructorCache _constructorCache;
    private readonly ModuleDefinition _moduleDefinition;

    public DelegateCommandTransformer(ModuleDefinition moduleDefinition, XElement config)
    {
        _moduleDefinition = moduleDefinition;

        _config = new WeaverConfig(moduleDefinition, config);
        _constructorCache = new ConstructorCache(moduleDefinition);
    }

    public void Transform(MethodDefinition method)
    {
        RemoveDelegateCommandAttribute(method);

        var commandField = CreateBackingFieldForCommand(method);
        AddAttributesToBackingField(commandField);
        AddBackingFieldToType(method.DeclaringType, commandField);

        var canExecuteMethod = GetCanExecuteMethodLazy(method);
        var delegateCommandCtor = _constructorCache.GetDelegateCommandConstructor(_config, canExecuteMethod?.Value != null);
        var commandProperty = CreateCommandProperty(method, commandField);

        UpdateConstructor(method.DeclaringType, method, commandField, delegateCommandCtor, canExecuteMethod);

        method.DeclaringType.Properties.Add(commandProperty);
        method.DeclaringType.Methods.Add(commandProperty.GetMethod);
        MakeMethodPrivate(method);
    }

    private void RemoveDelegateCommandAttribute(MethodDefinition method)
    {
        var attribute = method.CustomAttributes.FirstOrDefault(a => a.AttributeType.Name == AttributeName) ?? throw new WeavingException($"The method '{method.FullName}' is missing the required '{AttributeName}' attribute. Please ensure that the attribute is applied to the method.");
        method.CustomAttributes.Remove(attribute);
    }

    private FieldDefinition CreateBackingFieldForCommand(MethodDefinition method)
    {
        var commandMethodName = string.Format(CommandMethodNameFormat, method.Name);
        var commandFieldName = string.Format(CommandBackingFieldNameFormat, commandMethodName);
        var commandFieldType = _config.DelegateCommandType;

        return new FieldDefinition(commandFieldName, FieldAttributes.Private | FieldAttributes.InitOnly, commandFieldType);
    }

    private void AddAttributesToBackingField(FieldDefinition commandField)
    {
        commandField.AddAttribute<CompilerGeneratedAttribute>(_moduleDefinition, "System.Runtime");
        commandField.AddAttribute<DebuggerBrowsableAttribute>(_moduleDefinition, "System.Runtime", DebuggerBrowsableState.Never);
    }

    private void AddBackingFieldToType(TypeDefinition type, FieldDefinition commandField)
    {
        type.Fields.Add(commandField);
    }

    private MethodDefinition FindCanExecuteMethod(MethodDefinition method)
    {
        var canExecuteMethodName = string.Format(_config.CanExecuteMethodPattern, method.Name);

        return method.DeclaringType.Methods.FirstOrDefault(m => m.Name == canExecuteMethodName && m.ReturnType.MetadataType == MetadataType.Boolean && !m.HasParameters);
    }

    private Lazy<MethodDefinition> GetCanExecuteMethodLazy(MethodDefinition method)
    {
        return new Lazy<MethodDefinition>(() => FindCanExecuteMethod(method));
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

        commandProperty.GetMethod.AddAttribute<CompilerGeneratedAttribute>(_moduleDefinition, "System.Runtime");

        return commandProperty;
    }

    private void UpdateConstructor(TypeDefinition type, MethodDefinition method, FieldDefinition commandField, MethodDefinition delegateCommandCtor, Lazy<MethodDefinition> canExecuteMethodLazy = null)
    {
        var actionInstructions = new List<Instruction>
        {
            Instruction.Create(OpCodes.Nop),
            Instruction.Create(OpCodes.Ldarg_0),
            Instruction.Create(OpCodes.Ldarg_0),
            Instruction.Create(OpCodes.Ldftn, method),
            Instruction.Create(OpCodes.Newobj, _constructorCache.GetActionConstructor())
        };

        type.InsertInstructionsBeforeLastRet(actionInstructions);

        if (canExecuteMethodLazy?.IsValueCreated ?? false)
        {
            var canExecuteMethod = canExecuteMethodLazy.Value;

            if (canExecuteMethod != null)
            {
                var funcInstructions = new List<Instruction>
                {
                    Instruction.Create(OpCodes.Ldarg_0),
                    Instruction.Create(OpCodes.Ldftn, canExecuteMethod),
                    Instruction.Create(OpCodes.Newobj, _constructorCache.GetFuncConstructor())
                };

                type.InsertInstructionsBeforeLastRet(funcInstructions);
            }
        }

        var delegateCommandInstructions = new List<Instruction>
        {
            Instruction.Create(OpCodes.Newobj, _moduleDefinition.ImportReference(delegateCommandCtor)),
            Instruction.Create(OpCodes.Stfld, commandField)
        };

        type.InsertInstructionsBeforeLastRet(delegateCommandInstructions);
    }

    private void MakeMethodPrivate(MethodDefinition method)
    {
        method.IsPrivate = true;
    }
}
