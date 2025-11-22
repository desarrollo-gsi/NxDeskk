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
using System.Collections.Generic;

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
                // CORRECCIÓN: Eliminada la línea que referenciaba a ScreenButtonsItemsControl
                // ScreenContextMenu se limpiará automáticamente cuando lleguen nuevas pantallas.

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
                    _webRTCService.OnScreensInfoReceived -= PopulateScreenButtons;

                    await _webRTCService.DisposeAsync();
                    _webRTCService = null;
                    _signalingService = null;
                }

                ContentArea.Content = _welcomeView;
                InSessionControls.Visibility = Visibility.Collapsed;
                PreSessionControls.Visibility = Visibility.Visible;

                // CORRECCIÓN: En lugar de limpiar el ItemsControl viejo, reiniciamos el Menú
                ScreenContextMenu.Items.Clear();
                ScreenContextMenu.Items.Add(new MenuItem { Header = "Esperando pantallas...", IsEnabled = false });

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
            Dispatcher.Invoke(() =>
            {
                // Limpiamos los items actuales del menú context
                ScreenContextMenu.Items.Clear();

                if (screenNames == null || screenNames.Count == 0)
                {
                    var emptyItem = new MenuItem { Header = "No hay pantallas disponibles", IsEnabled = false };
                    ScreenContextMenu.Items.Add(emptyItem);
                    return;
                }

                for (int i = 0; i < screenNames.Count; i++)
                {
                    string name = screenNames[i];
                    int index = i;

                    var menuItem = new MenuItem
                    {
                        Header = name,
                        // Puedes agregar un icono aquí si deseas
                    };

                    menuItem.Click += (s, e) => SwitchScreen(index);

                    ScreenContextMenu.Items.Add(menuItem);
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

        private void MenuButton_Click(object sender, RoutedEventArgs e)
        {
            if (MenuButton.ContextMenu != null)
            {
                MenuButton.ContextMenu.PlacementTarget = MenuButton;
                MenuButton.ContextMenu.IsOpen = true;
            }
        }
    }
}