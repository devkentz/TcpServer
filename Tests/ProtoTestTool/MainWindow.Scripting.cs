using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Documents;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using RoslynPad.Editor;
using RoslynPad.Editor;
using RoslynPad.Roslyn;
using Newtonsoft.Json;
using ProtoTestTool.Roslyn;

namespace ProtoTestTool
{
    public partial class MainWindow
    {
        private async void InitializeRoslynEditor()
        {
            try
            {
                var initialCode = "// Loading...";
                var docId = _roslynService.Host.AddDocument(new DocumentCreationArgs(
                    new StringTextSource(initialCode), "PacketHandler.csx", SourceCodeKind.Script)
                {
                    WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory
                });

                // Initialize all editors
                await RegistryEditor.InitializeAsync(_roslynService.Host, new ClassificationHighlightColors(),
                    AppDomain.CurrentDomain.BaseDirectory, docId.Id.ToString(), SourceCodeKind.Script);

                await SerializerEditor.InitializeAsync(_roslynService.Host, new ClassificationHighlightColors(),
                    AppDomain.CurrentDomain.BaseDirectory, docId.Id.ToString(), SourceCodeKind.Script);

                await ContextEditor.InitializeAsync(_roslynService.Host, new ClassificationHighlightColors(),
                    AppDomain.CurrentDomain.BaseDirectory, docId.Id.ToString(), SourceCodeKind.Script);
            }
            catch (Exception ex)
            {
                // Fallback or log if needed
            }
        }

        class StringTextSource : SourceTextContainer
        {
            private SourceText _text;

            public StringTextSource(string text)
            {
                _text = SourceText.From(text);
            }

            public override SourceText CurrentText => _text;

            public override event EventHandler<TextChangeEventArgs>? TextChanged
            {
                add { }
                remove { }
            }
        }

        #region Script Loading & Editing

        // -- Registry --
        private async void LoadRegistryBtn_Click(object sender, RoutedEventArgs e) => await LoadFileToEditor(RegistryEditor, "PacketRegistry.csx");
        private async void SaveRegistryBtn_Click(object sender, RoutedEventArgs e) => await SaveEditorToFile(RegistryEditor, "PacketRegistry.csx");

        // -- Serializer --
        private async void LoadSerializerBtn_Click(object sender, RoutedEventArgs e) => await LoadFileToEditor(SerializerEditor, "PacketSerializer.csx");
        private async void SaveSerializerBtn_Click(object sender, RoutedEventArgs e) => await SaveEditorToFile(SerializerEditor, "PacketSerializer.csx");

        // -- Context --
        private async void LoadContextBtn_Click(object sender, RoutedEventArgs e) => await LoadFileToEditor(ContextEditor, "PacketHandler.csx");
        private async void SaveContextBtn_Click(object sender, RoutedEventArgs e) => await SaveEditorToFile(ContextEditor, "PacketHandler.csx");

        private async Task LoadFileToEditor(RoslynPad.Editor.RoslynCodeEditor editor, string defaultName)
        {
            var openFileDialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "C# Script (*.csx)|*.csx|All files (*.*)|*.*",
                FileName = defaultName,
                Title = $"Load {defaultName}"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                try
                {
                    editor.Text = await File.ReadAllTextAsync(openFileDialog.FileName);
                    ScriptLogBox.Text += $"\nLoaded {Path.GetFileName(openFileDialog.FileName)}.";
                }
                catch (Exception ex)
                {
                    ScriptLogBox.Text += $"\nError loading: {ex.Message}";
                }
            }
        }

        private async Task SaveEditorToFile(RoslynPad.Editor.RoslynCodeEditor editor, string defaultName)
        {
            var saveFileDialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "C# Script (*.csx)|*.csx|All files (*.*)|*.*",
                FileName = defaultName,
                Title = $"Save {defaultName}"
            };

