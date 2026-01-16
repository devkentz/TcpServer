using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using Newtonsoft.Json;
using ProtoTestTool.Network;
using ProtoTestTool.ScriptContract;
using ProtoTestTool.Roslyn;

namespace ProtoTestTool
{
    public partial class MainWindow : Window
    {
        private SimpleTcpClient? _client;
        private IScriptContext? _scriptContext;
        private readonly ScriptLoader _scriptLoader = new();
        private readonly List<byte> _receiveBuffer = new();
        
        // Roslyn
        private readonly RoslynService _roslynService;
        
        // Editor State
        private string _currentEditingFile = "";

        public MainWindow()
        {
            InitializeComponent();
            
            _roslynService = new RoslynService();
            InitializeRoslynEditor();
        }


        


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
                    AppendLog($"Connected to {ip}:{port}", Brushes.Blue);
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

        private void UpdateConnectionState(bool connected)
        {
            ConnectBtn.IsEnabled = !connected;
            DisconnectBtn.IsEnabled = connected;
            SendBtn.IsEnabled = connected;
        }
        #endregion

        #region Sending & Receiving
        private void SendBtn_Click(object sender, RoutedEventArgs e)
        {
            if (_client == null || !_client.IsConnected)
            {
                AppendLog("Not connected.", Brushes.Red);
                return;
            }

            if (_scriptContext == null)
            {
                AppendLog("Script not loaded.", Brushes.Red);
                return;
            }

            if (PacketListBox.SelectedItem is not Type packetType)
            {
                AppendLog("No packet selected.", Brushes.Red);
                return;
            }

            try
            {
                var json = JsonEditor.Text;
                var packetObj = JsonConvert.DeserializeObject(json, packetType);
                if (packetObj == null)
                {
                    AppendLog("Failed to deserialize JSON to object.", Brushes.Red);
                    return;
                }

                var bytes = _scriptContext.Serializer.Serialize(packetObj);
                _client.SendAsync(bytes);

                AppendLog($"[Send] {packetType.Name} ({bytes.Length} bytes)");
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
                if (_scriptContext == null)
                {
                    AppendLog($"Received {data.Length} bytes (Script not loaded)", Brushes.Gray);
                    return;
                }

                _receiveBuffer.AddRange(data);
                ProcessReceiveBuffer();
            });
        }

        private void ProcessReceiveBuffer()
        {
            if (_scriptContext == null) return;

            var serializer = _scriptContext.Serializer;
            var headerSize = serializer.GetHeaderSize();

            while (_receiveBuffer.Count >= headerSize)
            {
                var headerBytes = _receiveBuffer.Take(headerSize).ToArray();
                int totalLength = serializer.GetTotalLength(headerBytes);

                if (_receiveBuffer.Count < totalLength)
                {
                    return;
                }

                var packetBytes = _receiveBuffer.Take(totalLength).ToArray();
                _receiveBuffer.RemoveRange(0, totalLength);

                try
                {
                    var packet = serializer.Deserialize(packetBytes);
                    var json = JsonConvert.SerializeObject(packet, Formatting.Indented);
                    AppendLog($"[Recv] {packet.GetType().Name}:\n{json}", Brushes.DarkGreen);
                }
                catch (Exception ex)
                {
                    AppendLog($"Deserialization Error: {ex.Message}", Brushes.Red);
                }
            }
        }
        #endregion



        private void AppendLog(string message, Brush? color = null)
        {
            var paragraph = new Paragraph(new Run(message)) 
            {
                Foreground = color ?? Brushes.Black,
                Margin = new Thickness(0)
            };
            LogBox.Document.Blocks.Add(paragraph);
            LogBox.ScrollToEnd();
        }
    }
}