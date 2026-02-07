using System;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.IO;

namespace speech_input
{
    public class VllmClient
    {
        private ClientWebSocket _ws;
        private readonly Uri _uri;
        private readonly string _model;
        private CancellationTokenSource _cts;
        private readonly SemaphoreSlim _sendLock = new SemaphoreSlim(1, 1);
        private int _audioPacketCount = 0;

        public event Action<string> DeltaReceived;
        public event Action<string> ErrorOccurred;
        public event Action<string> StatusChanged;

        public VllmClient(string url, string model)
        {
            _uri = new Uri(url);
            _model = model;
        }

        public async Task StartAsync()
        {
            if (_ws != null) return;

            _ws = new ClientWebSocket();
            _cts = new CancellationTokenSource();

            try
            {
                StatusChanged?.Invoke("接続中...");
                await _ws.ConnectAsync(_uri, _cts.Token);

                // 1. Wait for session.created
                StatusChanged?.Invoke("セッション作成待ち...");
                var hello = await ReceiveOneMessageAsync();
                try { File.AppendAllText("vllm_ws.log", $"[{DateTime.Now:HH:mm:ss.fff}] Server Hello: {hello}{Environment.NewLine}"); } catch { }

                // 2. Update session
                // vLLM might expect these at top level or nested. Trying top level for better compatibility.
                var updateMsg = JsonConvert.SerializeObject(new
                {
                    type = "session.update",
                    model = _model,
                    modalities = new[] { "text" },
                    instructions = "You are a speech-to-text assistant. Please transcribe the user's speech accurately into text.",
                    input_audio_transcription = new { model = "whisper-1" },
                    turn_detection = new { type = "server_vad" },
                    session = new // Also include nested just in case
                    {
                        modalities = new[] { "text" },
                        instructions = "You are a speech-to-text assistant. Please transcribe the user's speech accurately into text.",
                        input_audio_transcription = new { model = "whisper-1" },
                        turn_detection = new { type = "server_vad" }
                    }
                });
                await SendMessageAsync(updateMsg);

                // 3. Clear buffer to start fresh
                var clearMsg = JsonConvert.SerializeObject(new { type = "input_audio_buffer.clear" });
                await SendMessageAsync(clearMsg);

                StatusChanged?.Invoke("録音中（接続済み）");

                _ = ReceiveLoopAsync();
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(ex.Message);
                await CleanupAsync();
            }
        }

        public async Task StopAsync()
        {
            await CleanupAsync();
            StatusChanged?.Invoke("待機中");
        }

        private async Task CleanupAsync()
        {
            if (_cts != null)
            {
                _cts.Cancel();
                _cts.Dispose();
                _cts = null;
            }

            if (_ws != null)
            {
                if (_ws.State == WebSocketState.Open)
                {
                    try
                    {
                        await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
                    }
                    catch { }
                }
                _ws.Dispose();
                _ws = null;
            }
        }

        public async Task SendAudioAsync(string base64Audio)
        {
            if (_ws?.State != WebSocketState.Open) return;

            var msg = JsonConvert.SerializeObject(new
            {
                type = "input_audio_buffer.append",
                audio = base64Audio
            });
            await SendMessageAsync(msg);

            _audioPacketCount++;
            if (_audioPacketCount % 20 == 0)
            {
                try { File.AppendAllText("vllm_ws.log", $"[{DateTime.Now:HH:mm:ss.fff}] Sent 20 audio packets (Total: {_audioPacketCount}){Environment.NewLine}"); } catch { }
            }
        }

        private async Task SendMessageAsync(string message)
        {
            if (_ws == null) return;

            await _sendLock.WaitAsync();
            try
            {
                if (_ws?.State != WebSocketState.Open) return;

                if (!message.Contains("input_audio_buffer.append")) // Don't log audio data to keep log file small
                {
                    try { File.AppendAllText("vllm_ws.log", $"[{DateTime.Now:HH:mm:ss.fff}] Sending: {message}{Environment.NewLine}"); } catch { }
                }

                var bytes = Encoding.UTF8.GetBytes(message);
                await _ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, _cts?.Token ?? CancellationToken.None);
            }
            finally
            {
                _sendLock.Release();
            }
        }

        private async Task<string> ReceiveOneMessageAsync()
        {
            var buffer = new ArraySegment<byte>(new byte[1024 * 32]);
            using (var ms = new MemoryStream())
            {
                WebSocketReceiveResult result;
                do
                {
                    result = await _ws.ReceiveAsync(buffer, _cts.Token);
                    ms.Write(buffer.Array, buffer.Offset, result.Count);
                } while (!result.EndOfMessage);

                if (result.MessageType == WebSocketMessageType.Close) return null;

                ms.Seek(0, SeekOrigin.Begin);
                using (var reader = new StreamReader(ms, Encoding.UTF8))
                {
                    return await reader.ReadToEndAsync();
                }
            }
        }

        private async Task ReceiveLoopAsync()
        {
            try
            {
                while (_ws != null && _ws.State == WebSocketState.Open && !_cts.Token.IsCancellationRequested)
                {
                    var message = await ReceiveOneMessageAsync();
                    if (message == null) break;

                    try
                    {
                        File.AppendAllText("vllm_ws.log", $"[{DateTime.Now:HH:mm:ss.fff}] Received: {message}{Environment.NewLine}");
                    }
                    catch { }

                    var data = JObject.Parse(message);
                    var type = data["type"]?.ToString();

                    // Handle various types of transcription/text events
                    // We check for many possible types and fields to be as resilient as possible
                    if (type != null && (type.Contains("delta") || type.Contains("transcription") || type.Contains("text") || type.Contains("response")))
                    {
                        // Try to find the text in common fields
                        string text = data["delta"]?.ToString()
                                   ?? data["text"]?.ToString()
                                   ?? data["content"]?.ToString()
                                   ?? data["transcription"]?.ToString();

                        // If it's a nested structure like response.audio_transcription.delta
                        if (string.IsNullOrEmpty(text) && data["item"] != null)
                        {
                            text = data["item"]["content"]?[0]?["text"]?.ToString()
                                ?? data["item"]["text"]?.ToString();
                        }

                        if (!string.IsNullOrEmpty(text))
                        {
                            DeltaReceived?.Invoke(text);
                        }
                    }
                    else if (type == "error")
                    {
                        var error = data["error"];
                        var msg = error is JObject ? error["message"]?.ToString() : error?.ToString();
                        ErrorOccurred?.Invoke(msg ?? "Unknown error");
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(ex.Message);
            }
            finally
            {
                if (_ws != null && !_cts.Token.IsCancellationRequested)
                {
                    await StopAsync();
                }
            }
        }
    }
}
