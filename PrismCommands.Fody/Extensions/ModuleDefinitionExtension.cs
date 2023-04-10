using Fody;
using Mono.Cecil;

namespace PrismCommands.Fody.Extensions;

public static class ModuleDefinitionExtension
{
    public static TypeReference ImportTypeFromAssembly(this ModuleDefinition moduleDefinition, string type, string assemblyName)
    {
        var assembly = moduleDefinition.AssemblyResolver.Resolve(new AssemblyNameReference(assemblyName, null)) ?? throw new WeavingException($"Unable to find assembly '{assemblyName}'.");
        var module = assembly.MainModule;
        var typeDefinition = module.GetType(type) ?? throw new WeavingException($"Unable to find type '{type}' in assembly '{assemblyName}'.");

        return moduleDefinition.ImportReference(typeDefinition);
    }
}
