using System;
using System.Diagnostics;
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
        private string ActivityName;
        private CancellationTokenSource _cts;

        // 持久化到多人服务器的连接（用于注册/发送多人消息）
        private TcpClient _mpClient;
        private NetworkStream _mpStream;
        private bool _mpRegistered;

        // 用户名（可通过命令行或弹窗设置）
        private string _username;

        // 调试日志：写入系统临时目录，避免工作目录差异
        private readonly string _debugLogPath = Path.Combine(Path.GetTempPath(), "ActivitySelect_receive.log");

        public Form1()
        {
            InitializeComponent();
            _cts = new CancellationTokenSource();
            this.Load += Form1_Load;
            this.FormClosing += Form1_FormClosing;
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            ParseCommandLineForUsername();

            // 记录基目录和启动时间，便于定位日志文件位置
            try { File.AppendAllText(_debugLogPath, $"[{DateTime.Now:O}] Form1 loaded. AppBase={AppDomain.CurrentDomain.BaseDirectory}{Environment.NewLine}"); } catch { }

            _ = StartActivityReceiver("211.101.245.150", 30001, _cts.Token);
            // 在 Form1_Load 中添加：
            _ = SendPlayerOnlyAsync("211.101.245.150", 30001);
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            _cts.Cancel();

            try
            {
                _mpRegistered = false;
                _mpStream?.Close();
                _mpClient?.Close();
            }
            catch { }
        }

        // 解析命令行 -user 或 --user 参数（优先）
        private void ParseCommandLineForUsername()
        {
            var args = Environment.GetCommandLineArgs().Skip(1).ToArray();
            for (int i = 0; i < args.Length; i++)
            {
                var a = args[i];
                if (string.Equals(a, "-user", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(a, "--user", StringComparison.OrdinalIgnoreCase))
                {
                    if (i + 1 < args.Length)
                    {
                        _username = args[i + 1];
                        return;
                    }
                }
                if (string.Equals(a, "-u", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                {
                    _username = args[i + 1];
                    return;
                }
            }
        }

        // 获取用户名：优先命令行 -> 已保存 -> fallback (Environment.UserName_PID)
        private string GetOrPromptUsername()
        {
            if (!string.IsNullOrWhiteSpace(_username))
                return _username;

            var baseUser = Environment.UserName;
            if (string.IsNullOrWhiteSpace(baseUser)) baseUser = "ActivityUser";
            _username = $"{baseUser}_{Process.GetCurrentProcess().Id}";
            return _username;
        }

        private async Task StartActivityReceiver(string host, int port, CancellationToken token)
        {
            var encoding = Encoding.Unicode; // UTF-16LE
            var recv = new System.Collections.Generic.List<byte>();

            while (!token.IsCancellationRequested)
            {
                try
                {
                    using (var client = new TcpClient())
                    {
                        var connectTask = client.ConnectAsync(host, port);
                        using (var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
                        using (var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(token, timeoutCts.Token))
                        {
                            var linkedToken = linkedCts.Token;
                            var t = await Task.WhenAny(connectTask, Task.Delay(Timeout.Infinite, linkedToken)).ConfigureAwait(false);
                            if (t != connectTask || !client.Connected)
                            {
                                await Task.Delay(2000, token).ConfigureAwait(false);
                                continue;
                            }
                        }

                        using (var ns = client.GetStream())
                        {
                            var buffer = new byte[4096];
                            while (!token.IsCancellationRequested)
                            {
                                int bytesRead;
                                try
                                {
                                    bytesRead = await ns.ReadAsync(buffer, 0, buffer.Length, token).ConfigureAwait(false);
                                }
                                catch (OperationCanceledException) when (token.IsCancellationRequested)
                                {
                                    break;
                                }
                                if (bytesRead <= 0) break;

                                // append bytes
                                for (int i = 0; i < bytesRead; i++) recv.Add(buffer[i]);

                                // strip UTF-8 BOM if present at front
                                if (recv.Count >= 3 && recv[0] == 0xEF && recv[1] == 0xBB && recv[2] == 0xBF)
                                    recv.RemoveRange(0, 3);

                                // 只在有偶数字节时尝试（UTF-16LE）
                                while (recv.Count >= 2)
                                {
                                    // 在缓冲任意位置寻找 UTF-16LE 的 ':'（0x3A 0x00）
                                    int colonCharIndex = -1;
                                    for (int ci = 0; ; ci++)
                                    {
                                        int bi = ci * 2;
                                        if (bi + 1 >= recv.Count) break;
                                        if (recv[bi] == 0x3A && recv[bi + 1] == 0x00) { colonCharIndex = ci; break; }
                                    }
                                    if (colonCharIndex < 0) break; // 没找到冒号，等待更多数据

                                    // 找到冒号，向前收集数字字符（0-9）和可选空格
                                    int headerStartChar = colonCharIndex - 1;
                                    while (headerStartChar >= 0)
                                    {
                                        int bi = headerStartChar * 2;
                                        byte lo = recv[bi];
                                        byte hi = recv[bi + 1];
                                        if (hi == 0x00 && (lo >= 0x30 && lo <= 0x39)) headerStartChar--;
                                        else if (hi == 0x00 && (lo == 0x20 || lo == 0x09)) headerStartChar--;
                                        else break;
                                    }
                                    headerStartChar++; // 移回第一个有效字符位置

                                    // 构造数字字符串（从 headerStartChar 到 colonCharIndex-1）
                                    var sb = new System.Text.StringBuilder();
                                    for (int ch = headerStartChar; ch < colonCharIndex; ch++)
                                    {
                                        int bi = ch * 2;
                                        byte lo = recv[bi];
                                        byte hi = recv[bi + 1];
                                        if (hi != 0x00) continue;
                                        char c = (char)lo;
                                        if (char.IsDigit(c)) sb.Append(c);
                                    }

                                    if (!int.TryParse(sb.ToString(), out int payloadChars))
                                    {
                                        // 非法 header，丢弃到冒号后一位再继续
                                        int dropBytes = (colonCharIndex + 1) * 2;
                                        recv.RemoveRange(0, Math.Min(dropBytes, recv.Count));
                                        continue;
                                    }

                                    // 计算 header 实际占用的字节（包含冒号及可能的空格）
                                    int headerCharsCount = (colonCharIndex + 1); // 包含冒号
                                    int headerBytes = headerCharsCount * 2;
                                    if (recv.Count >= headerBytes + 2 && recv[headerBytes] == 0x20 && recv[headerBytes + 1] == 0x00)
                                    {
                                        headerBytes += 2;
                                        headerCharsCount++;
                                    }

                                    int totalCharsNeeded = headerCharsCount + payloadChars;
                                    int totalBytesNeeded = totalCharsNeeded * 2;
                                    if (recv.Count < totalBytesNeeded) break; // 等待更多字节

                                    // 提取 payload 字节并解码
                                    int payloadByteStart = headerBytes;
                                    int payloadByteLen = payloadChars * 2;
                                    var payloadBytes = recv.Skip(payloadByteStart).Take(payloadByteLen).ToArray();
                                    string payload;
                                    try { payload = encoding.GetString(payloadBytes); }
                                    catch { payload = null; }

                                    // 消耗缓冲（header + payload）
                                    int consume = headerBytes + payloadByteLen;
                                    recv.RemoveRange(0, Math.Min(consume, recv.Count));

                                    if (payload == null) continue;

                                    // 解析并处理实际活动字符串（冒号后的部分）
                                    var trimmed = payload.Trim();
                                    int colonPos = trimmed.IndexOf(':');
                                    string actual = (colonPos >= 0 && colonPos + 1 < trimmed.Length) ? trimmed.Substring(colonPos + 1).Trim() : trimmed;

                                    try { ProcessReceivedActivity(actual); } catch { /* 忽略处理异常 */ }
                                } // end while recv >=2
                            } // end read loop
                        } // end using ns
                    } // end using client
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    break;
                }
                catch
                {
                    try { await Task.Delay(2000, token).ConfigureAwait(false); } catch (OperationCanceledException) { break; }
                }
            } // outer while
        }

        private async Task SendPlayerOnlyAsync(string host, int port)
        {
            try
            {
                // 建立持久连接（若已存在则复用）
                await EnsureMpConnectedAsync(host, port).ConfigureAwait(false);

                // 获取用户名（已带 PID 保证唯一）
                string user = GetOrPromptUsername();

                // 构造 PLAYER 帧（与服务器期望格式一致）
                string playerPayload = "PLAYER " + user + " Code 0 0 0 0 0 0 0 0\rLead\rCON\rROUTE\rPATH\r0\r";
                string framedPlayer = " " + playerPayload.Length + ": " + playerPayload;
                var bytes = Encoding.Unicode.GetBytes(framedPlayer);

                // 发送 PLAYER（保持连接，等待服务器接受）
                await _mpStream.WriteAsync(bytes, 0, bytes.Length).ConfigureAwait(false);
                await _mpStream.FlushAsync().ConfigureAwait(false);

                _mpRegistered = true;

                // 可选短等待确保服务器解析
                await Task.Delay(200).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // 写日志到临时文件，便于排查
                try { File.AppendAllText(Path.Combine(Path.GetTempPath(), "ActivitySelect_receive.log"), $"[{DateTime.Now:O}] SendPlayerOnlyAsync error: {ex}{Environment.NewLine}"); } catch { }
            }
        }

        private async Task EnsureMpConnectedAndRegisteredAsync(string host, int port)
        {
            if (_mpClient != null && _mpClient.Connected)
                return;

            _mpClient = new TcpClient();
            await _mpClient.ConnectAsync(host, port).ConfigureAwait(false);
            _mpStream = _mpClient.GetStream();
            _mpRegistered = false;
        }

        private async Task EnsureMpConnectedAsync(string host, int port)
        {
            await EnsureMpConnectedAndRegisteredAsync(host, port).ConfigureAwait(false);
        }

        private async Task SendPlayerAndMessageInOneWriteAsync(string _ignoredUserName, string messagePayload)
        {
            if (_mpClient == null || !_mpClient.Connected || _mpStream == null)
                throw new InvalidOperationException("MP client not connected");

            // 固定为测试时使用的 player（与工具一致）
            string playerPayload = "PLAYER Administrator_3480 Code 0 0 0 0 0 0 0 0\rLead\rCON\rROUTE\rPATH\r0\r";
            string framedPlayer = " " + playerPayload.Length + ": " + playerPayload;
            string framedMessage = " " + messagePayload.Length + ": " + messagePayload;

            // 直接用 Unicode 编码（UTF-16LE），这与工具完全一致
            var enc = Encoding.Unicode;
            byte[] bytesPlayer = enc.GetBytes(framedPlayer);
            byte[] bytesMessage = enc.GetBytes(framedMessage);

            var logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ActivitySelect_send.log");
            try { File.AppendAllText(logPath, $"[{DateTime.Now:O}] (EmbedBytes) Sending PLAYER {bytesPlayer.Length} bytes{Environment.NewLine}"); } catch { }

            // 1) 发送 PLAYER（先注册）
            await _mpStream.WriteAsync(bytesPlayer, 0, bytesPlayer.Length).ConfigureAwait(false);
            await _mpStream.FlushAsync().ConfigureAwait(false);

            // 2) 等待并尝试读取服务器短回应（最多等待 800ms），以确保服务器已解析 PLAYER
            var readBuf = new byte[4096];
            try
            {
                _mpClient.ReceiveTimeout = 800;
                var readTask = _mpStream.ReadAsync(readBuf, 0, readBuf.Length);
                var completed = await Task.WhenAny(readTask, Task.Delay(800)).ConfigureAwait(false);
                if (completed == readTask)
                {
                    int r = await readTask.ConfigureAwait(false);
                    if (r > 0)
                    {
                        try
                        {
                            var text = enc.GetString(readBuf, 0, r);
                            var textEscaped = text.Replace("\r", "\\r").Replace("\n", "\\n");
                            File.AppendAllText(logPath, $"[{DateTime.Now:O}] Server response (decoded): {textEscaped}{Environment.NewLine}");
                        }
                        catch
                        {
                            File.AppendAllText(logPath, $"[{DateTime.Now:O}] Server response: {r} bytes (binary){Environment.NewLine}");
                        }
                    }
                    else
                    {
                        File.AppendAllText(logPath, $"[{DateTime.Now:O}] No server response data.{Environment.NewLine}");
                    }
                }
                else
                {
                    File.AppendAllText(logPath, $"[{DateTime.Now:O}] No immediate server response after PLAYER.{Environment.NewLine}");
                }
            }
            catch (Exception ex)
            {
                try { File.AppendAllText(logPath, $"[{DateTime.Now:O}] ReadResponse error: {ex.Message}{Environment.NewLine}"); } catch { }
            }

            // 3) 发送 MESSAGE（activity）
            try { File.AppendAllText(logPath, $"[{DateTime.Now:O}] (EmbedBytes) Sending MESSAGE {bytesMessage.Length} bytes{Environment.NewLine}"); } catch { }
            await _mpStream.WriteAsync(bytesMessage, 0, bytesMessage.Length).ConfigureAwait(false);
            await _mpStream.FlushAsync().ConfigureAwait(false);

            _mpRegistered = true;

            // 4) 等待短时间以便服务器广播到其它客户端
            await Task.Delay(300).ConfigureAwait(false);

            try { File.AppendAllText(logPath, $"[{DateTime.Now:O}] SendPlayerAndMessage completed.{Environment.NewLine}"); } catch { }
        }

        // button1: 建立持久连接并一次性发送 PLAYER + MESSAGE(activity)
        private async void button1_Click(object sender, EventArgs e)
        {
            // 改为 ASCII 标识
            ActivityName = "T236";

            try
            {
                await EnsureMpConnectedAsync("211.101.245.150", 30001).ConfigureAwait(false);

                // 获取或生成唯一用户名
                string user = GetOrPromptUsername();

                // 使用 ASCII 活动名 T236
                string messagePayload = "MESSAGE All\tInfo\tactivity: T236";

                // 一次性写入 PLAYER + MESSAGE，并保持连接
                await SendPlayerAndMessageInOneWriteAsync(user, messagePayload).ConfigureAwait(false);

                // 本地立即禁用（确保 UI 反馈）
                if (button1.InvokeRequired) button1.Invoke(new Action(() => button1.Enabled = false));
                else button1.Enabled = false;
            }
            catch (Exception ex)
            {
                if (this.InvokeRequired)
                {
                    this.Invoke(new Action(() => MessageBox.Show($"发送多人消息失败：{ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error)));
                }
                else
                {
                    MessageBox.Show($"发送多人消息失败：{ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }

            //TrySetActivityFromButton("T236任务.act");
        }

        private void ProcessReceivedActivity(string received)
        {
            string logPath = Path.Combine(Path.GetTempPath(), "ActivitySelect_receive.log");
            try
            {
                // 1) 清理 BOM 与不可见控制字符
                if (received == null) received = "";
                received = received.Trim().Replace("\uFEFF", "").Replace("\uFFFE", "");

                // 2) 记录原始接收字符串
                File.AppendAllText(logPath, $"[{DateTime.Now:O}] Received raw: \"{received}\"{Environment.NewLine}");

                // 3) 提取首个 ASCII token（如 T236）
                var m = System.Text.RegularExpressions.Regex.Match(received, @"([A-Za-z0-9]+)");
                string asciiToken = m.Success ? m.Groups[1].Value : null;

                // 4) 规范化比较：ASCII 或完整中文
                bool matchAscii = !string.IsNullOrEmpty(asciiToken) && string.Equals(asciiToken, "T236", StringComparison.OrdinalIgnoreCase);
                bool containsAscii = !string.IsNullOrEmpty(received) && received.IndexOf("T236", StringComparison.OrdinalIgnoreCase) >= 0;
                bool equalChinese = string.Equals(received, "T236任务", StringComparison.Ordinal);
                bool containsChinese = received.IndexOf("T236任务", StringComparison.Ordinal) >= 0;

                File.AppendAllText(logPath, $"[{DateTime.Now:O}] asciiToken=\"{asciiToken}\", matchAscii={matchAscii}, containsAscii={containsAscii}, equalChinese={equalChinese}, containsChinese={containsChinese}{Environment.NewLine}");

                // 5) 如果仍然未匹配，记录原始字节（方便比对 hex）
                if (!(matchAscii || containsAscii || equalChinese || containsChinese))
                {
                    // dump codepoints for diagnosis
                    var sb = new StringBuilder();
                    foreach (var c in received) sb.AppendFormat("U+{0:X4} ", (int)c);
                    File.AppendAllText(logPath, $"[{DateTime.Now:O}] Codepoints: {sb}{Environment.NewLine}");
                }

                // 6) 最终判断
                if (matchAscii || containsAscii || equalChinese || containsChinese)
                {
                    if (button1.InvokeRequired) button1.Invoke(new Action(() => button1.Enabled = false));
                    else button1.Enabled = false;
                    File.AppendAllText(logPath, $"[{DateTime.Now:O}] button1 disabled{Environment.NewLine}");
                }
            }
            catch (Exception ex)
            {
                try { File.AppendAllText(logPath, $"[{DateTime.Now:O}] ProcessReceivedActivity error: {ex}{Environment.NewLine}"); } catch { }
            }
        }

        // 其余按钮与 TrySetActivityFromButton 保持不变（省略）
        private void button2_Click(object sender, EventArgs e) { TrySetActivityFromButton("26112任务.act"); }
        private void button3_Click(object sender, EventArgs e) { TrySetActivityFromButton("36369任务.act"); }
        private void button4_Click(object sender, EventArgs e) { TrySetActivityFromButton("46283任务.act"); }
        private void button5_Click(object sender, EventArgs e) { TrySetActivityFromButton("46437任务.act"); }
        private void button6_Click(object sender, EventArgs e) { TrySetActivityFromButton("K34任务.act"); }
        private void button7_Click(object sender, EventArgs e) { TrySetActivityFromButton("K101任务.act"); }
        private void button8_Click(object sender, EventArgs e) { TrySetActivityFromButton("K1556任务.act"); }
        private void button9_Click(object sender, EventArgs e) { TrySetActivityFromButton("X373任务.act"); }
        private void button10_Click(object sender, EventArgs e) { TrySetActivityFromButton("X8715.act"); }
        private void button11_Click(object sender, EventArgs e) { TrySetActivityFromButton("梅钢.act"); }

        private void TrySetActivityFromButton(string targetActName)
        {
            var cmdArgs = Environment.GetCommandLineArgs().Skip(1).ToList();
            if (cmdArgs.Count == 0)
            {
                MessageBox.Show("未检测到启动参数。请确认该窗体是从 Menu/RunActivity 项目启动的，或在调试时传入参数。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

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

            var matched = Directory.GetFiles(activitiesDir, "*.act")
                                  .FirstOrDefault(f => string.Equals(Path.GetFileName(f), targetActName, StringComparison.OrdinalIgnoreCase));
            if (matched == null)
            {
                MessageBox.Show($"在 {activitiesDir} 中未找到 {targetActName}", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var newArgs = new System.Collections.Generic.List<string>();
            for (int i = 0; i < cmdArgs.Count; i++)
            {
                var token = cmdArgs[i];
                if (token.StartsWith("-"))
                {
                    newArgs.Add("-activity");
                    newArgs.Add(matched);
                    if (i + 1 < cmdArgs.Count && !cmdArgs[i + 1].StartsWith("-"))
                        i++;
                }
            }

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

