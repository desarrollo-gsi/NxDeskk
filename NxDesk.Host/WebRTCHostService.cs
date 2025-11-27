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
using SIPSorceryMedia.Encoders;
using System.Runtime.InteropServices;
using System.Drawing.Drawing2D;

namespace NxDesk.Host
{
    public class WebRTCHostService : IDisposable
    {
        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        private readonly VpxVideoEncoder _vpxEncoder;
        private readonly string _roomId;
        private readonly string _serverUrl;
        private readonly IdentityService _identityService;
        private NetworkDiscoveryService _discoveryService;

        private readonly ILogger<WebRTCHostService> _logger;
        private HubConnection _hubConnection;
        private RTCPeerConnection _peerConnection;
        private RTCDataChannel _dataChannel;
        private bool _isCapturing = false;

        private int _currentScreenIndex = 0;
        private Screen[] _allScreens;
        private readonly object _captureLock = new object();

        public WebRTCHostService(ILogger<WebRTCHostService> logger, IConfiguration configuration)
        {
            try { SetProcessDPIAware(); } catch { }

            _logger = logger;

            _serverUrl = configuration.GetValue<string>("SignalR:ServerUrl");
            if (string.IsNullOrEmpty(_serverUrl) || _serverUrl.Contains("[IP_DE_TU_SERVIDOR]"))
            {
                _serverUrl = "https://localhost:7099/signalinghub";
            }

            _identityService = new IdentityService();
            _roomId = _identityService.MyID;

            RefreshScreens();

            _logger.LogInformation("IdentityService cargado. Host ID: {HostID}", _roomId);

            _discoveryService = new NetworkDiscoveryService(_identityService.MyID, _identityService.MyAlias);
            _vpxEncoder = new VpxVideoEncoder();
        }

        private void RefreshScreens()
        {
            _allScreens = Screen.AllScreens;
            _logger.LogInformation("Pantallas detectadas: {ScreenCount}", _allScreens.Length);
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
                _logger.LogInformation("Conectado a SignalR. Sala: {RoomId}", _roomId);
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

            _isCapturing = false;

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
            // Iniciar el loop de captura
            StartScreenCapture();
        }

