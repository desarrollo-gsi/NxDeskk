using NxDesk.Client.Services;
using NxDesk.Client.Views;
using NxDesk.Client.Views.WelcomeView.Models;
using NxDesk.Client.Views.WelcomeView.ViewModel;
using NxDesk.Core.DTOs;
using System;
using System.Collections.Generic; // <--- AÑADIDO
using System.Diagnostics;       // <--- AÑADIDO
using System.Windows;
using System.Windows.Controls;    // <--- AÑADIDO
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace NxDesk.Client
{
    public partial class MainWindow : Window
    {
        private readonly SignalingService _signalingService;
        private readonly WebRTCService _webRTCService;
        private WelcomeViewControl _welcomeView;
        private RemoteViewControl? _remoteView;

        public MainWindow()
        {
            InitializeComponent();

            _signalingService = new SignalingService();
            _webRTCService = new WebRTCService(_signalingService);
            _webRTCService.OnVideoFrameReady += UpdateVideoFrame;
            _webRTCService.OnConnectionStateChanged += UpdateStatus;
            this.Closing += async (s, e) => await _webRTCService.DisposeAsync();
            this.KeyDown += MainWindow_KeyDown;
            this.KeyUp += MainWindow_KeyUp;

            _welcomeView = new WelcomeViewControl();

            if (_welcomeView.DataContext is WelcomeViewModel vm)
            {
                vm.OnDeviceSelected += HandleDeviceSelected;
            }

            ContentArea.Content = _welcomeView;

            // Conecta el botón de Conectar (de la barra Pre-Sesión)
            ConnectButton.Click += ConnectButton_Click;

            // Conecta el botón de Desconectar (de la barra En-Sesión)
            DisconnectButton.Click += DisconnectButton_Click;
        }

        // --- 1. MÉTODO DE CONEXIÓN PRIVADO ---
        private void StartConnection(string connectionId)
        {
            if (string.IsNullOrWhiteSpace(connectionId))
            {
                StatusTextBlock.Text = "Error: El ID no puede estar vacío.";
                return;
            }

            Dispatcher.Invoke(async () =>
            {
                _remoteView = new RemoteViewControl();
                _remoteView.OnInputEvent += ev => _webRTCService.SendInputEvent(ev);

                ContentArea.Content = _remoteView;

                // --- CAMBIAR LA VISIBILIDAD DE LAS BARRAS DE HERRAMIENTAS ---
                PreSessionControls.Visibility = Visibility.Collapsed;
                InSessionControls.Visibility = Visibility.Visible;
                // -------------------------------------------------------------

                await _webRTCService.StartConnectionAsync(connectionId);

                // --- SIMULACIÓN DE RECEPCIÓN DE PANTALLAS ---
                // TODO: Reemplazar esto con la información real del Host
                // que deberías recibir a través del DataChannel o al conectar.
                var dummyScreenNames = new List<string> { "Pantalla 1", "Pantalla 2" };
                PopulateScreenButtons(dummyScreenNames);
                // --------------------------------------------------
            });
        }

        // --- 2. MÉTODO DE CLIC DE TARJETA ---
        private void HandleDeviceSelected(DiscoveredDevice device)
        {
            StartConnection(device.ConnectionID);
        }

        // --- 3. MÉTODO DE CLIC DE BOTÓN CONECTAR ---
        private void ConnectButton_Click(object sender, RoutedEventArgs e)
        {
            StartConnection(RoomIdTextBox.Text);
        }

        // --- 4. NUEVO MÉTODO PARA DESCONECTAR ---
        private void DisconnectButton_Click(object sender, RoutedEventArgs e)
        {
            Dispatcher.Invoke(async () =>
            {
                // 1. Cierra la conexión WebRTC
                await _webRTCService.DisposeAsync();

                // 2. Muestra la pantalla de bienvenida
                ContentArea.Content = _welcomeView;

                // 3. Restaura la barra de herramientas superior
                InSessionControls.Visibility = Visibility.Collapsed;
                PreSessionControls.Visibility = Visibility.Visible;

                // 4. Limpiar botones dinámicos
                ScreenButtonsItemsControl.Items.Clear();

                // 5. Resetea el texto de estado
                StatusTextBlock.Text = "Desconectado. Esperando conexión...";
            });
        }

        // --- 5. NUEVO MÉTODO PARA CAMBIAR DE PANTALLA (Placeholder) ---
        private void SwitchScreen(int screenIndex)
        {
            // TODO: Enviar un mensaje al Host a través del DataChannel
            // para solicitar el cambio de pantalla.

            string screenName = $"Pantalla {screenIndex + 1}";
            StatusTextBlock.Text = $"Cambiando a {screenName}...";
            Debug.WriteLine($"[NxDesk Client] Solicitando cambio a {screenName}");

            // Ejemplo de cómo podrías enviar el evento (requiere implementar en WebRTCService)
            // _webRTCService.SendInputEvent(new InputEvent 
            // { 
            //    EventType = "control", 
            //    Command = "switch_screen", 
            //    Value = screenIndex 
            // });
        }

        // --- 6. NUEVO MÉTODO PARA GENERAR BOTONES DE PANTALLA ---
        private void PopulateScreenButtons(List<string> screenNames)
        {
            // Asegurarnos de que corra en el Hilo de UI
            Dispatcher.Invoke(() =>
            {
                ScreenButtonsItemsControl.Items.Clear(); // Limpiar botones anteriores

                for (int i = 0; i < screenNames.Count; i++)
                {
                    var screenName = screenNames[i];
                    var screenIndex = i; // Importante: Capturar el índice para el lambda

                    Button btn = new Button
                    {
                        Content = screenName,
                        Style = (Style)FindResource("ModernTextButton"),
                        Margin = new Thickness(5, 0, 5, 0)
                    };

                    // Asignar el evento Click para llamar a tu método SwitchScreen
                    btn.Click += (s, e) => SwitchScreen(screenIndex);

                    ScreenButtonsItemsControl.Items.Add(btn);
                }
            });
        }

        // --- (El resto de tus métodos) ---

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