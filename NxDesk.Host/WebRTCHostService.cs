using Microsoft.AspNetCore.SignalR.Client;
using Newtonsoft.Json;
using NxDesk.Core.DTOs;
using NxDesk.Core.Services;
using NxDesk.Host.Services;
using SIPSorcery.Net;
using SIPSorceryMedia.Abstractions;
using System.Drawing;
using System.Drawing.Imaging;
using System.Text;
using System.Windows.Forms;
using SdpMessage = NxDesk.Core.DTOs.SdpMessage;

namespace NxDesk.Host
{
    public class WebRTCHostService : IDisposable
    {
        private readonly string _roomId;
        private readonly string _serverUrl;
        private readonly IdentityService _identityService;
        private NetworkDiscoveryService _discoveryService;

        private readonly ILogger<WebRTCHostService> _logger;
        private HubConnection _hubConnection;
        private RTCPeerConnection _peerConnection;
        private RTCDataChannel _dataChannel;
        private System.Threading.Timer _screenCaptureTimer;
        private bool _isCapturing = false;
        private const int FRAME_RATE = 20;

        private int _currentScreenIndex = 0;
        private Screen[] _allScreens;
        private readonly object _captureLock = new object();

        public WebRTCHostService(ILogger<WebRTCHostService> logger, IConfiguration configuration)
        {
            _logger = logger;

            _serverUrl = configuration.GetValue<string>("SignalR:ServerUrl");
            if (string.IsNullOrEmpty(_serverUrl) || _serverUrl.Contains("[IP_DE_TU_SERVIDOR]"))
            {
                _logger.LogError("SignalR:ServerUrl no válido en appsettings.json. Usando localhost por defecto.");
                _serverUrl = "https://localhost:7099/signalinghub";
            }

            _identityService = new IdentityService();
            _roomId = _identityService.MyID;

            _allScreens = Screen.AllScreens;
            _logger.LogInformation("Pantallas detectadas: {ScreenCount}", _allScreens.Length);
            for (int i = 0; i < _allScreens.Length; i++)
            {
                _logger.LogInformation("  - Pantalla {Index}: {DeviceName}", i, _allScreens[i].DeviceName);
            }

            _logger.LogInformation("IdentityService cargado. Este Host ID es: {HostID}", _roomId);

            _discoveryService = new NetworkDiscoveryService(_identityService.MyID, _identityService.MyAlias);
        }

        public async Task StartAsync()
        {
            _logger.LogInformation("Iniciando servicio de Host NxDesk.");
            _discoveryService.Start();
            await InitializeSignalR();
        }

        private async Task InitializeSignalR()
        {
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
                await HandleSignalingMessageAsync(message);
            });

            try
            {
                await _hubConnection.StartAsync();
                _logger.LogInformation("Conectado a SignalR. Uniéndose a la sala: {RoomId}", _roomId);
                await _hubConnection.InvokeAsync("JoinRoom", _roomId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al conectar a SignalR.");
            }
        }

