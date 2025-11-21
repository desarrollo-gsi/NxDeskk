using NxDesk.Client.Services;
using NxDesk.Client.Views;
using NxDesk.Client.Views.WelcomeView.ViewModel;
using NxDesk.Core.DTOs;
using NxDesk.Core.Models; 
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace NxDesk.Client
{
    public partial class MainWindow : Window
    {
        private  SignalingService _signalingService;
        private  WebRTCService _webRTCService;
        private WelcomeViewControl _welcomeView;
        private RemoteViewControl? _remoteView;

        public MainWindow()
        {
            InitializeComponent();

            this.Closing += async (s, e) =>
            {
                if (_webRTCService != null) await _webRTCService.DisposeAsync();
            };
            this.KeyDown += MainWindow_KeyDown;
            this.KeyUp += MainWindow_KeyUp;

            _welcomeView = new WelcomeViewControl();
            if (_welcomeView.DataContext is WelcomeViewModel vm)
            {
                vm.OnDeviceSelected += HandleDeviceSelected;
            }

            ContentArea.Content = _welcomeView;
            ConnectButton.Click += ConnectButton_Click;
            DisconnectButton.Click += DisconnectButton_Click;
        }

        private void StartConnection(string connectionId)
        {
            if (string.IsNullOrWhiteSpace(connectionId))
            {
                StatusTextBlock.Text = "Error: El ID no puede estar vacío.";
                return;
            }

            Dispatcher.Invoke(async () =>
            {
                _signalingService = new SignalingService();
                _webRTCService = new WebRTCService(_signalingService);

                _webRTCService.OnVideoFrameReady += UpdateVideoFrame;
                _webRTCService.OnConnectionStateChanged += UpdateStatus;

                _remoteView = new RemoteViewControl();
                _remoteView.OnInputEvent += ev => _webRTCService.SendInputEvent(ev);

                ContentArea.Content = _remoteView;
                PreSessionControls.Visibility = Visibility.Collapsed;
                InSessionControls.Visibility = Visibility.Visible;

                await _webRTCService.StartConnectionAsync(connectionId);

                var dummyScreenNames = new List<string> { "Pantalla 1", "Pantalla 2" };
                PopulateScreenButtons(dummyScreenNames);
            });
        }

        private void HandleDeviceSelected(DiscoveredDevice device) 
        {
            StartConnection(device.ConnectionID);
        }

        private void ConnectButton_Click(object sender, RoutedEventArgs e)
        {
            StartConnection(RoomIdTextBox.Text);
        }

        private void DisconnectButton_Click(object sender, RoutedEventArgs e)
        {
            Dispatcher.Invoke(async () =>
            {
                if (_webRTCService != null)
                {
                    _webRTCService.OnVideoFrameReady -= UpdateVideoFrame;
                    _webRTCService.OnConnectionStateChanged -= UpdateStatus;

                    await _webRTCService.DisposeAsync();
                    _webRTCService = null;
                    _signalingService = null;
                }

                ContentArea.Content = _welcomeView;
                InSessionControls.Visibility = Visibility.Collapsed;
                PreSessionControls.Visibility = Visibility.Visible;
                ScreenButtonsItemsControl.Items.Clear();
                StatusTextBlock.Text = "Desconectado. Esperando conexión...";
            });
        }

        private void SwitchScreen(int screenIndex)
        {

            string screenName = $"Pantalla {screenIndex + 1}";
            StatusTextBlock.Text = $"Cambiando a {screenName}...";
            Debug.WriteLine($"[NxDesk Client] Solicitando cambio a {screenName}");
        }

        private void PopulateScreenButtons(List<string> screenNames)
        {
            Dispatcher.Invoke(() =>
            {
                ScreenButtonsItemsControl.Items.Clear(); 

                for (int i = 0; i < screenNames.Count; i++)
                {
                    var screenName = screenNames[i];
                    var screenIndex = i; 

                    Button btn = new Button
                    {
                        Content = screenName,
                        Style = (Style)FindResource("ModernTextButton"),
                        Margin = new Thickness(5, 0, 5, 0)
                    };

                    btn.Click += (s, e) => SwitchScreen(screenIndex);

                    ScreenButtonsItemsControl.Items.Add(btn);
                }
            });
        }


        private void UpdateStatus(string status)
        {
            Dispatcher.Invoke(() => StatusTextBlock.Text = status);
        }

        private void UpdateVideoFrame(BitmapSource bitmap)
        {
            Dispatcher.Invoke(() =>
            {
                if (_remoteView != null)
                    _remoteView.SetFrame(bitmap);
            });
        }

        private void MainWindow_KeyDown(object sender, KeyEventArgs e)
        {
            if (Keyboard.FocusedElement is System.Windows.Controls.TextBox)
            {
                return;
            }

            if (ContentArea.Content != _remoteView || _remoteView == null)
            {
                return;
            }

            _webRTCService.SendInputEvent(new InputEvent
            {
                EventType = "keydown",
                Key = e.Key.ToString()
            });
        }

        private void MainWindow_KeyUp(object sender, KeyEventArgs e)
        {
            if (Keyboard.FocusedElement is System.Windows.Controls.TextBox)
            {
                return;
            }

            if (ContentArea.Content != _remoteView || _remoteView == null)
            {
                return;
            }

            _webRTCService.SendInputEvent(new InputEvent
            {
                EventType = "keyup",
                Key = e.Key.ToString()
            });
        }
    }
}