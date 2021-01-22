# SingleSharpInstance
 Single instance boot library

* Developed on Net Standard 2.1

* Library used for NamedPipeServerStream with PipeSecurity class: [NamedPipeServerStream.NetFrameworkVersion](https://github.com/HavenDV/H.Pipes/tree/master/src/libs/NamedPipeServerStream.NetFrameworkVersion)

## Use

```c#
using SingleSharpInstance;
using SingleSharpInstance.Events;
using System;

class Program
{
    static void Main(string[] args)
    {
        //Create single instance context for the application id
        var single = SingleSharp.From(new Guid("<App id>"));

        //Receive activation events
        single.OnReceiveActivation += OnReceiveActivation;

        //Sends the arguments for the application activation
        single.SendActivation(new string[] { "arg1", "arg2" });
    }

    private static void OnReceiveActivation(object sender, ActivationEventArgs e)
    {
        //Retrieves received arguments
        string[] args = e.Args;

        //Checks if it is the first activation
        bool isFirstActivation = e.IsFirstActivation;

        //Sends to the console the number of arguments received and if it is the first activation of the program
        Console.WriteLine($"Received Arguments: {args.Length}. First Activation: {isFirstActivation}");
    }
}
```

**Compatibility**

* [x] Windows
* [ ] Mac os
* [ ] Linux