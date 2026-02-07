using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.IO;
using System.Runtime.InteropServices;

namespace speech_input
{
    public partial class Form1 : Form
    {
        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, int fsModifiers, int vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        private const int HOTKEY_ID = 1;
        private const int WM_HOTKEY = 0x0312;
        private const int VK_F13 = 0x7C;

        private VllmClient _client;
        private AudioRecorder _recorder;
        private InputHandler _inputHandler;
        private RecordingIndicator _indicator;
        private bool _isRecording = false;

        private const string WS_URL = "ws://localhost:8000/v1/realtime";
        private const string MODEL_NAME = "mistralai/Voxtral-Mini-4B-Realtime-2602";

        public Form1()
        {
            InitializeComponent();

            // 1. プロパティでも設定可能ですが、念のためコードでも指定
            this.ShowInTaskbar = false;      // タスクバーに表示しない
            this.WindowState = FormWindowState.Minimized; // 最小化状態で起動

            _inputHandler = new InputHandler();
            _recorder = new AudioRecorder();
            _client = new VllmClient(WS_URL, MODEL_NAME);
            _indicator = new RecordingIndicator();

            _recorder.DataAvailable += OnAudioDataAvailable;
            _client.DeltaReceived += OnDeltaReceived;
            _client.ErrorOccurred += OnErrorOccurred;
            _client.StatusChanged += OnStatusChanged;
            _client.MessageReceived += OnMessageReceived;

            // ハンドルが作成された後にホットキーを登録する
            this.HandleCreated += (s, e) => {
                RegisterHotKey(this.Handle, HOTKEY_ID, 0, VK_F13);
            };
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY && m.WParam.ToInt32() == HOTKEY_ID)
            {
                ToggleRecording();
            }
            base.WndProc(ref m);
        }

        private void ToggleRecording()
        {
            if (!_isRecording)
            {
                StartRecording();
            }
            else
            {
                StopRecording();
            }
        }

        private async void OnAudioDataAvailable(byte[] data)
        {
            string base64Audio = Convert.ToBase64String(data);
            await _client.SendAudioAsync(base64Audio);
        }

        private bool _firstMessageReceived = false;

        private void OnMessageReceived(string message)
        {
            if (!_firstMessageReceived)
            {
                _firstMessageReceived = true;
                // Popup is no longer needed as per user request
            }
        }

        private void OnDeltaReceived(string delta)
        {
            // メインスレッドで実行する必要がある場合があるが、InputSimulatorは基本大丈夫。
            // ただし、UIスレッドを介した方が安全なこともある。
            this.BeginInvoke(new Action(() =>
            {
                _inputHandler.TypeText(delta);
            }));
        }

        private void OnErrorOccurred(string message)
        {
            this.BeginInvoke(new Action(() =>
            {
                notifyIcon1.ShowBalloonTip(3000, "Error", message, ToolTipIcon.Error);
                StopRecording();
            }));
        }

        private void OnStatusChanged(string status)
        {
            this.BeginInvoke(new Action(() =>
            {
                notifyIcon1.Text = $"Speech-Input: {status}";
            }));
        }

        private async void StartRecording()
        {
            if (_isRecording) return;
            _isRecording = true;
            _firstMessageReceived = false;

            _indicator.Show();

            await _client.StartAsync();
            _recorder.Start();
        }

        private async void StopRecording()
        {
            if (!_isRecording) return;
            _isRecording = false;

            _indicator.Hide();

            _recorder.Stop();
            await _client.StopAsync();
        }

        // 起動時にフォームを一瞬も表示させないための核心部分
        protected override void SetVisibleCore(bool value)
        {
            if (!this.IsHandleCreated)
            {
                value = false;
                CreateHandle();
            }
            base.SetVisibleCore(value);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            UnregisterHotKey(this.Handle, HOTKEY_ID);
            _indicator?.Dispose();
            base.OnFormClosing(e);
        }

        private void 終了ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            // タスクトレイのアイコンを明示的に非表示にする（これをしないと終了後もアイコンが残ることがあります）
            notifyIcon1.Visible = false;

            // プロセスを完全に終了させる
            Application.Exit();
        }
    }
}
