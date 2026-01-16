using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Threading;
using TestClient.Proto;
using Newtonsoft.Json;
using Proto;
using System.Windows.Media.Animation;
using Microsoft.Extensions.Logging;

namespace TestClient
{
	/// <summary>
	/// Interaction logic for MainWindow.xaml
	/// </summary>
	public partial class MainWindow : Window, IClientEvent
	{
		private readonly DispatcherTimer _networkUpdateTimer = new();
		private readonly Proto.TestClient _client;
		private const int MaxLogLine = 50000;
        private readonly DispatcherTimer _repeatTimer = new();
		private int _remainingCount = 0;

        public ObservableCollection<PacketConvertor> Items { get; }

		public MainWindow()
		{
			InitializeComponent();

			var loggerFactory = LoggerFactory.Create(builder => { builder.AddConsole(); });
			var logger = loggerFactory.CreateLogger<Proto.TestClient>();

			_client = new Proto.TestClient(this, logger);

			ProtoLoaderManager.Instance.LoadAllProtos();
			Items = new ObservableCollection<PacketConvertor>(ProtoLoaderManager.Instance.GetSendPackets());

			PacketListBox.ItemsSource = Items;
			PlatformCodeBox.ItemsSource = Enum.GetValues<EAccountPlatform>().Skip(1);
			PlatformCodeBox.SelectedIndex = 0;

			RegionCodeBox.ItemsSource = Enum.GetValues<ERegionCode>();
			RegionCodeBox.SelectedIndex = 1;

			_networkUpdateTimer.Interval = new TimeSpan(0, 0, 0, 0, 10);
			_networkUpdateTimer.Tick += (o, e) => _client.Update();
			_networkUpdateTimer.Start();

			InitializeMonacoEditorAsync();
		}

		private async void InitializeMonacoEditorAsync()
		{
			await MonacoEditor.EnsureCoreWebView2Async(null);
			var htmlPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Monaco", "editor.html");
			MonacoEditor.Source = new Uri(htmlPath);
		}

		private async Task SetMonacoCodeAsync(string code)
		{
			var jsonArg = JsonConvert.SerializeObject(code);
			await MonacoEditor.ExecuteScriptAsync($"SetCode({jsonArg})");
		}

		private async Task<string> GetMonacoCodeAsync()
		{
			var jsonResult = await MonacoEditor.ExecuteScriptAsync("GetCode()");
			return JsonConvert.DeserializeObject<string>(jsonResult) ?? "";
		}

		public void Terminate()
		{
			_networkUpdateTimer.Stop();
		}
        private Task ShowToast(string message, int durationMs = 1000)
        {
            ToastText.Text = message;
            ToastLabel.Visibility = Visibility.Visible;
            ToastLabel.Opacity = 0.95;

            var sb = (Storyboard)Resources["ToastFadeOutStoryboard"];
            sb.Completed += (_, _) => ToastLabel.Visibility = Visibility.Collapsed;
            sb.Begin();
            return Task.CompletedTask;
        }

        private async void Connect_Click(object sender, RoutedEventArgs e)
		{
			try
			{
				ConnectBtn.IsEnabled = false;

				if (false == await BeginConnect())
				{
					Terminate();
				}
				else
				{
					PrintLog("Connected");
				}
			}
			catch (Exception ex)
			{
				PrintLog(ex.ToString());
			}
			finally
			{
				ConnectBtn.IsEnabled =true;
			}
		}

		public async Task<bool> BeginConnect()
		{
			if (_client.IsConnected())
			{
				_client.Disconnect();
			}

			if (false == _networkUpdateTimer.IsEnabled)
				_networkUpdateTimer.Start();

			if (false == int.TryParse(PortTextBox.Text, out var port))
				port = 9000;

			if (false == System.Net.IPAddress.TryParse(IpTextBox.Text, out var _))
				IpTextBox.Text = "127.0.0.1";

			if (false == await _client.ConnectAsync(IpTextBox.Text, port))
				return false;

			return true;
		}


