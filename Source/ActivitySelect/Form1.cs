using System;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace ActivitySelect
{
    public partial class Form1 : Form
    {
        public Form1()
        {
            InitializeComponent();
        }

        private void button1_Click(object sender, EventArgs e)
        {
            TrySetActivityFromButton("T236任务.act");
        }

        private void button2_Click(object sender, EventArgs e)
        {
            TrySetActivityFromButton("26112任务.act");
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

            // 查找包含 "explor" 的开关（-explorer / -exploreactivity / -explore 等）
            var explorIndex = cmdArgs.FindIndex(a => a.StartsWith("-") && a.ToLowerInvariant().Contains("explor"));
            if (explorIndex < 0)
            {
                MessageBox.Show("未找到包含 'explor' 的参数（例如 -explorer、-exploreactivity 等），无法推断路由目录。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (explorIndex + 1 >= cmdArgs.Count)
            {
                MessageBox.Show("explore 开关后缺少路径参数，无法推断路由目录。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var possiblePath = cmdArgs[explorIndex + 1];
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

            // 构造新的参数列表：移除 explore 段（保守跳过开关 + 最多 5 个参数），插入 -activity "<matched>"
            var skipAfterExplor = 1 + 5;
            var newArgs = cmdArgs.Take(explorIndex).ToList();
            newArgs.Add("-activity");
            newArgs.Add(matched);
            if (explorIndex + skipAfterExplor < cmdArgs.Count)
                newArgs.AddRange(cmdArgs.Skip(explorIndex + skipAfterExplor));

            // 写入环境变量以供 RunActivity.Program.Main 使用（调试时可观察）
            var serialized = string.Join("|", newArgs);
            Environment.SetEnvironmentVariable("RUNACT_OVERRIDE_ARGS", serialized);

            // 显示成功提示，关闭表单以继续后续流程
            MessageBox.Show($"已为按钮设置并找到活动：{Path.GetFileName(matched)}。\n窗体将关闭，程序将继续启动。", "完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
            DialogResult = DialogResult.OK;
            Close();
        }

        private void Form1_Load(object sender, EventArgs e)
        {

        }
    }
}
