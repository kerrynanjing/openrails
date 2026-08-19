using System;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Collections.Generic;

namespace ActivitySelect
{
    public partial class Form1 : Form
    {
        private readonly string _serverIp = "211.101.245.150";
        private const int _serverPort = 30001;
        private TcpClient _serverClient;
        private CancellationTokenSource _cts;
        private readonly Dictionary<string, Button> _activityButtons = new Dictionary<string, Button>(StringComparer.OrdinalIgnoreCase);

        public Form1()
        {
            InitializeComponent();

            // 将活动名与按钮建立映射（用于收到广播后禁用对应按钮）
            _activityButtons["T236任务"] = button1;
            _activityButtons["26112任务"] = button2;
            _activityButtons["36369任务"] = button3;
            _activityButtons["46283任务"] = button4;
            _activityButtons["46437任务"] = button5;
            _activityButtons["K34任务"] = button6;
            _activityButtons["K101任务"] = button7;
            _activityButtons["K1556任务"] = button8;
            _activityButtons["X373任务"] = button9;
            _activityButtons["X8715"] = button10;
            _activityButtons["梅钢"] = button11;

            Shown += Form1_Shown;
            FormClosing += Form1_FormClosing;
        }

        private async void Form1_Shown(object sender, EventArgs e)
        {
            // 启动与广播服务器的长连接并开始接收消息
            _cts = new CancellationTokenSource();
            await ConnectToServerAsync(_cts.Token).ConfigureAwait(false);
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            try
            {
                _cts?.Cancel();
                _serverClient?.Close();
            }
            catch
            {
                // 忽略清理错误
            }
        }

        private async Task ConnectToServerAsync(CancellationToken ct)
        {
            try
            {
                // 如果已经连接则不重复连接
                if (_serverClient != null && _serverClient.Connected)
                    return;

                _serverClient = new TcpClient();
                await _serverClient.ConnectAsync(_serverIp, _serverPort).ConfigureAwait(false);

                // 启动后台读取循环（不阻塞 UI 线程）
                _ = Task.Run(() => ReadLoopAsync(ct), ct);
            }
            catch
            {
                // 连接失败则忽略（可扩展为重试或显示状态）
            }
        }

        private async Task ReadLoopAsync(CancellationToken ct)
        {
            try
            {
                var ns = _serverClient.GetStream();
                try
                {
                    var reader = new StreamReader(ns, Encoding.UTF8);
                    try
                    {
                        while (!ct.IsCancellationRequested)
                        {
                            // 服务器按行发送消息（协议：ACTIVITY:<活动名> 或 直接 <活动名>）
                            var line = await reader.ReadLineAsync().ConfigureAwait(false);
                            if (line == null) break;
                            ProcessServerMessage(line);
                        }
                    }
                    finally
                    {
                        reader.Dispose();
                    }
                }
                finally
                {
                    ns.Dispose();
                }
            }
            catch
            {
                // 读取失败或连接中断，结束读取循环
            }
        }

        private void ProcessServerMessage(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return;

            var msg = raw.Trim();
            if (msg.StartsWith("ACTIVITY:", StringComparison.OrdinalIgnoreCase))
                msg = msg.Substring("ACTIVITY:".Length).Trim();

            // 如果收到的活动名在映射表中，则在 UI 线程禁用对应按钮
            if (_activityButtons.TryGetValue(msg, out var btn))
            {
                if (btn.InvokeRequired)
                {
                    btn.BeginInvoke(new Action(() => btn.Enabled = false));
                }
                else
                {
                    btn.Enabled = false;
                }
            }
        }

        private async Task SendActivityToServerAsync(string activity)
        {
            if (string.IsNullOrWhiteSpace(activity))
                return;

            try
            {
                // 确保已连接
                if (_serverClient == null || !_serverClient.Connected)
                    await ConnectToServerAsync(CancellationToken.None).ConfigureAwait(false);

                if (_serverClient?.Connected == true)
                {
                    var ns = _serverClient.GetStream();
                    try
                    {
                        var writer = new StreamWriter(ns, Encoding.UTF8, 4096, leaveOpen: true) { AutoFlush = true };
                        try
                        {
                            // 按行发送，服务器按行解析；前缀 ACTIVITY: 可让服务器更容易识别
                            await writer.WriteLineAsync("ACTIVITY:" + activity).ConfigureAwait(false);
                        }
                        finally
                        {
                            writer.Dispose();
                        }
                    }
                    finally
                    {
                        ns.Dispose();
                    }
                }
            }
            catch
            {
                // 发送失败可忽略或扩展为重试/提示
            }
        }

