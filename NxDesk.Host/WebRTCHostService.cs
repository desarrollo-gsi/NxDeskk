using Microsoft.AspNetCore.SignalR.Client;
using Newtonsoft.Json;
using NxDesk.Core.DTOs;
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
        private const string SERVER_URL = "https://localhost:7099/signalinghub";
        private readonly string _roomId;

        private readonly ILogger<WebRTCHostService> _logger;
        private HubConnection _hubConnection;
        private RTCPeerConnection _peerConnection;
        private RTCDataChannel _dataChannel;
        private System.Threading.Timer _screenCaptureTimer;
        private bool _isCapturing = false;
        private const int FRAME_RATE = 20;
        private int _currentScreenIndex = 0;
        private Screen[] _allScreens;

        public WebRTCHostService(ILogger<WebRTCHostService> logger)
        {
            _logger = logger;
            var identityService = new IdentityService();
            _roomId = identityService.MyID;

            _allScreens = Screen.AllScreens;
            _logger.LogInformation("Pantallas detectadas: {ScreenCount}", _allScreens.Length);
            for (int i = 0; i < _allScreens.Length; i++)
            {
                _logger.LogInformation("  - Pantalla {Index}: {DeviceName}", i, _allScreens[i].DeviceName);
            }

            _logger.LogInformation("IdentityService cargado. Este Host ID es: {HostID}", _roomId);
        }


        public async Task StartAsync()
        {
            _logger.LogInformation("Iniciando servicio de Host NxDesk.");
            await InitializeSignalR();
        }

        private async Task InitializeSignalR()
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
            if (message.SenderId == _hubConnection.ConnectionId)
            {
                _logger.LogInformation("Ignorando eco de mensaje propio (Tipo: {Type})", message.Type);
                return;
            }

            _logger.LogInformation("Mensaje recibido: {Type}", message.Type);

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

                // --- NUEVO: Enviar info de pantallas cuando el canal ABRA ---
                _dataChannel.onopen += () =>
                {
                    _logger.LogInformation("DataChannel abierto. Enviando información de pantallas...");
                    try
                    {
                        // 1. Crear la lista de nombres
                        var screenNames = _allScreens.Select((s, i) => $"Pantalla {i + 1} ({s.Bounds.Width}x{s.Bounds.Height})").ToList();

                        // 2. Crear el payload
                        var screenInfo = new ScreenInfoPayload { ScreenNames = screenNames };

                        // 3. Enviar el mensaje envuelto
                        var message = new DataChannelMessage
                        {
                            Type = "system:screen_info",
                            Payload = JsonConvert.SerializeObject(screenInfo)
                        };

                        _dataChannel.send(JsonConvert.SerializeObject(message));
                        _logger.LogInformation("Información de {Count} pantallas enviada.", screenNames.Count);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error al enviar información de pantallas.");
                    }
                };
                // -----------------------------------------------------

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
                        SenderId = _hubConnection.ConnectionId // ¡IMPORTANTE!
                    };
                    await _hubConnection.InvokeAsync("RelayMessage", _roomId, iceMsg);
                }
            };

            await Task.CompletedTask;
        }
        private void StartScreenCapture()
        {
            _isCapturing = true;
            int intervalMs = 1000 / FRAME_RATE;

            _screenCaptureTimer = new System.Threading.Timer(_ =>
            {
                if (!_isCapturing || _peerConnection == null) return;

                try
                {
                    var frame = CaptureScreen();
                    if (frame != null && frame.Length > 0)
                    {
                        uint timestamp = (uint)(DateTime.UtcNow.Ticks / TimeSpan.TicksPerMillisecond);
                        _peerConnection.SendVideo(timestamp, frame);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error capturando pantalla.");
                }

            }, null, 0, intervalMs);
        }

        private byte[] CaptureScreen()
        {
            try
            {
                if (_currentScreenIndex >= _allScreens.Length)
                {
                    _currentScreenIndex = 0;
                }
                var bounds = _allScreens[_currentScreenIndex].Bounds;

                using (var bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb))
                using (var graphics = Graphics.FromImage(bitmap))
                {
                    graphics.CopyFromScreen(bounds.X, bounds.Y, 0, 0, bounds.Size, CopyPixelOperation.SourceCopy);
                    using (var ms = new MemoryStream())
                    {
                        var encoder = ImageCodecInfo.GetImageEncoders().FirstOrDefault(c => c.MimeType == "image/jpeg");

                        if (encoder != null)
                        {
                            var encoderParams = new EncoderParameters(1);
                            encoderParams.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 85L);
                            bitmap.Save(ms, encoder, encoderParams);
                        }
                        else
                        {
                            bitmap.Save(ms, ImageFormat.Jpeg);
                        }

                        return ms.ToArray();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error en captura de pantalla");
                return Array.Empty<byte>();
            }
        }

        private void SwitchCaptureScreen(int screenIndex)
        {
            if (screenIndex >= 0 && screenIndex < _allScreens.Length)
            {
                _logger.LogInformation("Solicitud de cambio a pantalla {Index} recibida.", screenIndex);
                _currentScreenIndex = screenIndex;
            }
            else
            {
                _logger.LogWarning("Índice de pantalla inválido recibido: {Index}", screenIndex);
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
                SenderId = _hubConnection.ConnectionId // ¡IMPORTANTE!
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
                if (message == null || message.Type != "input")
                {
                    _logger.LogWarning("Mensaje de DataChannel desconocido recibido: {Type}", message?.Type);
                    return;
                }

                var input = JsonConvert.DeserializeObject<InputEvent>(message.Payload);
                if (input == null) return;

                switch (input.EventType)
                {
                    case "mousemove":
                        if (input.X.HasValue && input.Y.HasValue)
                            InputSimulator.SimulateMouseMove(input.X.Value, input.Y.Value);
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
                                byte keyCode = (byte)winKey;
                                InputSimulator.SimulateKeyEvent(keyCode, input.EventType == "keydown");
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
                _logger.LogWarning(ex, "Error procesando input.");
            }
        }

        public void Dispose()
        {
            _logger.LogInformation("Deteniendo servicio NxDesk Host.");

            _isCapturing = false;
            _screenCaptureTimer?.Dispose();
            _dataChannel?.close();
            _peerConnection?.close();

            _hubConnection?.DisposeAsync().AsTask().Wait();
        }
    }
}
