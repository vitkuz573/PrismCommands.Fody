using Fody;
using Mono.Cecil;

namespace PrismCommands.Fody.Extensions;

public static class ModuleDefinitionExtensions
{
    public static TypeReference ImportReference(this ModuleDefinition moduleDefinition, string type, string assemblyName = null)
    {
        TypeDefinition typeDefinition = null;
        AssemblyDefinition assembly = null;

        if (assemblyName != null)
        {
            assembly = moduleDefinition.AssemblyResolver.Resolve(new AssemblyNameReference(assemblyName, null));
            var module = assembly.MainModule;
            typeDefinition = module.GetType(type);
        }
        else
        {
            string[] possibleAssemblies = ["System.Runtime", "mscorlib"];

            foreach (var possibleAssemblyName in possibleAssemblies)
            {
                try
                {
                    assembly = moduleDefinition.AssemblyResolver.Resolve(new AssemblyNameReference(possibleAssemblyName, null));
                    var module = assembly.MainModule;
                    typeDefinition = module.GetType(type);
                }
                catch { /* Ignore exceptions and try the next assembly */ }

                if (typeDefinition != null)
                {
                    break;
                }
            }
        }

        if (typeDefinition == null || assembly == null)
        {
            throw new WeavingException($"Unable to find type '{type}' in any of the specified assemblies.");
        }

        return moduleDefinition.ImportReference(typeDefinition);
    }
}
