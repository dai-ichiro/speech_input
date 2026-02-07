using System;
using NAudio.Wave;

namespace speech_input
{
    public class AudioRecorder
    {
        private WaveInEvent _waveIn;
        public event Action<byte[]> DataAvailable;

        public void Start()
        {
            if (_waveIn != null) return;

            _waveIn = new WaveInEvent();
            _waveIn.WaveFormat = new WaveFormat(16000, 16, 1); // 16kHz, 16-bit, Mono
            _waveIn.DataAvailable += (s, e) =>
            {
                if (e.BytesRecorded > 0)
                {
                    byte[] data = new byte[e.BytesRecorded];
                    Array.Copy(e.Buffer, data, e.BytesRecorded);
                    DataAvailable?.Invoke(data);
                }
            };

            _waveIn.StartRecording();
        }

        public void Stop()
        {
            if (_waveIn != null)
            {
                _waveIn.StopRecording();
                _waveIn.Dispose();
                _waveIn = null;
            }
        }
    }
}
