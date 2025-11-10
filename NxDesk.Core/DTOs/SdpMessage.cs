namespace NxDesk.Core.DTOs
{
    /// <summary>
    /// Este DTO (Data Transfer Object) se usa para
    /// intercambiar los mensajes de establecimiento de WebRTC
    /// a través del SignalingServer.
    /// </summary>
    public class SdpMessage
    {
        /// <summary>
        /// El tipo de mensaje.
        /// Puede ser "offer", "answer" o "ice-candidate".
        /// </summary>
        public string? Type { get; set; }

        /// <summary>
        /// El contenido del mensaje.
        /// Si Type es "offer" o "answer", esto será el string SDP.
        /// Si Type es "ice-candidate", esto será el JSON del candidato ICE.
        /// </summary>
        public string? Payload { get; set; }
    }
}
