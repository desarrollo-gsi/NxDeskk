using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;
using NxDesk.Core.DTOs;
using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace NxDesk.Client.Services
{
    public class SignalingService : IAsyncDisposable
    {
        private readonly string _serverUrl;
        private HubConnection _hubConnection;
        private string _roomId;

        // Evento para recibir mensajes
        public event Func<SdpMessage, Task> OnMessageReceived;

        public SignalingService()
        {
            // 1. Cargar la configuración desde appsettings.json
            var config = new ConfigurationBuilder()
                .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .Build();

            // --- CORRECCIÓN CRÍTICA ---
            // Usamos el indexador directo ["Sección:Clave"] en lugar de GetConnectionString
            // Esto asegura que lea la URL de ngrok correctamente.
            _serverUrl = config["SignalR:ServerUrl"];

            // Validación de seguridad: Si falla la lectura, avisamos en debug pero intentamos usar localhost
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

            // 2. Configurar la conexión SignalR
            _hubConnection = new HubConnectionBuilder()
                .WithUrl(_serverUrl, options =>
                {
                    // Ignorar errores de certificado SSL (útil para ngrok/dev)
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
                .WithAutomaticReconnect() // Reintentar si se cae la red
                .Build();

            // 3. Escuchar mensajes del Hub
            _hubConnection.On<SdpMessage>("ReceiveMessage", async (message) =>
            {
                if (OnMessageReceived != null)
                {
                    await OnMessageReceived(message);
                }
            });
        }

        // Método necesario para obtener el ID de conexión actual (útil para evitar rebote de mensajes)
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

                // Unirse a la sala (que es el ID del Host)
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