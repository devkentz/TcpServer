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
                Assembly.Load("ICSharpCode.AvalonEdit"),

                // Scripting Assemblies
                Assembly.Load("Microsoft.CodeAnalysis.CSharp.Scripting"),
                Assembly.Load("Microsoft.CodeAnalysis.Scripting"),

                typeof(object).Assembly, 
                typeof(System.Linq.Enumerable).Assembly, 
                typeof(Google.Protobuf.IMessage).Assembly,
                typeof(ProtoTestTool.ScriptContract.ScriptGlobals).Assembly,
                typeof(System.Buffers.ReadOnlySequence<>).Assembly,
                typeof(System.Memory<>).Assembly,
                Assembly.Load("System.Runtime"),
                Assembly.Load("System.Collections"),
                Assembly.Load("netstandard"),
                Assembly.Load("System.Core"),
                Assembly.Load("System.Text.RegularExpressions"),
                Assembly.Load("System.ComponentModel"),
                Assembly.Load("System.ComponentModel.Primitives"),
                Assembly.Load("System.ComponentModel.TypeConverter"),
                Assembly.Load("System.ObjectModel"),
                typeof(System.Threading.Tasks.Task).Assembly,
                typeof(System.Net.Http.HttpClient).Assembly,
                typeof(System.Text.Json.JsonSerializer).Assembly,
            };

            var references = RoslynHostReferences.NamespaceDefault.With(
                assemblyReferences: assemblies
            );
            
            Host = new RoslynHost(additionalAssemblies: assemblies, references: references);
        }

        public void AddReference(DocumentId docId, string assemblyPath)
        {
            var doc = Host.GetDocument(docId);
            if (doc != null)
            {
                var reference = MetadataReference.CreateFromFile(assemblyPath);
                var project = doc.Project.AddMetadataReference(reference);
                var result = project.Solution.Workspace.TryApplyChanges(project.Solution);
            }
        }
    }
}