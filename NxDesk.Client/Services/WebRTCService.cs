using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using NxDesk.Core.DTOs;
using SIPSorcery.Net;
using SIPSorceryMedia.Abstractions;
using System.Windows.Media.Imaging;
using System.Windows;
using SdpMessage = NxDesk.Core.DTOs.SdpMessage;

namespace NxDesk.Client.Services
{
    public class WebRTCService : IAsyncDisposable
    {
        private readonly SignalingService _signalingService;
        private RTCPeerConnection? _peerConnection;
        private RTCDataChannel? _dataChannel;
        private WriteableBitmap? _videoBitmap;
        private static readonly ILogger _logger = new Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory()
            .CreateLogger<WebRTCService>();

        public event Action<string>? OnConnectionStateChanged;
        public event Action<WriteableBitmap>? OnVideoFrameReady;

        public WebRTCService(SignalingService signalingService)
        {
            _signalingService = signalingService;
            _signalingService.OnMessageReceived += HandleSignalingMessageAsync;
        }

        public async Task StartConnectionAsync(string hostId)
        {
            OnConnectionStateChanged?.Invoke("Conectando al servidor de señalización...");

            bool connected = await _signalingService.ConnectAsync(hostId);

            if (!connected)
            {
                OnConnectionStateChanged?.Invoke("Error: No se pudo conectar al servidor.");
                return;
            }

            OnConnectionStateChanged?.Invoke("Conectado. Iniciando WebRTC...");
            var config = new RTCConfiguration
            {
                iceServers = new List<RTCIceServer>
                {
                    new RTCIceServer { urls = "stun:stun.l.google.com:19302" }
                }
            };

            _peerConnection = new RTCPeerConnection(config);

            _peerConnection.onicecandidate += async (candidate) =>
            {
                if (candidate != null && !string.IsNullOrWhiteSpace(candidate.candidate))
                {
                    var msg = new SdpMessage
                    {
                        Type = "ice-candidate",
                        Payload = candidate.toJSON()
                    };
                    await _signalingService.RelayMessageAsync(msg);
                }
            };

            _peerConnection.onconnectionstatechange += (state) =>
            {
                OnConnectionStateChanged?.Invoke($"P2P: {state}");
                _logger.LogInformation($"Connection state: {state}");
            };

            // CORRECCIÓN: Usar VP8 para coincidir con el Host
            var videoFormats = new List<VideoFormat>
            {
                new VideoFormat(VideoCodecsEnum.VP8, 96)
            };

            var videoTrack = new MediaStreamTrack(videoFormats, MediaStreamStatusEnum.RecvOnly);
            _peerConnection.addTrack(videoTrack);

            // CORRECCIÓN: Usar OnVideoFrameReceived para recibir frames de video
            _peerConnection.OnVideoFrameReceived += (endpoint, timestamp, frame, format) =>
            {
                try
                {
                    _logger.LogInformation($"Frame recibido: {frame.Length} bytes, formato: {format}");

                    Application.Current?.Dispatcher.Invoke(() =>
                    {
                        try
                        {
                            // Detectar dimensiones del frame
                            // Para RGB24: ancho * alto * 3 = tamaño del buffer
                            int expectedPixels = frame.Length / 3;
                            int width = (int)Math.Sqrt(expectedPixels * 16.0 / 9.0); // Asumir 16:9
                            width = (width / 4) * 4; // Redondear a múltiplo de 4
                            int height = (width * 9) / 16;

                            // Si no cuadra, usar dimensiones comunes
                            if (width * height * 3 != frame.Length)
                            {
                                // Intentar dimensiones comunes
                                var commonSizes = new[]
                                {
                                    (1920, 1080), (1280, 720), (1024, 768),
                                    (800, 600), (640, 480)
                                };

                                foreach (var (w, h) in commonSizes)
                                {
                                    if (w * h * 3 == frame.Length)
                                    {
                                        width = w;
                                        height = h;
                                        break;
                                    }
                                }
                            }

                            _logger.LogInformation($"Dimensiones detectadas: {width}x{height}");

                            // Crear o actualizar el bitmap
                            if (_videoBitmap == null ||
                                _videoBitmap.PixelWidth != width ||
                                _videoBitmap.PixelHeight != height)
                            {
                                _videoBitmap = new WriteableBitmap(
                                    width,
                                    height,
                                    96,
                                    96,
                                    System.Windows.Media.PixelFormats.Bgr24,
                                    null);
                            }

                            // Escribir los píxeles
                            int stride = width * 3;
                            _videoBitmap.WritePixels(
                                new System.Windows.Int32Rect(0, 0, width, height),
                                frame,
                                stride,
                                0);

                            OnVideoFrameReady?.Invoke(_videoBitmap);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning($"Error procesando frame: {ex.Message}");
                        }
                    });
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"Error en frame: {ex.Message}");
                }
            };

