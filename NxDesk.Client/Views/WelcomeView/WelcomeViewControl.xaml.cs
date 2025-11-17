// Contenido completo de NxDesk.Client/Views/WelcomeView/WelcomeViewControl.xaml.cs
using NxDesk.Core.Models; // <--- ¡¡AQUÍ ESTÁ LA CORRECCIÓN!!
using NxDesk.Client.Views.WelcomeView.ViewModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

// BORRA esta línea si existe: using NxDesk.Client.Views.WelcomeView.Models;

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
            if (sender is FrameworkElement fe && fe.DataContext is DiscoveredDevice device) // <-- Esto ahora compila
            {
                if (this.DataContext is WelcomeViewModel vm)
                {
                    vm.SelectDevice(device);
                }
            }
        }
    }
}