        private async Task HandleSignalingMessageAsync(SdpMessage message)
        {
            if (message.SenderId == _hubConnection.ConnectionId) return;

            try
            {
                switch (message.Type)
                {
                    case "offer":
                        await InitializePeerConnection();

                        var offerSdp = SDP.ParseSDPDescription(message.Payload);
                        var offerInit = new RTCSessionDescriptionInit
                        {
                            type = RTCSdpType.offer,
                            sdp = offerSdp.ToString()
                        };

                        _peerConnection.setRemoteDescription(offerInit);
                        await CreateAnswerAsync();
                        break;

                    case "ice-candidate":
                        if (_peerConnection == null) break;
                        var candidateInit = JsonConvert.DeserializeObject<RTCIceCandidateInit>(message.Payload);
                        _peerConnection.addIceCandidate(candidateInit);
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error manejando mensaje de señalización.");
            }
        }

        private async Task InitializePeerConnection()
        {
            var config = new RTCConfiguration
            {
                iceServers = new List<RTCIceServer>
                {
                    new RTCIceServer { urls = "stun:stun.l.google.com:19302" }
                }
            };

            _peerConnection = new RTCPeerConnection(config);

            var videoFormats = new List<VideoFormat> { new VideoFormat(VideoCodecsEnum.VP8, 96) };
            var videoTrack = new MediaStreamTrack(videoFormats, MediaStreamStatusEnum.SendOnly);
            _peerConnection.addTrack(videoTrack);

            StartScreenCapture();
            _logger.LogInformation("Captura de pantalla iniciada.");

            _peerConnection.ondatachannel += (dc) =>
            {
                _dataChannel = dc;

                _dataChannel.onopen += () =>
                {
                    _logger.LogInformation("DataChannel abierto.");
                    SendScreenList();
                };

                dc.onmessage += (RTCDataChannel channel, DataChannelPayloadProtocols protocol, byte[] data) =>
                {
                    OnInputReceived(channel, protocol, data);
                };

                _logger.LogInformation("Canal de datos '{Label}' abierto.", dc.label);
            };

            _peerConnection.onicecandidate += async (candidate) =>
            {
                if (candidate != null && !string.IsNullOrWhiteSpace(candidate.candidate))
                {
                    var iceMsg = new SdpMessage
                    {
                        Type = "ice-candidate",
                        Payload = candidate.toJSON(),
                        SenderId = _hubConnection.ConnectionId
                    };
                    await _hubConnection.InvokeAsync("RelayMessage", _roomId, iceMsg);
                }
            };

            await Task.CompletedTask;
        }

        private void SendScreenList()
        {
            if (_dataChannel == null || _dataChannel.readyState != RTCDataChannelState.open) return;

            try
            {
                _allScreens = Screen.AllScreens;
                var screenNames = _allScreens.Select((s, i) => $"Pantalla {i + 1} ({s.Bounds.Width}x{s.Bounds.Height})").ToList();

                var screenInfo = new ScreenInfoPayload { ScreenNames = screenNames };

                var message = new DataChannelMessage
                {
                    Type = "system:screen_info",
                    Payload = JsonConvert.SerializeObject(screenInfo)
                };

                _dataChannel.send(JsonConvert.SerializeObject(message));
                _logger.LogInformation("Lista de pantallas enviada al cliente ({Count} pantallas).", screenNames.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error enviando lista de pantallas.");
            }
        }

        private async void StartScreenCapture()
        {
            _isCapturing = true;

            await Task.Run(async () =>
            {
                // Buffer para detección simple de movimiento (opcional pero recomendado)
                byte[] lastFrameHash = null;

                while (_isCapturing && _peerConnection != null)
                {
                    var startTime = DateTime.Now;

                    try
                    {
                        byte[] frameData = null;

                        // Bloqueamos para capturar de forma segura
                        lock (_captureLock)
                        {
                            // Llamamos a la captura optimizada (ver Paso 2)
                            frameData = CaptureScreenOptimized();
                        }

                        if (frameData != null && frameData.Length > 0)
                        {
                            // OPTIMIZACIÓN DE ANCHO DE BANDA:
                            // Si el frame es idéntico al anterior (o muy similar por tamaño), lo saltamos.
                            // Esto reduce el uso de red drásticamente cuando la pantalla está quieta.
                            if (lastFrameHash != null && frameData.Length == lastFrameHash.Length)
                            {
                                // Si quieres ser más estricto, compara bytes, pero longitud es un filtro rápido barato.
                                // En pantallas estáticas, esto elimina la "interferencia" por saturación.
                            }
                            else
                            {
                                uint timestamp = (uint)(DateTime.UtcNow.Ticks / TimeSpan.TicksPerMillisecond);
                                _peerConnection.SendVideo(timestamp, frameData);
                                lastFrameHash = frameData;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        // Ignorar errores puntuales para no romper el stream
                    }

                    // CONTROL DE FPS:
                    // Calculamos cuánto tardó el proceso y esperamos solo lo necesario.
                    // 20 FPS = 50ms por cuadro.
                    var elapsed = (DateTime.Now - startTime).TotalMilliseconds;
                    var waitTime = 50 - (int)elapsed;

                    if (waitTime > 0)
                        await Task.Delay(waitTime);
                    else
                        // Si tardamos mucho, cedemos el control un momento para no colgar la CPU
                        await Task.Yield();
                }
            });
        }

        private byte[] CaptureScreenOptimized()
        {
            try
            {
                // Validación de índice de pantalla
                if (_currentScreenIndex >= _allScreens.Length || _currentScreenIndex < 0) _currentScreenIndex = 0;
                var bounds = _allScreens[_currentScreenIndex].Bounds;

                // 1. DEFINIR TAMAÑO OBJETIVO (Downscaling)
                // Si la pantalla es mayor a 1280x720 (HD), la reducimos. 
                // Para escritorio remoto fluido, 1280x720 u 1600x900 es el estándar dorado.
                int targetWidth = bounds.Width;
                int targetHeight = bounds.Height;

                if (targetWidth > 1600) // Si es muy grande (ej. 1920 o 4K)
                {
                    float ratio = (float)bounds.Height / bounds.Width;
                    targetWidth = 1600; // Forzar ancho máximo
                    targetHeight = (int)(targetWidth * ratio);
                }

                // Creamos el bitmap con el tamaño ajustado directamente
                using (var bitmap = new Bitmap(targetWidth, targetHeight, PixelFormat.Format32bppArgb))
                using (var graphics = Graphics.FromImage(bitmap))
                {
                    // Configuración de calidad gráfica para que el redimensionado se vea nítido y no borroso
                    graphics.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighSpeed;
                    graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.Bicubic;
                    graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighSpeed;

                    // Copiamos y redimensionamos al vuelo (el driver lo hace rápido)
                    graphics.CopyFromScreen(bounds.X, bounds.Y, 0, 0, new Size(targetWidth, targetHeight), CopyPixelOperation.SourceCopy);

                    // Codificar a JPEG
                    using (var ms = new MemoryStream())
                    {
                        // Buscamos el codec JPEG
                        var encoder = ImageCodecInfo.GetImageEncoders().FirstOrDefault(c => c.MimeType == "image/jpeg");

                        var encoderParams = new EncoderParameters(1);
                        // CALIDAD: 75 es el balance perfecto. 
                        // 60 se ve mal (bloques), 90 es muy pesado (lento).
                        encoderParams.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 75L);

                        bitmap.Save(ms, encoder, encoderParams);
                        return ms.ToArray();
                    }
                }
            }
            catch
            {
                return Array.Empty<byte>();
            }
        }

        private void SwitchCaptureScreen(int screenIndex)
        {
            lock (_captureLock)
            {
                if (screenIndex >= 0 && screenIndex < _allScreens.Length)
                {
                    _logger.LogInformation("Cambiando a Pantalla {Index}", screenIndex);
                    _currentScreenIndex = screenIndex;
                }
            }
        }

        private async Task CreateAnswerAsync()
        {
            var answer = _peerConnection.createAnswer();
            await _peerConnection.setLocalDescription(answer);

            var answerMsg = new SdpMessage
            {
                Type = "answer",
                Payload = answer.sdp,
                SenderId = _hubConnection.ConnectionId
            };

            await _hubConnection.InvokeAsync("RelayMessage", _roomId, answerMsg);
            _logger.LogInformation("Answer enviada.");
        }

        private void OnInputReceived(RTCDataChannel dc, DataChannelPayloadProtocols protocol, byte[] data)
        {
            try
            {
                if (protocol != DataChannelPayloadProtocols.WebRTC_String) return;

                var json = Encoding.UTF8.GetString(data);
                var message = JsonConvert.DeserializeObject<DataChannelMessage>(json);

                if (message == null) return;

                if (message.Type == "system:get_screens")
                {
                    _logger.LogInformation("Recibida solicitud de lista de pantallas.");
                    SendScreenList();
                    return;
                }

                if (message.Type != "input") return;

                var input = JsonConvert.DeserializeObject<InputEvent>(message.Payload);
                if (input == null) return;

                switch (input.EventType)
                {
                    case "mousemove":
                        if (input.X.HasValue && input.Y.HasValue)
                        {
                            var screenBounds = _allScreens[_currentScreenIndex].Bounds;
                            double absX = screenBounds.X + (input.X.Value * screenBounds.Width);
                            double absY = screenBounds.Y + (input.Y.Value * screenBounds.Height);
                            InputSimulator.SimulateMouseMove(input.X.Value, input.Y.Value, _currentScreenIndex);
                        }
                        break;

                    case "mousedown":
                        InputSimulator.SimulateMouseDown(input.Button);
                        break;

                    case "mouseup":
                        InputSimulator.SimulateMouseUp(input.Button);
                        break;

                    case "mousewheel":
                        if (input.Delta.HasValue)
                            InputSimulator.SimulateMouseWheel((int)input.Delta.Value);
                        break;

                    case "keydown":
                    case "keyup":
                        if (!string.IsNullOrEmpty(input.Key))
                        {
                            if (Enum.TryParse<Keys>(input.Key, true, out Keys winKey))
                            {
                                InputSimulator.SimulateKeyEvent((byte)winKey, input.EventType == "keydown");
                            }
                        }
                        break;

                    case "control":
                        if (input.Command == "switch_screen" && input.Value.HasValue)
                        {
                            SwitchCaptureScreen(input.Value.Value);
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error procesando mensaje DataChannel.");
            }
        }

        public void Dispose()
        {
            _isCapturing = false;
            _screenCaptureTimer?.Dispose();
            _dataChannel?.close();
            _peerConnection?.close();
            _discoveryService?.Stop();
            _hubConnection?.DisposeAsync().AsTask().Wait();
        }
    }
}