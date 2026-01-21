using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Collections.ObjectModel;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using RoslynPad.Editor;
using RoslynPad.Roslyn;
using ProtoTestTool.Network;
using ProtoTestTool.ScriptContract;

namespace ProtoTestTool
{
    public class ToolScriptLogger : IScriptLogger
    {
        private readonly Action<string, SolidColorBrush> _logAction;

        public ToolScriptLogger(Action<string, SolidColorBrush> logAction)
        {
            _logAction = logAction;
        }

        public void Info(string message) => _logAction($"[INFO] {message}", Brushes.White);
        public void Warn(string message) => _logAction($"[WARN] {message}", Brushes.Orange);
        public void Error(string message) => _logAction($"[ERROR] {message}", Brushes.Red);
    }

    public class ToolClientApi : IClientApi
    {
        private readonly MainWindow _window;

        public ToolClientApi(MainWindow window)
        {
            _window = window;
        }

        public void Send<TMessage>(TMessage message)
        {
            // Delegate to MainWindow's Send logic
            // Since we are moving to PacketConvertor/Codec, we should use that.
            // For now, let's just use the SendPacket functionality if exposed, 
            // or we might need to expose a method in MainWindow.
            _window.Dispatcher.Invoke(() =>
            {
                // Using reflection or checking type to send?
                // Spec says: Send<TMessage>(message).
                // Implementation: serialized -> send.

                // TODO: Connect this to actual Send logic.
                // _window.SendPacket(message); 
                // For now, logging intention.
                _window.AppendLog($"[ClientApi] Sending {message}", Brushes.LightGreen);
            });
        }

        public void Delay(int milliseconds)
        {
            System.Threading.Thread.Sleep(milliseconds);
        }
    }

    public partial class MainWindow
    {
        private ReverseProxyServer? _proxyServer;
        private IScriptStateStore? _scriptState;
        private IClientPacketInterceptor? _clientInterceptor;

        // Document IDs for Reference Updates
        private DocumentId? _docIdReg;
        private DocumentId? _docIdSer;
        private DocumentId? _docIdCtx;
        private DocumentId? _docIdHead;

        private async Task InitializeRoslynEditorAsync()
        {
            // 1. Packet Registry
            _docIdReg = _roslynService.Host.AddDocument(new DocumentCreationArgs(
                new StringTextSource("// Loading Registry..."), "PacketRegistry.csx", SourceCodeKind.Script)
            {
                WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory
            });
            await RegistryEditor.InitializeAsync(_roslynService.Host, new ClassificationHighlightColors(),
                AppDomain.CurrentDomain.BaseDirectory, _docIdReg.Id.ToString(), SourceCodeKind.Script);

            // 2. Packet Serializer
            _docIdSer = _roslynService.Host.AddDocument(new DocumentCreationArgs(
                new StringTextSource("// Loading Serializer..."), "PacketSerializer.csx", SourceCodeKind.Script)
            {
                WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory
            });
            await SerializerEditor.InitializeAsync(_roslynService.Host, new ClassificationHighlightColors(),
                AppDomain.CurrentDomain.BaseDirectory, _docIdSer.Id.ToString(), SourceCodeKind.Script);

            // 3. User Logic (Context)
            _docIdCtx = _roslynService.Host.AddDocument(new DocumentCreationArgs(
                new StringTextSource("// Loading Logic..."), "PacketHandler.csx", SourceCodeKind.Script)
            {
                WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory
            });
            await ContextEditor.InitializeAsync(_roslynService.Host, new ClassificationHighlightColors(),
                AppDomain.CurrentDomain.BaseDirectory, _docIdCtx.Id.ToString(), SourceCodeKind.Script);

            // 4. Request Headers Script
            _docIdHead = _roslynService.Host.AddDocument(new DocumentCreationArgs(
                new StringTextSource("// Headers Script"), "Headers.csx", SourceCodeKind.Script)
            {
                WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory
            });
            await HeaderScriptEditor.InitializeAsync(_roslynService.Host, new ClassificationHighlightColors(),
                AppDomain.CurrentDomain.BaseDirectory, _docIdHead.Id.ToString(), SourceCodeKind.Script);
            HeaderScriptEditor.Text = "// Headers[\"Authorization\"] = \"Bearer token\";\n";

            // Load Files or Defaults
            await LoadRegistryAsync();
            await LoadSerializerAsync();
            await LoadContextAsync();
            await LoadHeaderScriptAsync();
        }

