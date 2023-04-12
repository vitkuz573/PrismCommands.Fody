using Fody;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using PrismCommands.Fody.Extensions;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

public class ModuleWeaver : BaseModuleWeaver
{
    private const string DelegateCommandAttributeName = "DelegateCommandAttribute";
    private const string CommandBackingFieldNameFormat = "<{0}>k__BackingField";
    private const string GetCommandMethodNameFormat = "get_{0}";
    private const string CommandMethodNameFormat = "{0}Command";

    private string _canExecuteMethodPattern;
    private TypeReference _delegateCommandType;

    public override IEnumerable<string> GetAssembliesForScanning()
    {
        yield return "Prism";
    }

    public override void Execute()
    {
        _canExecuteMethodPattern = Config.Attribute("CanExecuteMethodPattern")?.Value ?? "Can{0}";

        if (!_canExecuteMethodPattern.Contains("{0}") || _canExecuteMethodPattern == "{0}" || Regex.IsMatch(_canExecuteMethodPattern, @"\{[1-9]\d*\}"))
        {
            throw new WeavingException("The CanExecuteMethodPattern parameter is incorrectly formatted. It must contain the '{0}' placeholder, must not be equal to '{0}', and should not contain any other placeholders like '{1}', '{2}', etc. Please review your configuration and ensure the correct pattern is used.");
        }

        _delegateCommandType = ModuleDefinition.ImportReference("Prism.Commands.DelegateCommand", "Prism");

        foreach (var method in ModuleDefinition.Types.SelectMany(type => type.Methods.Where(m => m.CustomAttributes.Any(a => a.AttributeType.Name == DelegateCommandAttributeName)).ToList()))
        {
            RemoveDelegateCommandAttribute(method);

            var commandField = CreateBackingFieldForCommand(method);
            AddAttributesToBackingField(commandField);
            AddBackingFieldToType(method.DeclaringType, commandField);

            var canExecuteMethod = FindCanExecuteMethod(method);

            MethodDefinition delegateCommandCtor;

            if (canExecuteMethod != null)
            {
                delegateCommandCtor = FindDelegateCommandConstructor(true);
            }
            else
            {
                delegateCommandCtor = FindDelegateCommandConstructor(false);
            }

            var commandProperty = CreateCommandProperty(method, commandField);

            UpdateConstructor(method.DeclaringType, method, commandField, delegateCommandCtor, canExecuteMethod);

            method.DeclaringType.Properties.Add(commandProperty);
            method.DeclaringType.Methods.Add(commandProperty.GetMethod);
            MakeMethodPrivate(method);
        }
    }

    private MethodDefinition FindCanExecuteMethod(MethodDefinition method)
    {
        var canExecuteMethodName = string.Format(_canExecuteMethodPattern, method.Name);

        return method.DeclaringType.Methods.FirstOrDefault(m => m.Name == canExecuteMethodName && m.ReturnType.MetadataType == MetadataType.Boolean && !m.HasParameters);
    }

    private void RemoveDelegateCommandAttribute(MethodDefinition method)
    {
        var attribute = method.CustomAttributes.FirstOrDefault(a => a.AttributeType.Name == DelegateCommandAttributeName) ?? throw new WeavingException($"The method '{method.FullName}' is missing the required '{DelegateCommandAttributeName}' attribute. Please ensure that the attribute is applied to the method.");
        method.CustomAttributes.Remove(attribute);
    }

