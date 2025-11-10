using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using NxDesk.Core.DTOs;
using SIPSorcery.Net;
using SIPSorceryMedia.Abstractions;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

// Alias para evitar conflicto con SIPSorcery.SdpMessage
using SdpMessage = NxDesk.Core.DTOs.SdpMessage;

namespace NxDesk.Client.Services
{
    public class WebRTCService : IAsyncDisposable
    {
        private readonly SignalingService _signalingService;
        private RTCPeerConnection? _peerConnection;
        private RTCDataChannel? _dataChannel;
        private readonly int _videoWidth = 1920;
        private readonly int _videoHeight = 1080;
        private static readonly ILogger _logger = new Microsoft.Extensions.Logging.Abstractions.NullLoggerFactory()
            .CreateLogger<WebRTCService>();

        public event Action<string>? OnConnectionStateChanged;
        public event Action<WriteableBitmap>? OnVideoFrameReady;

        public WebRTCService(SignalingService signalingService)
        {
            _signalingService = signalingService;
            _signalingService.OnMessageReceived += HandleSignalingMessageAsync;
            _signalingService.OnParticipantJoined += CreateOfferAsync;
        }

        public async Task StartConnectionAsync(string hostId)
        {
            OnConnectionStateChanged?.Invoke("Iniciando WebRTC...");

            var config = new RTCConfiguration
            {
                iceServers = new List<RTCIceServer>
                {
                    new RTCIceServer { urls = "stun:stun.l.google.com:19302" }
                }
            };

            _peerConnection = new RTCPeerConnection(config);

            // H263 como solicitaste
            var videoFormats = new List<VideoFormat>
            {
                new VideoFormat(SDPWellKnownMediaFormatsEnum.H263)
            };

            var videoTrack = new MediaStreamTrack(videoFormats, MediaStreamStatusEnum.RecvOnly);
            _peerConnection.addTrack(videoTrack);

            // Recepción de video
            _peerConnection.OnVideoFrameReceived += (ep, timestamp, frame, format) =>
            {
                try
                {
                    var bmp = ConvertToWriteableBitmap(frame, _videoWidth, _videoHeight);
                    OnVideoFrameReady?.Invoke(bmp);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"Error en frame: {ex.Message}");
                }
            };

            // ICE Candidates (corregido: await + validación)
            _peerConnection.onicecandidate += async (candidate) =>
            {
                if (candidate != null && !string.IsNullOrWhiteSpace(candidate.candidate))
                {
                    var msg = new SdpMessage
                    {
                        Type = "ice-candidate",
                        Payload = candidate.toJSON()
                    };
                    await _signalingService.RelayMessageAsync(msg); // await!
                }
            };

            // Estado de conexión
            _peerConnection.onconnectionstatechange += (state) =>
            {
                OnConnectionStateChanged?.Invoke($"P2P: {state}");
            };

            // Canal de datos
            _dataChannel = await _peerConnection.createDataChannel("input-channel");
            _dataChannel.onopen += () => OnConnectionStateChanged?.Invoke("Canal de datos abierto");
            _dataChannel.onclose += () => OnConnectionStateChanged?.Invoke("Canal de datos cerrado");

            await _signalingService.ConnectAsync(hostId);
            OnConnectionStateChanged?.Invoke("Esperando host...");
        }

        private WriteableBitmap ConvertToWriteableBitmap(byte[] frame, int width, int height)
        {
            var bitmap = new WriteableBitmap(width, height, 96, 96, System.Windows.Media.PixelFormats.Bgr24, null);
            try
            {
                int stride = width * 3;
                if (frame.Length >= stride * height)
                {
                    bitmap.WritePixels(new System.Windows.Int32Rect(0, 0, width, height), frame, stride, 0);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Error al escribir pixels: {ex.Message}");
            }
            return bitmap;
        }

        private async Task CreateOfferAsync()
        {
            if (_peerConnection == null) return;

            OnConnectionStateChanged?.Invoke("Creando oferta...");

            // ✅ createOffer devuelve RTCSessionDescriptionInit (sin await)
            var offer = _peerConnection.createOffer(null);

            // ✅ setLocalDescription devuelve Task (sin resultado)
            await _peerConnection.setLocalDescription(offer);

            // Ya no existe SetDescriptionResultEnum en esta versión
            // Así que sólo continuamos normalmente
            string sdpString = _peerConnection.localDescription?.ToString() ?? string.Empty;

            var msg = new SdpMessage
            {
                Type = "offer",
                Payload = sdpString
            };

            await _signalingService.RelayMessageAsync(msg);

            OnConnectionStateChanged?.Invoke("Oferta enviada");
        }

        private async Task HandleSignalingMessageAsync(SdpMessage message)
        {
            if (_peerConnection == null) return;

            switch (message.Type)
            {
                case "answer":
                    OnConnectionStateChanged?.Invoke("Procesando respuesta...");

                    var answerSdp = SDP.ParseSDPDescription(message.Payload);
                    var answerInit = new RTCSessionDescriptionInit
                    {
                        type = RTCSdpType.answer,
                        sdp = answerSdp.ToString() // ✅ string, no SDP
                    };

                    var setRemoteResult = _peerConnection.setRemoteDescription(answerInit); // ✅ sin await

                    if (setRemoteResult != SetDescriptionResultEnum.OK)
                    {
                        OnConnectionStateChanged?.Invoke($"Error remoto: {setRemoteResult}");
                    }
                    break;
            }
        }

        public void SendInputEvent(InputEvent inputEvent)
        {
            if (_dataChannel?.readyState == RTCDataChannelState.open)
            {
                _dataChannel.send(JsonConvert.SerializeObject(inputEvent));
            }
        }

        public async ValueTask DisposeAsync()
        {
            _signalingService.OnMessageReceived -= HandleSignalingMessageAsync;
            _signalingService.OnParticipantJoined -= CreateOfferAsync;

            await _signalingService.DisposeAsync();
            _dataChannel?.close();
            _peerConnection?.close();
        }
    }
}