        private async Task<string?> LoadScriptAsync(string fileName)
        {
            var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, fileName);
            if (File.Exists(path))
            {
                return await File.ReadAllTextAsync(path);
            }

            return null;
        }

        private async Task SaveAllSilentAsync()
        {
            // TODO: Make this truly silent (no dialog if file exists)
            // For now, simple direct write to Default Paths to avoid dialog hell.

            await File.WriteAllTextAsync(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PacketRegistry.csx"), RegistryEditor.Text);
            await File.WriteAllTextAsync(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PacketSerializer.csx"), SerializerEditor.Text);
            await File.WriteAllTextAsync(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PacketHandler.csx"), ContextEditor.Text);
            await File.WriteAllTextAsync(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Headers.csx"), HeaderScriptEditor.Text);
        }

        private async Task LoadRegistryAsync()
        {
            var code = await LoadScriptAsync("PacketRegistry.csx");
            if (string.IsNullOrWhiteSpace(code)) code = GetDefaultTemplate("PacketRegistry");
            RegistryEditor.Text = code;
        }

        private async Task LoadSerializerAsync()
        {
            var code = await LoadScriptAsync("PacketSerializer.csx");
            if (string.IsNullOrWhiteSpace(code)) code = GetDefaultTemplate("PacketSerializer");
            SerializerEditor.Text = code;
        }

        private async Task LoadContextAsync()
        {
            var code = await LoadScriptAsync("PacketHandler.csx");
            if (string.IsNullOrWhiteSpace(code)) code = GetDefaultTemplate("MyScriptContext");
            ContextEditor.Text = code;
        }

        private async Task LoadHeaderScriptAsync()
        {
            var code = await LoadScriptAsync("Headers.csx");
            if (string.IsNullOrWhiteSpace(code)) code = "// Headers[\"Authorization\"] = \"Bearer token\";\n";
            HeaderScriptEditor.Text = code;
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
        private void LoadRegistryBtn_Click(object sender, RoutedEventArgs e) => _ = LoadRegistryBtn_ClickAsync();
        private async Task LoadRegistryBtn_ClickAsync()
        {
            try
            {
                await LoadRegistryAsync();
            }
            catch (Exception ex)
            {
                AppendLog($"[Error] {ex.Message}", Brushes.Red);
            }
        }

        private void SaveRegistryBtn_Click(object sender, RoutedEventArgs e) => _ = SaveRegistryBtn_ClickAsync();
        private async Task SaveRegistryBtn_ClickAsync()
        {
            try
            {
                await SaveEditorToFileAsync(RegistryEditor, "PacketRegistry.csx");
            }
            catch (Exception ex)
            {
                AppendLog($"[Error] {ex.Message}", Brushes.Red);
            }
        }

        // -- Serializer --
        private void LoadSerializerBtn_Click(object sender, RoutedEventArgs e) => _ = LoadSerializerBtn_ClickAsync();
        private async Task LoadSerializerBtn_ClickAsync()
        {
            try
            {
                await LoadSerializerAsync();
            }
            catch (Exception ex)
            {
                AppendLog($"[Error] {ex.Message}", Brushes.Red);
            }
        }

        private void SaveSerializerBtn_Click(object sender, RoutedEventArgs e) => _ = SaveSerializerBtn_ClickAsync();
        private async Task SaveSerializerBtn_ClickAsync()
        {
            try
            {
                await SaveEditorToFileAsync(SerializerEditor, "PacketSerializer.csx");
            }
            catch (Exception ex)
            {
                AppendLog($"[Error] {ex.Message}", Brushes.Red);
            }
        }

        // -- Context --
        private void LoadContextBtn_Click(object sender, RoutedEventArgs e) => _ = LoadContextBtn_ClickAsync();
        private async Task LoadContextBtn_ClickAsync()
        {
            try
            {
                await LoadContextAsync();
            }
            catch (Exception ex)
            {
                AppendLog($"[Error] {ex.Message}", Brushes.Red);
            }
        }

        private void SaveContextBtn_Click(object sender, RoutedEventArgs e) => _ = SaveContextBtn_ClickAsync();
        private async Task SaveContextBtn_ClickAsync()
        {
            try
            {
                await SaveEditorToFileAsync(ContextEditor, "PacketHandler.csx");
            }
            catch (Exception ex)
            {
                AppendLog($"[Error] {ex.Message}", Brushes.Red);
            }
        }

        private async Task LoadFileToEditorAsync(RoslynPad.Editor.RoslynCodeEditor editor, string defaultName)
        {
            var openFileDialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "C# Script (*.csx)|*.csx|All files (*.*)|*.*",
                FileName = defaultName,
                Title = $"Load {defaultName}"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                // Logic to load
                editor.Text = await File.ReadAllTextAsync(openFileDialog.FileName);
                ScriptLogBox.Text += $"\nLoaded {Path.GetFileName(openFileDialog.FileName)}.";
            }
        }

        private async Task SaveEditorToFileAsync(RoslynPad.Editor.RoslynCodeEditor editor, string defaultName)
        {
            var saveFileDialog = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "C# Script (*.csx)|*.csx|All files (*.*)|*.*",
                FileName = defaultName,
                Title = $"Save {defaultName}"
            };

            if (saveFileDialog.ShowDialog() == true)
            {
                await File.WriteAllTextAsync(saveFileDialog.FileName, editor.Text);
                ScriptLogBox.Text += $"\nSaved {Path.GetFileName(saveFileDialog.FileName)}.";
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


        private void LoadFileToEditor(string fileName)
        {
            // Deprecated in favor of LoadAllFilesAsync
        }

        private void SaveScript_Click(object sender, RoutedEventArgs e) => _ = SaveScriptFromTabsAsync();

        private void SaveAsScript_Click(object sender, RoutedEventArgs e) => _ = SaveScriptFromTabsAsync(forceSaveAs: true);

        private async Task SaveScriptFromTabsAsync(bool forceSaveAs = false)
        {
            try
            {
                var selectedTab = MethodTabs.SelectedItem as TabItem;
                var header = selectedTab?.Header as string ?? "";

                RoslynPad.Editor.RoslynCodeEditor? editor = null;
                string defaultName = "";

                if (header.Contains("Packet Registry"))
                {
                    editor = RegistryEditor;
                    defaultName = "PacketRegistry.csx";
                }
                else if (header.Contains("Packet Serializer"))
                {
                    editor = SerializerEditor;
                    defaultName = "PacketSerializer.csx";
                }
                else if (header.Contains("Packet Handler"))
                {
                    editor = ContextEditor;
                    defaultName = "PacketHandler.csx";
                }

                if (editor != null)
                {
                    await SaveEditorToFileAsync(editor, defaultName);
                }
            }
            catch (Exception ex)
            {
                AppendLog($"[Error] SaveScript: {ex.Message}", Brushes.Red);
            }
        }

        private void CompileScriptBtn_Click(object sender, RoutedEventArgs e) => _ = CompileScriptBtn_ClickAsync();
        private async Task CompileScriptBtn_ClickAsync()
        {
            try
            {
                // Auto-save before compile
                await SaveAllSilentAsync();

                var selectedTab = MethodTabs.SelectedItem as TabItem;
                var header = selectedTab?.Header as string ?? "";

                // Note: Header names might be localized now, e.g. "Packet Registry (패킷 등록)"
                // Use Contains to be safe
                // Logging Helper
                Action<string> uiLogger = (msg) => 
                {
                    Dispatcher.Invoke(() => 
                    {
                        ScriptLogBox.Text += $"\n{msg}";
                        ScriptLogBox.ScrollToEnd();
                    });
                };

                 if (header.Contains("Packet Registry") || header.Contains("Packet Serializer"))
                {
                    // Validate only the current code
                    bool isRegistry = header.Contains("Packet Registry");
                    string code = isRegistry ? RegistryEditor.Text : SerializerEditor.Text;
                    string filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, isRegistry ? "PacketRegistry.csx" : "PacketSerializer.csx");
                    string regDllPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PacketRegistry.dll");
                    
                    ScriptLogBox.Text = $"검증 중 (Validating)...";
                    ScriptLogBox.Foreground = Brushes.White;

                    await Task.Run(() =>
                    {
                        var extraRefs = new List<string>();
                        if (header.Contains("Packet Serializer") && File.Exists(regDllPath)) 
                        {
                            extraRefs.Add(regDllPath);
                        }

                        var (diagnostics, resolvedRefs) = _scriptLoader.ValidateScript(code, filePath, uiLogger, extraRefs);
                        
                        // Update Editor References
                        if (resolvedRefs != null && _roslynService != null)
                        {
                            var targetDocId = isRegistry ? _docIdReg : _docIdSer;
                            if (targetDocId != null)
                            {
                                // Dispatch to UI thread if RoslynHost requires it? No, Workspace updates are thread safe usually.
                                // But let's be safe or just call it.
                                foreach (var refPath in resolvedRefs)
                                {
                                     _roslynService.AddReference(targetDocId, refPath);
                                }
                                foreach (var extra in extraRefs)
                                {
                                    _roslynService.AddReference(targetDocId, extra);
                                }
                            }
                        }

                        var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();

                        Dispatcher.Invoke(() =>
                        {
                            if (errors.Count > 0)
                            {
                                var errorMsg = string.Join(Environment.NewLine, errors.Select(d => $"Line {d.Location.GetLineSpan().StartLinePosition.Line + 1}: {d.GetMessage()}"));
                                ScriptLogBox.Text += $"\n[{DateTime.Now:HH:mm:ss}] 검증 실패 (Validation Failed):\n{errorMsg}";
                                ScriptLogBox.Foreground = Brushes.Red;
                            }
                            else
                            {
                                ScriptLogBox.Text += $"\n[{DateTime.Now:HH:mm:ss}] 검증 성공 (Validation Success)!";
                                ScriptLogBox.Foreground = Brushes.DeepSkyBlue;
                            }
                        });
                    });
                }
                else
                {
                    // For Context, we run the full compilation (Integration Test)
                    await CompileScriptAsync(uiLogger);
                }
            }
            catch (Exception ex)
            {
                ScriptLogBox.Text += $"\n[Error] {ex.GetType().Name}: {ex.Message}";
                ScriptLogBox.Foreground = Brushes.Red;
                AppendLog($"[Error] CompileScript: {ex.Message}", Brushes.Red);
            }
        }


        private System.Reflection.Assembly? _scriptAssembly;

        private async Task CompileScriptAsync(Action<string>? logger = null)
        {
            ScriptLogBox.Text = "스크립트 체인 컴파일 중... (Compiling)";
            ScriptLogBox.Foreground = Brushes.White;

            try
            {
                var dir = AppDomain.CurrentDomain.BaseDirectory;

                // 1. Compile Registry
                var regPath = Path.Combine(dir, "PacketRegistry.csx");
                if (!File.Exists(regPath)) throw new FileNotFoundException("PacketRegistry.csx 없음");

                ScriptLogBox.Text += "\nPacket Registry 컴파일 중...";
                var regDllPath = await _scriptLoader.CompileToDllAsync(regPath, null, logger);

                // 2. Compile Serializer
                var serPath = Path.Combine(dir, "PacketSerializer.csx");
                if (!File.Exists(serPath)) throw new FileNotFoundException("PacketSerializer.csx 없음");

                ScriptLogBox.Text += "\nPacket Serializer 컴파일 중...";
                var serDllPath = await _scriptLoader.CompileToDllAsync(serPath, null, logger);

                // 3. Compile Context (User Logic)
                var mainPath = Path.Combine(dir, "PacketHandler.csx");
                var buildPath = Path.Combine(dir, "PacketHandler.Build.csx");

                if (!File.Exists(mainPath))
                {
                    ContextEditor.Text = GetDefaultTemplate("MyScriptContext");
                    await File.WriteAllTextAsync(mainPath, ContextEditor.Text);
                }

                var mainCode = await File.ReadAllTextAsync(mainPath);
                // Remove #load
                var lines = mainCode.Split(new[] {"\r\n", "\n"}, StringSplitOptions.None);
                var cleanLines = lines.Where(l => !l.TrimStart().StartsWith("#load")).ToArray();
                await File.WriteAllLinesAsync(buildPath, cleanLines);

                ScriptLogBox.Text += "\nUser Logic 컴파일 중...";

                // Collect References
                var protoGenDir = Path.Combine(dir, "ProtoGen");
                var protoRefs = new List<string> {regDllPath, serDllPath};

                if (Directory.Exists(protoGenDir))
                {
                    var protoDlls = Directory.GetFiles(protoGenDir, "*.dll", SearchOption.AllDirectories);
                    protoRefs.AddRange(protoDlls);
                }

                // Load Registry & Codec Instances early to Init Globals
                var regAssembly = System.Reflection.Assembly.Load(File.ReadAllBytes(regDllPath));
                var regType = regAssembly.GetTypes().FirstOrDefault(t => typeof(IPacketRegistry).IsAssignableFrom(t) && !t.IsAbstract && !t.IsInterface);
                if (regType == null) throw new Exception("IPacketRegistry 구현체를 찾을 수 없습니다.");

                // Try to inject logger if constructor supports it
                IPacketRegistry registry;
                var ctorWithLogger = regType.GetConstructor(new[] {typeof(Action<string>)});
                if (ctorWithLogger != null)
                {
                    registry = (IPacketRegistry) ctorWithLogger.Invoke(new object[] {(Action<string>) ((msg) => Dispatcher.Invoke(() => AppendLog(msg)))});
                }
                else
                {
                    registry = (IPacketRegistry) Activator.CreateInstance(regType)!;
                }

                var serAssembly = System.Reflection.Assembly.Load(File.ReadAllBytes(serDllPath));
                var serType = serAssembly.GetTypes().FirstOrDefault(t => typeof(IPacketCodec).IsAssignableFrom(t) && !t.IsAbstract && !t.IsInterface);
                if (serType == null) throw new Exception("IPacketCodec 구현체를 찾을 수 없습니다.");
                var codec = (IPacketCodec) Activator.CreateInstance(serType)!;

                // Init Globals
                // Ensure StateStore exists
                if (_scriptState == null) _scriptState = new ScriptStateStore();

                var toolLogger = new ToolScriptLogger((msg, color) => { Dispatcher.Invoke(() => AppendLog(msg, color)); });
                var clientApi = new ToolClientApi(this);

                ScriptGlobals.Initialize(_scriptState, toolLogger);
                ScriptGlobals.SetApis(clientApi, null); // ProxyApi not yet implemented globally
                ScriptGlobals.SetServices(registry, codec);

                // Load User Logic
                var contextAssembly = await _scriptLoader.LoadScriptWithReferencesAsync(buildPath, protoRefs, logger);
                _scriptAssembly = contextAssembly;

                // Find Interceptors
                var clientInterceptorType = _scriptAssembly.GetTypes().FirstOrDefault(t => typeof(IClientPacketInterceptor).IsAssignableFrom(t) && !t.IsAbstract && !t.IsInterface);
                if (clientInterceptorType != null)
                {
                    _clientInterceptor = (IClientPacketInterceptor) Activator.CreateInstance(clientInterceptorType)!;
                }
                else
                {
                    _clientInterceptor = null;
                }

                // Proxy Interceptor is mostly for ProxySession, which needs to find it per connection or prototype.
                // We'll leave Proxy logic as is for now (StartProxyServer finds IProxyPacketInterceptor).

                ScriptLogBox.Text += $"\n[{DateTime.Now:HH:mm:ss}] Compilation Success!";
                ScriptLogBox.Foreground = Brushes.DeepSkyBlue;

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
            if (ScriptGlobals.Registry == null) return;

            var types = ScriptGlobals.Registry.GetMessageTypes()
                .OrderBy(t => t.Name)
                .ToList();

            PacketListBox.ItemsSource = types;
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
using System.Buffers;
using ProtoTestTool.ScriptContract;

public class PacketCodec : IPacketCodec
{
    // Default: 4-byte Length + Payload
    // Format: [Length(4)][MsgId(4)][Payload...]
    
    public bool TryDecode(ref ReadOnlySequence<byte> buffer, out object? message)
    {
        message = null;
        if (buffer.Length < 4) return false;

        var set = buffer.Slice(0, 4);
        Span<byte> header = stackalloc byte[4];
        set.CopyTo(header);
        var len = BitConverter.ToInt32(header);

        if (buffer.Length < 4 + len) return false;

        // Check Registry for MsgId (Assumes [Len][MsgId][Protobuf])
        var payloadSeq = buffer.Slice(4, len);
        
        // Example: Peeking MsgId at offset 4 (after length)
        if (payloadSeq.Length >= 4)
        {
             var msgIdSeq = payloadSeq.Slice(0, 4);
             Span<byte> msgIdBytes = stackalloc byte[4];
             msgIdSeq.CopyTo(msgIdBytes);
             int msgId = BitConverter.ToInt32(msgIdBytes);
             
             // TODO: Use a Registry if available or Reflection lookup
             // System.Console.WriteLine($""MsgId: {msgId}"");
        }

        // Return raw wrapper for now or implement ProtoParser call
        message = new { RawSize = len, Data = payloadSeq.ToArray() };

        buffer = buffer.Slice(4 + len);
        return true;
    }

    public ReadOnlyMemory<byte> Encode(object message)
    {
        // Placeholder
        return new byte[4]; 
    }
}",
                "MyScriptContext" =>
                    @"using System;
using ProtoTestTool.ScriptContract;
using System.Threading.Tasks;

// ****************************************
// *      USER IMPLEMENTATION AREA        *
// ****************************************

public class MyInterceptor : IProxyPacketInterceptor
{
    public ValueTask OnInboundAsync(ProxyPacketContext context)
    {
        // Example: Count packets using State
        if (ScriptGlobals.State.TryGet<int>(""PacketCount"", out var count))
        {
            ScriptGlobals.State.Set(""PacketCount"", count + 1);
        }
        else
        {
            ScriptGlobals.State.Set(""PacketCount"", 1);
        }

        ScriptGlobals.Log.Info($""[Script] Packet Received. Count: {ScriptGlobals.State.Get<int>(""PacketCount"")}"");
        
        return ValueTask.CompletedTask;
    }

    public ValueTask OnOutboundAsync(ProxyPacketContext context)
    {
        return ValueTask.CompletedTask;
    }
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

        #region Proto Manager

        private ObservableCollection<string> _loadedProtoFiles = new ObservableCollection<string>();
        // Note: _loadedMessageTypes would normally be derived from registry, 
        // but here we can track what we just imported.

        private void LoadProtoFileBtn_Click(object sender, RoutedEventArgs e) => _ = LoadProtoFileBtn_ClickAsync();
        private async Task LoadProtoFileBtn_ClickAsync()
        {
            try
            {
                var openFileDialog = new Microsoft.Win32.OpenFileDialog
                {
                    Filter = "Proto files (*.proto)|*.proto",
                    Title = "Proto 파일 선택 (Select Proto)",
                    Multiselect = true
                };

                if (openFileDialog.ShowDialog() == true)
                {
                    await ProcessProtosAsync(openFileDialog.FileNames);
                }
            }
            catch (Exception ex)
            {
                AppendLog($"[Error] LoadProtoFile: {ex.Message}", Brushes.Red);
            }
        }

        private void LoadProtoFolderBtn_Click(object sender, RoutedEventArgs e) => _ = LoadProtoFolderBtn_ClickAsync();
        private async Task LoadProtoFolderBtn_ClickAsync()
        {
            try
            {
                var dialog = new Microsoft.Win32.OpenFolderDialog
                {
                    Title = "Proto 폴더 선택 (Select Folder)"
                };

                if (dialog.ShowDialog() == true)
                {
                    var files = Directory.GetFiles(dialog.FolderName, "*.proto", SearchOption.AllDirectories);
                    if (files.Length == 0)
                    {
                        ProtoLogBox.Text += $"\n[Manager] No .proto files found in {dialog.FolderName}";
                        return;
                    }

                    await ProcessProtosAsync(files);
                }
            }
            catch (Exception ex)
            {
                AppendLog($"[Error] LoadProtoFolder: {ex.Message}", Brushes.Red);
            }
        }

        private void ReloadProtoBtn_Click(object sender, RoutedEventArgs e) => _ = ReloadProtoBtn_ClickAsync();
        private async Task ReloadProtoBtn_ClickAsync()
        {
            try
            {
                if (_loadedProtoFiles.Count == 0)
                {
                    ProtoLogBox.Text += "\n[Manager] No files to reload.";
                    return;
                }

                await ProcessProtosAsync(_loadedProtoFiles.ToArray());
            }
            catch (Exception ex)
            {
                AppendLog($"[Error] ReloadProto: {ex.Message}", Brushes.Red);
            }
        }

        private async Task ProcessProtosAsync(string[] protoFiles)
        {
            try
            {
                ProtoLogBox.Text += $"\n[Manager] Processing {protoFiles.Length} files...";
                ProtoLogBox.ScrollToEnd();
                AppendLog($"[Proto] Processing {protoFiles.Length} files...", Brushes.MediumPurple);

                var tempDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ProtoGen");
                if (!Directory.Exists(tempDir)) Directory.CreateDirectory(tempDir);

                var compiler = new ProtoCompiler();
                var generatedDlls = new List<string>();
                var registeredMessages = new List<string>();
                var messageDisplayList = new List<string>();

                foreach (var protoPath in protoFiles)
                {
                    if (!_loadedProtoFiles.Contains(protoPath)) _loadedProtoFiles.Add(protoPath);

                    try
                    {
                        ProtoLogBox.Text += $"\n  - Compiling {Path.GetFileName(protoPath)}...";
                        compiler.CompileProtoToCSharp(protoPath, tempDir);

                        // Find generated CS
                        // Note: Potentially multiple files or mapped names.
                        // Simple heuristic: compile all *.cs in tempDir?
                        // No, that might duplicate. 
                        // The compiler output is predictable though.
                        // For Prototype, let's compile ALL .cs in ProtoGen (accumulative) or clean it?
                        // If we clean, we lose previous imports.
                        // So we should Compile ALL .cs found in ProtoGen to be safe, or just the new ones.
                        // Let's assume ProtoGen acts as a cache.
                        // Ideally we compile the specific output.
                    }
                    catch (Exception ex)
                    {
                        ProtoLogBox.Text += $"\n[Error] {Path.GetFileName(protoPath)}: {ex.Message}";
                        AppendLog($"[Proto Error] {Path.GetFileName(protoPath)}: {ex.Message}", Brushes.Red);
                    }
                }

                // Compile C# -> DLLs
                var csFiles = Directory.GetFiles(tempDir, "*.cs");
                foreach (var csFile in csFiles)
                {
                    try
                    {
                        var dllPath = await _scriptLoader.CompileToDllAsync(csFile, null);
                        generatedDlls.Add(dllPath); // Dedup?
                    }
                    catch (Exception ex)
                    {
                        ProtoLogBox.Text += $"\n[Warn] CS Compile Fail {Path.GetFileName(csFile)}: {ex.Message}";
                    }
                }

                // Load and Scan for Messages
                foreach (var dllPath in generatedDlls.Distinct())
                {
                    try
                    {
                        var assembly = System.Reflection.Assembly.Load(File.ReadAllBytes(dllPath));
                        var messageTypes = assembly.GetTypes()
                            .Where(t => typeof(Google.Protobuf.IMessage).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract);

                        foreach (var type in messageTypes) 
                            ProtoLoaderManager.Instance.RegisterPacket(type);
                    }
                    catch
                    {
                        /* Ignore load errors */
                    }
                }

                // Update UI (Proto Manager List)
                ProtoFileListBox.ItemsSource = _loadedProtoFiles;
                ProtoMessageListBox.ItemsSource = messageDisplayList;

                // Update UI (Test Client List)
                // Need access to PacketListBox. Since this is partial class of MainWindow, we can access it.
                // Re-bind PacketListBox from ProtoLoaderManager
                PacketListBox.ItemsSource = ProtoLoaderManager.Instance.GetSendPackets();


                // Update Registry Editor
                if (registeredMessages.Count > 0)
                {
                    var code = RegistryEditor.Text;
                    var linesToAdd = new List<string>();
                    foreach (var line in registeredMessages)
                    {
                        if (!code.Contains(line.Trim()))
                        {
                            linesToAdd.Add(line);
                        }
                    }

                    if (linesToAdd.Count > 0)
                    {
                        var insertIdx = code.LastIndexOf('}'); // Class closing
                        if (insertIdx > 0)
                        {
                            insertIdx = code.LastIndexOf('}', insertIdx - 1); // Method closing
                        }

                        if (insertIdx > 0)
                        {
                            var newCode = code.Insert(insertIdx, string.Join(Environment.NewLine, linesToAdd) + Environment.NewLine);
                            RegistryEditor.Text = newCode;
                            ProtoLogBox.Text += $"\n[Manager] Added {linesToAdd.Count} types to Registry.";
                            AppendLog($"[Proto] Added {linesToAdd.Count} types to Registry.", Brushes.MediumPurple);
                        }
                        else
                        {
                            ProtoLogBox.Text += "\n[Warn] Could not auto-update RegistryEditor. Please add lines manually.";
                        }
                    }
                    else
                    {
                        ProtoLogBox.Text += "\n[Manager] Registry already up-to-date.";
                    }
                }

                ProtoLogBox.ScrollToEnd();
            }
            catch (Exception ex)
            {
                ProtoLogBox.Text += $"\n[Critical Error] {ex.Message}";
            }
        }

        #endregion

        private void ProxyStartBtn_Click(object sender, RoutedEventArgs e) => _ = ProxyStartBtn_ClickAsync();
        private async Task ProxyStartBtn_ClickAsync()
        {
            if (_proxyServer != null && _proxyServer.IsStarted)
            {
                // Stop Proxy
                _proxyServer.Stop();
                _proxyServer.Dispose();
                _proxyServer = null;
                ProxyStartBtn.Content = "프록시 시작 (Start Proxy)";
                AppendProxyLog("Proxy Stopped.");
                return;
            }

            // Start Proxy
            if (_scriptAssembly == null)
            {
                MessageBox.Show("스크립트를 먼저 컴파일해 주세요 (Compile First).");
                return;
            }

            if (!int.TryParse(ProxyLocalPortBox.Text, out var localPort) ||
                !int.TryParse(ProxyTargetPortBox.Text, out var targetPort))
            {
                MessageBox.Show("포트 번호가 올바르지 않습니다.");
                return;
            }

            var targetIp = ProxyTargetIpBox.Text;

            try
            {
                await StartProxyServerAsync(localPort, targetIp, targetPort);
                ProxyStartBtn.Content = "프록시 중지 (Stop Proxy)";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"프록시 시작 실패: {ex.Message}");
                AppendProxyLog($"Start Failed: {ex.Message}");
            }
        }

        
        private Task StartProxyServerAsync(int localPort, string targetIp, int targetPort)
        {
            return Task.Run(() =>
            {
                try
                {
                    var assembly = _scriptAssembly;
                    if (assembly == null) throw new Exception("Script assembly not loaded.");

                    // 1. Get Codec from Globals
                    if (ScriptGlobals.Codec == null) throw new Exception("IPacketCodec이 초기화되지 않았습니다. (Compile First)");
                    var codec = ScriptGlobals.Codec;

                    // 2. Find Interceptors
                    var pipeline = new ProxyInterceptorPipeline();
                    var interceptorTypes = assembly.GetTypes()
                        .Where(t => typeof(IProxyPacketInterceptor).IsAssignableFrom(t) && !t.IsAbstract)
                        .ToList();

                    // Update UI List
                    Dispatcher.Invoke(() =>
                    {
                        InterceptorListBox.Items.Clear();
                        foreach (var t in interceptorTypes)
                            InterceptorListBox.Items.Add(t.Name);
                    });

                    foreach (var t in interceptorTypes)
                    {
                        var interceptor = (IProxyPacketInterceptor) Activator.CreateInstance(t)!;
                        pipeline.Add(interceptor);
                    }

                    // 3. Create Server
                    _proxyServer = new ReverseProxyServer("0.0.0.0", localPort, targetIp, targetPort, pipeline, codec);
                    _proxyServer.Start();

                    Dispatcher.Invoke(() => AppendProxyLog($"Proxy Started on {localPort} -> {targetIp}:{targetPort}"));
                }
                catch (Exception ex)
                {
                    Dispatcher.Invoke(() => AppendProxyLog($"Error starting proxy: {ex.Message}"));
                    throw;
                }
            });
        }


        private void AppendProxyLog(string msg)
        {
            // Simple text append
            ProxyLogBox.Text += $"[{DateTime.Now:HH:mm:ss}] {msg}\n";
            ProxyLogBox.ScrollToEnd();
        }
    }
}