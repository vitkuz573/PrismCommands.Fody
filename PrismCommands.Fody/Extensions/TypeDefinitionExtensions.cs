using Fody;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using System.Collections.Generic;
using System.Linq;

namespace PrismCommands.Fody.Extensions;

public static class TypeDefinitionExtensions
{
    public static void InsertInstructionsBeforeLastRet(this TypeDefinition type, List<Instruction> instructions)
    {
        var ctor = type.GetConstructors().FirstOrDefault() ?? throw new WeavingException($"Failed to find or generate a default constructor for the type '{type.FullName}'. This is an unexpected error. Please ensure the proper project setup and verify the generated code.");
        var lastRetInstruction = ctor.Body.Instructions.LastOrDefault(i => i.OpCode == OpCodes.Ret) ?? throw new WeavingException($"The constructor '{ctor.FullName}' is missing a return instruction (ret). Please verify the constructor implementation to ensure proper weaving.");
        var ilCtor = ctor.Body.GetILProcessor();

        foreach (var instruction in instructions)
        {
            ilCtor.InsertBefore(lastRetInstruction, instruction);
        }
    }
}