        private async void button1_Click(object sender, EventArgs e)
        {
            var ActivityName = "T236任务";

            // 先向广播服务器发送要加入的活动名（让服务器转发给其它已在线客户端）
            await SendActivityToServerAsync(ActivityName).ConfigureAwait(false);

            // 再执行已有的本地活动设置逻辑（注意：该方法会关闭窗体）
            TrySetActivityFromButton("T236任务.act");
        }

        private void button2_Click(object sender, EventArgs e)
        {
            TrySetActivityFromButton("26112任务.act");
        }

        private void button3_Click(object sender, EventArgs e)
        {
            TrySetActivityFromButton("36369任务.act");
        }

        private void button4_Click(object sender, EventArgs e)
        {
            TrySetActivityFromButton("46283任务.act");
        }

        private void button5_Click(object sender, EventArgs e)
        {
            TrySetActivityFromButton("46437任务.act");
        }

        private void button6_Click(object sender, EventArgs e)
        {
            TrySetActivityFromButton("K34任务.act");
        }

        private void button7_Click(object sender, EventArgs e)
        {
            TrySetActivityFromButton("K101任务.act");
        }

        private void button8_Click(object sender, EventArgs e)
        {
            TrySetActivityFromButton("K1556任务.act");
        }

        private void button9_Click(object sender, EventArgs e)
        {
            TrySetActivityFromButton("X373任务.act");
        }

        private void button10_Click(object sender, EventArgs e)
        {
            TrySetActivityFromButton("X8715.act");
        }

        private void button11_Click(object sender, EventArgs e)
        {
            TrySetActivityFromButton("梅钢.act");
        }

        private void TrySetActivityFromButton(string targetActName)
        {
            // 读取命令行参数（跳过 exe 本身）
            var cmdArgs = Environment.GetCommandLineArgs().Skip(1).ToList();
            if (cmdArgs.Count == 0)
            {
                MessageBox.Show("未检测到启动参数。请确认该窗体是从 Menu/RunActivity 项目启动的，或在调试时传入参数。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // 尝试用第一个非开关参数推断 route 路径（例如 PATH 文件路径）
            var firstNonSwitchIndex = cmdArgs.FindIndex(a => !a.StartsWith("-"));
            if (firstNonSwitchIndex < 0)
            {
                MessageBox.Show("未找到可用于推断路由目录的路径参数。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var possiblePath = cmdArgs[firstNonSwitchIndex];
            string routeDir;
            try
            {
                var full = Path.GetFullPath(possiblePath);
                var parent = Path.GetDirectoryName(full); // 预期为 ...\PATHS
                if (string.IsNullOrEmpty(parent))
                {
                    MessageBox.Show($"无法解析路径：{possiblePath}", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                routeDir = Path.GetDirectoryName(parent); // 上一级：route 目录
                if (string.IsNullOrEmpty(routeDir))
                {
                    MessageBox.Show($"无法推断路由目录（来自：{possiblePath}）。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"解析路径时发生异常：{ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            var activitiesDir = Path.Combine(routeDir, "ACTIVITIES");
            if (!Directory.Exists(activitiesDir))
            {
                MessageBox.Show($"ACTIVITIES 目录不存在：{activitiesDir}", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // 匹配目标 .act（忽略大小写）
            var matched = Directory.GetFiles(activitiesDir, "*.act")
                                  .FirstOrDefault(f => string.Equals(Path.GetFileName(f), targetActName, StringComparison.OrdinalIgnoreCase));
            if (matched == null)
            {
                MessageBox.Show($"在 {activitiesDir} 中未找到 {targetActName}", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // 将原始参数中所有以 '-' 开头的开关（无论名称）都替换为 '-activity' 并使用 matched 作为其参数。
            var newArgs = new System.Collections.Generic.List<string>();
            for (int i = 0; i < cmdArgs.Count; i++)
            {
                var token = cmdArgs[i];
                if (token.StartsWith("-"))
                {
                    // 无论原来是什么开关，都替换为 -activity <matched>
                    newArgs.Add("-activity");
                    newArgs.Add(matched);

                    // 如果原开关后有一个参数（非开关），跳过它以避免重复
                    if (i + 1 < cmdArgs.Count && !cmdArgs[i + 1].StartsWith("-"))
                        i++;
                }
                else
                {
                    // 跳过原始的第一个非开关参数（用于推断 route），其余非开关参数也跳过以避免混淆
                    //（保持参数集中为 -activity ...）
                }
            }

            // 确保至少有一个 -activity 参数
            if (!newArgs.Any())
            {
                newArgs.Add("-activity");
                newArgs.Add(matched);
            }

            var serialized = string.Join("|", newArgs);
            Environment.SetEnvironmentVariable("RUNACT_OVERRIDE_ARGS", serialized);

            MessageBox.Show($"已将所有非 -activity 开关替换为 -activity 并设置活动：{Path.GetFileName(matched)}。\n窗体将关闭，程序将继续启动。", "完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}