    private FieldDefinition CreateBackingFieldForCommand(MethodDefinition method)
    {
        var commandMethodName = string.Format(CommandMethodNameFormat, method.Name);
        var commandFieldName = string.Format(CommandBackingFieldNameFormat, commandMethodName);
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

    private MethodDefinition FindDelegateCommandConstructor(bool hasCanExecuteMethod)
    {
        var delegateCommandConstructors = _delegateCommandType.Resolve().GetConstructors();

        MethodDefinition delegateCommandCtor;

        if (hasCanExecuteMethod)
        {
            delegateCommandCtor = delegateCommandConstructors.FirstOrDefault(m =>
                m.Parameters.Count == 2 &&
                m.Parameters[0].ParameterType.FullName == typeof(Action).FullName &&
                m.Parameters[1].ParameterType.IsGenericInstance &&
                m.Parameters[1].ParameterType.GetElementType().FullName == typeof(Func<>).FullName &&
                ((GenericInstanceType)m.Parameters[1].ParameterType).GenericArguments[0].MetadataType == MetadataType.Boolean
            );
        }
        else
        {
            delegateCommandCtor = delegateCommandConstructors.FirstOrDefault(m => m.Parameters.Count == 1 &&
                m.Parameters[0].ParameterType.FullName == typeof(Action).FullName);
        }

        if (delegateCommandCtor == null)
        {
            throw new WeavingException($"Unable to find DelegateCommand constructor {(hasCanExecuteMethod ? "with two parameters of types Action and Func`1<Boolean>" : "with a single parameter of type Action")}. Available constructors: {string.Join(", ", delegateCommandConstructors.Select(c => c.ToString()))}");
        }

        return delegateCommandCtor;
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

    private void UpdateConstructor(TypeDefinition type, MethodDefinition method, FieldDefinition commandField, MethodDefinition delegateCommandCtor, MethodDefinition canExecuteMethod = null)
    {
        var ctor = type.GetConstructors().FirstOrDefault() ?? throw new WeavingException($"Failed to find or generate a default constructor for the type '{type.FullName}'. This is an unexpected error. Please ensure the proper project setup and verify the generated code.");
        var lastRetInstruction = ctor.Body.Instructions.LastOrDefault(i => i.OpCode == OpCodes.Ret) ?? throw new WeavingException($"The constructor '{ctor.FullName}' is missing a return instruction (ret). Please verify the constructor implementation to ensure proper weaving.");
        var ilCtor = ctor.Body.GetILProcessor();

        InsertActionInstructions(ilCtor, lastRetInstruction, method, GetActionConstructor());

        if (canExecuteMethod != null)
        {
            InsertFuncInstructions(ilCtor, lastRetInstruction, canExecuteMethod, GetFuncConstructor());
        }

        InsertDelegateCommandInstructions(ilCtor, lastRetInstruction, commandField, delegateCommandCtor);
    }

    private void InsertActionInstructions(ILProcessor ilCtor, Instruction lastRetInstruction, MethodDefinition method, MethodReference actionConstructor)
    {
        ilCtor.InsertBefore(lastRetInstruction, Instruction.Create(OpCodes.Nop));
        ilCtor.InsertBefore(lastRetInstruction, Instruction.Create(OpCodes.Ldarg_0));
        ilCtor.InsertBefore(lastRetInstruction, Instruction.Create(OpCodes.Ldarg_0));
        ilCtor.InsertBefore(lastRetInstruction, Instruction.Create(OpCodes.Ldftn, method));
        ilCtor.InsertBefore(lastRetInstruction, Instruction.Create(OpCodes.Newobj, actionConstructor));
    }

    private void InsertFuncInstructions(ILProcessor ilCtor, Instruction lastRetInstruction, MethodDefinition canExecuteMethod, MethodReference funcConstructor)
    {
        ilCtor.InsertBefore(lastRetInstruction, Instruction.Create(OpCodes.Ldarg_0));
        ilCtor.InsertBefore(lastRetInstruction, Instruction.Create(OpCodes.Ldftn, canExecuteMethod));
        ilCtor.InsertBefore(lastRetInstruction, Instruction.Create(OpCodes.Newobj, funcConstructor));
    }

    private void InsertDelegateCommandInstructions(ILProcessor ilCtor, Instruction lastRetInstruction, FieldDefinition commandField, MethodDefinition delegateCommandCtor)
    {
        ilCtor.InsertBefore(lastRetInstruction, Instruction.Create(OpCodes.Newobj, ModuleDefinition.ImportReference(delegateCommandCtor)));
        ilCtor.InsertBefore(lastRetInstruction, Instruction.Create(OpCodes.Stfld, commandField));
    }

    private MethodReference GetActionConstructor()
    {
        var actionType = ModuleDefinition.ImportReference(typeof(Action).FullName, "System.Runtime");
        var actionConstructorInfo = actionType.Resolve().GetConstructors().FirstOrDefault(c => c.Parameters.Count == 2 && c.Parameters[0].ParameterType.MetadataType == MetadataType.Object && c.Parameters[1].ParameterType.MetadataType == MetadataType.IntPtr) ?? throw new WeavingException($"The required Action constructor with two parameters was not found in the type '{actionType.FullName}'. Ensure that the proper version of the System.Runtime assembly is referenced in your project.");

        return ModuleDefinition.ImportReference(actionConstructorInfo);
    }

    private MethodReference GetFuncConstructor()
    {
        var openFuncType = ModuleDefinition.ImportReference(typeof(Func<>).FullName, "System.Runtime");
        var boolType = ModuleDefinition.TypeSystem.Boolean;
        var closedFuncType = openFuncType.MakeGenericInstanceType(boolType);
        var openFuncConstructorInfo = openFuncType.Resolve().GetConstructors().FirstOrDefault(c => c.Parameters.Count == 2 && c.Parameters[0].ParameterType.MetadataType == MetadataType.Object && c.Parameters[1].ParameterType.MetadataType == MetadataType.IntPtr) ?? throw new WeavingException($"Unable to find Func<> constructor with two parameters in the type '{openFuncType.FullName}'.");

        var closedFuncConstructorInfo = new MethodReference(".ctor", openFuncConstructorInfo.ReturnType, closedFuncType)
        {
            CallingConvention = openFuncConstructorInfo.CallingConvention,
            HasThis = openFuncConstructorInfo.HasThis,
            ExplicitThis = openFuncConstructorInfo.ExplicitThis
        };

        foreach (var parameter in openFuncConstructorInfo.Parameters)
        {
            closedFuncConstructorInfo.Parameters.Add(new ParameterDefinition(parameter.ParameterType));
        }

        return ModuleDefinition.ImportReference(closedFuncConstructorInfo);
    }

    private void MakeMethodPrivate(MethodDefinition method)
    {
        method.IsPrivate = true;
    }

    public override bool ShouldCleanReference => true;
}