            // Crear canal de datos
            _dataChannel = await _peerConnection.createDataChannel("input-channel");
            _dataChannel.onopen += () =>
            {
                OnConnectionStateChanged?.Invoke("Canal de datos abierto");
                _logger.LogInformation("Data channel abierto");
            };
            _dataChannel.onclose += () =>
            {
                OnConnectionStateChanged?.Invoke("Canal de datos cerrado");
                _logger.LogInformation("Data channel cerrado");
            };

            await CreateOfferAsync();
        }

        private async Task CreateOfferAsync()
        {
            if (_peerConnection == null) return;

            OnConnectionStateChanged?.Invoke("Creando oferta...");

            var offer = _peerConnection.createOffer(null);
            await _peerConnection.setLocalDescription(offer);

            string sdpString = _peerConnection.localDescription?.sdp?.ToString() ?? string.Empty;

            _logger.LogInformation($"SDP Offer:\n{sdpString}");

            var msg = new SdpMessage
            {
                Type = "offer",
                Payload = sdpString
            };

            await _signalingService.RelayMessageAsync(msg);

            OnConnectionStateChanged?.Invoke("Oferta enviada. Esperando respuesta del host...");
        }

        private async Task HandleSignalingMessageAsync(SdpMessage message)
        {
            if (_peerConnection == null) return;

            switch (message.Type)
            {
                case "answer":
                    OnConnectionStateChanged?.Invoke("Respuesta recibida. Procesando...");
                    _logger.LogInformation($"SDP Answer:\n{message.Payload}");

                    var answerSdp = SDP.ParseSDPDescription(message.Payload);
                    var answerInit = new RTCSessionDescriptionInit
                    {
                        type = RTCSdpType.answer,
                        sdp = answerSdp.ToString()
                    };

                    var setRemoteResult = _peerConnection.setRemoteDescription(answerInit);

                    if (setRemoteResult != SetDescriptionResultEnum.OK)
                    {
                        OnConnectionStateChanged?.Invoke($"Error remoto: {setRemoteResult}");
                        _logger.LogError($"Error en setRemoteDescription: {setRemoteResult}");
                    }
                    else
                    {
                        OnConnectionStateChanged?.Invoke("Descripción remota (answer) aceptada.");
                        _logger.LogInformation("Answer aceptado correctamente");
                    }
                    break;

                case "ice-candidate":
                    OnConnectionStateChanged?.Invoke("Candidato ICE recibido.");
                    var candidateInit = JsonConvert.DeserializeObject<RTCIceCandidateInit>(message.Payload);
                    _peerConnection.addIceCandidate(candidateInit);
                    _logger.LogInformation($"ICE Candidate agregado: {message.Payload}");
                    break;
            }
        }

        public void SendInputEvent(InputEvent inputEvent)
        {
            if (_dataChannel?.readyState == RTCDataChannelState.open)
            {
                var json = JsonConvert.SerializeObject(inputEvent);
                _dataChannel.send(json);
                _logger.LogInformation($"Input enviado: {inputEvent.EventType}");
            }
            else
            {
                _logger.LogWarning($"Data channel no está abierto. Estado: {_dataChannel?.readyState}");
            }
        }

        public async ValueTask DisposeAsync()
        {
            _signalingService.OnMessageReceived -= HandleSignalingMessageAsync;

            await _signalingService.DisposeAsync();
            _dataChannel?.close();
            _peerConnection?.close();
        }
    }
}