namespace NxDesk.Core.DTOs
{
    /// <summary>
    /// Este DTO representa un solo evento de input (mouse o teclado)
    /// enviado desde el Cliente (C#) al Host (C++).
    /// </summary>
    public class InputEvent
    {
        /// <summary>
        /// Tipo de evento: "mousemove", "mousedown", "mouseup", "keydown", "keyup", "mousewheel"
        /// </summary>
        public string? EventType { get; set; }

        // --- Propiedades del Mouse ---

        /// <summary>
        /// Posición X normalizada (de 0.0 a 1.0)
        /// </summary>
        public double? X { get; set; }

        /// <summary>
        /// Posición Y normalizada (de 0.0 a 1.0)
        /// </summary>
        public double? Y { get; set; }

        /// <summary>
        /// Botón presionado: "left", "right", "middle"
        /// </summary>
        public string? Button { get; set; }

        /// <summary>
        /// Delta de la rueda del mouse (positivo para arriba, negativo para abajo)
        /// </summary>
        public double? Delta { get; set; }

        // --- Propiedades del Teclado ---

        /// <summary>
        /// Código de la tecla (ej. "A", "Enter", "ControlLeft")
        /// </summary>
        public string? Key { get; set; }
    }
}
