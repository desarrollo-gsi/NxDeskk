using Microsoft.AspNetCore.SignalR;
using System.Threading.Tasks;
using NxDesk.Core.DTOs; // ¡Importante! Referencia a tu proyecto Core
using System;

namespace NxDesk.SignalingServer.Hubs
{
    /// <summary>
    /// Este es el "lobby" de SignalR. Actúa como un simple retransmisor (relay).
    /// No entiende qué es un "offer" o "answer", solo pasa los mensajes.
    /// </summary>
    public class SignalingHub : Hub
    {
        /// <summary>
        /// Llamado por un cliente (Host o Cliente) para unirse a una "sala" de conexión.
        /// El 'roomId' será el Host ID único.
        /// </summary>
        /// <param name="roomId">El ID de la sala (Host ID) a la que unirse.</param>
        public async Task JoinRoom(string roomId)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, roomId);

            // Notifica al otro participante (si ya está en la sala) que alguien se ha unido.
            await Clients.OthersInGroup(roomId).SendAsync("ParticipantJoined");
        }

        /// <summary>
        /// Retransmite un mensaje SDP (offer, answer) o un candidato ICE al otro
        /// participante en la sala.
        /// </summary>
        /// <param name="roomId">El ID de la sala (Host ID).</param>
        /// <param name="message">El mensaje SdpMessage (serializado por el cliente).</param>
        public async Task RelayMessage(string roomId, SdpMessage message)
        {
            // Envía el mensaje a TODOS los demás en la sala, excepto al remitente.
            await Clients.OthersInGroup(roomId).SendAsync("ReceiveMessage", message);
        }

        /// <summary>
        /// Maneja la desconexión de un cliente.
        /// </summary>
        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            // Opcional: Podrías notificar al otro miembro de la sala que el par se ha desconectado.
            // (Esto requiere rastrear en qué sala estaba cada ConnectionId)
            await base.OnDisconnectedAsync(exception);
        }
    }
}