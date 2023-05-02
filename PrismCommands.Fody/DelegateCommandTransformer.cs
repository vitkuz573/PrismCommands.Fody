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

/// <summary>
/// Transforms the methods marked with the DelegateCommandAttribute into DelegateCommands.
/// </summary>
public class DelegateCommandTransformer
{
    private const string CommandBackingFieldNameFormat = "<{0}>k__BackingField";
    private const string GetCommandMethodNameFormat = "get_{0}";
    private const string CommandMethodNameFormat = "{0}Command";

    private readonly WeaverConfig _config;
    private readonly ConstructorCache _constructorCache;
    private readonly ModuleDefinition _moduleDefinition;

    /// <summary>
    /// Gets the name of the DelegateCommandAttribute.
    /// </summary>
    public string AttributeName { get; } = "DelegateCommandAttribute";

    /// <summary>
    /// Initializes a new instance of the <see cref="DelegateCommandTransformer"/> class.
    /// </summary>
    /// <param name="moduleDefinition">The module definition to be used in the configuration.</param>
    /// <param name="config">The XML configuration element.</param>
    public DelegateCommandTransformer(ModuleDefinition moduleDefinition, XElement config)
    {
        _moduleDefinition = moduleDefinition;

        _config = new WeaverConfig(moduleDefinition, config);
        _constructorCache = new ConstructorCache(moduleDefinition);
    }

    /// <summary>
    /// Transforms the specified method into a DelegateCommand.
    /// </summary>
    /// <param name="method">The method to transform.</param>
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

    /// <summary>
    /// Removes the DelegateCommandAttribute from the specified method.
    /// </summary>
    /// <param name="method">The method to remove the attribute from.</param>
    private void RemoveDelegateCommandAttribute(MethodDefinition method)
    {
        var attribute = method.CustomAttributes.FirstOrDefault(a => a.AttributeType.Name == AttributeName) ?? throw new WeavingException($"The method '{method.FullName}' is missing the required '{AttributeName}' attribute. Please ensure that the attribute is applied to the method.");
        method.CustomAttributes.Remove(attribute);
    }

    /// <summary>
    /// Creates a backing field for the DelegateCommand.
    /// </summary>
    /// <param name="method">The method associated with the command.</param>
    /// <returns>A FieldDefinition for the backing field.</returns>
    private FieldDefinition CreateBackingFieldForCommand(MethodDefinition method)
    {
        var commandMethodName = string.Format(CommandMethodNameFormat, method.Name);
        var commandFieldName = string.Format(CommandBackingFieldNameFormat, commandMethodName);
        var commandFieldType = _config.DelegateCommandType;

        return new FieldDefinition(commandFieldName, FieldAttributes.Private | FieldAttributes.InitOnly, commandFieldType);
    }

    /// <summary>
    /// Adds necessary attributes to the backing field.
    /// </summary>
    /// <param name="commandField">The backing field to add attributes to.</param>
    private void AddAttributesToBackingField(FieldDefinition commandField)
    {
        commandField.AddAttribute<CompilerGeneratedAttribute>(_moduleDefinition, "System.Runtime");
        commandField.AddAttribute<DebuggerBrowsableAttribute>(_moduleDefinition, "System.Runtime", DebuggerBrowsableState.Never);
    }

    /// <summary>
    /// Adds the backing field to the type.
    /// </summary>
    /// <param name="type">The type to add the backing field to.</param>
    /// <param name="commandField">The backing field to add.</param>
    private void AddBackingFieldToType(TypeDefinition type, FieldDefinition commandField)
    {
        type.Fields.Add(commandField);
    }

    /// <summary>
    /// Finds the CanExecute method associated with the specified method.
    /// </summary>
    /// <param name="method">The method to find the CanExecute method for.</param>
    /// <returns>The CanExecute method, or null if not found.</returns>
    private MethodDefinition FindCanExecuteMethod(MethodDefinition method)
    {
        var canExecuteMethodName = string.Format(_config.CanExecuteMethodPattern, method.Name);

        return method.DeclaringType.Methods.FirstOrDefault(m => m.Name == canExecuteMethodName && m.ReturnType.MetadataType == MetadataType.Boolean && !m.HasParameters);
    }

    /// <summary>
    /// Gets a Lazy<MethodDefinition> for the CanExecute method.
    /// </summary>
    /// <param name="method">The method to get the CanExecute method for.</param>
    /// <returns>A Lazy<MethodDefinition> for the CanExecute method.</returns>
    private Lazy<MethodDefinition> GetCanExecuteMethodLazy(MethodDefinition method)
    {
        return new Lazy<MethodDefinition>(() => FindCanExecuteMethod(method));
    }

    /// <summary>
    /// Creates a command property for the specified method and backing field.
    /// </summary>
    /// <param name="method">The method associated with the command.</param>
    /// <param name="commandField">The backing field for the command property.</param>
    /// <returns>A PropertyDefinition for the command property.</returns>
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

    /// <summary>
    /// Updates the constructor of the type to initialize the DelegateCommand.
    /// </summary>
    /// <param name="type">The type to update the constructor of.</param>
    /// <param name="method">The method associated with the command.</param>
    /// <param name="commandField">The backing field for the command property.</param>
    /// <param name="delegateCommandCtor">The constructor for the DelegateCommand.</param>
    /// <param name="canExecuteMethodLazy">A Lazy<MethodDefinition> for the CanExecute method.</param>
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

    /// <summary>
    /// Makes the specified method private.
    /// </summary>
    /// <param name="method">The method to make private.</param>
    private void MakeMethodPrivate(MethodDefinition method)
    {
        method.IsPrivate = true;
    }
}
