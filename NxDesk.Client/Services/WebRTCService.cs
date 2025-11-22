using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using NxDesk.Core.DTOs;
using SIPSorcery.Net;
using SIPSorceryMedia.Abstractions;
using System.Windows.Media.Imaging;
using System.Windows;
using SdpMessage = NxDesk.Core.DTOs.SdpMessage;
using System.IO;
using System.Text;

namespace NxDesk.Client.Services
{
    public class WebRTCService : IAsyncDisposable
    {
        private readonly SignalingService _signalingService;
        private RTCPeerConnection? _peerConnection;
        private RTCDataChannel? _dataChannel;

        private static readonly ILogger _logger = new Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory()
            .CreateLogger<WebRTCService>();

        public event Action<string>? OnConnectionStateChanged;
        public event Action<System.Windows.Media.Imaging.BitmapSource>? OnVideoFrameReady;
        public event Action<List<string>>? OnScreensInfoReceived;

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
            };

            var videoFormats = new List<VideoFormat>
            {
                new VideoFormat(VideoCodecsEnum.VP8, 96)
            };
            var videoTrack = new MediaStreamTrack(videoFormats, MediaStreamStatusEnum.RecvOnly);
            _peerConnection.addTrack(videoTrack);

            _peerConnection.OnVideoFrameReceived += (endpoint, timestamp, frame, format) =>
            {
                try
                {
                    Application.Current?.Dispatcher.Invoke(() =>
                    {
                        try
                        {
                            var bitmapImage = new BitmapImage();
                            using (var stream = new MemoryStream(frame))
                            {
                                bitmapImage.BeginInit();
                                bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                                bitmapImage.StreamSource = stream;
                                bitmapImage.EndInit();
                                bitmapImage.Freeze();
                            }
                            OnVideoFrameReady?.Invoke(bitmapImage);
                        }
                        catch { }
                    });
                }
                catch { }
            };

            _dataChannel = await _peerConnection.createDataChannel("input-channel");

            // --- MODIFICADO: Pedir pantallas al abrir el canal ---
            _dataChannel.onopen += () =>
            {
                OnConnectionStateChanged?.Invoke("Canal de datos abierto. Solicitando pantallas...");
                _logger.LogInformation("Data channel abierto");

                // Enviamos solicitud explícita
                RequestScreenList();
            };

            _dataChannel.onclose += () =>
            {
                OnConnectionStateChanged?.Invoke("Canal de datos cerrado");
            };

            _dataChannel.onmessage += (RTCDataChannel channel, DataChannelPayloadProtocols protocol, byte[] data) =>
            {
                if (protocol == DataChannelPayloadProtocols.WebRTC_String)
                {
                    try
                    {
                        string json = Encoding.UTF8.GetString(data);
                        var message = JsonConvert.DeserializeObject<DataChannelMessage>(json);

                        if (message != null && message.Type == "system:screen_info")
                        {
                            var screenInfo = JsonConvert.DeserializeObject<ScreenInfoPayload>(message.Payload);
                            if (screenInfo != null && screenInfo.ScreenNames != null)
                            {
                                OnScreensInfoReceived?.Invoke(screenInfo.ScreenNames);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"Error procesando mensaje del DataChannel: {ex.Message}");
                    }
                }
            };

            await CreateOfferAsync();
        }

        // --- NUEVO MÉTODO: Enviar solicitud de pantallas ---
        private void RequestScreenList()
        {
            if (_dataChannel != null && _dataChannel.readyState == RTCDataChannelState.open)
            {
                var msg = new DataChannelMessage
                {
                    Type = "system:get_screens",
                    Payload = ""
                };
                string json = JsonConvert.SerializeObject(msg);
                _dataChannel.send(json);
            }
        }

        private async Task CreateOfferAsync()
        {
            if (_peerConnection == null) return;
            OnConnectionStateChanged?.Invoke("Creando oferta...");
            var offer = _peerConnection.createOffer(null);
            await _peerConnection.setLocalDescription(offer);
            string sdpString = _peerConnection.localDescription?.sdp?.ToString() ?? string.Empty;

            var msg = new SdpMessage
            {
                Type = "offer",
                Payload = sdpString
            };
            await _signalingService.RelayMessageAsync(msg);
            OnConnectionStateChanged?.Invoke("Oferta enviada...");
        }

        private async Task HandleSignalingMessageAsync(SdpMessage message)
        {
            if (_peerConnection == null) return;
            switch (message.Type)
            {
                case "answer":
                    OnConnectionStateChanged?.Invoke("Respuesta recibida...");
                    var answerSdp = SDP.ParseSDPDescription(message.Payload);
                    var answerInit = new RTCSessionDescriptionInit
                    {
                        type = RTCSdpType.answer,
                        sdp = answerSdp.ToString()
                    };
                    var setRemoteResult = _peerConnection.setRemoteDescription(answerInit);
                    if (setRemoteResult == SetDescriptionResultEnum.OK)
                    {
                        OnConnectionStateChanged?.Invoke("Conexión establecida.");
                    }
                    break;
                case "ice-candidate":
                    var candidateInit = JsonConvert.DeserializeObject<RTCIceCandidateInit>(message.Payload);
                    _peerConnection.addIceCandidate(candidateInit);
                    break;
            }
        }

        public void SendInputEvent(InputEvent inputEvent)
        {
            if (_dataChannel?.readyState == RTCDataChannelState.open)
            {
                var payloadJson = JsonConvert.SerializeObject(inputEvent);
                var messageWrapper = new DataChannelMessage
                {
                    Type = "input",
                    Payload = payloadJson
                };
                var finalJson = JsonConvert.SerializeObject(messageWrapper);
                _dataChannel.send(finalJson);
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (_signalingService != null)
                _signalingService.OnMessageReceived -= HandleSignalingMessageAsync;

            await _signalingService.DisposeAsync();
            _dataChannel?.close();
            _peerConnection?.close();
        }
    }
}