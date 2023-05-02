using Fody;
using Mono.Cecil;
using PrismCommands.Fody.Extensions;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace PrismCommands.Fody;

/// <summary>
/// Represents the configuration for the PrismCommands Weaver.
/// </summary>
public class WeaverConfig
{
    /// <summary>
    /// Gets the CanExecute method pattern.
    /// </summary>
    public string CanExecuteMethodPattern { get; }

    /// <summary>
    /// Gets the DelegateCommand type reference.
    /// </summary>
    public TypeReference DelegateCommandType { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="WeaverConfig"/> class.
    /// </summary>
    /// <param name="moduleDefinition">The module definition to be used in the configuration.</param>
    /// <param name="config">The XML configuration element.</param>
    public WeaverConfig(ModuleDefinition moduleDefinition, XElement config)
    {
        CanExecuteMethodPattern = config.Attribute("CanExecuteMethodPattern")?.Value ?? "Can{0}";

        if (!IsValidMethodPattern(CanExecuteMethodPattern))
        {
            throw new WeavingException($"The CanExecuteMethodPattern parameter '{CanExecuteMethodPattern}' is incorrectly formatted. The pattern should start with a letter or an underscore, followed by any combination of letters, digits, or underscores, and contain the '{{0}}' placeholder exactly once. Please review your configuration and ensure the correct pattern is used.");
        }

        DelegateCommandType = moduleDefinition.ImportReference("Prism.Commands.DelegateCommand", "Prism");
    }

    /// <summary>
    /// Validates the method pattern.
    /// </summary>
    /// <param name="pattern">The pattern to validate.</param>
    /// <returns>Returns true if the pattern is valid, false otherwise.</returns>
    private bool IsValidMethodPattern(string pattern)
    {
        var regex = new Regex(@"^[\p{L}_][\p{L}\p{N}_]*\{0\}[\p{L}\p{N}_]*$", RegexOptions.Compiled);

        return regex.IsMatch(pattern);
    }
}
