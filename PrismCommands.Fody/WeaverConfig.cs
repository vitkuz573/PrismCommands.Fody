using Fody;
using Mono.Cecil;
using PrismCommands.Fody.Extensions;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace PrismCommands.Fody;

public class WeaverConfig
{
    public string CanExecuteMethodPattern { get; }

    public TypeReference DelegateCommandType { get; }

    public WeaverConfig(ModuleDefinition moduleDefinition, XElement config)
    {
        CanExecuteMethodPattern = config.Attribute("CanExecuteMethodPattern")?.Value ?? "Can{0}";

        if (!IsValidMethodPattern(CanExecuteMethodPattern))
        {
            throw new WeavingException($"The CanExecuteMethodPattern parameter '{CanExecuteMethodPattern}' is incorrectly formatted. The pattern should start with a letter or an underscore, followed by any combination of letters, digits, or underscores, and contain the '{{0}}' placeholder exactly once. Please review your configuration and ensure the correct pattern is used.");
        }

        DelegateCommandType = moduleDefinition.ImportReference("Prism.Commands.DelegateCommand", "Prism");
    }

    private bool IsValidMethodPattern(string pattern)
    {
        var regex = new Regex(@"^[\p{L}_][\p{L}\p{N}_]*\{0\}[\p{L}\p{N}_]*$", RegexOptions.Compiled);

        return regex.IsMatch(pattern);
    }
}
