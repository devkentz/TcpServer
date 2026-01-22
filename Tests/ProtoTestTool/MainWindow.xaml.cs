using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.IO;
using Newtonsoft.Json;
using ProtoTestTool.Network;
using ProtoTestTool.ScriptContract;
using System.Collections.ObjectModel;
using Google.Protobuf;
using Google.Protobuf.Reflection;


namespace ProtoTestTool
{
    public partial class MainWindow : Wpf.Ui.Controls.UiWindow
    {
        private SimpleTcpClient? _client;
        private readonly ScriptLoader _scriptLoader = new();
        private readonly List<byte> _receiveBuffer = new();

        // Roslyn
        // private readonly RoslynService _roslynService;
        private ScriptEditorWindow? _scriptEditorWindow;

        // Editor State
        private string _currentEditingFile = "";

        // Workspace
        private string _workspacePath = "";
        private const string SettingsFileName = "prototesttool.settings.json";

        // UI Binding Models
        public class KeyValueItem
        {
            public string Key { get; set; } = string.Empty;
            public string Value { get; set; } = string.Empty;
        }

        public class HeaderScriptGlobals
        {
            public Dictionary<string, string> Headers { get; set; } = new Dictionary<string, string>();
        }

        private ObservableCollection<KeyValueItem> _requestHeaders = new ObservableCollection<KeyValueItem>(); // Kept for compilation safety until removed
        private ObservableCollection<KeyValueItem> _responseHeaders = new ObservableCollection<KeyValueItem>();

        public MainWindow()
        {
            InitializeComponent();
            Closing += MainWindow_Closing;

            // Initialize Globals with dummy logger for startup (real logger injected on compilation)
            ScriptContract.ScriptGlobals.Initialize(
                new ScriptContract.ScriptStateStore(),
                new ToolScriptLogger((msg, color) => { /* Startup Log */ })
            );

            // _roslynService = new RoslynService(); (Removed)

            // Load Workspace Settings
            LoadWorkspaceSettings();

            // Use Loaded event for async initialization (safer than constructor)
            Loaded += MainWindow_Loaded;

            // Load existing Protos
            Network.ProtoLoaderManager.Instance.LoadAllProtos();
            PacketListBox.ItemsSource = Network.ProtoLoaderManager.Instance.SendPackets.Values;

            // Bind Headers
            // RequestHeaderGrid removed (replaced by HeaderScriptEditor)
            ResponseHeaderGrid.ItemsSource = _responseHeaders;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e) => _ = MainWindow_LoadedAsync();
        private async Task MainWindow_LoadedAsync()
        {
            try
            {
                // Initialize Roslyn Editor (Removed for Monaco migration)
                // if (_roslynService?.Host != null)
                // {
                //    ProtoSourceViewer is now a TextBox
                // }
                // Show Workspace dialog if not set
                if (string.IsNullOrWhiteSpace(_workspacePath) || !Directory.Exists(_workspacePath))
                {
                    ShowWorkspaceDialog();
                }


            }
            catch (Exception ex)
            {
                FluentMessageBox.ShowError($"Editor Init Failed: {ex.Message}");
            }
        }

        #region Workspace Management
        private void WorkspaceBtn_Click(object sender, RoutedEventArgs e)
        {
            ShowWorkspaceDialog();
        }

