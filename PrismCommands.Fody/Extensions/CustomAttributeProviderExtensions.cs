﻿using Fody;
using Mono.Cecil.Rocks;
using Mono.Cecil;
using System;
using System.Linq;

namespace PrismCommands.Fody.Extensions;

public static class CustomAttributeProviderExtensions
{
    public static void AddAttribute<T>(this ICustomAttributeProvider provider, ModuleDefinition moduleDefinition, string assemblyName, params object[] constructorArgs) where T : Attribute
    {
        var attributeType = moduleDefinition.ImportReference(typeof(T).FullName, assemblyName);
        var ctor = attributeType.Resolve().GetConstructors().FirstOrDefault() ?? throw new WeavingException($"Unable to find a constructor for attribute '{attributeType.FullName}'.");
        var attribute = new CustomAttribute(moduleDefinition.ImportReference(ctor));

        if (constructorArgs?.Length > 0)
        {
            foreach (var arg in constructorArgs)
            {
                var argType = moduleDefinition.ImportReference(arg.GetType().FullName, assemblyName);
                attribute.ConstructorArguments.Add(new CustomAttributeArgument(moduleDefinition.ImportReference(argType), arg));
            }
        }

        provider.CustomAttributes.Add(attribute);
    }
}