        private void SendScreenList()
        {
            RefreshScreens();

            if (_dataChannel == null || _dataChannel.readyState != RTCDataChannelState.open) return;

            try
            {
                var screenNames = _allScreens.Select((s, i) => $"Pantalla {i + 1} ({s.Bounds.Width}x{s.Bounds.Height})").ToList();

                var screenInfo = new ScreenInfoPayload { ScreenNames = screenNames };

                var message = new DataChannelMessage
                {
                    Type = "system:screen_info",
                    Payload = JsonConvert.SerializeObject(screenInfo)
                };

                _dataChannel.send(JsonConvert.SerializeObject(message));
                _logger.LogInformation("Lista enviada: {Count} pantallas.", screenNames.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error enviando lista de pantallas.");
            }
        }

        private async void StartScreenCapture()
        {
            if (_isCapturing) return;
            _isCapturing = true;

            await Task.Run(async () =>
            {
                while (_isCapturing && _peerConnection != null)
                {
                    var startTime = DateTime.Now;

                    try
                    {
                        using (var bitmap = CaptureScreenRaw())
                        {
                            if (bitmap != null)
                            {
                                var rawBuffer = BitmapToBytes(bitmap);

                                var encodedBuffer = _vpxEncoder.EncodeVideo(
                                    bitmap.Width,
                                    bitmap.Height,
                                    rawBuffer,
                                    SIPSorceryMedia.Abstractions.VideoPixelFormatsEnum.Bgra,
                                    SIPSorceryMedia.Abstractions.VideoCodecsEnum.VP8);

                                if (encodedBuffer != null && encodedBuffer.Length > 0)
                                {
                                    uint timestamp = (uint)(DateTime.UtcNow.Ticks / TimeSpan.TicksPerMillisecond);
                                    _peerConnection.SendVideo(timestamp, encodedBuffer);
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                    }

                    var elapsed = (DateTime.Now - startTime).TotalMilliseconds;
                    var waitTime = 50 - (int)elapsed;
                    if (waitTime > 0) await Task.Delay(waitTime);
                }
            });
        }

        private Bitmap CaptureScreenRaw()
        {
            try
            {
                if (_currentScreenIndex >= _allScreens.Length || _currentScreenIndex < 0)
                {
                    RefreshScreens(); 
                    _currentScreenIndex = 0;
                }

                if (_allScreens.Length == 0) return null;

                var bounds = _allScreens[_currentScreenIndex].Bounds;

                int targetWidth = bounds.Width;
                int targetHeight = bounds.Height;

                if (targetWidth > 1920)
                {
                    float ratio = (float)bounds.Height / bounds.Width;
                    targetWidth = 1920;
                    targetHeight = (int)(targetWidth * ratio);
                }

                var finalBitmap = new Bitmap(targetWidth, targetHeight, PixelFormat.Format32bppArgb);

                using (var g = Graphics.FromImage(finalBitmap))
                {
                    g.CompositingMode = CompositingMode.SourceCopy;
                    g.InterpolationMode = InterpolationMode.Bilinear; 
                    g.PixelOffsetMode = PixelOffsetMode.HighSpeed;

                    if (targetWidth == bounds.Width && targetHeight == bounds.Height)
                    {
                        g.CopyFromScreen(bounds.X, bounds.Y, 0, 0, bounds.Size, CopyPixelOperation.SourceCopy);
                    }
                    else
                    {
                        using (var fullScreenBmp = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb))
                        using (var gFull = Graphics.FromImage(fullScreenBmp))
                        {
                            gFull.CopyFromScreen(bounds.X, bounds.Y, 0, 0, bounds.Size, CopyPixelOperation.SourceCopy);

                            g.DrawImage(fullScreenBmp, 0, 0, targetWidth, targetHeight);
                        }
                    }
                }
                return finalBitmap;
            }
            catch { return null; }
        }

        private byte[] BitmapToBytes(Bitmap bmp)
        {
            BitmapData bmpData = null;
            try
            {
                bmpData = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadOnly, bmp.PixelFormat);

                int bytesPerPixel = 4; 
                int widthInBytes = bmp.Width * bytesPerPixel;
                int size = widthInBytes * bmp.Height;
                byte[] rgbValues = new byte[size];

                for (int y = 0; y < bmp.Height; y++)
                {
                    IntPtr rowPtr = IntPtr.Add(bmpData.Scan0, y * bmpData.Stride);

                    Marshal.Copy(rowPtr, rgbValues, y * widthInBytes, widthInBytes);
                }

                return rgbValues;
            }
            finally
            {
                if (bmpData != null) bmp.UnlockBits(bmpData);
            }
        }

        private void SwitchCaptureScreen(int screenIndex)
        {
            lock (_captureLock)
            {
                RefreshScreens(); // Asegurarnos de tener la lista actualizada
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
                    _logger.LogInformation("Recibida solicitud de pantallas.");
                    SendScreenList();
                    return;
                }

                if (message.Type != "input") return;

                var input = JsonConvert.DeserializeObject<InputEvent>(message.Payload);
                if (input == null) return;

                // Lógica de inputs (sin cambios mayores)
                switch (input.EventType)
                {
                    case "mousemove":
                        if (input.X.HasValue && input.Y.HasValue)
                            InputSimulator.SimulateMouseMove(input.X.Value, input.Y.Value, _currentScreenIndex);
                        break;
                    case "mousedown":
                        InputSimulator.SimulateMouseDown(input.Button);
                        break;
                    case "mouseup":
                        InputSimulator.SimulateMouseUp(input.Button);
                        break;
                    case "mousewheel":
                        if (input.Delta.HasValue) InputSimulator.SimulateMouseWheel((int)input.Delta.Value);
                        break;
                    case "keydown":
                    case "keyup":
                        if (!string.IsNullOrEmpty(input.Key) && Enum.TryParse<Keys>(input.Key, true, out Keys winKey))
                            InputSimulator.SimulateKeyEvent((byte)winKey, input.EventType == "keydown");
                        break;
                    case "control":
                        if (input.Command == "switch_screen" && input.Value.HasValue)
                            SwitchCaptureScreen(input.Value.Value);
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
            _vpxEncoder?.Dispose();
            _dataChannel?.close();
            _peerConnection?.close();
            _discoveryService?.Stop();
            _hubConnection?.DisposeAsync().AsTask().Wait();
        }
    }
}