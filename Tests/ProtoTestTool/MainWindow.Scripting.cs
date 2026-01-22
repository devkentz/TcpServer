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





        public async Task CompileScriptsAsync(string dir, Action<string, Brush> logAction)
        {
            if (string.IsNullOrEmpty(dir)) return;

            try
            {
                logAction("Starting Compilation (from Disk)...", Brushes.White);

                // 1. Compile Registry
                var regPath = Path.Combine(dir, "PacketRegistry.csx");
                if (!File.Exists(regPath)) throw new FileNotFoundException("PacketRegistry.csx 없음");

                logAction("Packet Registry 컴파일 중...", Brushes.White);
                
                var regDllPath = await _scriptLoader.CompileToDllAsync(regPath, new string[0], (msg) => logAction(msg, Brushes.Gray));

                // 1.5 Compile Header
                var headerPath = Path.Combine(dir, "PacketHeader.csx");
                string headerDllPath = "";
                if (File.Exists(headerPath))
                {
                     logAction("Packet Header 컴파일 중...", Brushes.White);
                     headerDllPath = await _scriptLoader.CompileToDllAsync(headerPath, new [] { regDllPath }, (msg) => logAction(msg, Brushes.Gray));
                }

                // 2. Compile Serializer
                var serPath = Path.Combine(dir, "PacketSerializer.csx");
                if (!File.Exists(serPath)) throw new FileNotFoundException("PacketSerializer.csx 없음");

                logAction("Packet Serializer 컴파일 중...", Brushes.White);
                var serRefs = new List<string> { regDllPath };
                if (!string.IsNullOrEmpty(headerDllPath)) serRefs.Add(headerDllPath);
                
                var serDllPath = await _scriptLoader.CompileToDllAsync(serPath, serRefs, (msg) => logAction(msg, Brushes.Gray));

                // 3. Compile Context (User Logic)
                var mainPath = Path.Combine(dir, "PacketHandler.csx");
                var buildPath = Path.Combine(dir, "PacketHandler.Build.csx");

                if (!File.Exists(mainPath))
                {
                    CreateIfMissing(dir, "PacketHandler.csx", "MyScriptContext");
                }

                var mainCode = await File.ReadAllTextAsync(mainPath);
                // Remove #load
                var lines = mainCode.Split(new[] {"\r\n", "\n"}, StringSplitOptions.None);
                var cleanLines = lines.Where(l => !l.TrimStart().StartsWith("#load")).ToArray();
                await File.WriteAllLinesAsync(buildPath, cleanLines);

                logAction("User Logic 컴파일 중...", Brushes.White);

                // Collect References
                var protoGenDir = Path.Combine(dir, "ProtoGen");
                var protoRefs = new List<string> {regDllPath, serDllPath};
                if (!string.IsNullOrEmpty(headerDllPath)) protoRefs.Add(headerDllPath);

                if (Directory.Exists(protoGenDir))
                {
                    var protoDlls = Directory.GetFiles(protoGenDir, "*.dll", SearchOption.AllDirectories);
                    protoRefs.AddRange(protoDlls);
                }

                // Load Registry & Codec Instances
                var regAssembly = System.Reflection.Assembly.LoadFrom(regDllPath);
                var regType = regAssembly.GetTypes().FirstOrDefault(t => typeof(IPacketRegistry).IsAssignableFrom(t) && !t.IsAbstract && !t.IsInterface);
                if (regType == null) throw new Exception($"IPacketRegistry implementation not found in {regDllPath}");
                var registry = (IPacketRegistry)Activator.CreateInstance(regType)!;

                var serAssembly = System.Reflection.Assembly.LoadFrom(serDllPath);
                var codecType = serAssembly.GetTypes().FirstOrDefault(t => typeof(IPacketCodec).IsAssignableFrom(t) && !t.IsAbstract && !t.IsInterface);
                if (codecType == null) throw new Exception($"IPacketCodec implementation not found in {serDllPath}");
                var codec = (IPacketCodec)Activator.CreateInstance(codecType)!;

                // Init Globals
                if (_scriptState == null) _scriptState = new ScriptStateStore();

                // Runtime Logger (still routes to Main LogBox via dispatcher, kept as is or passed?)
                // The runtime logger is for "during execution". 
                // We'll keep the current behavior: toolLogger writes to Main LogBox.
                // The compilation logger (passed in) writes to ScriptEditor LogBox.
                var toolLogger = new ToolScriptLogger((msg, color) => { Dispatcher.Invoke(() => AppendLog(msg, color)); });
                var clientApi = new ToolClientApi(this);

                ScriptGlobals.Initialize(_scriptState, toolLogger);
                ScriptGlobals.SetApis(clientApi, null);
                ScriptGlobals.SetServices(registry, codec);

                // Load User Logic
                Action<string> compileLogger = (msg) => logAction(msg, Brushes.Gray);
                var contextAssembly = await _scriptLoader.LoadScriptWithReferencesAsync(buildPath, protoRefs, compileLogger);
                
                _scriptAssembly = contextAssembly;

                if (!string.IsNullOrEmpty(headerDllPath))
                {
                    _headerAssembly = System.Reflection.Assembly.LoadFrom(headerDllPath);
                }

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

                logAction("Compilation Success!", Brushes.DeepSkyBlue);
                RefreshPacketList();
            }
            catch (Exception ex)
            {
                logAction($"Error:\n{ex.Message}", Brushes.Red);
            }
        }


        private System.Reflection.Assembly? _scriptAssembly;
        private System.Reflection.Assembly? _headerAssembly;



        private void RefreshPacketList()
        {
            PacketListBox.ItemsSource = null;
            if (ScriptGlobals.Registry == null) return;

            var types = ScriptGlobals.Registry.GetMessageTypes()
                .OrderBy(t => t.Name)
                .ToList();

            PacketListBox.ItemsSource = types;
        }





        public void InitializeWorkspaceFiles(string workspacePath)
        {
            if (string.IsNullOrWhiteSpace(workspacePath) || !Directory.Exists(workspacePath)) return;

            CreateIfMissing(workspacePath, "PacketRegistry.csx", "PacketRegistry");
            CreateIfMissing(workspacePath, "PacketHeader.csx", "PacketHeader");
            CreateIfMissing(workspacePath, "PacketSerializer.csx", "PacketSerializer");
            CreateIfMissing(workspacePath, "PacketHandler.csx", "MyScriptContext");
            
            // Create default config
            var configPath = Path.Combine(workspacePath, "workspace_config.json");
            if (!File.Exists(configPath))
            {
                var defaultConfig = new WorkspaceConfig(); // Defaults: 127.0.0.1:9000
                defaultConfig.Save(workspacePath);
                Dispatcher.Invoke(() => AppendLog($"[Workspace] Created workspace_config.json", Brushes.Green));
            }
        }



        private void CreateIfMissing(string dir, string fileName, string templateName)
        {
            var path = Path.Combine(dir, fileName);
            if (!File.Exists(path))
            {
                try
                {
                    File.WriteAllText(path, GetDefaultTemplate(templateName));
                    Dispatcher.Invoke(() => AppendLog($"[Workspace] Created {fileName}", Brushes.Green));
                }
                catch (Exception ex)
                {
                    Dispatcher.Invoke(() => AppendLog($"[Error] Failed to create {fileName}: {ex.Message}", Brushes.Red));
                }
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
    public int GetMessageId(Type type) => _typeToId.TryGetValue(type, out var id) ? id : 0;
}",
                "PacketHeader" =>
                    @"using System;
using Newtonsoft.Json;
using ProtoTestTool.ScriptContract;

public class Header : IHeader
{
    public int MsgId { get; set; }
    public byte Flags { get; set; }
    
    public string ToJsonString()
    {
        return JsonConvert.SerializeObject(this);
    }
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

                    _protoFolderPath = dialog.FolderName;
                    SaveWorkspaceConfiguration();

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
                FluentMessageBox.ShowError("스크립트를 먼저 컴파일해 주세요.");
                return;
            }

            if (!int.TryParse(ProxyLocalPortBox.Text, out var localPort) ||
                !int.TryParse(ProxyTargetPortBox.Text, out var targetPort))
            {
                FluentMessageBox.ShowError("포트 번호가 올바르지 않습니다.");
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
                FluentMessageBox.ShowError($"프록시 시작 실패: {ex.Message}");
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