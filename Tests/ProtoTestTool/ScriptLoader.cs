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
                    Assembly.Load("netstandard"),
                    typeof(System.Buffers.Binary.BinaryPrimitives).Assembly,
                    typeof(System.Memory<>).Assembly
                )
                .WithImports(
                    "System",
                    "System.Collections.Generic",
                    "Google.Protobuf",
                    "ProtoTestTool.ScriptContract",
                    "System.Buffers",
                    "System.Buffers.Binary"
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

        private class PreProcessResult
        {
            public string Code { get; set; } = "";
            public List<string> References { get; set; } = new List<string>();
        }

        private PreProcessResult PreProcess(string code, string scriptPath)
        {
            var result = new PreProcessResult { Code = code };
            
            // Check for #r "nuget:..."
            var lines = code.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            var nugetLines = lines.Where(l => l.TrimStart().StartsWith("#r \"nuget:", StringComparison.OrdinalIgnoreCase)).ToList();

            if (nugetLines.Any())
            {
                // Comment out nuget lines in the code to satisfy Roslyn
                var sb = new StringBuilder();
                foreach (var line in lines)
                {
                    if (line.TrimStart().StartsWith("#r \"nuget:", StringComparison.OrdinalIgnoreCase))
                    {
                        sb.AppendLine($"// {line}"); // Comment out
                    }
                    else
                    {
                        sb.AppendLine(line);
                    }
                }
                result.Code = sb.ToString();

                // Resolve Packages
                // We need a physical file for the resolver.
                // Use a temp file if scriptPath is not valid or we want to resolve current content.
                // BUT, to avoid excessive temp files, if scriptPath exists and content matches, use it.
                // Assuming content passed might be newer (editor), use temp file.
                
                var tempFile = Path.GetTempFileName();
                var tempCsx = Path.ChangeExtension(tempFile, ".csx");
                File.Move(tempFile, tempCsx);

                try
                {
                    File.WriteAllText(tempCsx, code); // Write ORIGINAL code with #r nuget

                    var loggerFactory = new Dotnet.Script.DependencyModel.Logging.LogFactory(type => (level, message, exception) => System.Diagnostics.Debug.WriteLine($"[{level}] {message}"));
                    var resolver = new Dotnet.Script.DependencyModel.Runtime.RuntimeDependencyResolver(loggerFactory, true);
                    
                    var dependencies = resolver.GetDependencies(tempCsx, Array.Empty<string>());
                    foreach (dynamic dep in dependencies)
                    {
                        foreach (string refPath in dep.AssemblyPaths)
                        {
                             result.References.Add(refPath);
                        }
                    }
                }
                catch (Exception ex)
                {
                     System.Diagnostics.Debug.WriteLine($"NuGet resolution failed: {ex.Message}");
                }
                finally
                {
                    if (File.Exists(tempCsx)) File.Delete(tempCsx);
                }
            }

            return result;
        }

        public (System.Collections.Immutable.ImmutableArray<Diagnostic> Diagnostics, List<string> References) ValidateScript(string code, string scriptPath = "", Action<string>? logger = null, IEnumerable<string>? extraRefPaths = null)
        {
            var preProcess = PreProcess(code, scriptPath, logger);
            var refs = preProcess.References.Select(r => MetadataReference.CreateFromFile(r)).ToList(); 
            // Add extra refs
            if (extraRefPaths != null)
            {
                refs.AddRange(extraRefPaths.Select(r => MetadataReference.CreateFromFile(r)));
            }

            var mergedRefs = preProcess.References.Concat(extraRefPaths ?? Enumerable.Empty<string>()).ToList();
            var baseOptions = GetScriptOptions(scriptPath, mergedRefs);
            
            var syntaxTree = CSharpSyntaxTree.ParseText(preProcess.Code, new CSharpParseOptions(LanguageVersion.Latest), scriptPath);
            
            var compilation = CSharpCompilation.Create(
                Path.GetFileNameWithoutExtension(scriptPath) ?? "Validation",
                new[] { syntaxTree },
                baseOptions.MetadataReferences, 
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
                
            return (compilation.GetDiagnostics(), mergedRefs);
        }

        public async Task<string> CompileToDllAsync(string scriptPath, IEnumerable<string>? referencePaths = null, Action<string>? logger = null)
        {
            if (!File.Exists(scriptPath))
                throw new FileNotFoundException($"Script file not found: {scriptPath}");

            var code = await File.ReadAllTextAsync(scriptPath);
            var preProcess = PreProcess(code, scriptPath, logger);
            
            var refs = (referencePaths ?? Enumerable.Empty<string>()).Concat(preProcess.References).ToList();

            var options = GetScriptOptions(scriptPath, refs);
            
            var sourceText = SourceText.From(preProcess.Code, Encoding.UTF8, checksumAlgorithm: SourceHashAlgorithm.Sha1);
            
            var dir = Path.GetDirectoryName(scriptPath) ?? AppDomain.CurrentDomain.BaseDirectory;
            var name = Path.GetFileNameWithoutExtension(scriptPath);
            var randomId = Guid.NewGuid().ToString("N").Substring(0, 8);
            var dllPath = Path.Combine(dir, $"{name}.{randomId}.dll");
            var pdbPath = Path.Combine(dir, $"{name}.{randomId}.pdb");

            // Cleanup old files
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

            // Always use Standard C# Compilation to ensure types are top-level (not nested in Script/Submission class)
            // This allows PacketRegistry types to be visible to PacketSerializer.
            var parseOptions = new CSharpParseOptions(LanguageVersion.Latest, DocumentationMode.Parse, SourceCodeKind.Regular);
            var syntaxTree = CSharpSyntaxTree.ParseText(sourceText, parseOptions, scriptPath);
            var assemblyName = Path.GetFileNameWithoutExtension(scriptPath);
            var references = options.MetadataReferences;

            CSharpCompilation compilation = CSharpCompilation.Create(
                assemblyName,
                new[] { syntaxTree },
                references,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, 
                    optimizationLevel: OptimizationLevel.Debug, 
                    platform: Platform.AnyCpu)
            );

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

        public Task<Assembly> LoadScriptWithReferencesAsync(string scriptPath, IEnumerable<string> referencePaths, Action<string>? logger = null)
        {
            if (!File.Exists(scriptPath))
                throw new FileNotFoundException($"Script file not found: {scriptPath}");

            var code = File.ReadAllText(scriptPath);
            var preProcess = PreProcess(code, scriptPath, logger);
            var refs = referencePaths.Concat(preProcess.References);

            var options = GetScriptOptions(scriptPath, refs);
            
            var script = CSharpScript.Create(preProcess.Code, options); 
            var compilation = script.GetCompilation();
            
             using var stream = File.OpenRead(scriptPath);
             var sourceText = SourceText.From(preProcess.Code, Encoding.UTF8, checksumAlgorithm: SourceHashAlgorithm.Sha1);
             
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

        private PreProcessResult PreProcess(string code, string scriptPath, Action<string>? logger = null)
        {
            var result = new PreProcessResult { Code = code };
            
            var lines = code.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            var nugetLines = lines.Where(l => l.TrimStart().StartsWith("#r \"nuget:", StringComparison.OrdinalIgnoreCase)).ToList();

            if (nugetLines.Any())
            {
                logger?.Invoke($"Found {nugetLines.Count} NuGet references. Resolving...");

                var sb = new StringBuilder();
                foreach (var line in lines)
                {
                    if (line.TrimStart().StartsWith("#r \"nuget:", StringComparison.OrdinalIgnoreCase))
                    {
                        sb.AppendLine($"// {line}"); 
                    }
                    else
                    {
                        sb.AppendLine(line);
                    }
                }
                result.Code = sb.ToString();

                var tempFile = Path.GetTempFileName();
                var tempCsx = Path.ChangeExtension(tempFile, ".csx");
                File.Move(tempFile, tempCsx);

                try
                {
                    File.WriteAllText(tempCsx, code);

                    var loggerFactory = new Dotnet.Script.DependencyModel.Logging.LogFactory(type => (level, message, exception) => 
                    {
                        var msg = $"[{level}] {message}";
                        System.Diagnostics.Debug.WriteLine(msg);
                        logger?.Invoke(msg);
                    });
                    
                    var resolver = new Dotnet.Script.DependencyModel.Runtime.RuntimeDependencyResolver(loggerFactory, true);
                    
                    var dependencies = resolver.GetDependencies(tempCsx, Array.Empty<string>());
                    int count = 0;
                    foreach (dynamic dep in dependencies)
                    {
                        try 
                        {
                            // 'Assemblies' contains RuntimeAssembly objects
                            foreach (dynamic asm in dep.Assemblies)
                            {
                                 try
                                 {
                                     string refPath = asm.Path;
                                     result.References.Add(refPath);
                                     logger?.Invoke($"Resolved: {Path.GetFileName(refPath)}");
                                     count++;
                                 }
                                 catch (Exception ex)
                                 {
                                     // Fallback or debug
                                     logger?.Invoke($"[Error] Could not read Path from assembly object: {ex.Message}");
                                     logger?.Invoke($"Assembly Object Properties:");
                                     foreach (var prop in ((object)asm).GetType().GetProperties())
                                     {
                                         logger?.Invoke($" - {prop.Name} ({prop.PropertyType.Name})");
                                     }
                                 }
                            }
                        }
                        catch (Exception ex)
                        {
                            logger?.Invoke($"[Error] Error iterating dependencies: {ex.Message}");
                            // Dump dependency properties just in case
                             foreach (var prop in ((object)dep).GetType().GetProperties())
                             {
                                 logger?.Invoke($" - Dep Property: {prop.Name}");
                             }
                        }
                    }
                    logger?.Invoke($"Total resolved references: {count}");
                }
                catch (Exception ex)
                {
                     logger?.Invoke($"[Error] NuGet resolution failed: {ex.Message}");
                     System.Diagnostics.Debug.WriteLine($"NuGet resolution failed: {ex.Message}");
                }
                finally
                {
                    if (File.Exists(tempCsx)) File.Delete(tempCsx);
                }
            }

            return result;
        }
    }
}