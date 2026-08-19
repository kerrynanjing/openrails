using System;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ActivitySelect
{
    public partial class Form1 : Form
    {
        // 网络广播客户端字段
        private TcpClient _broadcastClient;
        private StreamWriter _writer;
        private StreamReader _reader;
        private CancellationTokenSource _cts;

        private readonly string _serverIp = "211.101.245.150";
        private readonly int _serverPort = 30001;
        private readonly string _userName = Environment.UserName;

        public Form1()
        {
            InitializeComponent();

            // 窗体加载时建立到广播服务器的连接并开始监听
            Load += async (s, e) => await ConnectToBroadcastServerAsync();

            // 窗体关闭时断开连接
            FormClosing += (s, e) => Disconnect();
        }

        private async Task ConnectToBroadcastServerAsync()
        {
            try
            {
                // 如果已经连接则不重复连接
                if (_broadcastClient != null && _broadcastClient.Connected)
                    return;

                _cts = new CancellationTokenSource();
                _broadcastClient = new TcpClient();
                await _broadcastClient.ConnectAsync(_serverIp, _serverPort).ConfigureAwait(false);
                var ns = _broadcastClient.GetStream();
                _reader = new StreamReader(ns, Encoding.UTF8);
                _writer = new StreamWriter(ns, Encoding.UTF8) { AutoFlush = true };

                // 后台读取服务器消息
                _ = Task.Run(() => ListenLoopAsync(_cts.Token));
            }
            catch (Exception)
            {
                // 连接失败：可以在这里记录日志或实现重试策略
            }
        }

        private async Task ListenLoopAsync(CancellationToken token)
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    var line = await _reader.ReadLineAsync().ConfigureAwait(false);
                    if (line == null)
                        break; // 连接已关闭
                    HandleServerMessage(line);
                }
            }
            catch (Exception)
            {
                // 读取异常：可记录或触发重连
            }
        }

        private void HandleServerMessage(string msg)
        {
            // 简单协议：服务器广播 PLAYER_JOINED:用户名
            if (msg.StartsWith("PLAYER_JOINED:"))
            {
                // 在 UI 线程禁用 button1
                if (InvokeRequired)
                {
                    BeginInvoke(new Action(() => button1.Enabled = false));
                }
                else
                {
                    button1.Enabled = false;
                }
            }
        }

        private async Task SendJoinMessageAsync()
        {
            try
            {
                if (_writer == null)
                {
                    // 尝试建立连接一次
                    await ConnectToBroadcastServerAsync().ConfigureAwait(false);
                    if (_writer == null)
                        return;
                }

                var msg = $"JOIN:{_userName}";
                await _writer.WriteLineAsync(msg).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // 发送失败：忽略或记录，视需要实现重试
            }
        }

        private void Disconnect()
        {
            try
            {
                _cts?.Cancel();
                _reader?.Dispose();
                _writer?.Dispose();
                if (_broadcastClient != null)
                {
                    try { _broadcastClient.Close(); } catch { }
                    _broadcastClient = null;
                }
            }
            catch { }
        }

        private async void button1_Click(object sender, EventArgs e)
        {
            // 先发送 JOIN 消息，告知服务器该玩家已加入
            await SendJoinMessageAsync().ConfigureAwait(false);

            // 原有行为：设置并启动活动
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

