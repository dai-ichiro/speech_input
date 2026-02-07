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
                await ReceiveOneMessageAsync();

                // 2. Update session
                var updateMsg = JsonConvert.SerializeObject(new
                {
                    type = "session.update",
                    model = _model
                });
                await SendMessageAsync(updateMsg);

                // 3. Initial commit
                var commitMsg = JsonConvert.SerializeObject(new { type = "input_audio_buffer.commit" });
                await SendMessageAsync(commitMsg);

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
        }

        private async Task SendMessageAsync(string message)
        {
            if (_ws == null) return;

            await _sendLock.WaitAsync();
            try
            {
                if (_ws?.State != WebSocketState.Open) return;
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

                    var data = JObject.Parse(message);
                    var type = data["type"]?.ToString();

                    if (type == "transcription.delta" || type == "response.audio_transcription.delta")
                    {
                        var delta = data["delta"]?.ToString();
                        if (!string.IsNullOrEmpty(delta))
                        {
                            DeltaReceived?.Invoke(delta);
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
