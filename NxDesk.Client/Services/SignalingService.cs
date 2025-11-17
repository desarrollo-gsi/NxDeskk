// Contenido completo de NxDesk.Client/Services/SignalingService.cs
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration; // <-- Paquete NuGet requerido
using NxDesk.Core.DTOs;
using System; // <-- AÑADIDO para AppDomain
using System.Diagnostics;
using System.IO; // <-- AÑADIDO para SetBasePath
using System.Net.Http;
using System.Threading.Tasks; // <-- AÑADIDO

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
            // --- AÑADIDO: Leer config ---
            var config = new ConfigurationBuilder()
                .SetBasePath(AppDomain.CurrentDomain.BaseDirectory) // Necesita 'using System;'
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .Build();

            // Esto funcionará después de instalar los NuGets
            _serverUrl = config.GetConnectionString("SignalR:ServerUrl");
            if (string.IsNullOrEmpty(_serverUrl) || _serverUrl.Contains("[IP_DE_TU_SERVIDOR]"))
            {
                Debug.WriteLine("[SignalR] SignalR:ServerUrl no válido en appsettings.json. Usando localhost.");
                _serverUrl = "https://localhost:7099/signalinghub"; // Fallback
            }
            // -----------------------------

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