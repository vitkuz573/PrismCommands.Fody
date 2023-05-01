using Fody;
using Mono.Cecil;
using PrismCommands.Fody;
using System.Collections.Generic;
using System.Linq;

public class ModuleWeaver : BaseModuleWeaver
{
    private DelegateCommandTransformer _delegateCommandTransformer;

    public override IEnumerable<string> GetAssembliesForScanning()
    {
        yield return "Prism";
    }

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

    public override bool ShouldCleanReference => true;
}
