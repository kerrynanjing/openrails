// COPYRIGHT 2013 by the Open Rails project.
// 
// This file is part of Open Rails.
// 
// Open Rails is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
// 
// Open Rails is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
// 
// You should have received a copy of the GNU General Public License
// along with Open Rails.  If not, see <http://www.gnu.org/licenses/>.

using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Orts.Common;
using Orts.Simulation;
using Orts.Viewer3D;
using Orts.Viewer3D.Debugging;
using Orts.Viewer3D.Processes;
using ORTS.Common;
using ORTS.Settings;
using ActivitySelect;

namespace Orts
{
    static class NativeMethods
    {
        [DllImport("kernel32.dll", CallingConvention = CallingConvention.StdCall)]
        internal static extern bool SetDllDirectory(string pathName);
    }

    static class Program
    {
        public static Simulator Simulator;
        public static Viewer Viewer;
        public static MapViewer MapForm;
        public static SoundDebugForm SoundDebugForm;
        public static ORTraceListener ORTraceListener;
        public static string logFileName = "";          // contains path to file
        public static string EvaluationFilename = "";   // contains path to file

        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        [ThreadName("Render")]
        static void Main(string[] args)
        {
            var options = args.Where(a => a.StartsWith("-") || a.StartsWith("/")).Select(a => a.Substring(1));
            var settings = new UserSettings(options);

            // enables loading of dll for specific architecture(32 or 64bit) from distinct folders...
            string path = Path.Combine(ApplicationInfo.ProcessDirectory, "Native");
            path = Path.Combine(path, (Environment.Is64BitProcess) ? "X64" : "X86");
            NativeMethods.SetDllDirectory(path);

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // --- 在此处先显示 Form1，用户关闭窗体后再继续启动游戏 ---
            // 如果你的窗体类不是 Form1，请改为实际类名。
            using (var form = new Form1())
            {
                // 使用 Application.Run 会运行 WinForms 消息循环，用户关闭窗体后返回继续执行。
                Application.Run(form);
            }

            // 继续原有流程：创建并运行 Game
            var game = new Game(settings);
            game.PushState(new GameStateRunActivity(args));
            game.Run();
        }
    }
}
