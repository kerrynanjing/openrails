using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace ActivityBroadcastServer
{
    class Program
    {
        const int Port = 30001;
        static readonly ConcurrentDictionary<TcpClient, StreamWriter> Clients = new ConcurrentDictionary<TcpClient, StreamWriter>();

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
                listener.Stop();
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
                foreach (var c in Clients.Keys)
                {
                    try { c.Close(); } catch { }
                }
                Console.WriteLine("服务器已停止。");
            }
        }

        static async Task HandleClientAsync(TcpClient client)
        {
            var endPoint = client.Client.RemoteEndPoint?.ToString() ?? "<unknown>";
            Console.WriteLine($"客户端已连接：{endPoint}");
            try
            {
                var stream = client.GetStream();
                var reader = new StreamReader(stream, Encoding.UTF8);
                var writer = new StreamWriter(stream, Encoding.UTF8) { AutoFlush = true };
                Clients.TryAdd(client, writer);

                // 连接建立后立即把当前已知的 activity 发送给新客户端（如果有）
                if (!string.IsNullOrEmpty(LastActivity))
                {
                    try
                    {
                        await writer.WriteLineAsync(LastActivity).ConfigureAwait(false);
                    }
                    catch
                    {
                        // 忽略发送错误，接下来循环会清理
                    }
                }

                while (client.Connected)
                {
                    string line;
                    try
                    {
                        line = await reader.ReadLineAsync().ConfigureAwait(false);
                    }
                    catch
                    {
                        break;
                    }

                    if (line == null)
                        break;

                    // 收到 activity 名称，保存并广播给所有客户端
                    var msg = line.Trim();
                    Console.WriteLine($"来自 {endPoint} 的 activity: {msg}");
                    LastActivity = msg;
                    Broadcast(msg);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"处理客户端 {endPoint} 时发生异常：{ex.Message}");
            }
            finally
            {
                Clients.TryRemove(client, out _);
                try { client.Close(); } catch { }
                Console.WriteLine($"客户端已断开：{endPoint}");
            }
        }

        static void Broadcast(string message)
        {
            foreach (var kv in Clients)
            {
                var writer = kv.Value;
                try
                {
                    writer.WriteLine(message);
                }
                catch
                {
                    // 忽略单个发送错误，清理会在读取循环中完成
                }
            }
        }
    }
}
