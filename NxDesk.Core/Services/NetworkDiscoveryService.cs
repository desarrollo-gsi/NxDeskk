using System.Net;
using System.Net.Sockets;
using System.Text;
using NxDesk.Core.Models;
using System.Diagnostics;

namespace NxDesk.Core.Services
{
    public class NetworkDiscoveryService
    {
        private const int DISCOVERY_PORT = 50002;
        private UdpClient _udpListener;

        public event Action<DiscoveredDevice> OnDeviceDiscovered;

        private string _myID;
        private string _myAlias;

        public NetworkDiscoveryService(string myID, string myAlias)
        {
            _myID = myID;
            _myAlias = myAlias;
        }

        public void Start()
        {
            Task.Run(() => StartListening());
            Task.Run(() => StartBroadcasting());
        }

        private async Task StartListening()
        {
            try
            {
                _udpListener = new UdpClient(DISCOVERY_PORT);
                _udpListener.EnableBroadcast = true;

                Debug.WriteLine($"[NxDesk Discovery] Listener iniciado en el puerto {DISCOVERY_PORT}");

                var fromEp = new IPEndPoint(IPAddress.Any, DISCOVERY_PORT);

                while (true)
                {
                    var result = await _udpListener.ReceiveAsync();
                    string data = Encoding.UTF8.GetString(result.Buffer);

                    Debug.WriteLine($"[NxDesk Discovery] Paquete recibido de {result.RemoteEndPoint.Address}: {data}");

                    if (data.StartsWith("NXDESK_DISCOVERY:"))
                    {
                        var parts = data.Split(':');
                        if (parts.Length == 3)
                        {
                            var device = new DiscoveredDevice
                            {
                                ConnectionID = parts[1],
                                Alias = parts[2],
                                IPAddress = result.RemoteEndPoint.Address.ToString()
                            };

                            Debug.WriteLine($"[NxDesk Discovery] Dispositivo parseado: {device.Alias} ({device.ConnectionID})");
                            OnDeviceDiscovered?.Invoke(device);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[NxDesk Discovery] FALLO EL LISTENER: {ex.Message}");
            }
        }

        private async Task StartBroadcasting()
        {
            var broadcaster = new UdpClient();
            broadcaster.EnableBroadcast = true;
            var broadcastAddress = new IPEndPoint(IPAddress.Broadcast, DISCOVERY_PORT);

            string message = $"NXDESK_DISCOVERY:{_myID}:{_myAlias}";
            byte[] data = Encoding.UTF8.GetBytes(message);

            try
            {
                Debug.WriteLine("[NxDesk Discovery] Broadcaster iniciado.");
                while (true)
                {
                    await broadcaster.SendAsync(data, data.Length, broadcastAddress);
                    await Task.Delay(5000);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[NxDesk Discovery] FALLO EL BROADCASTER: {ex.Message}");
            }
        }

        public void Stop()
        {
            _udpListener?.Close();
        }
    }
}
