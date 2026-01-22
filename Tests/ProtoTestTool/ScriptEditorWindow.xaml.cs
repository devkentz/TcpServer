using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ProtoTestTool.Roslyn;

namespace ProtoTestTool
{
    public partial class ScriptEditorWindow : Wpf.Ui.Controls.UiWindow
    {
        private readonly string _workspacePath;
        private readonly ScriptLoader _scriptLoader;
        private readonly RoslynService _roslynService;
        
        // Callback when compilation succeeds


        public ScriptEditorWindow(string workspacePath, ScriptLoader scriptLoader, RoslynService roslynService)
        {
            InitializeComponent();
            _workspacePath = workspacePath;
            _scriptLoader = scriptLoader;
            _roslynService = roslynService;

            Loaded += ScriptEditorWindow_Loaded;
        }

        private async void ScriptEditorWindow_Loaded(object sender, RoutedEventArgs e)
        {
            await InitializeEditorsAsync();
            LoadFiles();
        }

        private async Task InitializeEditorsAsync()
        {
            if (_roslynService?.Host != null)
            {
                var darkColors = GetDarkThemeColors();
                
                await RegistryEditor.InitializeAsync(_roslynService.Host, darkColors, _workspacePath, String.Empty, Microsoft.CodeAnalysis.SourceCodeKind.Script);
                await HeaderEditor.InitializeAsync(_roslynService.Host, darkColors, _workspacePath, String.Empty, Microsoft.CodeAnalysis.SourceCodeKind.Script);
                await SerializerEditor.InitializeAsync(_roslynService.Host, darkColors, _workspacePath, String.Empty, Microsoft.CodeAnalysis.SourceCodeKind.Script);
                await ContextEditor.InitializeAsync(_roslynService.Host, darkColors, _workspacePath, String.Empty, Microsoft.CodeAnalysis.SourceCodeKind.Script);
            }
        }

        private RoslynPad.Editor.ClassificationHighlightColors GetDarkThemeColors()
        {
            var colors = new RoslynPad.Editor.ClassificationHighlightColors();
            
            // Helper to set private/internal properties via Reflection
            void SetColor(string propName, System.Windows.Media.Color color)
            {
                var prop = typeof(RoslynPad.Editor.ClassificationHighlightColors).GetProperty(propName);
                if (prop != null)
                {
                    // Access private setter if necessary
                    prop.SetValue(colors, new SolidColorBrush(color));
                }
            }

            // VS Code Dark Theme Colors
            SetColor("DefaultBrush", Color.FromRgb(255, 255, 255));      // White (Default)
            SetColor("KeywordBrush", Color.FromRgb(86, 156, 214));       // #569CD6 (Blue)
            SetColor("StringBrush", Color.FromRgb(206, 145, 120));       // #CE9178 (Orange)
            SetColor("CommentBrush", Color.FromRgb(106, 153, 85));       // #6A9955 (Green)
            SetColor("XmlCommentBrush", Color.FromRgb(106, 153, 85));
            SetColor("TypeBrush", Color.FromRgb(78, 201, 176));          // #4EC9B0 (Teal)
            SetColor("MethodBrush", Color.FromRgb(220, 220, 170));       // #DCDCAA (Yellow)
            SetColor("StaticSymbolBrush", Color.FromRgb(78, 201, 176));
            SetColor("PreprocessorKeywordBrush", Color.FromRgb(155, 155, 155));

            return colors;
        }

        private void LoadFiles()
        {
            LoadFile("PacketRegistry.csx", RegistryEditor);
            LoadFile("PacketHeader.csx", HeaderEditor);
            LoadFile("PacketSerializer.csx", SerializerEditor);
            LoadFile("PacketHandler.csx", ContextEditor);
        }

        private void LoadFile(string fileName, RoslynPad.Editor.RoslynCodeEditor editor)
        {
            var path = Path.Combine(_workspacePath, fileName);
            if (File.Exists(path))
            {
                editor.Text = File.ReadAllText(path);
            }
        }

        private void CompileScriptBtn_Click(object sender, RoutedEventArgs e)
        {
            StatusText.Text = "Compiling...";
            ScriptLogBox.Text = "";
            CompileScriptBtn.IsEnabled = false;

            try
            {
                // 1. Save all files
                File.WriteAllText(Path.Combine(_workspacePath, "PacketRegistry.csx"), RegistryEditor.Text);
                File.WriteAllText(Path.Combine(_workspacePath, "PacketHeader.csx"), HeaderEditor.Text);
                File.WriteAllText(Path.Combine(_workspacePath, "PacketSerializer.csx"), SerializerEditor.Text);
                File.WriteAllText(Path.Combine(_workspacePath, "PacketHandler.csx"), ContextEditor.Text);

                AppendLog("Files Saved. Starting Compilation...", Brushes.Gray);

                // 2. Request Compilation
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
