using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using NxDesk.Core.DTOs;
using SIPSorcery.Net;
using SIPSorceryMedia.Abstractions;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

// Alias para evitar conflicto con SIPSorcery
using SdpMessage = NxDesk.Core.DTOs.SdpMessage;

namespace NxDesk.Host
{
    public class WebRTCHostService : IDisposable
    {
        private const string SERVER_URL = "https://localhost:7099/signalinghub";
        private const string ROOM_ID = "test-room";

        private readonly ILogger<WebRTCHostService> _logger;
        private HubConnection _hubConnection;
        private RTCPeerConnection _peerConnection;
        private RTCDataChannel _dataChannel;
        private Timer _screenCaptureTimer;
        private bool _isCapturing = false;
        private const int FRAME_RATE = 30; // 30 FPS

        public WebRTCHostService(ILogger<WebRTCHostService> logger)
        {
            _logger = logger;
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
                _logger.LogInformation("Conectado a SignalR. Uniéndose a la sala: {RoomId}", ROOM_ID);
                await _hubConnection.InvokeAsync("JoinRoom", ROOM_ID);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al conectar a SignalR.");
            }
        }

        private async Task HandleSignalingMessageAsync(SdpMessage message)
        {
            _logger.LogInformation("Mensaje de señalización recibido: {Type}", message.Type);

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

            // Configurar track de video para envío
            var videoFormats = new List<VideoFormat>
            {
                new VideoFormat(VideoCodecsEnum.VP8, 96)
            };

            var videoTrack = new MediaStreamTrack(
                videoFormats,
                MediaStreamStatusEnum.SendOnly);

            _peerConnection.addTrack(videoTrack);

            // Iniciar captura de pantalla personalizada
            StartScreenCapture();
            _logger.LogInformation("Captura de pantalla iniciada.");

            // Configurar canal de datos
            _peerConnection.ondatachannel += (dc) =>
            {
                _dataChannel = dc;
                _dataChannel.onmessage += (RTCDataChannel channel, DataChannelPayloadProtocols protocol, byte[] data) =>
                {
                    OnInputReceived(channel, protocol, data);
                };
                _logger.LogInformation("Canal de datos '{Label}' abierto.", dc.label);
            };

            // Configurar ICE
            _peerConnection.onicecandidate += async (candidate) =>
            {
                if (candidate != null && !string.IsNullOrWhiteSpace(candidate.candidate))
                {
                    var iceMsg = new SdpMessage
                    {
                        Type = "ice-candidate",
                        Payload = candidate.toJSON()
                    };
                    await _hubConnection.InvokeAsync("RelayMessage", ROOM_ID, iceMsg);
                }
            };

            await Task.CompletedTask;
        }

        private void StartScreenCapture()
        {
            _isCapturing = true;
            int intervalMs = 1000 / FRAME_RATE;

            _screenCaptureTimer = new Timer(_ =>
            {
                if (!_isCapturing || _peerConnection == null) return;

                try
                {
                    var frame = CaptureScreen();
                    if (frame != null && frame.Length > 0)
                    {
                        // Enviar el frame como video crudo RGB24
                        uint timestamp = (uint)(DateTime.UtcNow.Ticks / TimeSpan.TicksPerMillisecond);

                        // Log cada 30 frames (1 segundo a 30fps)
                        if (timestamp % 1000 < 35)
                        {
                            _logger.LogInformation($"Enviando frame: {frame.Length} bytes, timestamp: {timestamp}");
                        }

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
                // Obtener el tamaño de la pantalla principal
                var bounds = System.Windows.Forms.Screen.PrimaryScreen.Bounds;

                using (var bitmap = new Bitmap(bounds.Width, bounds.Height))
                using (var graphics = Graphics.FromImage(bitmap))
                {
                    // Capturar la pantalla
                    graphics.CopyFromScreen(bounds.X, bounds.Y, 0, 0, bounds.Size);

                    // Convertir a byte array en formato RGB24
                    var bitmapData = bitmap.LockBits(
                        new Rectangle(0, 0, bitmap.Width, bitmap.Height),
                        ImageLockMode.ReadOnly,
                        PixelFormat.Format24bppRgb);

                    try
                    {
                        int bytes = Math.Abs(bitmapData.Stride) * bitmap.Height;
                        byte[] rgbValues = new byte[bytes];
                        Marshal.Copy(bitmapData.Scan0, rgbValues, 0, bytes);

                        return rgbValues;
                    }
                    finally
                    {
                        bitmap.UnlockBits(bitmapData);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error en CaptureScreen");
                return Array.Empty<byte>();
            }
        }

        private async Task CreateAnswerAsync()
        {
            var answer = _peerConnection.createAnswer();
            await _peerConnection.setLocalDescription(answer);

            var answerMsg = new SdpMessage
            {
                Type = "answer",
                Payload = answer.sdp
            };

            await _hubConnection.InvokeAsync("RelayMessage", ROOM_ID, answerMsg);
            _logger.LogInformation("Respuesta (answer) enviada.");
        }

        private void OnInputReceived(RTCDataChannel dc, DataChannelPayloadProtocols protocol, byte[] data)
        {
            try
            {
                if (protocol == DataChannelPayloadProtocols.WebRTC_String)
                {
                    var json = Encoding.UTF8.GetString(data);
                    var input = JsonConvert.DeserializeObject<InputEvent>(json);

                    if (input == null) return;

                    // TODO: Implementar llamadas nativas de Windows (SendInput)
                    _logger.LogInformation("Input Recibido: {Type} X:{X} Y:{Y} Key:{Key}",
                        input.EventType, input.X, input.Y, input.Key);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error al procesar input.");
            }
        }

        public void Dispose()
        {
            _logger.LogInformation("Deteniendo servicio de Host NxDesk.");
            _isCapturing = false;
            _screenCaptureTimer?.Dispose();
            _dataChannel?.close();
            _peerConnection?.close();
            _hubConnection?.DisposeAsync().AsTask().Wait();
        }
    }
}