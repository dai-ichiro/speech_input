using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace speech_input
{
    public partial class Form1 : Form
    {
        private VllmClient _client;
        private AudioRecorder _recorder;
        private InputHandler _inputHandler;

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

            _recorder.DataAvailable += OnAudioDataAvailable;
            _client.DeltaReceived += OnDeltaReceived;
            _client.ErrorOccurred += OnErrorOccurred;
            _client.StatusChanged += OnStatusChanged;
        }

        private async void OnAudioDataAvailable(byte[] data)
        {
            string base64Audio = Convert.ToBase64String(data);
            await _client.SendAudioAsync(base64Audio);
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

        private async void 録音開始ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            録音開始ToolStripMenuItem.Enabled = false;
            録音停止ToolStripMenuItem.Enabled = true;

            // Test if typing works
            _inputHandler.TypeText("[Speech-Input Started]");

            await _client.StartAsync();
            _recorder.Start();
        }

        private void 録音停止ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            StopRecording();
        }

        private async void StopRecording()
        {
            録音開始ToolStripMenuItem.Enabled = true;
            録音停止ToolStripMenuItem.Enabled = false;

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
        private void 終了ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            // タスクトレイのアイコンを明示的に非表示にする（これをしないと終了後もアイコンが残ることがあります）
            notifyIcon1.Visible = false;

            // プロセスを完全に終了させる
            Application.Exit();
        }
    }
}
