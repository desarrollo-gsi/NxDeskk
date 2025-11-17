using NxDesk.Core.DTOs;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace NxDesk.Client.Views
{
    public partial class RemoteViewControl : UserControl
    {
        public event Action<InputEvent>? OnInputEvent;

        public RemoteViewControl()
        {
            InitializeComponent();

            VideoImage.MouseMove += VideoImage_MouseMove;
            VideoImage.MouseDown += VideoImage_MouseDown;
            VideoImage.MouseUp += VideoImage_MouseUp;
            VideoImage.MouseWheel += VideoImage_MouseWheel;
        }

        public void SetFrame(BitmapSource frame)
        {
            VideoImage.Source = frame;
        }

        private void Send(InputEvent ev)
        {
            OnInputEvent?.Invoke(ev);
        }

        private void VideoImage_MouseMove(object sender, MouseEventArgs e)
        {
            var pos = e.GetPosition(VideoImage);

            Send(new InputEvent
            {
                EventType = "mousemove",
                X = pos.X / VideoImage.ActualWidth,
                Y = pos.Y / VideoImage.ActualHeight
            });
        }

        private void VideoImage_MouseDown(object sender, MouseButtonEventArgs e)
        {
            var pos = e.GetPosition(VideoImage);
            Send(new InputEvent
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
            Send(new InputEvent
            {
                EventType = "mouseup",
                Button = e.ChangedButton.ToString().ToLower(),
                X = pos.X / VideoImage.ActualWidth,
                Y = pos.Y / VideoImage.ActualHeight
            });
        }

        private void VideoImage_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            Send(new InputEvent
            {
                EventType = "mousewheel",
                Delta = e.Delta
            });
        }
    }
}
