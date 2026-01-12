using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Threading;
using Microsoft.IdentityModel.Tokens;
using TestClient.Proto;
using Newtonsoft.Json;
using Proto;
using System.Windows.Media.Animation;

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
        private readonly DispatcherTimer repeatTimer = new();
		private int remainingCount = 0;

        public ObservableCollection<PacketConvertor> Items { get; }

		public MainWindow()
		{
			InitializeComponent();

			_client = new Proto.TestClient(this);

			ProtoLoaderManager.Instance.LoadAllProtos();
			Items = new ObservableCollection<PacketConvertor>(ProtoLoaderManager.Instance.GetSendPackets());

			PacketListBox.ItemsSource = Items;
			PlatformCodeBox.ItemsSource = Enum.GetValues<EAccountPlatform>().Skip(1);
			PlatformCodeBox.SelectedIndex = 0;

			RegionCodeBox.ItemsSource = Enum.GetValues<ERegionCode>();
			RegionCodeBox.SelectedIndex = 1;

			_networkUpdateTimer.Interval = new TimeSpan(0, 0, 0, 0, 10);
			_networkUpdateTimer.Tick += (o, e) => _client.Update();
		}

		public void Terminate()
		{
			_networkUpdateTimer.Stop();
		}
        private async Task ShowToast(string message, int durationMs = 1000)
        {
            ToastLabel.Content = message;
            ToastLabel.Visibility = Visibility.Visible;
            ToastLabel.Opacity = 0.85; // 초기 투명도

            // 스토리보드 가져오기
            var sb = (Storyboard)this.Resources["ToastFadeOutStoryboard"];
            sb.Completed += (s, e) =>
            {
                ToastLabel.Visibility = Visibility.Collapsed;
            };
            sb.Begin();
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
				PrintLog(ex.Message);
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

		private void ComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
		{
			if (e.AddedItems.Count > 0 && e.AddedItems[0] is PacketConvertor packet)
			{
				var (_, json) = packet.DefaultJsonString();

				PacketEditTextBox.Document.Blocks.Clear();
				PacketEditTextBox.Document.Blocks.Add(new Paragraph(new Run(json)));
			}
		}

		private async void Button_Click(object sender, RoutedEventArgs e)
		{
			if (PlatformUidTextBox.Text is { } text && false == text.IsNullOrEmpty())
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
					var textRange = new TextRange(PacketEditTextBox.Document.ContentStart, PacketEditTextBox.Document.ContentEnd);
					var res = await _client.RequestAsync(1, packetConvertor.ToPacket(textRange.Text));

					if (res.Descriptor.Name != Player_DataRes.Descriptor.Name)
						PrintLog($"{res.Descriptor.Name}{Environment.NewLine}{JsonConvert.SerializeObject(res, Formatting.Indented)}");
				}
			}
			catch (Exception exception)
			{
				PrintLog(exception.Message);
			}
		}

        private async void RepeatSend_Click(object sender, RoutedEventArgs e)
        {
            if (remainingCount > 0)
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

            repeatTimer.Stop();

            remainingCount = repeatCount;
            repeatTimer.Interval = TimeSpan.FromMicroseconds(intervalMs);
            repeatTimer.Tick += async (_, _) =>
            {
                Send_Click(sender, e);

                remainingCount--;
                
                if (remainingCount <= 0)
                {
                    repeatTimer.Stop();
                    await ShowToast("반복 전송 완료!");
                }
            };

            repeatTimer.Start();
        }


        private async void CheatSend(object sender, RoutedEventArgs e)
		{
			if (!_client.IsConnected() || CheatTextbox.Text.IsNullOrEmpty())
				return;

			var res = await _client.RequestAsync<Cheat_Res>(1, new Cheat_Req { Params = CheatTextbox.Text });
			PrintLog(JsonConvert.SerializeObject(res, Formatting.Indented));
		}

		private void ClearLog_Click(object sender, RoutedEventArgs e)
		{
			LogTextBox.Document.Blocks.Clear();
		}
	}
}