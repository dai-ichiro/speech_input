using System;
using System.Drawing;
using System.Windows.Forms;
using System.Runtime.InteropServices;

namespace speech_input
{
    public class RecordingIndicator : Form
    {
        [DllImport("user32.dll")]
        static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll")]
        static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x20;
        private const int WS_EX_LAYERED = 0x80000;
        private const int WS_EX_TOOLWINDOW = 0x80;

        public RecordingIndicator()
        {
            this.FormBorderStyle = FormBorderStyle.None;
            this.ShowInTaskbar = false;
            this.TopMost = true;
            this.Size = new Size(100, 40);
            this.BackColor = Color.Black;
            this.Opacity = 0.8;
            this.StartPosition = FormStartPosition.Manual;

            // Position it at the top-right of the primary screen
            Rectangle workingArea = Screen.PrimaryScreen.WorkingArea;
            this.Location = new Point(workingArea.Right - this.Width - 20, workingArea.Top + 20);

            Label label = new Label();
            label.Text = "● REC";
            label.ForeColor = Color.Red;
            label.Font = new Font("MS UI Gothic", 16, FontStyle.Bold);
            label.AutoSize = true;
            label.Location = new Point(10, 8);
            this.Controls.Add(label);
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);

            // Make click-through and tool window
            int exStyle = GetWindowLong(this.Handle, GWL_EXSTYLE);
            SetWindowLong(this.Handle, GWL_EXSTYLE, exStyle | WS_EX_TRANSPARENT | WS_EX_LAYERED | WS_EX_TOOLWINDOW);
        }

        protected override bool ShowWithoutActivation => true;
    }
}
