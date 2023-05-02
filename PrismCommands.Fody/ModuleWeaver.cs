using Fody;
using Mono.Cecil;
using PrismCommands.Fody;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Represents a module weaver that is used to modify the compiled code by injecting Prism DelegateCommand functionality.
/// </summary>
public class ModuleWeaver : BaseModuleWeaver
{
    private DelegateCommandTransformer _delegateCommandTransformer;

    /// <summary>
    /// Gets the assemblies for scanning.
    /// </summary>
    /// <returns>An enumerable containing the assembly names.</returns>
    public override IEnumerable<string> GetAssembliesForScanning()
    {
        yield return "Prism";
    }

    /// <summary>
    /// Executes the weaving process, transforming the code as needed.
    /// </summary>
    public override void Execute()
    {
        _delegateCommandTransformer = new DelegateCommandTransformer(ModuleDefinition, Config);

        foreach (var type in ModuleDefinition.Types)
        {
            var methodsToTransform = new List<MethodDefinition>();

            foreach (var method in type.Methods)
            {
                if (method.CustomAttributes.Any(a => a.AttributeType.Name == _delegateCommandTransformer.AttributeName))
                {
                    methodsToTransform.Add(method);
                }
            }

            foreach (var method in methodsToTransform)
            {
                _delegateCommandTransformer.Transform(method);
            }
        }
    }

    /// <summary>
    /// Gets a value indicating whether to clean the reference.
    /// </summary>
    public override bool ShouldCleanReference => true;
}