		public void PrintLog(string message)
		{
			if (!Dispatcher.CheckAccess())
			{
				Dispatcher.BeginInvoke(() => PrintLog(message));
				return;
			}
			
			LogTextBox.Document.Blocks.Add(new Paragraph(new Run(message)));  // 새로운 텍스트 추가
			LogTextBox.ScrollToEnd();

			if (LogTextBox.Document.Blocks.Count > MaxLogLine)
			{
				var first = LogTextBox.Document.Blocks.First();
				LogTextBox.Document.Blocks.Remove(first);
			}
		}

		public void OnConnect(bool result)
		{
			PrintLog(result ? "Connect" : "Connect Failed");
		}

		public void OnDisconnect()
		{
			PrintLog("Disconnect");
			Terminate();
		}

		public void SetUserInfo(dynamic o)
		{
			UserInfoTextBox.Document ??= new FlowDocument();

			UserInfoTextBox.Document.Blocks.Clear();

			var para = new Paragraph();
			para.Inlines.Add(new Run(JsonConvert.SerializeObject(o)));
			UserInfoTextBox.Document.Blocks.Add(para);
		}

		private async void ComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
		{
			if (e.AddedItems.Count > 0 && e.AddedItems[0] is PacketConvertor packet)
			{
				var (_, json) = packet.DefaultJsonString();
				await SetMonacoCodeAsync(json);
			}
		}

		private async void Button_Click(object sender, RoutedEventArgs e)
		{
			if (PlatformUidTextBox.Text is { } text && false == string.IsNullOrEmpty(text))
			{
				try
				{
					var accountPlatform = (EAccountPlatform)PlatformCodeBox.SelectionBoxItem;
					var regionCode = (ERegionCode)RegionCodeBox.SelectionBoxItem;
					await _client.LoginAsync(text, accountPlatform, "", regionCode);
				}
				catch (Exception exception)
				{
					PrintLog(exception.Message);
				}
			}
		}

		private async void Send_Click(object sender, RoutedEventArgs e)
		{
			try
			{
				if (PacketListBox.SelectedItem is PacketConvertor packetConvertor)
				{
					var watch = new Stopwatch();
					var jsonText = await GetMonacoCodeAsync();

					watch.Start();
					var response = await _client.RequestAsync(packetConvertor.ToPacket(jsonText));
					watch.Stop();

					PrintLog($"elapsedTime : {watch.ElapsedMilliseconds}ms");
					PrintLog($"Message: {response}");
				}
			}
			catch (Exception exception)
			{
				PrintLog(exception.Message);
			}
		}

        private void RepeatSend_Click(object sender, RoutedEventArgs e)
        {
            if (_remainingCount > 0)
            {
                // 이미 반복 전송 중일 때
                MessageBox.Show("이미 반복 전송 중입니다!");
                return;
            }

            if (PacketListBox.SelectedItem is not PacketConvertor packetConvertor)
            {
                MessageBox.Show("패킷을 선택하세요.", "입력 오류", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(RepeatCountBox.Text))
            {
                MessageBox.Show("반복 횟수를 입력하세요.", "입력 오류", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (!int.TryParse(RepeatCountBox.Text, out var repeatCount) || repeatCount <= 0)
            {
                MessageBox.Show("반복 횟수는 1 이상의 숫자여야 합니다.", "입력 오류", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(RepeatIntervalBox.Text))
            {
                MessageBox.Show("간격(ms)을 입력하세요.", "입력 오류", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (!int.TryParse(RepeatIntervalBox.Text, out var intervalMs) || intervalMs <= 0)
            {
                MessageBox.Show("간격(ms)은 1 이상의 숫자여야 합니다.", "입력 오류", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _repeatTimer.Stop();

            _remainingCount = repeatCount;
            _repeatTimer.Interval = TimeSpan.FromMicroseconds(intervalMs);
            _repeatTimer.Tick += async (_, _) =>
            {
                Send_Click(sender, e);

                _remainingCount--;
                
                if (_remainingCount <= 0)
                {
                    _repeatTimer.Stop();
                    await ShowToast("반복 전송 완료!");
                }
            };

            _repeatTimer.Start();
        }

 
		private void ClearLog_Click(object sender, RoutedEventArgs e)
		{
			LogTextBox.Document.Blocks.Clear();
		}
	}
}