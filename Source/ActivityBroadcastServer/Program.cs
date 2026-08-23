using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace ActivityBroadcastServer
{
    class Program
    {
        const int Port = 30001;
        static readonly ConcurrentDictionary<string, TcpClient> Clients = new ConcurrentDictionary<string, TcpClient>();
        // 使用与 ActivitySelect 相同的编码（UTF-16LE）
        static readonly Encoding EncodingUnicode = Encoding.Unicode;
        // 保存最后一次收到的 activity（用于新连接的客户端立刻同步）
        static volatile string LastActivity = null;

        static async Task Main(string[] args)
        {
            Console.WriteLine($"ActivityBroadcastServer 启动，监听端口 {Port}");
            var listener = new TcpListener(IPAddress.Any, Port);
            listener.Start();

            Console.CancelKeyPress += (s, e) =>
            {
                Console.WriteLine("正在关闭服务器...");
                try { listener.Stop(); } catch { }
            };

            try
            {
                while (true)
                {
                    var client = await listener.AcceptTcpClientAsync().ConfigureAwait(false);
                    _ = HandleClientAsync(client);
                }
            }
            catch (SocketException ex)
            {
                Console.WriteLine($"监听中止：{ex.Message}");
            }
            finally
            {
                foreach (var kv in Clients)
                {
                    try { kv.Value.Close(); } catch { }
                }
                Console.WriteLine("服务器已停止。");
            }
        }

        static async Task HandleClientAsync(TcpClient client)
        {
            string clientKey = client.Client.RemoteEndPoint?.ToString() ?? Guid.NewGuid().ToString();
            Console.WriteLine($"客户端已连接：{clientKey}");
            Clients[clientKey] = client;

            var stream = client.GetStream();
            var recv = new List<byte>();

            try
            {
                while (client.Connected)
                {
                    var buf = new byte[4096];
                    int read;
                    try
                    {
                        read = await stream.ReadAsync(buf, 0, buf.Length).ConfigureAwait(false);
                    }
                    catch
                    {
                        break;
                    }
                    if (read <= 0) break;

                    for (int i = 0; i < read; i++) recv.Add(buf[i]);

                    // 尝试解析所有完整帧（Unicode 帧格式：" {len}: {payload}"）
                    while (TryParseUnicodeFrame(recv, out string payload, out int consumed))
                    {
                        if (consumed > 0) recv.RemoveRange(0, consumed);
                        if (string.IsNullOrWhiteSpace(payload)) continue;

                        // 处理 payload
                        // 1) PLAYER 注册：尝试提取用户名并用作 key
                        if (payload.StartsWith("PLAYER ", StringComparison.OrdinalIgnoreCase))
                        {
                            var tokens = payload.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                            if (tokens.Length >= 2)
                            {
                                var name = tokens[1];
                                // 替换 key（保持连接映射）
                                Clients.TryRemove(clientKey, out _);
                                clientKey = name;
                                Clients[clientKey] = client;
                                Console.WriteLine($"客户端注册为：{clientKey}");
                            }

                            // 注册完成后立即发送 ACTSET（若存在）
                            if (!string.IsNullOrEmpty(LastActivity))
                            {
                                var framed = BuildFrame($"ACTSET {LastActivity}");
                                await SafeSendAsync(client, framed).ConfigureAwait(false);
                            }
                        }
                        else if (payload.StartsWith("SETACT ", StringComparison.OrdinalIgnoreCase))
                        {
                            var val = payload.Substring("SETACT ".Length).Trim();
                            LastActivity = val;
                            var framed = BuildFrame($"ACTSET {val}");
                            Broadcast(framed);
                            Console.WriteLine($"SETACT -> {val}（已广播）");
                        }
                        else if (payload.StartsWith("GETACT", StringComparison.OrdinalIgnoreCase))
                        {
                            var framed = BuildFrame($"ACTSET {(LastActivity ?? string.Empty)}");
                            await SafeSendAsync(client, framed).ConfigureAwait(false);
                            Console.WriteLine($"GETACT -> 回送 ACTSET {(LastActivity ?? string.Empty)}");
                        }
                        else
                        {
                            // 兼容性：如果 payload 本身看起来像直接的 activity 名称，保存并广播 ACTSET
                            // 例如某些客户端直接发送 "T236"
                            var trimmed = payload.Trim();
                            if (!string.IsNullOrEmpty(trimmed))
                            {
                                LastActivity = trimmed;
                                var framed = BuildFrame($"ACTSET {trimmed}");
                                Broadcast(framed);
                                Console.WriteLine($"直接接收 activity -> {trimmed}（已广播）");
                            }
                        }
                    } // end while parse frames
                } // end while connected
            }
            catch (Exception ex)
            {
                Console.WriteLine($"处理客户端 {clientKey} 时发生异常：{ex.Message}");
            }
            finally
            {
                Clients.TryRemove(clientKey, out _);
                try { client.Close(); } catch { }
                Console.WriteLine($"客户端已断开：{clientKey}");
            }
        }

        // 构造 Unicode 帧：" {len}: {payload}" 并返回字节数组（UTF-16LE）
        static byte[] BuildFrame(string payload)
        {
            return EncodingUnicode.GetBytes($" {payload.Length}: {payload}");
        }

        // 广播二进制 framed 消息给所有客户端（并发安全）
        static void Broadcast(byte[] framed)
        {
            var snapshot = Clients.Values.ToArray();
            foreach (var c in snapshot)
            {
                _ = SafeSendAsync(c, framed);
            }
        }

        // 安全发送（忽略单次失败）
        static async Task SafeSendAsync(TcpClient client, byte[] data)
        {
            if (client == null || !client.Connected) return;
            try
            {
                var s = client.GetStream();
                await s.WriteAsync(data, 0, data.Length).ConfigureAwait(false);
                await s.FlushAsync().ConfigureAwait(false);
            }
            catch
            {
                // 忽略单次失败，客户端清理在接收循环完成时进行
            }
        }

        // 解析 Unicode 帧；返回 payload（字符串）和已消耗的字节数
        static bool TryParseUnicodeFrame(List<byte> buffer, out string payload, out int consumed)
        {
            payload = null;
            consumed = 0;
            if (buffer.Count < 2) return false;

            // 在 UTF-16LE 下寻找 ':' (0x3A 0x00)
            int colonCharIndex = -1;
            for (int ci = 0; ; ci++)
            {
                int bi = ci * 2;
                if (bi + 1 >= buffer.Count) break;
                if (buffer[bi] == 0x3A && buffer[bi + 1] == 0x00) { colonCharIndex = ci; break; }
            }
            if (colonCharIndex < 0) return false;

            // 向前找 header 的数字部分（长度），允许空格
            int headerStartChar = colonCharIndex - 1;
            while (headerStartChar >= 0)
            {
                int bi = headerStartChar * 2;
                byte lo = buffer[bi];
                byte hi = buffer[bi + 1];
                if (hi == 0x00 && (lo >= 0x30 && lo <= 0x39)) headerStartChar--;
                else if (hi == 0x00 && (lo == 0x20 || lo == 0x09)) headerStartChar--;
                else break;
            }
            headerStartChar++;

            var sb = new StringBuilder();
            for (int ch = headerStartChar; ch < colonCharIndex; ch++)
            {
                int bi = ch * 2;
                byte lo = buffer[bi];
                byte hi = buffer[bi + 1];
                if (hi != 0x00) continue;
                char c = (char)lo;
                if (char.IsDigit(c)) sb.Append(c);
            }
            if (!int.TryParse(sb.ToString(), out int payloadChars)) return false;

            int headerCharsCount = (colonCharIndex + 1);
            int headerBytes = headerCharsCount * 2;
            if (buffer.Count >= headerBytes + 2 && buffer[headerBytes] == 0x20 && buffer[headerBytes + 1] == 0x00)
            {
                headerBytes += 2;
                headerCharsCount++;
            }

            int totalBytesNeeded = (headerCharsCount + payloadChars) * 2;
            if (buffer.Count < totalBytesNeeded) return false;

            int payloadByteStart = headerBytes;
            int payloadByteLen = payloadChars * 2;
            var payloadBytes = buffer.Skip(payloadByteStart).Take(payloadByteLen).ToArray();
            try
            {
                payload = EncodingUnicode.GetString(payloadBytes);
            }
            catch
            {
                payload = null;
            }

            consumed = totalBytesNeeded;
            return true;
        }
    }
}