            if (saveFileDialog.ShowDialog() == true)
            {
                try
                {
                    await File.WriteAllTextAsync(saveFileDialog.FileName, editor.Text);
                    ScriptLogBox.Text += $"\nSaved {Path.GetFileName(saveFileDialog.FileName)}.";
                }
                catch (Exception ex)
                {
                    ScriptLogBox.Text += $"\nError saving: {ex.Message}";
                }
            }
        }


        private void ScriptEditorTab_Selected(object sender, RoutedEventArgs e)
        {
            // Maybe refresh files?
        }

        private void RefreshScriptFileList()
        {
            // Optional
        }



        private async void LoadFileToEditor(string fileName)
        {
            // Deprecated in favor of LoadAllFilesAsync
        }


        private async void CompileScriptBtn_Click(object sender, RoutedEventArgs e)
        {
            // Auto-save before compile
            await SaveAllSilent();

            var selectedTab = MethodTabs.SelectedItem as TabItem;
            string header = selectedTab?.Header as string ?? "";

            // Note: Header names might be localized now, e.g. "Packet Registry (패킷 등록)"
            // Use Contains to be safe
            if (header.Contains("Packet Registry") || header.Contains("Packet Serializer"))
            {
                // Validate only the current code
                string code = header.Contains("Packet Registry") ? RegistryEditor.Text : SerializerEditor.Text;

                ScriptLogBox.Text = $"검증 중 (Validating)...";
                ScriptLogBox.Foreground = Brushes.Black;

                await Task.Run(() =>
                {
                    var diagnostics = _scriptLoader.ValidateScript(code);
                    var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();

                    Dispatcher.Invoke(() =>
                    {
                        if (errors.Count > 0)
                        {
                            var errorMsg = string.Join(Environment.NewLine, errors.Select(d => $"Line {d.Location.GetLineSpan().StartLinePosition.Line + 1}: {d.GetMessage()}"));
                            ScriptLogBox.Text = $"[{DateTime.Now:HH:mm:ss}] 검증 실패 (Validation Failed):\n{errorMsg}";
                            ScriptLogBox.Foreground = Brushes.Red;
                        }
                        else
                        {
                            ScriptLogBox.Text = $"[{DateTime.Now:HH:mm:ss}] 검증 성공 (Validation Success)!";
                            ScriptLogBox.Foreground = Brushes.Blue;
                        }
                    });
                });
            }
            else
            {
                // For Context, we run the full compilation (Integration Test)
                await CompileScriptAsync();
            }
        }

        private async Task SaveAllSilent()
        {
            try
            {
                var dir = AppDomain.CurrentDomain.BaseDirectory;

                // Backup existing if needed? For now just overwrite.
                await File.WriteAllTextAsync(Path.Combine(dir, "PacketRegistry.csx"), RegistryEditor.Text);
                await File.WriteAllTextAsync(Path.Combine(dir, "PacketSerializer.csx"), SerializerEditor.Text);

                // Wrap context for saving
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("#load \"PacketRegistry.csx\"");
                sb.AppendLine("#load \"PacketSerializer.csx\"");
                sb.AppendLine("using System;");
                sb.AppendLine("using ProtoTestTool.ScriptContract;");
                sb.AppendLine();
                sb.AppendLine(ContextEditor.Text);
                sb.AppendLine();
                sb.AppendLine("return new MyScriptContext();");

                await File.WriteAllTextAsync(Path.Combine(dir, "PacketHandler.csx"), sb.ToString());
            }
            catch (Exception ex)
            {
                ScriptLogBox.Text += $"\n[System] 자동 저장 실패: {ex.Message}";
            }
        }

        private async Task CompileScriptAsync()
        {
            ScriptLogBox.Text = "스크립트 체인 컴파일 중... (Compiling)";
            ScriptLogBox.Foreground = Brushes.Black;

            try
            {
                var dir = AppDomain.CurrentDomain.BaseDirectory;

                // 1. Compile Registry
                var regPath = Path.Combine(dir, "PacketRegistry.csx");
                if (!File.Exists(regPath)) throw new FileNotFoundException("PacketRegistry.csx 없음");

                ScriptLogBox.Text += "\nPacket Registry 컴파일 중...";
                var regDllPath = await _scriptLoader.CompileToDllAsync(regPath, null);

                // 2. Compile Serializer
                var serPath = Path.Combine(dir, "PacketSerializer.csx");
                if (!File.Exists(serPath)) throw new FileNotFoundException("PacketSerializer.csx 없음");

                ScriptLogBox.Text += "\nPacket Serializer 컴파일 중...";
                var serDllPath = await _scriptLoader.CompileToDllAsync(serPath, null);

                // 3. Compile Context
                var mainPath = Path.Combine(dir, "PacketHandler.csx");
                // Pre-process Main Path to remove #load if present, as we are manually linking
                // We create a temporary build file to avoid modifying the user's source
                var buildPath = Path.Combine(dir, "PacketHandler.Build.csx");

                if (!File.Exists(mainPath))
                {
                    // Fallback create
                    ContextEditor.Text = GetDefaultTemplate("MyScriptContext");
                    await File.WriteAllTextAsync(mainPath, ContextEditor.Text);
                }

                var mainCode = await File.ReadAllTextAsync(mainPath);
                // Remove #load directives
                var lines = mainCode.Split(new[] {"\r\n", "\n"}, StringSplitOptions.None);
                var cleanLines = lines.Where(l => !l.TrimStart().StartsWith("#load")).ToArray();
                await File.WriteAllLinesAsync(buildPath, cleanLines);

                ScriptLogBox.Text += "\nScript Context 컴파일 중...";

                // Collect ProtoGen references
                var protoGenDir = Path.Combine(dir, "ProtoGen");
                var protoRefs = new List<string> {regDllPath, serDllPath};

                if (Directory.Exists(protoGenDir))
                {
                    var protoDlls = Directory.GetFiles(protoGenDir, "*.dll", SearchOption.AllDirectories);
                    protoRefs.AddRange(protoDlls);
                }

                var context = await _scriptLoader.LoadScriptWithReferencesAsync(buildPath, protoRefs);

                context.SetLogger((msg) => 
                {
                    Dispatcher.Invoke(() => AppendLog($"[Script] {msg}", Brushes.Teal));
                }); 
                context.Initialize(context.Registry, context.Serializer);

                ScriptLogBox.Text += $"\n[{DateTime.Now:HH:mm:ss}] Compilation Success!";
                ScriptLogBox.Foreground = Brushes.Blue;

                _scriptContext = context;
                RefreshPacketList();
            }
            catch (Exception ex)
            {
                ScriptLogBox.Text += $"\n[{DateTime.Now:HH:mm:ss}] Error:\n{ex.Message}";
                ScriptLogBox.Foreground = Brushes.Red;
            }
        }

        private void RefreshPacketList()
        {
            PacketListBox.ItemsSource = null;
            if (_scriptContext?.Registry == null) return;

            var types = _scriptContext.Registry.GetMessageTypes()
                .OrderBy(t => t.Name)
                .ToList();

            PacketListBox.ItemsSource = types;
        }

        private void PacketListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (PacketListBox.SelectedItem is Type packetType)
            {
                try
                {
                    var instance = Activator.CreateInstance(packetType);
                    JsonEditor.Text = JsonConvert.SerializeObject(instance, Formatting.Indented);
                }
                catch (Exception ex)
                {
                    JsonEditor.Text = $"Error creating template: {ex.Message}";
                }
            }
        }

        #endregion

        private void LoadScriptIntoTabs(string code)
        {
            var tree = CSharpSyntaxTree.ParseText(code);
            var root = tree.GetRoot();
            var classes = root.DescendantNodes().OfType<ClassDeclarationSyntax>().ToList();

            // Default templates if missing
            RegistryEditor.Text = GetDefaultTemplate("PacketRegistry");
            SerializerEditor.Text = GetDefaultTemplate("PacketSerializer");
            ContextEditor.Text = GetDefaultTemplate("MyScriptContext");

            foreach (var cls in classes)
            {
                // Simple heuristic: check base list or class name
                if (IsImplementationOf(cls, "IPacketRegistry") || cls.Identifier.Text.Contains("Registry"))
                {
                    RegistryEditor.Text = cls.ToFullString();
                }
                else if (IsImplementationOf(cls, "IPacketSerializer") || cls.Identifier.Text.Contains("Serializer"))
                {
                    SerializerEditor.Text = cls.ToFullString();
                }
                else if (IsImplementationOf(cls, "IScriptContext") || cls.Identifier.Text.Contains("Context"))
                {
                    ContextEditor.Text = cls.ToFullString();
                }
            }

            bool IsImplementationOf(ClassDeclarationSyntax cls, string interfaceName)
            {
                if (cls.BaseList == null) return false;
                return cls.BaseList.Types.Any(t => t.Type.ToString().Contains(interfaceName));
            }
        }

        private string GetDefaultTemplate(string featureName)
        {
            return featureName switch
            {
                "PacketRegistry" =>
                    @"using System;
using System.Collections.Generic;
using ProtoTestTool.ScriptContract;

public class PacketRegistry : IPacketRegistry
{
    private readonly Dictionary<int, Type> _idToType = new Dictionary<int, Type>();
    private readonly Dictionary<Type, int> _typeToId = new Dictionary<Type, int>();

    public void Register(int id, Type type)
    {
        _idToType[id] = type;
        _typeToId[type] = id;
    }

    public IEnumerable<Type> GetMessageTypes() => _idToType.Values;

    public Type? GetMessageType(int id) => _idToType.TryGetValue(id, out var type) ? type : null;

    public int GetMessageId(Type type) => _typeToId.TryGetValue(type, out var id) ? id : 0;
}",
                "PacketSerializer" =>
                    @"using System;
using ProtoTestTool.ScriptContract;

public class PacketSerializer : IPacketSerializer
{
    public object Deserialize(byte[] buffer)
    {
        throw new NotImplementedException();
    }

    public byte[] Serialize(object packet)
    {
        throw new NotImplementedException();
    }

    public int GetHeaderSize()
    {
        return 4;
    }

    public int GetTotalLength(byte[] headerBuffer)
    {
        return BitConverter.ToInt32(headerBuffer, 0);
    }
}",
                "MyScriptContext" =>
                    @"using System;
using ProtoTestTool.ScriptContract;

public class MyScriptContext : IScriptContext
{
    public IPacketSerializer Serializer { get; } = new PacketSerializer();
    public IPacketRegistry Registry { get; } = new PacketRegistry();

    public void Initialize(IPacketRegistry registry, IPacketSerializer serializer)
    {
        // Example: registry.Register(1001, typeof(MyPacket));
    }

    private Action<string> _logger;
    public void SetLogger(Action<string> logAction)
    {
        _logger = logAction;
    }

    private void Log(string msg) => _logger?.Invoke(msg);
}",
                _ => "// Not found"
            };
        }

        private string SaveScriptFromTabs()
        {
            // Combine components
            // Strategy: Take all using directives from all tabs, deduplicate them at the top.
            // Then append the class bodies.
            // Then append the return new MyScriptContext();

            var combinedCode = new System.Text.StringBuilder();

            // 1. Gather Import
            // A simple approach: Just concat them all. C# can handle duplicate imports usually, 
            // but multiple `namespace` blocks or partials might be weird.
            // User snippet shows standard classes.

            // To allow users to just copy-paste entire files, we will just concatenate them with logic to hoist usings if we wanted to be fancy,
            // but for simplicity and robustness with standard copy-pastes, we can just append them.
            // HOWEVER, we need to ensure the final 'return' statement exists outside any class.

            combinedCode.AppendLine(RegistryEditor.Text);
            combinedCode.AppendLine();
            combinedCode.AppendLine(SerializerEditor.Text);
            combinedCode.AppendLine();
            combinedCode.AppendLine(ContextEditor.Text);
            combinedCode.AppendLine();

            // Final Return
            combinedCode.AppendLine("return new MyScriptContext();");

            var finalCode = combinedCode.ToString();

            return finalCode;
        }


        private async void ImportProtoBtn_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Proto files (*.proto)|*.proto",
                Title = "Proto 파일 선택 (Select Proto)"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                try
                {
                    ScriptLogBox.Text = $"가져오는 중 (Importing) {Path.GetFileName(openFileDialog.FileName)}...";
                    ScriptLogBox.Foreground = Brushes.Black;

                    var protoPath = openFileDialog.FileName;
                    var compiler = new ProtoCompiler();
                    var tempDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ProtoGen");
                    if (!Directory.Exists(tempDir)) Directory.CreateDirectory(tempDir);

                    compiler.CompileProtoToCSharp(protoPath, tempDir);

                    var csFiles = Directory.GetFiles(tempDir, "*.cs");
                    if (csFiles.Length == 0) throw new Exception("Proto에서 생성된 C# 파일이 없습니다.");

                    var generatedDlls = new List<string>();

                    foreach (var csFile in csFiles)
                    {
                        var dllPath = await _scriptLoader.CompileToDllAsync(csFile, null);
                        generatedDlls.Add(dllPath);
                    }

                    var registeredMessages = new List<string>();

                    foreach (var dllPath in generatedDlls)
                    {
                        var assembly = System.Reflection.Assembly.LoadFrom(dllPath);
                        var messageTypes = assembly.GetTypes()
                            .Where(t => typeof(Google.Protobuf.IMessage).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract);

                        foreach (var type in messageTypes)
                        {
                            var id = Math.Abs(type.Name.GetHashCode() % 10000) + 1000;
                            registeredMessages.Add($"        registry.Register({id}, typeof({type.FullName})); // Auto-generated ID");
                        }
                    }

                    if (registeredMessages.Count > 0)
                    {
                        var contextCode = ContextEditor.Text;
                        var methodSig = "Initialize(IPacketRegistry registry, IPacketSerializer serializer)";
                        var idx = contextCode.IndexOf(methodSig);
                        if (idx != -1)
                        {
                            var openBrace = contextCode.IndexOf('{', idx);
                            if (openBrace != -1)
                            {
                                var insertion = Environment.NewLine + "// Imported from " + Path.GetFileName(protoPath) + Environment.NewLine +
                                                string.Join(Environment.NewLine, registeredMessages);
                                contextCode = contextCode.Insert(openBrace + 1, insertion);
                                ContextEditor.Text = contextCode;
                            }
                        }

                        ScriptLogBox.Text += $"\n{registeredMessages.Count}개 메시지 가져오기 성공.\nScript Context에 추가됨.";
                        ScriptLogBox.Foreground = Brushes.Blue;
                    }
                    else
                    {
                        ScriptLogBox.Text += "\n생성된 코드에서 IMessage 타입을 찾을 수 없습니다.";
                        ScriptLogBox.Foreground = Brushes.Orange;
                    }
                }
                catch (Exception ex)
                {
                    ScriptLogBox.Text += $"\nProto 가져오기 오류:\n{ex.Message}";
                    ScriptLogBox.Foreground = Brushes.Red;
                }
            }
        }
    }
}