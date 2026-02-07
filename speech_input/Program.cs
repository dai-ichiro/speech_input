using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace speech_input
{
    internal static class Program
    {
        private static Mutex mutex = null;
        private const string MutexName = "background_app_singleton_mutex";
        /// <summary>
        /// アプリケーションのメイン エントリ ポイントです。
        /// </summary>
        [STAThread]
        static void Main()
        {
            bool createdNew = false;
            mutex = new Mutex(true, MutexName, out createdNew);

            if (!createdNew)
            {
                // アプリケーションが既に起動している場合
                MessageBox.Show(
                    "このアプリケーションは既に実行中です。",
                    "アプリケーション警告",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning
                );
                return;
            }

            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new Form1());
            }
            finally
            {
                if (mutex != null)
                {
                    // 自分が Mutex を新規作成（所有）した時だけ解放する
                    if (createdNew)
                    {
                        mutex.ReleaseMutex();
                    }
                    mutex.Dispose();
                }
            }
        }
    }
}