        private void ShowWorkspaceDialog()
        {
            var dialog = new WorkspaceDialog(_workspacePath)
            {
                Owner = this
            };

            if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.SelectedPath))
            {
                _workspacePath = dialog.SelectedPath;
                // Save workspace to Global Settings is done inside Dialog.SelectWorkspace
                // But we still persist specific settings if needed:
                SaveWorkspaceSettings(); 
                
                InitializeWorkspaceFiles(_workspacePath);
                LoadWorkspaceConfiguration(_workspacePath);
                UpdateWorkspaceUI();
                AppendLog($"Workspace Loaded: {_workspacePath}", Brushes.DeepSkyBlue);
            }
            else
            {
                // If user cancels on startup (and we needed a workspace), we might want to close
                // But since this is also called from the button, we check:
                if (string.IsNullOrWhiteSpace(_workspacePath))
                {
                    Application.Current.Shutdown();
                }
            }
        }

        private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            SaveWorkspaceSettings();       // Global Recent List
            SaveWorkspaceConfiguration();  // Current Workspace Config
        }

        private void LoadWorkspaceSettings()
        {
            try
            {
                var settingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, SettingsFileName);
                if (File.Exists(settingsPath))
                {
                    var json = File.ReadAllText(settingsPath);
                    var settings = JsonConvert.DeserializeObject<AppSettings>(json);
                    if (settings != null && !string.IsNullOrWhiteSpace(settings.WorkspacePath))
                    {
                        _workspacePath = settings.WorkspacePath;
                        InitializeWorkspaceFiles(_workspacePath);

                        LoadWorkspaceConfiguration(_workspacePath);
                    }
                }
            }
            catch
            {
                // Ignore settings load errors
            }

            UpdateWorkspaceUI();
        }

        private void LoadWorkspaceConfiguration(string path)
        {
            var config = WorkspaceConfig.Load(path);
            
            // Connection
            IpBox.Text = config.TargetIp;
            PortBox.Text = config.TargetPort.ToString();

            // Proxy
            ProxyLocalPortBox.Text = config.ProxyLocalPort.ToString();
            ProxyTargetIpBox.Text = config.ProxyTargetIp;
            ProxyTargetPortBox.Text = config.ProxyTargetPort.ToString();

            // Proto Path
            if (!string.IsNullOrWhiteSpace(config.ProtoFolderPath) && Directory.Exists(config.ProtoFolderPath))
            {
                _protoFolderPath = config.ProtoFolderPath;
                 // Asynchronously load protos without blocking UI
                _ = LoadProtosFromFolderAsync(_protoFolderPath);
            }
        }

        private void SaveWorkspaceConfiguration()
        {
            if (string.IsNullOrWhiteSpace(_workspacePath)) return;

            var config = new WorkspaceConfig
            {
                TargetIp = IpBox.Text,
                ProtoFolderPath = _protoFolderPath // needs to be tracked
            };

            if (int.TryParse(PortBox.Text, out var port)) config.TargetPort = port;
            if (int.TryParse(ProxyLocalPortBox.Text, out var pLocal)) config.ProxyLocalPort = pLocal;
            if (int.TryParse(ProxyTargetPortBox.Text, out var pTarget)) config.ProxyTargetPort = pTarget;
            config.ProxyTargetIp = ProxyTargetIpBox.Text;

            config.Save(_workspacePath);
            Dispatcher.Invoke(() => AppendLog($"[Config] Saved to {_workspacePath}", Brushes.Gray));
        }

        // Field to track current proto folder
        private string _protoFolderPath = "";

        private async Task LoadProtosFromFolderAsync(string folder)
        {
             try
             {
                 var files = Directory.GetFiles(folder, "*.proto", SearchOption.AllDirectories);
                 if (files.Length > 0)
                 {
                      await ProcessProtosAsync(files);
                      Dispatcher.Invoke(() => AppendLog($"[Proto] Auto-loaded from {folder}", Brushes.Green));
                 }
             }
             catch (Exception ex)
             {
                 Dispatcher.Invoke(() => AppendLog($"[Error] Auto-load proto: {ex.Message}", Brushes.Red));
             }
        }

        private void SaveWorkspaceSettings()
        {
            try
            {
                var settingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, SettingsFileName);
                var settings = new AppSettings { WorkspacePath = _workspacePath };
                var json = JsonConvert.SerializeObject(settings, Formatting.Indented);
                File.WriteAllText(settingsPath, json);
            }
            catch (Exception ex)
            {
                AppendLog($"설정 저장 실패: {ex.Message}", Brushes.Red);
            }
        }

        private void UpdateWorkspaceUI()
        {
            if (!string.IsNullOrWhiteSpace(_workspacePath))
            {
                WorkspacePathText.Text = _workspacePath;
                WorkspacePathText.ToolTip = _workspacePath;
            }
            else
            {
                WorkspacePathText.Text = "(설정되지 않음)";
                WorkspacePathText.ToolTip = null;
            }
        }

        public string GetWorkspacePath() => _workspacePath;

        private class AppSettings
        {
            public string WorkspacePath { get; set; } = "";
        }
        #endregion

        #region Mode Switching
        private void ModeClient_Click(object sender, RoutedEventArgs e)
        {
            ClientView.Visibility = Visibility.Visible;
            ProxyView.Visibility = Visibility.Collapsed;

        }

        private void ModeProxy_Click(object sender, RoutedEventArgs e)
        {
            ClientView.Visibility = Visibility.Collapsed;
            ProxyView.Visibility = Visibility.Visible;

        }

        private void ModeEditor_Click(object sender, RoutedEventArgs e)
        {
            // Removed: EditorView.Visibility = Visibility.Visible;
        }
        
        private void ResetState_Click(object sender, RoutedEventArgs e)
        {
            // ScriptGlobals.Initialize(new ScriptStateStore(), ...);
            // Or just clear.
            if (ScriptContract.ScriptGlobals.State is ScriptContract.ScriptStateStore store)
            {
                store.Clear();
                AppendLog("State Store Cleared.");
            }
        }
        #endregion


        


        #region Script Loading & Editing
        // Moved to MainWindow.Scripting.cs
        #endregion

        #region Network Connection
        private void ConnectBtn_Click(object sender, RoutedEventArgs e)
        {
            var ip = IpBox.Text;
            if (!int.TryParse(PortBox.Text, out var port))
            {
                AppendLog("Invalid Port", Brushes.Red);
                return;
            }

            SaveWorkspaceConfiguration();

            try
            {
                if (_client != null)
                {
                    _client.DisconnectAndStop();
                    _client = null;
                }

                _client = new SimpleTcpClient(ip, port);
                _client.Connected += () => Dispatcher.Invoke(() =>
                {
                    AppendLog($"Connected to {ip}:{port}", Brushes.DeepSkyBlue);
                    UpdateConnectionState(true);
                });
                _client.Disconnected += () => Dispatcher.Invoke(() =>
                {
                    AppendLog("Disconnected", Brushes.Orange);
                    UpdateConnectionState(false);
                });
                _client.DataReceived += OnDataReceived;
                _client.ErrorOccurred += (err) => Dispatcher.Invoke(() => AppendLog($"Socket Error: {err}", Brushes.Red));

                _client.ConnectAsync();
            }
            catch (Exception ex)
            {
                AppendLog($"Connection failed: {ex.Message}", Brushes.Red);
            }
        }

        private void DisconnectBtn_Click(object sender, RoutedEventArgs e)
        {
            _client?.DisconnectAndStop();
        }

        private void OpenScriptEditor_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_workspacePath)) 
            {
                AppendLog("Please load a workspace first.", Brushes.Red);
                return;
            }

            if (_scriptEditorWindow == null || !_scriptEditorWindow.IsLoaded)
            {
                _scriptEditorWindow = new ScriptEditorWindow(_workspacePath, _scriptLoader);
                
                _scriptEditorWindow.OnRequestCompilation += () => 
                {
                    var win = _scriptEditorWindow;
                    if (win == null) return;
                    
                    Action<string, Brush> editorLogger = (msg, color) => 
                    {
                        win.Dispatcher.Invoke(() => win.AppendLog(msg, color));
                    };

                    Dispatcher.InvokeAsync(async () => 
                    {
                        await CompileScriptsAsync(_workspacePath, editorLogger);
                    });
                };
                
                _scriptEditorWindow.Show();
            }
            else
            {
                _scriptEditorWindow.Activate();
                if (_scriptEditorWindow.WindowState == WindowState.Minimized) _scriptEditorWindow.WindowState = WindowState.Normal;
            }
        }

        private void UpdateConnectionState(bool connected)
        {
            ConnectBtn.IsEnabled = !connected;
            DisconnectBtn.IsEnabled = connected;
            SendBtn.IsEnabled = connected;
        }
        #endregion

        #region Sending & Receiving
        private void PacketListBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (PacketListBox.SelectedItem is PacketConvertor convertor)
            {
                // Generate Default JSON
                var (_, json) = convertor.DefaultJsonString();
                JsonEditor.Text = json;
                SendBtn.IsEnabled = _client != null && _client.IsConnected;
                
                _currentEditingFile = convertor.Name; 

                // Generate Default JSON for Header
                if (_headerAssembly != null)
                {
                    var headerType = _headerAssembly.GetTypes().FirstOrDefault(t => typeof(IHeader).IsAssignableFrom(t) && !t.IsAbstract && !t.IsInterface);
                    if (headerType == null) headerType = _headerAssembly.GetTypes().FirstOrDefault(t => t.Name == "Header");

                    if (headerType != null)
                    {
                        try
                        {
                            var headerInstance = Activator.CreateInstance(headerType);
                            var headerJson = JsonConvert.SerializeObject(headerInstance, Formatting.Indented);
                            HeaderJsonEditor.Text = headerJson;
                            AppendLog($"[Debug] Generated Header JSON ({headerJson.Length} chars)", Brushes.Gray);
                        }
                        catch (Exception ex)
                        {
                            HeaderJsonEditor.Text = "{}";
                            AppendLog($"[Error] Header Serialization Failed: {ex.Message}", Brushes.Red);
                        }
                    }
                    else
                    {
                        HeaderJsonEditor.Text = "{}";
                        AppendLog($"[Warning] 'Header' type not found in assembly.", Brushes.Orange);
                    }
                }
                else
                {
                    HeaderJsonEditor.Text = "{}";
                } 

                // Display Proto Source
                try
                {
                    // Use Reflection to get Descriptor
                    var descriptorProp = convertor.Type.GetProperty("Descriptor", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                    if (descriptorProp != null)
                    {
                        if (descriptorProp.GetValue(null) is MessageDescriptor descriptor)
                        {
                            var protoFileName = descriptor.File.Name; // e.g. "Common.proto" or "Folder/Common.proto"
                            
                            // Find matching file in _loadedProtoFiles
                            // _loadedProtoFiles contains Absolute Paths
                            var match = _loadedProtoFiles.FirstOrDefault(path => 
                                path.EndsWith(protoFileName, StringComparison.OrdinalIgnoreCase) || 
                                Path.GetFileName(path).Equals(Path.GetFileName(protoFileName), StringComparison.OrdinalIgnoreCase));

                            if (match != null && File.Exists(match))
                            {
                                ProtoSourceViewer.Text = File.ReadAllText(match);
                            }
                            else
                            {
                                ProtoSourceViewer.Text = $"// Source file not found for: {protoFileName}\n// Loaded files: {_loadedProtoFiles.Count}";
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    ProtoSourceViewer.Text = $"// Error loading source: {ex.Message}";
                }
            }
        }
        
        private async void SendBtn_Click(object sender, RoutedEventArgs e)
        {
             await SendBtn_ClickAsync(sender, e);
        }

        private async Task SendBtn_ClickAsync(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_scriptAssembly == null)
                {
                     // Compile First
                     await CompileScriptsAsync(_workspacePath, AppendLog);
                     if (_scriptAssembly == null) return; 
                }

                if (_client == null || !_client.IsConnected)
                {
                     FluentMessageBox.ShowError("서버에 연결되지 않았습니다.");
                     return;
                }

                var type = (PacketListBox.SelectedItem as PacketConvertor)?.Type;
                if (type == null)
                {
                    AppendLog("No packet type selected.", Brushes.Red);
                    return;
                }

                var json = JsonEditor.Text;

                if (JsonConvert.DeserializeObject(json, type) is not IMessage message) 
                    return;

                // Client Interceptor Hook
                if (_clientInterceptor != null)
                {
                    var ctx = new ClientPacketContext(message);



                    _clientInterceptor.OnBeforeSend(ctx);
                    message = ctx.Message;
                }
                
                // Find Header Type
                Type? headerType = null;
                // MainWindow.Scripting.cs should expose _headerAssembly or we access it via reflection/event
                // But _headerAssembly is private in MainWindow block.
                // We are in the same partial class 'MainWindow'.
                // verifying _headerAssembly visibility. It was added as 'private' in MainWindow.Scripting.cs (partial).
                // Private fields in partial classes are shared across files.
                
                if (_headerAssembly != null)
                {
                     headerType = _headerAssembly.GetTypes().FirstOrDefault(t => typeof(IHeader).IsAssignableFrom(t) && !t.IsAbstract && !t.IsInterface);
                     if (headerType == null) headerType = _headerAssembly.GetTypes().FirstOrDefault(t => t.Name == "Header");
                }

                if (headerType == null)
                {
                    AppendLog("PacketHeader.csx has not been compiled or Header class not found.", Brushes.Red);
                    return;
                }

                // Header JSON to Object
                IHeader? headerObj = null;
                try 
                {
                    // Use HeaderJsonEditor.Text
                    headerObj = (IHeader?)JsonConvert.DeserializeObject(HeaderJsonEditor.Text, headerType);
                }
                catch(Exception ex)
                {
                    AppendLog($"Header JSON Error: {ex.Message}", Brushes.Red);
                    return;
                }

                if (headerObj == null)
                {
                    AppendLog("Header object is null.", Brushes.Red);
                    return;
                }

                var packet = new Packet(headerObj, message);

                // Encode & Send
                var bytes = ScriptGlobals.Codec.Encode(packet);
                _client.SendAsync(bytes.Span);

                AppendLog($"[Send] {message.GetType().Name} ({bytes.Length} bytes)", Brushes.White);
            }
            catch (Exception ex)
            {
                FluentMessageBox.ShowError($"전송 오류: {ex.Message}");
                AppendLog($"[Error] {ex.Message}", Brushes.Red);
            }
        }

        public void SendPacket(IHeader header, IMessage message)
        {
            if (_client == null || !_client.IsConnected)
            {
                AppendLog("Not connected.", Brushes.Red);
                return;
            }
             
            if (ScriptGlobals.Codec == null)
            {
               AppendLog("Codec not initialized (Compile first).", Brushes.Red);
               return;
            }

            try 
            {
                // Client Interceptor Hook
                if (_clientInterceptor != null)
                {
                    var ctx = new ClientPacketContext(message);
                    _clientInterceptor.OnBeforeSend(ctx);
                    message = ctx.Message;
                }

                var bytes = ScriptGlobals.Codec.Encode(new Packet(header, message));
                _client.SendAsync(bytes.ToArray());
                AppendLog($"[Send] {message.GetType().Name} ({bytes.Length} bytes)");
            }
            catch (Exception ex)
            {
                AppendLog($"Send Error: {ex.Message}", Brushes.Red);
            }
        }



        private void OnDataReceived(byte[] data)
        {
            Dispatcher.Invoke(() =>
            {
                _receiveBuffer.AddRange(data);
                ProcessReceiveBuffer();
            });
        }

        private void ProcessReceiveBuffer()
        {
            if (ScriptGlobals.Codec == null) return;

            // Inefficient List -> Array -> ROS loop for prototype
            while (_receiveBuffer.Count > 0)
            {
                var currentBytes = _receiveBuffer.ToArray();
                var seq = new System.Buffers.ReadOnlySequence<byte>(currentBytes);
                var originalSeq = seq; // copy struct

                try 
                {
                    if (ScriptGlobals.Codec.TryDecode(ref seq, out var packet))
                    {
                        var consumed = originalSeq.Length - seq.Length;
                        _receiveBuffer.RemoveRange(0, (int)consumed);

                        if (packet != null)
                        {
                            var json = JsonConvert.SerializeObject(packet, Formatting.Indented);
                            AppendLog($"[Recv] {packet.GetType().Name}:\n{json}", Brushes.LimeGreen);
                        }
                    }
                    else
                    {
                        break; // Need more data
                    }
                }
                catch (Exception ex)
                {
                    AppendLog($"Decoder Error: {ex.Message}", Brushes.Red);
                    // Prevent infinite loop on bad data
                    _receiveBuffer.Clear(); 
                    break;
                }
            }
        }
        #endregion



        internal void AppendLog(string message, Brush? color = null)
        {
            var paragraph = new Paragraph(new Run(message)) 
            {
                Foreground = color ?? Brushes.LightGray,
                Margin = new Thickness(0)
            };
            LogBox.Document.Blocks.Add(paragraph);
            LogBox.ScrollToEnd();
        }
    }
}           