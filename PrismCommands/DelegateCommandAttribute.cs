using System;

#pragma warning disable CS1591

namespace PrismCommands;

[AttributeUsage(AttributeTargets.Method)]
public class DelegateCommandAttribute : Attribute
{
}