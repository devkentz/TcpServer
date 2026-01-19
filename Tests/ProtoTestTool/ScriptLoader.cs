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
                    typeof(ScriptGlobals).Assembly,                   
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



        public async Task<string> CompileToDllAsync(string scriptPath, IEnumerable<string>? referencePaths = null)
        {
            if (!File.Exists(scriptPath))
                throw new FileNotFoundException($"Script file not found: {scriptPath}");

            var options = GetScriptOptions(scriptPath, referencePaths);
            using var stream = File.OpenRead(scriptPath);
            var sourceText = SourceText.From(stream, Encoding.UTF8, checksumAlgorithm: SourceHashAlgorithm.Sha1);
            var dir = Path.GetDirectoryName(scriptPath) ?? AppDomain.CurrentDomain.BaseDirectory;
            var name = Path.GetFileNameWithoutExtension(scriptPath);
            var randomId = Guid.NewGuid().ToString("N").Substring(0, 8);
            var dllPath = Path.Combine(dir, $"{name}.{randomId}.dll");
            var pdbPath = Path.Combine(dir, $"{name}.{randomId}.pdb");

            // Cleanup old files (Try-Catch to ignore locked files)
            try
            {
                var oldFiles = Directory.GetFiles(dir, $"{name}.*.dll")
                    .Concat(Directory.GetFiles(dir, $"{name}.*.pdb"));
                foreach (var oldFile in oldFiles)
                {
                    try { File.Delete(oldFile); } catch { }
                }
            }
            catch { }

            var isScript = scriptPath.EndsWith(".csx", StringComparison.OrdinalIgnoreCase);

            CSharpCompilation compilation;

            if (isScript)
            {
                var script = CSharpScript.Create(sourceText.ToString(), options);
                compilation = (CSharpCompilation)script.GetCompilation();
                
                // Force parse options update if needed (usually handled by CSharpScript)
                var oldTree = compilation.SyntaxTrees.First();
                var parseOptions = oldTree.Options as CSharpParseOptions;
                var newTree = CSharpSyntaxTree.ParseText(sourceText, parseOptions, scriptPath);
                compilation = compilation.ReplaceSyntaxTree(oldTree, newTree);
            }
            else
            {
                // Standard C# Compilation (for .cs files)
                var parseOptions = new CSharpParseOptions(LanguageVersion.Latest, DocumentationMode.Parse, SourceCodeKind.Regular);
                var syntaxTree = CSharpSyntaxTree.ParseText(sourceText, parseOptions, scriptPath);
                
                var assemblyName = Path.GetFileNameWithoutExtension(scriptPath);
                
                // Get references from ScriptOptions
                var references = options.MetadataReferences;

                compilation = CSharpCompilation.Create(
                    assemblyName,
                    new[] { syntaxTree },
                    references,
                    new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, 
                        optimizationLevel: OptimizationLevel.Debug, // or Release
                        platform: Platform.AnyCpu)
                );
            }

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

        public Task<Assembly> LoadScriptWithReferencesAsync(string scriptPath, IEnumerable<string> referencePaths)
        {
            // We compile this one to memory, but referencing the DLLs on disk
            if (!File.Exists(scriptPath))
                throw new FileNotFoundException($"Script file not found: {scriptPath}");

            var options = GetScriptOptions(scriptPath, referencePaths);

            using var stream = File.OpenRead(scriptPath);
            var sourceText = SourceText.From(stream, Encoding.UTF8, checksumAlgorithm: SourceHashAlgorithm.Sha1);

            // Create script without return type expectation
            // Note: We use CSharpCompilation directly now if needed, but for 'scripts' we might use CSharpScript?
            // Actually this method is used for Context loading likely dealing with scripts (.csx).
            // Let's keep using CSharpScript for .csx logic or unify.
            // PacketHandler.csx is a script.
            
            var script = CSharpScript.Create(sourceText.ToString(), options);
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
            return Task.FromResult(assembly);
        }
    }
}