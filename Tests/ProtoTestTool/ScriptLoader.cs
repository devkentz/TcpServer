using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using Microsoft.CodeAnalysis.Text;
using ProtoTestTool.ScriptContract;

namespace ProtoTestTool
{
    public class ScriptLoader
    {
        private ScriptOptions GetScriptOptions(string scriptPath = "", IEnumerable<string>? extraRefPaths = null)
        {
             var scriptDirectory = !string.IsNullOrEmpty(scriptPath) 
                ? Path.GetDirectoryName(Path.GetFullPath(scriptPath)) 
                : AppDomain.CurrentDomain.BaseDirectory;

             var options = ScriptOptions.Default
                .WithFilePath(scriptPath)
                .WithSourceResolver(ScriptSourceResolver.Default.WithBaseDirectory(scriptDirectory))
                .WithEmitDebugInformation(true)
                .WithReferences(
                    typeof(object).Assembly,                           
                    typeof(Google.Protobuf.IMessage).Assembly,         
                    typeof(IScriptContext).Assembly,                   
                    Assembly.Load("System.Runtime"),
                    Assembly.Load("System.Collections"),
                    Assembly.Load("netstandard")
                )
                .WithImports(
                    "System",
                    "System.Collections.Generic",
                    "Google.Protobuf",
                    "ProtoTestTool.ScriptContract"
                );

            if (extraRefPaths != null)
            {
                foreach (var refPath in extraRefPaths)
                {
                    options = options.AddReferences(MetadataReference.CreateFromFile(refPath));
                }
            }
            return options;
        }

        public System.Collections.Immutable.ImmutableArray<Diagnostic> ValidateScript(string code)
        {
            var options = GetScriptOptions();
            var script = CSharpScript.Create(code, options);
            var compilation = script.GetCompilation();
            return compilation.GetDiagnostics();
        }

        public async Task<IScriptContext> LoadScriptAsync(string scriptPath)
        {
            // Backward compatibility for single file
            return await LoadScriptWithReferencesAsync(scriptPath, null);
        }

        public async Task<string> CompileToDllAsync(string scriptPath, IEnumerable<string> referencePaths = null)
        {
            if (!File.Exists(scriptPath))
                throw new FileNotFoundException($"Script file not found: {scriptPath}");

            var options = GetScriptOptions(scriptPath, referencePaths);

            using var stream = File.OpenRead(scriptPath);
            var sourceText = SourceText.From(stream, Encoding.UTF8, checksumAlgorithm: SourceHashAlgorithm.Sha1);

            var script = CSharpScript.Create(sourceText.ToString(), options);
            var compilation = script.GetCompilation();

            var oldTree = compilation.SyntaxTrees.First();
            var parseOptions = oldTree.Options as CSharpParseOptions;
            var newTree = CSharpSyntaxTree.ParseText(sourceText, parseOptions, scriptPath);
            compilation = compilation.ReplaceSyntaxTree(oldTree, newTree);

            var dllPath = Path.ChangeExtension(scriptPath, ".dll");
            var pdbPath = Path.ChangeExtension(scriptPath, ".pdb");

            await using var peStream = File.Create(dllPath);
            await using var pdbStream = File.Create(pdbPath);

            var emitResult = compilation.Emit(peStream, pdbStream);

            if (!emitResult.Success)
            {
                var errors = string.Join(Environment.NewLine, emitResult.Diagnostics
                    .Where(d => d.Severity == DiagnosticSeverity.Error)
                    .Select(d => $"[{Path.GetFileName(scriptPath)}] Line {d.Location.GetLineSpan().StartLinePosition.Line + 1}: {d.GetMessage()}"));
                throw new InvalidOperationException($"Compilation failed for {Path.GetFileName(scriptPath)}:{Environment.NewLine}{errors}");
            }
            
            return dllPath;
        }

        public async Task<IScriptContext> LoadScriptWithReferencesAsync(string scriptPath, IEnumerable<string> referencePaths)
        {
            // We compile this one to memory, but referencing the DLLs on disk
            if (!File.Exists(scriptPath))
                throw new FileNotFoundException($"Script file not found: {scriptPath}");

            var options = GetScriptOptions(scriptPath, referencePaths);

            using var stream = File.OpenRead(scriptPath);
            var sourceText = SourceText.From(stream, Encoding.UTF8, checksumAlgorithm: SourceHashAlgorithm.Sha1);

            var script = CSharpScript.Create<IScriptContext>(sourceText.ToString(), options);
            var compilation = script.GetCompilation();

            var oldTree = compilation.SyntaxTrees.First();
            var parseOptions = oldTree.Options as CSharpParseOptions;
            var newTree = CSharpSyntaxTree.ParseText(sourceText, parseOptions, scriptPath);
            compilation = compilation.ReplaceSyntaxTree(oldTree, newTree);

            using var peStream = new MemoryStream();
            using var pdbStream = new MemoryStream();

            var emitResult = compilation.Emit(peStream, pdbStream);

            if (!emitResult.Success)
            {
                var errors = string.Join(Environment.NewLine, emitResult.Diagnostics
                    .Where(d => d.Severity == DiagnosticSeverity.Error)
                    .Select(d => $"[{Path.GetFileName(scriptPath)}] Line {d.Location.GetLineSpan().StartLinePosition.Line + 1}: {d.GetMessage()}"));
                throw new InvalidOperationException($"Compilation failed for {Path.GetFileName(scriptPath)}:{Environment.NewLine}{errors}");
            }

            peStream.Seek(0, SeekOrigin.Begin);
            pdbStream.Seek(0, SeekOrigin.Begin);

            var assembly = Assembly.Load(peStream.ToArray(), pdbStream.ToArray());
            var type = assembly.GetType("Submission#0");
            var factoryMethod = type.GetMethod("<Factory>", BindingFlags.Static | BindingFlags.Public);

            if (factoryMethod == null) throw new InvalidOperationException("Could not find script entry point.");

            var task = (Task<IScriptContext>)factoryMethod.Invoke(null, new object[] { new object[] { null, null } });
            var result = await task;

            if (result == null) throw new InvalidOperationException("The script returned null.");

            return result;
        }
    }
}