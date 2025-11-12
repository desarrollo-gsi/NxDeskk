using Microsoft.AspNetCore.SignalR.Client;
using NxDesk.Core.DTOs;
using System.Diagnostics;
using System.Net.Http;

namespace NxDesk.Client.Services
{
    public class SignalingService : IAsyncDisposable
    {
        private const string SERVER_URL = "https://localhost:7099/signalinghub";
        private HubConnection _hubConnection;
        private string _roomId;
        public event Func<SdpMessage, Task> OnMessageReceived; 

        public SignalingService()
        {
            _hubConnection = new HubConnectionBuilder()
                .WithUrl(SERVER_URL, options =>
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
                await _hubConnection.InvokeAsync("JoinRoom", _roomId);
                return true; 
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error de conexión de SignalR: {ex.Message}");
                return false; 
            }
        }

        public async Task RelayMessageAsync(SdpMessage message)
        {
            if (_hubConnection.State != HubConnectionState.Connected)
            {
                throw new InvalidOperationException("No se puede enviar el mensaje, SignalR no está conectado.");
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