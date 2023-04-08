using System;

namespace PrismCommands;

/// <summary>
/// Attribute used to indicate that a method is a delegate command.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public class DelegateCommandAttribute : Attribute
{
}