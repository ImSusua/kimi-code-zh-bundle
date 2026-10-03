// KimiModelAdder - Kimi Code 模型供应商添加工具（GUI）
// 编译（系统自带 csc，C# 5 语法）：
//   csc /target:winexe /out:KimiModelAdder.exe /r:System.dll /r:System.Core.dll
//       /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll
//       /r:System.Net.Http.dll KimiModelAdder.cs
// 命令行：--selftest [config路径]  自检 TOML 解析/合并
//         --fetchtest <base_url> <api_key>  测试获取模型列表
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace KimiModelAdder
{
    internal class Kv
    {
        public string Key;
        public string Value; // 保留原始写法（含引号或数字）
        public Kv(string key, string value) { Key = key; Value = value; }
    }

    internal class TomlBlock
    {
        public string Name = "";            // ""=根, "providers.susu", "models.x", "providers.susu.models"
        public bool IsArrayTable;
        public List<Kv> Items = new List<Kv>();
        public List<string> RawLines = new List<string>(); // 原始行（含头），未托管块原样输出
    }

    internal class ModelInfo
    {
        public string Id;
        public string Alias;
    }

    internal static class TomlConfig
    {
        private static readonly Regex HeaderRe = new Regex("^\\s*\\[\\[(.+?)\\]\\]\\s*$", RegexOptions.Compiled);
        private static readonly HeaderRe2Helper H2 = new HeaderRe2Helper();
        private static readonly Regex KeyValRe = new Regex("^\\s*([A-Za-z0-9_\\-\\.]+)\\s*=\\s*(.+?)\\s*$", RegexOptions.Compiled);

        private class HeaderRe2Helper
        {
            public Regex Re = new Regex("^\\s*\\[(.+?)\\]\\s*$", RegexOptions.Compiled);
        }

        public static List<TomlBlock> Parse(string path)
        {
            var blocks = new List<TomlBlock>();
            var root = new TomlBlock { Name = "" };
            blocks.Add(root);
            var cur = root;
            foreach (var raw in File.ReadAllLines(path, Encoding.UTF8))
            {
                var line = raw;
                if (line.Trim().Length == 0 || line.TrimStart().StartsWith("#"))
                {
                    cur.RawLines.Add(line);
                    continue;
                }
                var mArr = HeaderRe.Match(line);
                var mSec = mArr.Success ? Match.Empty : H2.Re.Match(line);
                if (mArr.Success || mSec.Success)
                {
                    var b = new TomlBlock();
                    if (mArr.Success) { b.Name = mArr.Groups[1].Value.Trim(); b.IsArrayTable = true; }
                    else b.Name = mSec.Groups[1].Value.Trim();
                    b.RawLines.Add(line);
                    blocks.Add(b);
                    cur = b;
                    continue;
                }
                var mkv = KeyValRe.Match(line);
                if (mkv.Success)
                {
                    cur.Items.Add(new Kv(mkv.Groups[1].Value, mkv.Groups[2].Value));
                    cur.RawLines.Add(line);
                }
                else
                {
                    cur.RawLines.Add(line); // 不认识的行原样保留
                }
            }
            return blocks;
        }

        public static TomlBlock Find(List<TomlBlock> blocks, string name)
        {
            foreach (var b in blocks) if (b.Name == name && !b.IsArrayTable) return b;
            return null;
        }

        public static string GetValue(TomlBlock b, string key)
        {
            if (b == null) return null;
            foreach (var kv in b.Items) if (kv.Key == key) return Unquote(kv.Value);
            return null;
        }

        public static string Unquote(string v)
        {
            if (v == null) return null;
            v = v.Trim();
            if (v.Length >= 2 && v.StartsWith("\"") && v.EndsWith("\""))
                return v.Substring(1, v.Length - 2);
            return v;
        }

        public static string Q(string s)
        {
            return "\"" + (s == null ? "" : s.Replace("\\", "\\\\").Replace("\"", "\\\"")) + "\"";
        }

        // 托管写入：重写 providers.<id> 相关块与该 provider 的 models.<别名> 块，并设置根 default_*
        // 注意：根 default_model 必须是 [models.<别名>] 的别名键，不能是原始模型 ID（否则运行时 lookup 失败）
        public static void MergeProvider(List<TomlBlock> blocks, string id, string baseUrl, string apiKey,
            List<ModelInfo> models, int maxContext, int maxOutput, string defaultAlias, string defaultModelId)
        {
            var root = Find(blocks, "");
            // 1) 根键：删除旧的 default_provider/default_model，插入新的到最前
            root.Items.RemoveAll(delegate(Kv kv) { return kv.Key == "default_provider" || kv.Key == "default_model"; });
            root.Items.Insert(0, new Kv("default_model", Q(defaultAlias)));
            root.Items.Insert(0, new Kv("default_provider", Q(id)));

            // 2) 删除该 provider 的所有块，以及属于它的 [models.*] 别名块
            string pfx = "providers." + id + ".";
            var toRemove = new List<TomlBlock>();
            foreach (var b in blocks)
            {
                if (b.Name == "providers." + id) { toRemove.Add(b); continue; }
                if (b.Name.StartsWith(pfx)) { toRemove.Add(b); continue; }
                if (b.Name.StartsWith("models.") && !b.IsArrayTable)
                {
                    var p = GetValue(b, "provider");
                    if (p == id) toRemove.Add(b);
                }
            }
            foreach (var b in toRemove) blocks.Remove(b);

            // 3) 追加新块
            var prov = new TomlBlock { Name = "providers." + id };
            prov.Items.Add(new Kv("type", Q("openai")));
            prov.Items.Add(new Kv("base_url", Q(baseUrl)));
            if (!string.IsNullOrEmpty(apiKey)) prov.Items.Add(new Kv("api_key", Q(apiKey)));
            prov.Items.Add(new Kv("default_model", Q(defaultModelId)));
            blocks.Add(prov);
            foreach (var m in models)
            {
                var pm = new TomlBlock { Name = "providers." + id + ".models", IsArrayTable = true };
                pm.Items.Add(new Kv("model", Q(m.Id)));
                pm.Items.Add(new Kv("max_context_size", maxContext.ToString()));
                pm.Items.Add(new Kv("display_name", Q(m.Id)));
                blocks.Add(pm);
            }
            foreach (var m in models)
            {
                var mb = new TomlBlock { Name = "models." + m.Alias };
                mb.Items.Add(new Kv("provider", Q(id)));
                mb.Items.Add(new Kv("model", Q(m.Id)));
                mb.Items.Add(new Kv("maxContextSize", maxContext.ToString()));
                if (maxOutput > 0) mb.Items.Add(new Kv("maxOutputSize", maxOutput.ToString()));
                mb.Items.Add(new Kv("displayName", Q(m.Id)));
                blocks.Add(mb);
            }
        }

        public static void Save(List<TomlBlock> blocks, string path)
        {
            var sb = new StringBuilder();
            foreach (var b in blocks)
            {
                if (b.Name == "")
                {
                    // 根块：default_* 在前，其余键、注释原样
                    foreach (var kv in b.Items) sb.AppendLine(kv.Key + " = " + kv.Value);
                    foreach (var l in b.RawLines)
                    {
                        var t = l.Trim();
                        if (t.Length == 0 || t.StartsWith("#")) sb.AppendLine(l);
                    }
                }
                else
                {
                    sb.AppendLine(b.IsArrayTable ? "[[" + b.Name + "]]" : "[" + b.Name + "]");
                    foreach (var kv in b.Items) sb.AppendLine(kv.Key + " = " + kv.Value);
                    sb.AppendLine();
                }
            }
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        }
    }

    internal static class Api
    {
        // GET {base}/models，返回模型 id 列表；失败抛异常（含状态码与响应片段）
        public static List<string> FetchModels(string baseUrl, string apiKey)
        {
            var url = baseUrl.TrimEnd('/') + "/models";
            using (var client = new HttpClient())
            {
                client.Timeout = TimeSpan.FromSeconds(30);
                using (var req = new HttpRequestMessage(HttpMethod.Get, url))
                {
                    req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
                    req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                    var resp = client.SendAsync(req).GetAwaiter().GetResult();
                    var body = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                    if (!resp.IsSuccessStatusCode)
                        throw new Exception("HTTP " + (int)resp.StatusCode + " " + body.Substring(0, Math.Min(200, body.Length)));
                    var js = new JavaScriptSerializer { MaxJsonLength = 1 << 28 };
                    var obj = js.DeserializeObject(body) as Dictionary<string, object>;
                    if (obj == null || !obj.ContainsKey("data")) throw new Exception("响应缺少 data 字段: " + body.Substring(0, Math.Min(200, body.Length)));
                    var arr = obj["data"] as object[];
                    var ids = new List<string>();
                    if (arr != null)
                        foreach (var it in arr)
                        {
                            var d = it as Dictionary<string, object>;
                            if (d != null && d.ContainsKey("id") && d["id"] != null) ids.Add(d["id"].ToString());
                        }
                    ids.Sort(StringComparer.OrdinalIgnoreCase);
                    return ids;
                }
            }
        }

        public static string MakeAlias(string modelId, List<string> used)
        {
            var s = modelId;
            var slash = s.LastIndexOf('/');
            if (slash >= 0) s = s.Substring(slash + 1);
            var sb = new StringBuilder();
            foreach (var ch in s)
            {
                var c = ch;
                if (!(char.IsLetterOrDigit(c) || c == '_' || c == '-')) c = '-';
                sb.Append(c);
            }
            var alias = sb.ToString().Trim('-');
            if (alias.Length == 0) alias = "model";
            var candidate = alias; var i = 2;
            while (used.Contains(candidate)) { candidate = alias + "-" + i; i++; }
            used.Add(candidate);
            return candidate;
        }
    }

    internal class MainForm : Form
    {
        private TextBox txtProvider;
        private TextBox txtBase;
        private TextBox txtKey;
        private CheckBox chkShow;
        private Button btnFetch;
        private Button btnApply;
        private Button btnOpenCfg;
        private CheckedListBox clbModels;
        private ComboBox cboDefault;
        private NumericUpDown numContext;
        private NumericUpDown numOutput;
        private TextBox txtLog;
        private Label lblStatus;
        private TextBox txtSearch;
        private Button btnAll;
        private Button btnNone;
        private List<string> allIds = new List<string>();      // 全部模型 id
        private HashSet<string> checkedIds = new HashSet<string>(); // 勾选状态（跨过滤保留）

        public MainForm()
        {
            Text = "Kimi Code 模型供应商添加工具";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(760, 640);
            Font = new Font("Microsoft YaHei UI", 9F);

            var lbl1 = new Label { Text = "供应商 ID:", Location = new Point(16, 18), AutoSize = true };
            txtProvider = new TextBox { Location = new Point(110, 14), Width = 160, Text = "susu" };
            var lbl2 = new Label { Text = "Base URL:", Location = new Point(300, 18), AutoSize = true };
            txtBase = new TextBox { Location = new Point(380, 14), Width = 364, Text = "https://susu.fucku.top/v1" };

            var lbl3 = new Label { Text = "API Key:", Location = new Point(16, 52), AutoSize = true };
            txtKey = new TextBox { Location = new Point(110, 48), Width = 500, UseSystemPasswordChar = true };
            chkShow = new CheckBox { Text = "显示", Location = new Point(620, 48), AutoSize = true };
            chkShow.CheckedChanged += delegate { txtKey.UseSystemPasswordChar = !chkShow.Checked; };

            btnFetch = new Button { Text = "获取模型列表", Location = new Point(16, 84), Size = new Size(130, 30) };
            lblStatus = new Label { Text = "", Location = new Point(160, 90), AutoSize = true, ForeColor = Color.DimGray };

            var lblModels = new Label { Text = "模型（勾选要添加的）:", Location = new Point(16, 124), AutoSize = true };
            txtSearch = new TextBox { Location = new Point(16, 144), Width = 300 };
            btnAll = new Button { Text = "全选", Location = new Point(330, 142), Size = new Size(72, 26) };
            btnNone = new Button { Text = "全不选", Location = new Point(406, 142), Size = new Size(72, 26) };
            clbModels = new CheckedListBox { Location = new Point(16, 174), Size = new Size(460, 252), CheckOnClick = true };
            clbModels.ItemCheck += delegate(object s, ItemCheckEventArgs e)
            {
                try
                {
                    var id = clbModels.Items[e.Index] as string;
                    if (id == null) return;
                    if (e.NewValue == CheckState.Checked) checkedIds.Add(id); else checkedIds.Remove(id);
                }
                catch { }
            };

            var lblDefault = new Label { Text = "默认模型:", Location = new Point(496, 150), AutoSize = true };
            cboDefault = new ComboBox { Location = new Point(496, 170), Width = 248, DropDownStyle = ComboBoxStyle.DropDownList };
            var lblCtx = new Label { Text = "上下文长度:", Location = new Point(496, 210), AutoSize = true };
            numContext = new NumericUpDown { Location = new Point(496, 230), Width = 120, Maximum = 2000000, Value = 200000, Increment = 1000 };
            var lblOut = new Label { Text = "最大输出 (0=不限):", Location = new Point(496, 268), AutoSize = true };
            numOutput = new NumericUpDown { Location = new Point(496, 288), Width = 120, Maximum = 2000000, Value = 65536, Increment = 1024 };

            btnApply = new Button { Text = "写入全局 config.toml", Location = new Point(496, 340), Size = new Size(248, 36) };
            btnOpenCfg = new Button { Text = "打开配置目录", Location = new Point(496, 384), Size = new Size(248, 30) };

            txtLog = new TextBox { Location = new Point(16, 436), Size = new Size(728, 180), Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BackColor = Color.White };

            Controls.Add(lbl1); Controls.Add(txtProvider);
            Controls.Add(lbl2); Controls.Add(txtBase);
            Controls.Add(lbl3); Controls.Add(txtKey); Controls.Add(chkShow);
            Controls.Add(btnFetch); Controls.Add(lblStatus);
            Controls.Add(lblModels); Controls.Add(txtSearch); Controls.Add(btnAll); Controls.Add(btnNone);
            Controls.Add(clbModels);
            Controls.Add(lblDefault); Controls.Add(cboDefault);
            Controls.Add(lblCtx); Controls.Add(numContext);
            Controls.Add(lblOut); Controls.Add(numOutput);
            Controls.Add(btnApply); Controls.Add(btnOpenCfg);
            Controls.Add(txtLog);

            btnFetch.Click += OnFetch;
            btnApply.Click += OnApply;
            txtSearch.TextChanged += delegate { RenderModels(); };
            btnAll.Click += delegate { SetAllVisible(true); };
            btnNone.Click += delegate { SetAllVisible(false); };
            btnOpenCfg.Click += delegate
            {
                var dir = ConfigPath();
                try
                {
                    if (File.Exists(dir)) System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + dir + "\"");
                    else System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + dir + "\"");
                }
                catch { try { System.Diagnostics.Process.Start("explorer.exe", Path.GetDirectoryName(dir)); } catch { } }
            };

            Log("配置文件: " + ConfigPath());
            Log("填写 Base URL 与 API Key 后点击“获取模型列表”。");
        }

        private string ConfigPath()
        {
            // 全局配置：优先 KIMI_CODE_HOME 环境变量（kimi 官方约定），否则 %USERPROFILE%\.kimi-code
            var home = Environment.GetEnvironmentVariable("KIMI_CODE_HOME");
            if (string.IsNullOrEmpty(home)) home = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".kimi-code");
            return Path.Combine(home, "config.toml");
        }

        private void Log(string s)
        {
            txtLog.AppendText("[" + DateTime.Now.ToString("HH:mm:ss") + "] " + s + Environment.NewLine);
        }

        private void OnFetch(object sender, EventArgs e)
        {
            var baseUrl = txtBase.Text.Trim();
            var key = txtKey.Text.Trim();
            if (baseUrl.Length == 0 || key.Length == 0) { MessageBox.Show("请先填写 Base URL 与 API Key"); return; }
            btnFetch.Enabled = false;
            lblStatus.Text = "正在获取…";
            var self = this;
            Task.Factory.StartNew(delegate
            {
                try { return Api.FetchModels(baseUrl, key); }
                catch (Exception ex) { throw ex; }
            }).ContinueWith(delegate(Task<List<string>> t)
            {
                self.BeginInvoke(new Action(delegate
                {
                    btnFetch.Enabled = true;
                    if (t.IsFaulted)
                    {
                        var ex = t.Exception.Flatten().InnerException;
                        lblStatus.Text = "获取失败";
                        Log("获取模型失败: " + ex.Message);
                        MessageBox.Show("获取模型失败:\r\n" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                    var ids = t.Result;
                    allIds = ids;
                    checkedIds = new HashSet<string>(ids);
                    cboDefault.Items.Clear();
                    foreach (var id in ids) cboDefault.Items.Add(id);
                    if (ids.Count > 0) cboDefault.SelectedIndex = 0;
                    RenderModels();
                    lblStatus.Text = "获取到 " + ids.Count + " 个模型";
                    Log("获取到 " + ids.Count + " 个模型（已全选）。可用搜索框过滤、全选/全不选，选好默认模型后点“写入 config.toml”。");
                }));
            });
        }

        private void RenderModels()
        {
            var kw = (txtSearch.Text == null ? "" : txtSearch.Text.Trim()).ToLower();
            clbModels.BeginUpdate();
            clbModels.Items.Clear();
            foreach (var id in allIds)
            {
                if (kw.Length > 0 && id.ToLower().IndexOf(kw) < 0) continue;
                clbModels.Items.Add(id, checkedIds.Contains(id));
            }
            clbModels.EndUpdate();
        }

        private void SetAllVisible(bool on)
        {
            foreach (var id in clbModels.Items)
            {
                var s = id as string;
                if (s == null) continue;
                if (on) checkedIds.Add(s); else checkedIds.Remove(s);
            }
            RenderModels();
            Log(on ? "已全选（当前过滤结果）" : "已全部取消勾选（当前过滤结果）");
        }

        private void OnApply(object sender, EventArgs e)
        {
            var id = txtProvider.Text.Trim();
            if (id.Length == 0 || !Regex.IsMatch(id, "^[A-Za-z0-9_-]+$"))
            {
                MessageBox.Show("供应商 ID 只能包含字母、数字、- 和 _");
                return;
            }
            var chosen = new List<string>();
            foreach (var mid in allIds) if (checkedIds.Contains(mid)) chosen.Add(mid);
            if (chosen.Count == 0) { MessageBox.Show("请至少勾选一个模型"); return; }
            var def = cboDefault.SelectedItem as string;
            if (def == null || !chosen.Contains(def))
            {
                MessageBox.Show("请选择默认模型（必须在已勾选列表中）");
                return;
            }
            var ctx = (int)numContext.Value;
            var outp = (int)numOutput.Value;
            var baseUrl = txtBase.Text.Trim();
            var apiKey = txtKey.Text.Trim();
            var path = ConfigPath();

            try
            {
                List<TomlBlock> blocks;
                if (File.Exists(path)) blocks = TomlConfig.Parse(path);
                else
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    blocks = new List<TomlBlock> { new TomlBlock { Name = "" } };
                }
                var used = new List<string>();
                foreach (var b in blocks)
                    if (b.Name.StartsWith("models.")) used.Add(b.Name.Substring("models.".Length));
                var models = new List<ModelInfo>();
                foreach (var mid in chosen) models.Add(new ModelInfo { Id = mid, Alias = Api.MakeAlias(mid, used) });
                var defModel = models.First(m => m.Id == def);

                TomlConfig.MergeProvider(blocks, id, baseUrl, apiKey, models, ctx, outp, defModel.Alias, defModel.Id);
                TomlConfig.Save(blocks, path);
                Log("已写入(全局，对所有目录生效): " + path);
                Log("供应商: " + id + "  模型数: " + models.Count + "  默认: " + defModel.Alias + " (" + defModel.Id + ")");
                Log("新开终端运行 kimi 即可使用；会话内 /model 可切换模型。");
                MessageBox.Show("写入成功!\r\n默认模型: " + defModel.Alias + " (" + defModel.Id + ")\r\n\r\n新开终端运行 kimi 即可使用。", "完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                Log("写入失败: " + ex.Message);
                MessageBox.Show("写入失败:\r\n" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }

    internal static class Program
    {
        private static int Main(string[] args)
        {
            try { System.Net.ServicePointManager.SecurityProtocol |= (System.Net.SecurityProtocolType)3072 | (System.Net.SecurityProtocolType)12288; } catch { } // 启用 TLS 1.2/1.3
            try { Console.OutputEncoding = Encoding.UTF8; } catch { }
            if (args.Length > 0 && args[0] == "--selftest")
            {
                return SelfTest(args.Length > 1 ? args[1] : null);
            }
            if (args.Length > 0 && args[0] == "--fetchtest")
            {
                var ids = Api.FetchModels(args[1], args[2]);
                Console.WriteLine("模型数: " + ids.Count);
                foreach (var i in ids.Take(5)) Console.WriteLine("  " + i);
                return 0;
            }
            if (args.Length > 0 && args[0] == "--apply")
            {
                // --apply <providerId> <base_url> <api_key> <逗号分隔模型ID列表> [默认模型ID] [全局路径覆盖]
                var ids = args[4].Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToList();
                var defId = args.Length > 5 && args[5].Length > 0 ? args[5] : ids[0];
                var path = args.Length > 6 && args[6].Length > 0 ? args[6] : DefaultConfigPath();
                var used = new List<string>();
                List<TomlBlock> blocks;
                if (File.Exists(path)) blocks = TomlConfig.Parse(path);
                else { Directory.CreateDirectory(Path.GetDirectoryName(path)); blocks = new List<TomlBlock> { new TomlBlock { Name = "" } }; }
                foreach (var b in blocks) if (b.Name.StartsWith("models.")) used.Add(b.Name.Substring("models.".Length));
                var models = ids.Select(mid => new ModelInfo { Id = mid, Alias = Api.MakeAlias(mid, used) }).ToList();
                var defModel = models.First(m => m.Id == defId);
                TomlConfig.MergeProvider(blocks, args[1], args[2], args[3], models, 200000, 65536, defModel.Alias, defModel.Id);
                TomlConfig.Save(blocks, path);
                Console.WriteLine("OK 已写入 " + path + " 默认别名=" + defModel.Alias);
                return 0;
            }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
            return 0;
        }

        private static string DefaultConfigPath()
        {
            var home = Environment.GetEnvironmentVariable("KIMI_CODE_HOME");
            if (string.IsNullOrEmpty(home)) home = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".kimi-code");
            return Path.Combine(home, "config.toml");
        }

        private static int SelfTest(string configPath)
        {
            try
            {
                var real = configPath;
                if (real == null) real = DefaultConfigPath();
                Console.WriteLine("源配置: " + real);
                var tmp = Path.Combine(Path.GetTempPath(), "kimi-config-selftest.toml");
                File.Copy(real, tmp, true);

                var blocks = TomlConfig.Parse(tmp);
                Console.WriteLine("解析到块数: " + blocks.Count);
                foreach (var b in blocks.Take(8)) Console.WriteLine("  [" + (b.IsArrayTable ? "[" + b.Name + "]]" : b.Name + "]") + " keys=" + b.Items.Count);

                var models = new List<ModelInfo> {
                    new ModelInfo { Id = "zz/a.b", Alias = "zz-a-b" },
                    new ModelInfo { Id = "zz/c-d", Alias = "zz-c-d" }
                };
                TomlConfig.MergeProvider(blocks, "zzselftest", "https://example.invalid/v1", "sk-test", models, 200000, 65536, "zz-c-d", "zz/c-d");
                TomlConfig.Save(blocks, tmp);

                var again = TomlConfig.Parse(tmp);
                var prov = TomlConfig.Find(again, "providers.zzselftest");
                var root = TomlConfig.Find(again, "");
                var aliasCount = again.Count(b => b.Name.StartsWith("models.") && TomlConfig.GetValue(b, "provider") == "zzselftest");
                var dm = TomlConfig.GetValue(root, "default_model");
                Console.WriteLine("合并后: providers.zzselftest type=" + TomlConfig.GetValue(prov, "type")
                    + " | root default_provider=" + TomlConfig.GetValue(root, "default_provider")
                    + " | root default_model=" + dm
                    + " | zz 别名数=" + aliasCount);
                if (prov == null || aliasCount != 2 || dm != "zz-c-d") { Console.WriteLine("SELFTEST FAIL"); return 1; }
                // 再跑一次覆盖合并（幂等性检查）
                var third = TomlConfig.Parse(tmp);
                TomlConfig.MergeProvider(third, "zzselftest", "https://example.invalid/v1", "sk-test", models, 200000, 65536, "zz-c-d", "zz/c-d");
                TomlConfig.Save(third, tmp);
                var dupCount = TomlConfig.Parse(tmp).Count(b => b.Name == "providers.zzselftest");
                Console.WriteLine("重复合并后 providers.zzselftest 块数: " + dupCount + (dupCount == 1 ? " (幂等 OK)" : " (FAIL)"));
                File.Delete(tmp);
                Console.WriteLine("SELFTEST OK");
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine("SELFTEST ERROR: " + ex.Message);
                return 1;
            }
        }
    }
}
