using Fody;
using Mono.Cecil;
using Mono.Cecil.Rocks;
using PrismCommands.Fody;
using PrismCommands.Fody.Extensions;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Represents a cache for constructors used during the weaving process.
/// </summary>
public class ConstructorCache
{
    private readonly Dictionary<bool, MethodDefinition> _delegateCommandConstructors;
    private readonly ModuleDefinition _moduleDefinition;
    private MethodReference _actionConstructor;
    private MethodReference _funcConstructor;

    /// <summary>
    /// Initializes a new instance of the <see cref="ConstructorCache"/> class.
    /// </summary>
    /// <param name="moduleDefinition">The module definition to be used in the constructor cache.</param>
    public ConstructorCache(ModuleDefinition moduleDefinition)
    {
        _delegateCommandConstructors = new Dictionary<bool, MethodDefinition>();
        _moduleDefinition = moduleDefinition;
    }

    /// <summary>
    /// Gets the Action constructor reference.
    /// </summary>
    /// <returns>The Action constructor reference.</returns>
    public MethodReference GetActionConstructor()
    {
        return _actionConstructor ??= FindActionConstructor(_moduleDefinition);
    }

    /// <summary>
    /// Gets the Func constructor reference.
    /// </summary>
    /// <returns>The Func constructor reference.</returns>
    public MethodReference GetFuncConstructor()
    {
        return _funcConstructor ??= FindFuncConstructor(_moduleDefinition);
    }

    /// <summary>
    /// Gets the DelegateCommand constructor definition.
    /// </summary>
    /// <param name="config">The WeaverConfig instance.</param>
    /// <param name="hasCanExecuteMethod">Indicates whether the DelegateCommand constructor has a CanExecute method.</param>
    /// <returns>The DelegateCommand constructor definition.</returns>
    public MethodDefinition GetDelegateCommandConstructor(WeaverConfig config, bool hasCanExecuteMethod)
    {
        if (!_delegateCommandConstructors.TryGetValue(hasCanExecuteMethod, out var constructor))
        {
            constructor = FindDelegateCommandConstructor(config, hasCanExecuteMethod);
            _delegateCommandConstructors[hasCanExecuteMethod] = constructor;
        }

        return constructor;
    }

    /// <summary>
    /// Finds the Action constructor reference.
    /// </summary>
    /// <param name="moduleDefinition">The module definition to search for the constructor.</param>
    /// <returns>The Action constructor reference.</returns>
    private MethodReference FindActionConstructor(ModuleDefinition moduleDefinition)
    {
        var actionType = moduleDefinition.ImportReference(typeof(Action).FullName);
        var actionConstructorInfo = actionType.Resolve().GetConstructors().FirstOrDefault(c => c.Parameters.Count == 2 && c.Parameters[0].ParameterType.MetadataType == MetadataType.Object && c.Parameters[1].ParameterType.MetadataType == MetadataType.IntPtr) ?? throw new WeavingException($"The required Action constructor with two parameters was not found in the type '{actionType.FullName}'. Ensure that the proper version of the System.Runtime assembly is referenced in your project.");

        return moduleDefinition.ImportReference(actionConstructorInfo);
    }

    /// <summary>
    /// Finds the Func constructor reference.
    /// </summary>
    /// <param name="moduleDefinition">The module definition to search for the constructor.</param>
    /// <returns>The Func constructor reference.</returns>
    private MethodReference FindFuncConstructor(ModuleDefinition moduleDefinition)
    {
        var openFuncType = moduleDefinition.ImportReference(typeof(Func<>).FullName);
        var boolType = moduleDefinition.TypeSystem.Boolean;
        var closedFuncType = openFuncType.MakeGenericInstanceType(boolType);
        var openFuncConstructorInfo = openFuncType.Resolve().GetConstructors().FirstOrDefault(c => c.Parameters.Count == 2 && c.Parameters[0].ParameterType.MetadataType == MetadataType.Object && c.Parameters[1].ParameterType.MetadataType == MetadataType.IntPtr) ?? throw new WeavingException($"Unable to find Func<> constructor with two parameters in the type '{openFuncType.FullName}'. Ensure that the proper version of the System.Runtime assembly is referenced in your project.");

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

        return moduleDefinition.ImportReference(closedFuncConstructorInfo);
    }

    /// <summary>
    /// Finds the DelegateCommand constructor definition.
    /// </summary>
    /// <param name="config">The WeaverConfig instance.</param>
    /// <param name="hasCanExecuteMethod">Indicates whether the DelegateCommand constructor has a CanExecute method.</param>
    /// <returns>The DelegateCommand constructor definition.</returns>
    private MethodDefinition FindDelegateCommandConstructor(WeaverConfig config, bool hasCanExecuteMethod)
    {
        var delegateCommandConstructors = config.DelegateCommandType.Resolve().GetConstructors();

        var delegateCommandCtor = delegateCommandConstructors.FirstOrDefault(m => MatchesDelegateCommandConstructor(m, hasCanExecuteMethod));

        return delegateCommandCtor ?? throw new WeavingException($"Unable to find DelegateCommand constructor {(hasCanExecuteMethod ? "with two parameters of types Action and Func`1<Boolean>" : "with a single parameter of type Action")}. Available constructors: {string.Join(", ", delegateCommandConstructors.Select(c => c.ToString()))}");
    }

    /// <summary>
    /// Determines whether the given constructor matches the desired DelegateCommand constructor.
    /// </summary>
    /// <param name="constructor">The constructor to check.</param>
    /// <param name="hasCanExecuteMethod">Indicates whether the desired DelegateCommand constructor has a CanExecute method.</param>
    /// <returns>True if the constructor matches the desired DelegateCommand constructor, false otherwise.</returns>
    private bool MatchesDelegateCommandConstructor(MethodDefinition constructor, bool hasCanExecuteMethod)
    {
        if (constructor.Parameters[0].ParameterType.FullName != typeof(Action).FullName)
        {
            return false;
        }

        if (hasCanExecuteMethod)
        {
            return constructor.Parameters.Count == 2 &&
                   constructor.Parameters[1].ParameterType.IsGenericInstance &&
                   constructor.Parameters[1].ParameterType.GetElementType().FullName == typeof(Func<>).FullName &&
                   ((GenericInstanceType)constructor.Parameters[1].ParameterType).GenericArguments[0].MetadataType == MetadataType.Boolean;
        }
        else
        {
            return constructor.Parameters.Count == 1;
        }
    }
}
