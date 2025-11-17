using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using NxDesk.Client.Services;
using System.Windows;
using NxDesk.Core.Models; 
using NxDesk.Core.Services;
using System.Diagnostics;

namespace NxDesk.Client.Views.WelcomeView.ViewModel
{
    public class WelcomeViewModel : INotifyPropertyChanged
    {
        public event Action<DiscoveredDevice> OnDeviceSelected;

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private NetworkDiscoveryService _discoveryService;
        public string MyId { get; private set; }

        private ObservableCollection<DiscoveredDevice> _discoveredDevices;
        public ObservableCollection<DiscoveredDevice> DiscoveredDevices
        {
            get { return _discoveredDevices; }
            set { _discoveredDevices = value; OnPropertyChanged(); }
        }

        public WelcomeViewModel()
        {
            DiscoveredDevices = new ObservableCollection<DiscoveredDevice>();

            var identityService = new IdentityService();

            MyId = identityService.MyID;

            string myAlias = identityService.MyAlias;

            Debug.WriteLine($"[NxDesk VM] Mi ID único es: {MyId}");
            Debug.WriteLine($"[NxDesk VM] Mi Alias es: {myAlias}");

            _discoveryService = new NetworkDiscoveryService(MyId, myAlias);
            _discoveryService.OnDeviceDiscovered += HandleDeviceDiscovered;
            _discoveryService.Start();
        }

        private void HandleDeviceDiscovered(DiscoveredDevice device)
        {
            if (device.ConnectionID == MyId)
            {
                Debug.WriteLine("[NxDesk VM] Encontrado a sí mismo, ignorando.");
                return; 
            }

            Application.Current.Dispatcher.Invoke(() =>
            {
                bool alreadyExists = false;
                foreach (var d in DiscoveredDevices)
                {
                    if (d.ConnectionID == device.ConnectionID)
                    {
                        alreadyExists = true;
                        break;
                    }
                }

                if (!alreadyExists)
                {
                    Console.WriteLine($"[NxDesk VM] Dispositivo encontrado y añadido: {device.Alias} ({device.ConnectionID})");
                    Debug.WriteLine($"[NxDesk VM] Añadiendo dispositivo a la lista: {device.Alias}");

                    DiscoveredDevices.Add(device);
                }
            });
        }

        public void SelectDevice(DiscoveredDevice device)
        {
            Debug.WriteLine($"[NxDesk VM] Dispositivo seleccionado: {device.Alias} ({device.ConnectionID})");

            OnDeviceSelected?.Invoke(device);
        }
    }
}