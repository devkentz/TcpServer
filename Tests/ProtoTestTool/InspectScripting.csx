using System;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis.CSharp.Scripting;

var methods = typeof(CSharpScript).GetMethods(BindingFlags.Public | BindingFlags.Static)
    .Where(m => m.Name == "Create" || m.Name == "EvaluateAsync");

foreach (var m in methods)
{
    Console.WriteLine(m);
    foreach (var p in m.GetParameters())
    {
        Console.WriteLine($"  - {p.ParameterType} {p.Name}");
    }
}
