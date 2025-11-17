using NxDesk.Client.Views.WelcomeView.Models;
using NxDesk.Client.Views.WelcomeView.ViewModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace NxDesk.Client.Views
{
  
    public partial class WelcomeViewControl : UserControl
    {
        public WelcomeViewControl()
        {
            InitializeComponent();
        }

        private void DeviceCard_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is DiscoveredDevice device)
            {
                if (this.DataContext is WelcomeViewModel vm)
                {
                    vm.SelectDevice(device);
                }
            }
        }
    }
}