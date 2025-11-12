using NxDesk.Client.Services;
using NxDesk.Core.DTOs;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace NxDesk.Client
{
    public partial class MainWindow : Window
    {
        private readonly SignalingService _signalingService;
        private readonly WebRTCService _webRTCService;

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
            VideoImage.Focusable = true;
            VideoImage.Focus();
        }

        private void UpdateStatus(string status)
        {
            Dispatcher.Invoke(() => StatusTextBlock.Text = status);
        }

        private void UpdateVideoFrame(WriteableBitmap bitmap)
        {
            Dispatcher.Invoke(() => VideoImage.Source = bitmap);
        }

        private async void ConnectButton_Click(object sender, RoutedEventArgs e)
        {
            VideoImage.Focus(); 
            await _webRTCService.StartConnectionAsync(RoomIdTextBox.Text);
        }

        private void SendInput(InputEvent input)
        {
            _webRTCService.SendInputEvent(input);
        }

        private void VideoImage_MouseMove(object sender, MouseEventArgs e)
        {
            var pos = e.GetPosition(VideoImage);
            var normalizedX = pos.X / VideoImage.ActualWidth;
            var normalizedY = pos.Y / VideoImage.ActualHeight;

            if (normalizedX < 0 || normalizedX > 1 || normalizedY < 0 || normalizedY > 1) return;

            SendInput(new InputEvent
            {
                EventType = "mousemove",
                X = normalizedX,
                Y = normalizedY
            });
        }

        private void VideoImage_MouseDown(object sender, MouseButtonEventArgs e)
        {
            var pos = e.GetPosition(VideoImage);
            SendInput(new InputEvent
            {
                EventType = "mousedown",
                Button = e.ChangedButton.ToString().ToLower(), 
                X = pos.X / VideoImage.ActualWidth,
                Y = pos.Y / VideoImage.ActualHeight
            });
        }

        private void VideoImage_MouseUp(object sender, MouseButtonEventArgs e)
        {
            var pos = e.GetPosition(VideoImage);
            SendInput(new InputEvent
            {
                EventType = "mouseup",
                Button = e.ChangedButton.ToString().ToLower(),
                X = pos.X / VideoImage.ActualWidth,
                Y = pos.Y / VideoImage.ActualHeight
            });
        }

        private void VideoImage_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            SendInput(new InputEvent
            {
                EventType = "mousewheel",
                Delta = e.Delta 
            });
        }

        private void MainWindow_KeyDown(object sender, KeyEventArgs e)
        {
            SendInput(new InputEvent
            {
                EventType = "keydown",
                Key = e.Key.ToString() 
            });
        }

        private void MainWindow_KeyUp(object sender, KeyEventArgs e)
        {
            SendInput(new InputEvent
            {
                EventType = "keyup",
                Key = e.Key.ToString()
            });
        }
    }
}