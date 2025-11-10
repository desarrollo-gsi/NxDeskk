using Microsoft.AspNetCore.SignalR.Client;
using NxDesk.Core.DTOs;
using System;
using System.Threading.Tasks;

namespace NxDesk.Client.Services
{
    /// <summary>
    /// Maneja la conexión de bajo nivel con el Hub de SignalR.
    /// Su único trabajo es retransmitir mensajes entre la app y el servidor.
    /// No sabe nada sobre WebRTC, solo pasa mensajes SdpMessage.
    /// </summary>
    public class SignalingService : IAsyncDisposable
    {
        // TODO: Mover esto a un archivo de configuración (appsettings.json)
        private const string SERVER_URL = "https://localhost:7123/signalinghub";

        private HubConnection _hubConnection;
        private string _roomId;

        // Eventos para que el WebRTCService se suscriba
        public event Func<SdpMessage, Task> OnMessageReceived;
        public event Func<Task> OnParticipantJoined;

        public SignalingService()
        {
            _hubConnection = new HubConnectionBuilder()
                .WithUrl(SERVER_URL)
                .WithAutomaticReconnect()
                .Build();

            // Configura los listeners para los mensajes *entrantes* del servidor
            _hubConnection.On<SdpMessage>("ReceiveMessage", async (message) =>
            {
                if (OnMessageReceived != null)
                {
                    await OnMessageReceived(message);
                }
            });

            _hubConnection.On("ParticipantJoined", async () =>
            {
                if (OnParticipantJoined != null)
                {
                    await OnParticipantJoined();
                }
            });
        }

        /// <summary>
        /// Inicia la conexión con el servidor y se une a la sala.
        /// </summary>
        public async Task ConnectAsync(string roomId)
        {
            if (_hubConnection.State == HubConnectionState.Connected)
            {
                return;
            }

            _roomId = roomId;
            try
            {
                await _hubConnection.StartAsync();
                await _hubConnection.InvokeAsync("JoinRoom", _roomId);
            }
            catch (Exception ex)
            {
                // TODO: Manejar el error de conexión (ej. notificar a la UI)
                Console.WriteLine($"Error de conexión de SignalR: {ex.Message}");
            }
        }

        /// <summary>
        /// Envía un mensaje de señalización (offer, answer, ice) al otro par.
        /// </summary>
        public async Task RelayMessageAsync(SdpMessage message)
        {
            if (_hubConnection.State != HubConnectionState.Connected)
            {
                throw new InvalidOperationException("No se puede enviar el mensaje, SignalR no está conectado.");
            }
            await _hubConnection.InvokeAsync("RelayMessage", _roomId, message);
        }

        /// <summary>
        /// Cierra la conexión de SignalR.
        /// </summary>
        public async ValueTask DisposeAsync()
        {
            if (_hubConnection != null)
            {
                await _hubConnection.DisposeAsync();
            }
        }
    }
}