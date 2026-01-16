
using System;
using System.Reflection;
using System.Linq;
using RoslynPad.Editor;

var assembly = typeof(RoslynCodeEditor).Assembly;
var type = assembly.GetType("RoslynPad.Editor.RoslynCodeEditor");

Console.WriteLine("Methods in RoslynCodeEditor:");
foreach (var method in type.GetMethods().Where(m => m.Name == "InitializeAsync"))
{
    Console.WriteLine(method);
    foreach (var p in method.GetParameters())
    {
        Console.WriteLine($" - {p.ParameterType} {p.Name}");
    }
}
