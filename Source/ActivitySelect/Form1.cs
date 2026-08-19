using System;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ActivitySelect
{
    public partial class Form1 : Form
    {
        // 持有用于监听广播的取消令牌与客户端（窗体关闭时取消）
        CancellationTokenSource _broadcastListenCts;



        public Form1()
        {
            InitializeComponent();

            // 在窗体构造时启动后台监听（窗体关闭时会取消）
            _broadcastListenCts = new CancellationTokenSource();
            Task.Run(() => ListenBroadcastServerAsync(_broadcastListenCts.Token));
            
            var _diagPath = Path.Combine(Path.GetTempPath(), "ActivitySelect_ClientLog.txt");
            try
            {
                File.AppendAllText(_diagPath, $"{DateTime.UtcNow:O} Form1 constructed. CmdLine: {string.Join(" | ", Environment.GetCommandLineArgs())}{Environment.NewLine}", Encoding.UTF8);
            }
            catch { }

            // 在 ListenBroadcastServerAsync 开头加入
            try
            {
                var cmdArgs = Environment.GetCommandLineArgs().Skip(1).ToList();
                var parsedIp = ExtractServerIp(cmdArgs);
                File.AppendAllText(_diagPath, $"{DateTime.UtcNow:O} Listen start. ParsedServerIp: {parsedIp}{Environment.NewLine}", Encoding.UTF8);
            }
            catch (Exception ex)
            {
                File.AppendAllText(_diagPath, $"{DateTime.UtcNow:O} Listen exception: {ex.Message}{Environment.NewLine}", Encoding.UTF8);
            }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            // 关闭窗体时取消监听任务并释放资源
            try
            {
                _broadcastListenCts?.Cancel();
                _broadcastListenCts?.Dispose();
            }
            catch { }
            base.OnFormClosed(e);
        }

        private void button1_Click(object sender, EventArgs e)
        {
            var activityName = "T236任务";
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

            // 关键补充：主动发送 activity 名称 到广播服务器，等待短超时以提高可靠性
            var activityNameOnly = Path.GetFileNameWithoutExtension(matched);
            var sendTask = SendActivityToBroadcastServerAsync(activityNameOnly, cmdArgs);
            try
            {
                // 等待最多 1.5 秒以避免 UI 长时间阻塞；失败也继续启动游戏
                sendTask.Wait(1500);
            }
            catch
            {
                // 忽略异常，保持现有启动流程不被阻塞
            }

            MessageBox.Show($"已将所有非 -activity 开关替换为 -activity 并设置活动：{Path.GetFileName(matched)}。\n窗体将关闭，程序将继续启动。", "完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
            DialogResult = DialogResult.OK;
            Close();
        }

        // 后台任务：连接广播服务器并持续接收 activity 名称
        private async Task ListenBroadcastServerAsync(CancellationToken token)
        {
            try
            {
                // 取命令行参数推断服务器 IP（与发送端一致）
                var cmdArgs = Environment.GetCommandLineArgs().Skip(1).ToList();
                var serverIp = ExtractServerIp(cmdArgs);
                if (string.IsNullOrEmpty(serverIp))
                    return;

                while (!token.IsCancellationRequested)
                {
                    using (var client = new TcpClient())
                    {
                        try
                        {
                            var connectTask = client.ConnectAsync(serverIp, 30001);
                            var timeoutTask = Task.Delay(3000, token);
                            var finished = await Task.WhenAny(connectTask, timeoutTask).ConfigureAwait(false);
                            if (finished == timeoutTask)
                            {
                                // 无法连接，等待后重试
                                await Task.Delay(2000, token).ConfigureAwait(false);
                                continue;
                            }

                            if (!client.Connected)
                            {
                                await Task.Delay(2000, token).ConfigureAwait(false);
                                continue;
                            }

                            using (var stream = client.GetStream())
                            using (var reader = new StreamReader(stream, Encoding.UTF8))
                            {
                                // 持续读取服务器推送（每行一个 activity 名称）
                                while (!token.IsCancellationRequested)
                                {
                                    string line;
                                    try
                                    {
                                        line = await reader.ReadLineAsync().ConfigureAwait(false);
                                    }
                                    catch
                                    {
                                        // 读取中断，跳出到外层重连
                                        break;
                                    }

                                    if (line == null)
                                        break;

                                    var activityName = line.Trim();
                                    if (string.IsNullOrEmpty(activityName))
                                        continue;

                                    // 处理接收到的 activity 名称（如为 "T236任务" 则禁用 button1）
                                    HandleReceivedActivity(activityName);
                                }
                            }
                        }
                        catch (OperationCanceledException)
                        {
                            break;
                        }
                        catch
                        {
                            // 忽略异常并在循环中重连
                        }
                    }

                    // 断线后等待一会儿再重连
                    try
                    {
                        await Task.Delay(2000, token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
            }
            catch
            {
                // 忽略，窗体关闭时会停止
            }
        }

        // 在 UI 线程上处理收到的 activityName
        private void HandleReceivedActivity(string activityName)
        {
            if (string.IsNullOrEmpty(activityName))
                return;

            // 如果收到的 activityName 等于我们要禁用的活动名（示例：T236任务），则在 UI 线程上禁用对应按钮
            if (activityName.Equals("T236任务", StringComparison.Ordinal))
            {
                if (InvokeRequired)
                {
                    BeginInvoke(new Action(() => button1.Enabled = false));
                }
                else
                {
                    button1.Enabled = false;
                }
            }

            // 若需对其它按钮做类似处理，可在此处扩展：
            // e.g. if (activityName.Equals("26112任务", StringComparison.Ordinal)) Disable button2 ...
        }

        // 主动发送 activity 到广播服务器（不抛异常）
        private static async Task SendActivityToBroadcastServerAsync(string activityName, System.Collections.Generic.List<string> cmdArgs)
        {
            try
            {
                var serverIp = ExtractServerIp(cmdArgs);
                if (string.IsNullOrEmpty(serverIp))
                {
                    // 未检测到 IP，不发送
                    return;
                }

                using (var client = new TcpClient())
                {
                    var connectTask = client.ConnectAsync(serverIp, 30001);
                    var timeoutTask = Task.Delay(3000);
                    var finished = await Task.WhenAny(connectTask, timeoutTask).ConfigureAwait(false);
                    if (finished == timeoutTask)
                    {
                        // 超时
                        return;
                    }

                    if (!client.Connected)
                        return;

                    using (var writer = new StreamWriter(client.GetStream(), Encoding.UTF8) { AutoFlush = true })
                    {
                        await writer.WriteLineAsync(activityName).ConfigureAwait(false);
                    }
                }
            }
            catch
            {
                // 忽略网络错误：不影响主流程
            }
        }

        // 与发送端一致的 IP/主机名解析逻辑（从命令行参数提取）
        private static string ExtractServerIp(System.Collections.Generic.List<string> cmdArgs)
        {
            for (int i = 0; i < cmdArgs.Count; i++)
            {
                var t = cmdArgs[i].Trim();

                // 常见开关：-ip, -host, -hostname，后面跟地址
                if (t.Equals("-ip", System.StringComparison.OrdinalIgnoreCase) ||
                    t.Equals("ip", System.StringComparison.OrdinalIgnoreCase) ||
                    t.Equals("-host", System.StringComparison.OrdinalIgnoreCase) ||
                    t.Equals("host", System.StringComparison.OrdinalIgnoreCase) ||
                    t.Equals("-hostname", System.StringComparison.OrdinalIgnoreCase) ||
                    t.Equals("hostname", System.StringComparison.OrdinalIgnoreCase))
                {
                    if (i + 1 < cmdArgs.Count)
                    {
                        var candidate = StripPort(cmdArgs[i + 1]);
                        if (IsValidHostOrIp(candidate))
                            return candidate;
                    }
                }

                // 支持形如 "-connect=1.2.3.4:30001" 或 "connect:1.2.3.4"
                var m = System.Text.RegularExpressions.Regex.Match(t, @"[:=](?<addr>.+)$");
                if (m.Success)
                {
                    var addr = StripPort(m.Groups["addr"].Value.Trim());
                    if (IsValidHostOrIp(addr))
                        return addr;
                }
            }

            // 回退：从所有 token 中选第一个看起来像 host/ip 的，但跳过以 '-' 开头的参数（开关）
            foreach (var token in cmdArgs)
            {
                var s = token.Trim();
                if (s.StartsWith("-"))
                    continue;
                var candidate = StripPort(s);
                if (IsValidHostOrIp(candidate))
                    return candidate;
            }

            return null;
        }

        private static bool IsValidHostOrIp(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
                return false;

            token = token.Trim();

            // 不允许以 '-' 开头（避免把开关误判为主机名）
            if (token.StartsWith("-"))
                return false;

            // IPv4 验证并检查每段范围 0-255
            var ipv4 = new System.Text.RegularExpressions.Regex(@"^\d{1,3}(\.\d{1,3}){3}$");
            if (ipv4.IsMatch(token))
            {
                var parts = token.Split('.');
                foreach (var p in parts)
                {
                    if (!int.TryParse(p, out var v))
                        return false;
                    if (v < 0 || v > 255)
                        return false;
                }
                return true;
            }

            // 主机名：必须以字母或数字开始并以字母或数字结束，允许中间包含字母、数字、点或短横
            var hostname = new System.Text.RegularExpressions.Regex(@"^[A-Za-z0-9](?:[A-Za-z0-9\.\-]{0,252}[A-Za-z0-9])?$");
            if (hostname.IsMatch(token))
                return true;

            return false;
        }

        // 辅助：若带端口则剥离端口部分（host:port 或 [ipv6]:port）
        private static string StripPort(string input)
        {
            if (string.IsNullOrEmpty(input))
                return input;

            // 形如 host:port（不处理 IPv6 复杂情况，这里主要针对 IPv4/hostname:port）
            var idx = input.LastIndexOf(':');
            if (idx > 0)
            {
                var left = input.Substring(0, idx);
                var right = input.Substring(idx + 1);
                // 若冒号后的部分都是数字则视为端口，剥离
                if (int.TryParse(right, out _))
                    return left;
            }

            return input;
        }
    }
}
