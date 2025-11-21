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
using System.Collections.Generic; // Necesario para List<>

namespace NxDesk.Client
{
    public partial class MainWindow : Window
    {
        private SignalingService _signalingService;
        private WebRTCService _webRTCService;
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
                // Limpiar botones anteriores por si acaso
                ScreenButtonsItemsControl.Items.Clear();

                _signalingService = new SignalingService();
                _webRTCService = new WebRTCService(_signalingService);

                _webRTCService.OnVideoFrameReady += UpdateVideoFrame;
                _webRTCService.OnConnectionStateChanged += UpdateStatus;

                // SUSCRIPCIÓN AL NUEVO EVENTO DE PANTALLAS
                _webRTCService.OnScreensInfoReceived += PopulateScreenButtons;

                _remoteView = new RemoteViewControl();
                _remoteView.OnInputEvent += ev => _webRTCService.SendInputEvent(ev);

                ContentArea.Content = _remoteView;
                PreSessionControls.Visibility = Visibility.Collapsed;
                InSessionControls.Visibility = Visibility.Visible;

                await _webRTCService.StartConnectionAsync(connectionId);

                // YA NO USAMOS DATOS DUMMY
                // var dummyScreenNames = new List<string> { "Pantalla 1", "Pantalla 2" };
                // PopulateScreenButtons(dummyScreenNames);
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
                    _webRTCService.OnScreensInfoReceived -= PopulateScreenButtons; // Desuscribir

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
            Debug.WriteLine($"[NxDesk Client] Solicitando cambio a {screenName} (Index: {screenIndex})");

            // Enviar comando real al Host
            if (_webRTCService != null)
            {
                _webRTCService.SendInputEvent(new InputEvent
                {
                    EventType = "control",
                    Command = "switch_screen",
                    Value = screenIndex
                });
            }
        }

        private void PopulateScreenButtons(List<string> screenNames)
        {
            // Asegurarnos de estar en el hilo de la UI
            Dispatcher.Invoke(() =>
            {
                ScreenButtonsItemsControl.Items.Clear();

                // Si solo hay una pantalla, tal vez no quieras mostrar botones, o sí para confirmar.
                // Aquí los mostramos siempre si llegan.
                if (screenNames == null || screenNames.Count == 0) return;

                for (int i = 0; i < screenNames.Count; i++)
                {
                    var screenName = screenNames[i]; // Ej: "Pantalla 1 (1920x1080)"
                    var screenIndex = i;

                    Button btn = new Button
                    {
                        Content = (i + 1).ToString(), // Mostramos solo el número para ahorrar espacio, o screenName
                        ToolTip = screenName, // El nombre completo en el tooltip
                        Style = (Style)FindResource("ModernTextButton"),
                        Margin = new Thickness(5, 0, 5, 0),
                        Width = 30, // Botones un poco más compactos
                        Height = 30
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