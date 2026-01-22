using System;
using System.IO;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using Microsoft.Web.WebView2.Core;

namespace ProtoTestTool
{
    public partial class ScriptEditorWindow : Wpf.Ui.Controls.UiWindow
    {
        private readonly string _workspacePath;
        private readonly ScriptLoader _scriptLoader;
        
        // Callback when compilation succeeds


        public ScriptEditorWindow(string workspacePath, ScriptLoader scriptLoader)
        {
            InitializeComponent();
            _workspacePath = workspacePath;
            _scriptLoader = scriptLoader;

            Loaded += ScriptEditorWindow_Loaded;
        }

        private async void ScriptEditorWindow_Loaded(object sender, RoutedEventArgs e)
        {
            await InitializeEditorsAsync();
        }

        private async Task InitializeEditorsAsync()
        {
            var editorPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Monaco", "editor.html");
            if (!File.Exists(editorPath))
            {
                MessageBox.Show($"Editor host not found at: {editorPath}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            var editorUrl = new Uri(editorPath).AbsoluteUri;

            // Initialize WebView2 for each tab
            await InitializeWebView(RegistryEditor, editorUrl, "PacketRegistry.csx");
            await InitializeWebView(HeaderEditor, editorUrl, "PacketHeader.csx");
            await InitializeWebView(SerializerEditor, editorUrl, "PacketSerializer.csx");
            await InitializeWebView(ContextEditor, editorUrl, "PacketHandler.csx");
            
            SetStatus("Ready");
        }

        private async Task InitializeWebView(Microsoft.Web.WebView2.Wpf.WebView2 webView, string url, string fileName)
        {
            try
            {
                await webView.EnsureCoreWebView2Async();
                webView.Source = new Uri(url);
                
                // Wait for navigation to complete
                webView.NavigationCompleted += async (s, e) =>
                {
                    if (e.IsSuccess)
                    {
                        await LoadFileIntoEditor(webView, fileName);
                    }
                };
            }
            catch (Exception ex)
            {
                AppendLog($"Failed to initialize editor for {fileName}: {ex.Message}", Brushes.Red);
            }
        }

        private async Task LoadFileIntoEditor(Microsoft.Web.WebView2.Wpf.WebView2 webView, string fileName)
        {
            var path = Path.Combine(_workspacePath, fileName);
            if (File.Exists(path))
            {
                var content = await File.ReadAllTextAsync(path);
                // Escape for JS
                var safeContent = System.Text.Json.JsonSerializer.Serialize(content);
                await webView.ExecuteScriptAsync($"setContent({safeContent})");
            }
        }

        private async Task<string> GetEditorContent(Microsoft.Web.WebView2.Wpf.WebView2 webView)
        {
            try 
            {
                var result = await webView.ExecuteScriptAsync("getContent()");
                // Result is JSON encoded string, need to deserialize
                return System.Text.Json.JsonSerializer.Deserialize<string>(result) ?? string.Empty;
            }
            catch (Exception ex)
            {
                AppendLog($"Error getting content: {ex.Message}", Brushes.Red);
                return string.Empty;
            }
        }

        private async void CompileScriptBtn_Click(object sender, RoutedEventArgs e)
        {
            StatusText.Text = "Compiling...";
            ScriptLogBox.Text = "";
            CompileScriptBtn.IsEnabled = false;

            try
            {
                // 1. Get content from all editors asynchronously
                var registryCode = await GetEditorContent(RegistryEditor);
                var headerCode = await GetEditorContent(HeaderEditor);
                var serializerCode = await GetEditorContent(SerializerEditor);
                var handlerCode = await GetEditorContent(ContextEditor);

                // 2. Save all files
                await File.WriteAllTextAsync(Path.Combine(_workspacePath, "PacketRegistry.csx"), registryCode);
                await File.WriteAllTextAsync(Path.Combine(_workspacePath, "PacketHeader.csx"), headerCode);
                await File.WriteAllTextAsync(Path.Combine(_workspacePath, "PacketSerializer.csx"), serializerCode);
                await File.WriteAllTextAsync(Path.Combine(_workspacePath, "PacketHandler.csx"), handlerCode);

                AppendLog("Files Saved. Starting Compilation...", Brushes.Gray);

                // 3. Request Compilation
                OnRequestCompilation?.Invoke(); 
            }
            catch (Exception ex)
            {
                AppendLog($"Error: {ex.Message}", Brushes.Red);
                StatusText.Text = "Error";
            }
            finally
            {
                CompileScriptBtn.IsEnabled = true;
            }
        }
        
        public event Action? OnRequestCompilation;

        public void AppendLog(string message, Brush color)
        {
            ScriptLogBox.Text += $"\n[{DateTime.Now:HH:mm:ss}] {message}";
            ScriptLogBox.ScrollToEnd();
        }
        
        public void SetStatus(string status) => StatusText.Text = status;
    }
}
