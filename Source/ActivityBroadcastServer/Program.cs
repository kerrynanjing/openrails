using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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
        static readonly Encoding EncodingUnicode = Encoding.Unicode;

        // 合并兼容项到槽数组：index 0 = 兼容的 LastActivity（仅由无索引 SETACT 更新），1..10 = 按钮槽位
        static readonly string[] LastActivities = new string[11]; // 使用 0..10

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

                    while (TryParseUnicodeFrame(recv, out string payload, out int consumed))
                    {
                        if (consumed > 0) recv.RemoveRange(0, consumed);
                        if (string.IsNullOrWhiteSpace(payload)) continue;

                        Console.WriteLine($"收到 payload from {clientKey}: \"{payload}\"");

                        // PLAYER 注册
                        if (payload.StartsWith("PLAYER ", StringComparison.OrdinalIgnoreCase))
                        {
                            var tokens = payload.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                            if (tokens.Length >= 2)
                            {
                                var name = tokens[1];
                                Clients.TryRemove(clientKey, out _);
                                clientKey = name;
                                Clients[clientKey] = client;
                                Console.WriteLine($"客户端注册为：{clientKey}");
                            }

                            // 注册完成后回送：先收集 1..10 槽位的非空活动，再把兼容槽（0）追加（若未重复）
                            var slotActs = LastActivities
                                .Select((v, i) => new { Index = i, Val = v })
                                .Where(x => x.Index >= 1 && !string.IsNullOrEmpty(x.Val))
                                .Select(x => x.Val)
                                .ToList();

                            var compat = LastActivities[0];
                            if (!string.IsNullOrEmpty(compat) &&
                                !slotActs.Contains(compat, StringComparer.OrdinalIgnoreCase))
                            {
                                slotActs.Add(compat);
                            }

                            if (slotActs.Count > 0)
                            {
                                Console.WriteLine($"向 {clientKey} 回送 {slotActs.Count} 个 ACTSET（槽位+兼容）");
                                foreach (var act in slotActs)
                                {
                                    var framed = BuildFrame($"ACTSET {act}");
                                    await SafeSendAsync(client, framed).ConfigureAwait(false);
                                }
                            }
                            else
                            {
                                Console.WriteLine($"向 {clientKey} 未回送任何 ACTSET（无活动）");
                            }
                        }
                        else if (payload.StartsWith("SETACT ", StringComparison.OrdinalIgnoreCase))
                        {
                            var rest = payload.Substring("SETACT ".Length).Trim();
                            if (string.IsNullOrEmpty(rest)) continue;

                            // 支持 "SETACT <index> <value>" 和 "SETACT <value>"
                            var parts = rest.Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
                            if (parts.Length >= 2 && int.TryParse(parts[0], out int idx) && idx >= 1 && idx <= 10)
                            {
                                var val = parts[1].Trim();
                                if (string.IsNullOrEmpty(val)) continue;
                                // 写入指定槽位（不要覆盖兼容槽 0）
                                LastActivities[idx] = val;
                                var framed = BuildFrame($"ACTSET {val}");
                                Broadcast(framed);
                                Console.WriteLine($"SETACT slot {idx} -> {val}（已广播）");
                                DumpSlots();
                            }
                            else
                            {
                                // 兼容 single value -> 写入兼容槽 (index 0)
                                var val = rest;
                                LastActivities[0] = val;
                                var framed = BuildFrame($"ACTSET {val}");
                                Broadcast(framed);
                                Console.WriteLine($"SETACT -> {val}（已广播，更新兼容槽）");
                                DumpSlots();
                            }
                        }
                        else if (payload.StartsWith("GETACT", StringComparison.OrdinalIgnoreCase))
                        {
                            // 返回当前槽位的所有活动（先槽位 1..10，再兼容槽 0 去重添加）
                            var slotActs = LastActivities
                                .Select((v, i) => new { Index = i, Val = v })
                                .Where(x => x.Index >= 1 && !string.IsNullOrEmpty(x.Val))
                                .Select(x => x.Val)
                                .ToList();

                            var compat = LastActivities[0];
                            if (!string.IsNullOrEmpty(compat) &&
                                !slotActs.Contains(compat, StringComparer.OrdinalIgnoreCase))
                            {
                                slotActs.Add(compat);
                            }

                            if (slotActs.Count > 0)
                            {
                                Console.WriteLine($"GETACT -> 回送 {slotActs.Count} 个 ACTSET（槽位+兼容）");
                                foreach (var act in slotActs)
                                {
                                    var framed = BuildFrame($"ACTSET {act}");
                                    await SafeSendAsync(client, framed).ConfigureAwait(false);
                                }
                            }
                            else
                            {
                                Console.WriteLine("GETACT -> 无活动可回送（不发送空 ACTSET）");
                            }
                        }
                        else
                        {
                            // 兼容：如果 payload 看起来像直接的 activity 名称
                            var trimmed = payload.Trim();
                            if (!string.IsNullOrEmpty(trimmed))
                            {
                                // 写入兼容槽（只有无索引行为写入槽0，避免被索引更新覆盖）
                                LastActivities[0] = trimmed;
                                var framed = BuildFrame($"ACTSET {trimmed}");
                                Broadcast(framed);
                                Console.WriteLine($"直接接收 activity -> {trimmed}（已广播）");
                                DumpSlots();
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

        static void DumpSlots()
        {
            var pairs = Enumerable.Range(0, 11).Select(i => $"{i}:{(string.IsNullOrEmpty(LastActivities[i]) ? "<empty>" : LastActivities[i])}");
            Console.WriteLine("当前槽位 (0=兼容): " + string.Join(", ", pairs));
        }

        static byte[] BuildFrame(string payload)
        {
            return EncodingUnicode.GetBytes($" {payload.Length}: {payload}");
        }

        static void Broadcast(byte[] framed)
        {
            var snapshot = Clients.Values.ToArray();
            foreach (var c in snapshot)
            {
                _ = SafeSendAsync(c, framed);
            }
        }

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
                // 忽略单次失败
            }
        }

        static bool TryParseUnicodeFrame(List<byte> buffer, out string payload, out int consumed)
        {
            payload = null;
            consumed = 0;
            if (buffer.Count < 2) return false;

            int colonCharIndex = -1;
            for (int ci = 0; ; ci++)
            {
                int bi = ci * 2;
                if (bi + 1 >= buffer.Count) break;
                if (buffer[bi] == 0x3A && buffer[bi + 1] == 0x00) { colonCharIndex = ci; break; }
            }
            if (colonCharIndex < 0) return false;

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
