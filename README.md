# PrismCommands.Fody

`PrismCommands.Fody` is a [Fody](https://github.com/Fody/Fody) plugin that provides a simple way to replace methods with Prism DelegateCommand properties at compile time. 

This is useful when using the [Prism](https://github.com/PrismLibrary/Prism) library to build applications with the Model-View-ViewModel (MVVM) architecture. DelegateCommand is a class provided by Prism that implements the ICommand interface and allows you to bind a command from the view to a method in the view model.

## Installation

1. Install the `PrismCommands.Fody` NuGet package in your project.
2. Add a `[DelegateCommand]` attribute to any method in your code that you want to replace with a DelegateCommand property.
3. Build your project. The methods with the `[DelegateCommand]` attribute will be replaced with DelegateCommand properties.

## Example

```csharp
public class MyViewModel
{
    [DelegateCommand]
    public void DoSomething()
    {
        // Do something here.
    }
}
```

After building, each method marked with the `[DelegateCommand]` attribute will be replaced with a corresponding `DelegateCommand` property named using the "{MethodName}Command" pattern.

For example, if you have a `MyViewModel` class with a method named `DoSomething` marked with the `[DelegateCommand]` attribute, after building, this method will be replaced with a property named `DoSomethingCommand`.

```csharp
public class MyViewModel
{
    public DelegateCommand DoSomethingCommand { get; private set; }

    public MyViewModel()
    {
        DoSomethingCommand = new DelegateCommand(DoSomething);
    }

    private void DoSomething()
    {
        // Do something here.
    }
}
```

Thus, you can use the `DoSomethingCommand` property to bind the command to the view.

Note that if you have a method with a name that matches the property name created by `PrismCommands.Fody`, this can lead to conflicts and build errors. To avoid this, avoid using strings in method names that match the "Command" string.

## How it works

`PrismCommands.Fody` uses the Mono.Cecil library to modify the assembly at compile time. It scans the assembly for methods with the `[DelegateCommand]` attribute and replaces them with DelegateCommand properties. 

The implementation details can be found in the `ModuleWeaver` class.

## Contributing

Contributions are welcome! If you find a bug or have a feature request, please open an issue. If you want to contribute code, please fork the repository and submit a pull request. 

## License

`PrismCommands.Fody` is licensed under the [MIT License](https://github.com/vitkuz573/PrismCommands.Fody/blob/main/LICENSE).
