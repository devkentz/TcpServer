using System;
using System.Reflection;
using Microsoft.CodeAnalysis;
using RoslynPad.Roslyn;

namespace ProtoTestTool.Roslyn
{
    public class RoslynService
    {
        public RoslynHost Host { get; private set; }

        public RoslynService()
        {
            var assemblies = new[]
            {
                Assembly.Load("RoslynPad.Roslyn.Windows"),
                Assembly.Load("RoslynPad.Editor.Windows"),
                typeof(object).Assembly, 
                typeof(System.Linq.Enumerable).Assembly, 
                typeof(Google.Protobuf.IMessage).Assembly,
                typeof(ProtoTestTool.ScriptContract.ScriptGlobals).Assembly,
                typeof(System.Buffers.ReadOnlySequence<>).Assembly,
                typeof(System.Memory<>).Assembly,
                Assembly.Load("System.Runtime"),
                Assembly.Load("System.Collections"),
                Assembly.Load("netstandard")
            };

            var references = RoslynHostReferences.NamespaceDefault.With(
                assemblyReferences: assemblies
            );
            
            Host = new RoslynHost(additionalAssemblies: assemblies, references: references);
        }
    }
}