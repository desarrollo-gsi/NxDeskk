using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;
using NxDesk.Core.DTOs;
using System.Diagnostics;
using System.Net.Http;

namespace NxDesk.Client.Services
{
    public class SignalingService : IAsyncDisposable
    {
        private readonly string _serverUrl;
        private HubConnection _hubConnection;
        private string _roomId;

        public event Func<SdpMessage, Task> OnMessageReceived;

        public SignalingService()
        {
            var config = new ConfigurationBuilder()
                .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .Build();

            _serverUrl = config["SignalR:ServerUrl"];

            if (string.IsNullOrEmpty(_serverUrl) || _serverUrl.Contains("[IP_DE_TU_SERVIDOR]"))
            {
                Debug.WriteLine("----------------------------------------------------------------");
                Debug.WriteLine("[ERROR CRÍTICO] No se leyó la URL del servidor. Usando LOCALHOST.");
                Debug.WriteLine("Verifica que appsettings.json tenga 'SignalR': { 'ServerUrl': '...' }");
                Debug.WriteLine("----------------------------------------------------------------");
                _serverUrl = "https://localhost:7099/signalinghub";
            }
            else
            {
                Debug.WriteLine($"[SignalR] Conectando a: {_serverUrl}");
            }

            _hubConnection = new HubConnectionBuilder()
                .WithUrl(_serverUrl, options =>
                {
                    options.HttpMessageHandlerFactory = (handler) =>
                    {
                        if (handler is HttpClientHandler clientHandler)
                        {
                            clientHandler.ServerCertificateCustomValidationCallback =
                                (sender, certificate, chain, sslPolicyErrors) => true;
                        }
                        return handler;
                    };
                })
                .WithAutomaticReconnect() 
                .Build();

            _hubConnection.On<SdpMessage>("ReceiveMessage", async (message) =>
            {
                if (OnMessageReceived != null)
                {
                    await OnMessageReceived(message);
                }
            });
        }

        public string? GetConnectionId()
        {
            return _hubConnection?.ConnectionId;
        }

        public async Task<bool> ConnectAsync(string roomId)
        {
            if (_hubConnection.State == HubConnectionState.Connected)
            {
                return true;
            }

            _roomId = roomId;
            try
            {
                await _hubConnection.StartAsync();
                Debug.WriteLine($"[SignalR] Conectado. ID: {_hubConnection.ConnectionId}");

                await _hubConnection.InvokeAsync("JoinRoom", _roomId);
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SignalR Error] No se pudo conectar: {ex.Message}");
                return false;
            }
        }

        public async Task RelayMessageAsync(SdpMessage message)
        {
            if (_hubConnection.State != HubConnectionState.Connected)
            {
                Debug.WriteLine("[SignalR] Intento de envío fallido: No conectado.");
                return;
            }

            if (string.IsNullOrEmpty(message.SenderId))
            {
                message.SenderId = _hubConnection.ConnectionId;
            }

            await _hubConnection.InvokeAsync("RelayMessage", _roomId, message);
        }

        public async ValueTask DisposeAsync()
        {
            if (_hubConnection != null)
            {
                await _hubConnection.DisposeAsync();
            }
        }
    }
}