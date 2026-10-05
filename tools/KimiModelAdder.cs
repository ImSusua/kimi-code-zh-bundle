// KimiModelAdder - Kimi Code 配置管理器（GUI，小白友好版）
// 侧边栏导航 + 卡片式布局 + 分步引导 + 帮助页，覆盖 CLI 可配置的全部设置项
// 编译（系统自带 csc，C# 5 语法）：
//   csc -target:winexe -out:KimiModelAdder.exe -r:System.dll -r:System.Core.dll
//       -r:System.Drawing.dll -r:System.Windows.Forms.dll -r:System.Web.Extensions.dll
//       -r:System.Net.Http.dll KimiModelAdder.cs
// 命令行：--selftest [config路径] | --fetchtest <base_url> <api_key>
//         --apply <providerId> <base_url> <api_key> <模型ID,逗号分隔> [默认模型ID] [配置路径]
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
        public string Value;
        public Kv(string key, string value) { Key = key; Value = value; }
    }

    internal class TomlBlock
    {
        public string Name = "";
        public bool IsArrayTable;
        public List<Kv> Items = new List<Kv>();
        public List<string> RawLines = new List<string>();
    }

    internal static class TomlConfig
    {
        private static readonly Regex HeaderRe = new Regex("^\\s*\\[\\[(.+?)\\]\\]\\s*$", RegexOptions.Compiled);
        private static readonly Regex SectionRe = new Regex("^\\s*\\[(.+?)\\]\\s*$", RegexOptions.Compiled);
        private static readonly Regex KeyValRe = new Regex("^\\s*([A-Za-z0-9_\\-\\.]+)\\s*=\\s*(.+?)\\s*$", RegexOptions.Compiled);

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
                var mSec = mArr.Success ? Match.Empty : SectionRe.Match(line);
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
                else cur.RawLines.Add(line);
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

        public static List<string> ParseStringArray(string raw)
        {
            var list = new List<string>();
            if (raw == null) return list;
            var t = raw.Trim();
            if (!t.StartsWith("[")) return list;
            foreach (Match m in Regex.Matches(t, "\"((?:[^\"\\\\]|\\\\.)*)\"")) list.Add(UnescapeBasic(m.Groups[1].Value));
            return list;
        }

        public static string JoinStringArray(List<string> items)
        {
            var sb = new StringBuilder("[");
            for (int i = 0; i < items.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(Q(items[i]));
            }
            sb.Append("]");
            return sb.ToString();
        }

        private static string UnescapeBasic(string s)
        {
            return s.Replace("\\\\", "\u0001").Replace("\\\"", "\"").Replace("\u0001", "\\");
        }

        // 关键：根 default_model 必须是 [models.<别名>] 的别名键，不能是原始模型 ID
        public static void MergeProvider(List<TomlBlock> blocks, string id, string baseUrl, string apiKey,
            List<ModelInfo> models, int maxContext, int maxOutput, string defaultAlias, string defaultModelId)
        {
            var root = Find(blocks, "");
            root.Items.RemoveAll(delegate(Kv kv) { return kv.Key == "default_provider" || kv.Key == "default_model"; });
            root.Items.Insert(0, new Kv("default_model", Q(defaultAlias)));
            root.Items.Insert(0, new Kv("default_provider", Q(id)));

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

        public static void SetKey(TomlBlock b, string key, string value)
        {
            for (int i = 0; i < b.Items.Count; i++)
            {
                if (b.Items[i].Key == key) { b.Items[i] = new Kv(key, value); return; }
            }
            b.Items.Add(new Kv(key, value));
        }

        public static void RemoveKey(TomlBlock b, string key)
        {
            b.Items.RemoveAll(delegate(Kv kv) { return kv.Key == key; });
        }

        public static void UpsertSection(List<TomlBlock> blocks, string name, List<Kv> items)
        {
            var old = Find(blocks, name);
            if (old != null) blocks.Remove(old);
            var b = new TomlBlock { Name = name };
            foreach (var kv in items) if (kv.Value != null) b.Items.Add(kv);
            blocks.Add(b);
        }

        public static void RemoveSection(List<TomlBlock> blocks, string name)
        {
            var old = Find(blocks, name);
            if (old != null) blocks.Remove(old);
        }

        public static void Save(List<TomlBlock> blocks, string path)
        {
            var sb = new StringBuilder();
            foreach (var b in blocks)
            {
                if (b.Name == "")
                {
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
                    foreach (var l in b.RawLines)
                    {
                        var t = l.Trim();
                        if (t.Length == 0 || t.StartsWith("#")) sb.AppendLine(l);
                    }
                    sb.AppendLine();
                }
            }
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        }
    }

    internal class ModelInfo
    {
        public string Id;
        public string Alias;
    }

    internal static class Api
    {
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
        private static readonly Color Accent = Color.FromArgb(79, 110, 247);
        private static readonly Color AccentLight = Color.FromArgb(238, 242, 255);
        private static readonly Color PageBg = Color.FromArgb(245, 246, 250);
        private static readonly Color TextMain = Color.FromArgb(31, 41, 55);
        private static readonly Color TextSub = Color.FromArgb(107, 114, 128);
        private static readonly Color OkGreen = Color.FromArgb(22, 163, 74);
        private static readonly Color ErrRed = Color.FromArgb(220, 38, 38);

        private readonly string cfgPath;
        private readonly string tuiPath;
        private readonly List<Panel> pages = new List<Panel>();
        private readonly List<Button> navButtons = new List<Button>();

        // 供应商与模型
        private TextBox txtProvider, txtBase, txtKey, txtSearch;
        private CheckBox chkShow;
        private Button btnFetch, btnApply, btnOpenCfg, btnAll, btnNone, btnDelProvider, btnReload, btnTest;
        private CheckedListBox clbModels;
        private ComboBox cboDefault;
        private NumericUpDown numContext, numOutput;
        private Label lblStatus, lblNavTitle;
        private ListBox lstProviders;
        // 思考与子模型
        private CheckBox chkThinking, chkSecForce;
        private ComboBox cboEffort;
        private TextBox txtKeep, txtSecModel;
        // 权限
        private RadioButton rbManual, rbYolo, rbAuto;
        // 界面
        private ComboBox cboTheme, cboTuiMode, cboMermaid, cboNotifCond;
        private TextBox txtEditor, txtStatusItems;
        private CheckBox chkLatex, chkPasteBurst, chkCacheHint, chkSurvey, chkNotif, chkAutoUpgrade;
        private Button btnSaveTui, btnSaveRaw;
        // 高级
        private TextBox txtFlags, txtRawSection, txtRawBody;
        private CheckedListBox clbThinkAliases;
        private CheckBox chkTEffOff, chkTEffLow, chkTEffMedium, chkTEffHigh, chkTAdaptive;
        private TextBox txtOffEffort;
        private TextBox txtLog;

        private List<string> allIds = new List<string>();
        private HashSet<string> checkedIds = new HashSet<string>();

        public MainForm()
        {
            var home = Environment.GetEnvironmentVariable("KIMI_CODE_HOME");
            if (string.IsNullOrEmpty(home)) home = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".kimi-code");
            cfgPath = Path.Combine(home, "config.toml");
            tuiPath = Path.Combine(home, "tui.toml");

            Text = "Kimi Code 配置管理器";
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false; StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(940, 760);
            Font = new Font("Microsoft YaHei UI", 9F);
            BackColor = PageBg;

            // ================= 内容区（先加入，侧边栏后加，保证 Left 停靠生效） =================
            var contentHost = new Panel { Dock = DockStyle.Fill, BackColor = PageBg, Padding = new Padding(12, 12, 12, 6) };

            // 页面（先加入，日志最后加入：最后加入的先完成停靠，日志占据底部，页面填充其余）
            for (int i = 0; i < 7; i++)
            {
                var p = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = PageBg, Visible = false };
                pages.Add(p);
                contentHost.Controls.Add(p);
            }
            var logHost = new Panel { Dock = DockStyle.Bottom, Size = new Size(760, 92), BackColor = PageBg, Padding = new Padding(0, 4, 0, 0) };
            txtLog = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BackColor = Color.White, BorderStyle = BorderStyle.FixedSingle, Font = new Font("Microsoft YaHei UI", 8.5F) };
            logHost.Controls.Add(txtLog);
            contentHost.Controls.Add(logHost);
            Controls.Add(contentHost);

            // ================= 侧边栏（最后加入 + BringToFront） =================
            var sidebar = new Panel { Dock = DockStyle.Left, Width = 208, BackColor = Color.White };
            var brand1 = new Label { Text = "Kimi Code", Font = new Font("Microsoft YaHei UI", 13F, FontStyle.Bold), ForeColor = TextMain, Location = new Point(20, 18), AutoSize = true };
            var brand2 = new Label { Text = "配置管理器 · 全中文", Font = new Font("Microsoft YaHei UI", 8.5F), ForeColor = TextSub, Location = new Point(20, 48), AutoSize = true };
            sidebar.Controls.Add(brand1);
            sidebar.Controls.Add(brand2);
            var navTitles = new[] { "  供应商与模型", "  思考与子模型", "  思考强度启用", "  权限模式", "  界面偏好", "  高级", "  使用帮助" };
            for (int i = 0; i < navTitles.Length; i++)
            {
                var idx = i;
                var b = new Button
                {
                    Text = navTitles[i],
                    TextAlign = ContentAlignment.MiddleLeft,
                    Padding = new Padding(16, 0, 0, 0),
                    FlatStyle = FlatStyle.Flat,
                    Size = new Size(208, 46),
                    Location = new Point(0, 80 + i * 48),
                    BackColor = Color.White,
                    ForeColor = TextMain,
                    Font = new Font("Microsoft YaHei UI", 9.5F),
                    Cursor = Cursors.Hand
                };
                b.FlatAppearance.BorderSize = 0;
                b.FlatAppearance.MouseOverBackColor = AccentLight;
                b.Click += delegate { ShowPage(idx); };
                navButtons.Add(b);
                sidebar.Controls.Add(b);
            }
            var lblSide = new Label { Text = "配置对所有目录生效", Font = new Font("Microsoft YaHei UI", 8F), ForeColor = TextSub, Location = new Point(20, 690), AutoSize = true };
            sidebar.Controls.Add(lblSide);
            Controls.Add(sidebar);
            contentHost.BringToFront();

            // ================= 页 1：供应商与模型 =================
            var p1 = pages[0];
            PageHeader(p1, "供应商与模型", "三步接入你的中转/供应商：填写信息 → 获取模型 → 勾选写入");
            var c1 = Card(p1, 16, 80, 676, 146, "① 填写供应商信息", "这些信息由你的模型服务商（中转站）提供");
            txtProvider = new TextBox { Location = new Point(120, 54), Width = 140, Text = "susu" };
            txtBase = new TextBox { Location = new Point(120, 86), Width = 430 };
            txtKey = new TextBox { Location = new Point(120, 118), Width = 430, UseSystemPasswordChar = true };
            chkShow = new CheckBox { Text = "显示", Location = new Point(560, 116), AutoSize = true, ForeColor = TextSub };
            chkShow.CheckedChanged += delegate { txtKey.UseSystemPasswordChar = !chkShow.Checked; };
            CardLabel(c1, "供应商 ID", 18, 58);
            CardLabel(c1, "Base URL", 18, 90);
            CardLabel(c1, "API Key", 18, 122);
            c1.Controls.AddRange(new Control[] { txtProvider, txtBase, txtKey, chkShow });

            var c2 = Card(p1, 16, 234, 676, 246, "② 获取并勾选模型", "点击按钮拉取 /v1/models 列表；可用搜索框过滤，支持全选/全不选");
            btnFetch = new Button { Text = "获取模型列表", Location = new Point(18, 54), Size = new Size(130, 30) };
            StylePrimary(btnFetch);
            btnTest = new Button { Text = "测试连接", Location = new Point(156, 54), Size = new Size(96, 30) };
            StyleGhost(btnTest);
            lblStatus = new Label { Text = "", Location = new Point(262, 62), AutoSize = true, ForeColor = TextSub };
            var lblModels = new Label { Text = "搜索:", Location = new Point(18, 96), AutoSize = true, ForeColor = TextSub };
            txtSearch = new TextBox { Location = new Point(86, 92), Width = 230 };
            btnAll = new Button { Text = "全选", Location = new Point(324, 91), Size = new Size(64, 25) };
            btnNone = new Button { Text = "全不选", Location = new Point(392, 91), Size = new Size(72, 25) };
            StyleGhost(btnAll); StyleGhost(btnNone);
            clbModels = new CheckedListBox { Location = new Point(18, 120), Size = new Size(440, 108), CheckOnClick = true, BorderStyle = BorderStyle.FixedSingle };
            clbModels.ItemCheck += delegate(object s, ItemCheckEventArgs e)
            {
                try
                {
                    var mid = clbModels.Items[e.Index] as string;
                    if (mid == null) return;
                    if (e.NewValue == CheckState.Checked) checkedIds.Add(mid); else checkedIds.Remove(mid);
                }
                catch { }
            };
            var lblDefault = new Label { Text = "默认模型", Location = new Point(474, 90), AutoSize = true, ForeColor = TextMain };
            cboDefault = new ComboBox { Location = new Point(474, 110), Width = 180, DropDownStyle = ComboBoxStyle.DropDownList };
            var lblCtx = new Label { Text = "上下文长度", Location = new Point(474, 144), AutoSize = true, ForeColor = TextMain };
            numContext = new NumericUpDown { Location = new Point(474, 164), Width = 130, Maximum = 2000000, Value = 200000, Increment = 1000 };
            var lblOut = new Label { Text = "最大输出 (0=不限)", Location = new Point(474, 196), AutoSize = true, ForeColor = TextMain };
            numOutput = new NumericUpDown { Location = new Point(474, 216), Width = 130, Maximum = 2000000, Value = 65536, Increment = 1024 };
            c2.Controls.AddRange(new Control[] { btnFetch, btnTest, lblStatus, lblModels, txtSearch, btnAll, btnNone, clbModels, lblDefault, cboDefault, lblCtx, numContext, lblOut, numOutput });

            var c3 = Card(p1, 16, 488, 676, 124, "③ 确认并写入全局配置", "写入后所有工作目录都能用；重复写入同一供应商 ID 会整体更新");
            btnApply = new Button { Text = "写入全局配置（✔ 全部目录生效）", Location = new Point(18, 52), Size = new Size(270, 36) };
            StylePrimary(btnApply);
            btnReload = new Button { Text = "重新读取配置", Location = new Point(300, 58), Size = new Size(130, 30) };
            btnOpenCfg = new Button { Text = "打开配置目录", Location = new Point(436, 58), Size = new Size(130, 30) };
            StyleGhost(btnReload); StyleGhost(btnOpenCfg);
            var lblProvList = new Label { Text = "已配置供应商:", Location = new Point(18, 102), AutoSize = true, ForeColor = TextMain };
            lstProviders = new ListBox { Location = new Point(130, 96), Size = new Size(230, 26), BorderStyle = BorderStyle.FixedSingle };
            btnDelProvider = new Button { Text = "删除选中的供应商", Location = new Point(370, 98), Size = new Size(150, 26) };
            StyleGhost(btnDelProvider);
            c3.Controls.AddRange(new Control[] { btnApply, btnReload, btnOpenCfg, lblProvList, lstProviders, btnDelProvider });
            var lblG = new Label { Text = "配置文件位置见底部日志；删除供应商会同时移除它的全部模型别名。", Location = new Point(18, 618), AutoSize = true, ForeColor = TextSub };
            p1.Controls.Add(lblG);

            // ================= 页 2：思考与子模型 =================
            var p2 = pages[1];
            PageHeader(p2, "思考与子模型", "控制模型的思考强度，以及子智能体使用哪个模型");
            var c4 = Card(p2, 16, 80, 676, 162, "思考模式 [thinking]", "让模型在回答前进行更深入的推理");
            chkThinking = new CheckBox { Text = "启用思考", Location = new Point(20, 54), AutoSize = true };
            var lblEffort = new Label { Text = "思考强度 effort:", Location = new Point(20, 88), AutoSize = true, ForeColor = TextMain };
            cboEffort = new ComboBox { Location = new Point(150, 84), Width = 140, DropDownStyle = ComboBoxStyle.DropDown };
            cboEffort.Items.AddRange(new object[] { "off", "low", "medium", "high" });
            var lblEffortN = new Label { Text = "常用 off / low / medium / high，留空 = 不设置", Location = new Point(300, 88), AutoSize = true, ForeColor = TextSub };
            var lblKeep = new Label { Text = "keep 预算:", Location = new Point(20, 122), AutoSize = true, ForeColor = TextMain };
            txtKeep = new TextBox { Location = new Point(150, 118), Width = 140 };
            var lblKeepN = new Label { Text = "压缩上下文时保留的思考 token 预算，如 20000", Location = new Point(300, 122), AutoSize = true, ForeColor = TextSub };
            c4.Controls.AddRange(new Control[] { chkThinking, lblEffort, cboEffort, lblEffortN, lblKeep, txtKeep, lblKeepN });
            var c5 = Card(p2, 16, 252, 676, 142, "子智能体次级模型 [secondary_model]", "后台子智能体（coder / explore 等）使用的模型");
            txtSecModel = new TextBox { Location = new Point(20, 52), Width = 300 };
            var lblSec2 = new Label { Text = "填 [models.*] 别名（如 agnes-2-5-flash）；留空 = 跟随主模型", Location = new Point(20, 84), AutoSize = true, ForeColor = TextSub };
            chkSecForce = new CheckBox { Text = "force：强制所有子智能体使用，忽略会话级覆盖", Location = new Point(20, 108), AutoSize = true };
            c5.Controls.AddRange(new Control[] { txtSecModel, lblSec2, chkSecForce });
            var btnSaveThink = new Button { Text = "写入以上设置", Location = new Point(16, 406), Size = new Size(180, 36) };
            StylePrimary(btnSaveThink);
            var lbl23 = new Label { Text = "留空的键不会写入；修改后新会话生效（当前会话用 /reload）。", Location = new Point(210, 414), AutoSize = true, ForeColor = TextSub };
            p2.Controls.AddRange(new Control[] { btnSaveThink, lbl23 });

            // ================= 页 3：模型思考支持 =================
            var pT = pages[2];
            PageHeader(pT, "模型思考支持", "中转站的模型实际支持思考、但别名未声明 supportEfforts 时，kimi 里无法设置思考强度 —— 勾选并写入即可");
            var cT = Card(pT, 16, 80, 676, 268, "模型别名（勾选要启用的）", "来自 config.toml 的 [models.*] 段；已声明的会标注当前档位");
            clbThinkAliases = new CheckedListBox { Location = new Point(20, 34), Size = new Size(636, 216), CheckOnClick = true, BorderStyle = BorderStyle.FixedSingle, Font = new Font("Consolas", 9F) };
            cT.Controls.Add(clbThinkAliases);
            var cT2 = Card(pT, 16, 360, 676, 170, "要声明的思考档位", "写入 supportEfforts（同步写入供应商模型条目的 support_efforts）；之后在 kimi 会话内即可切换思考强度");
            chkTEffOff = new CheckBox { Text = "off", Location = new Point(20, 56), AutoSize = true };
            chkTEffLow = new CheckBox { Text = "low", Location = new Point(90, 56), AutoSize = true };
            chkTEffMedium = new CheckBox { Text = "medium", Location = new Point(160, 56), AutoSize = true };
            chkTEffHigh = new CheckBox { Text = "high", Location = new Point(250, 56), AutoSize = true };
            chkTEffOff.Checked = chkTEffLow.Checked = chkTEffMedium.Checked = chkTEffHigh.Checked = true;
            chkTAdaptive = new CheckBox { Text = "adaptiveThinking（模型自适应思考，实验性）", Location = new Point(20, 86), AutoSize = true };
            var lblOff = new Label { Text = "offEffort:", Location = new Point(20, 118), AutoSize = true, ForeColor = TextMain };
            txtOffEffort = new TextBox { Location = new Point(120, 114), Width = 120, Text = "none" };
            var lblOffN = new Label { Text = "关闭思考时发送的档位（模型\u201c默认思考\u201d时必须声明，如 none）；留空 = 不写", Location = new Point(250, 118), AutoSize = true, ForeColor = TextSub };
            cT2.Controls.AddRange(new Control[] { chkTEffOff, chkTEffLow, chkTEffMedium, chkTEffHigh, chkTAdaptive, lblOff, txtOffEffort, lblOffN });
            var btnThinkApply = new Button { Text = "写入思考支持", Location = new Point(16, 514), Size = new Size(180, 36) };
            StylePrimary(btnThinkApply);
            var btnThinkRemove = new Button { Text = "移除声明（恢复默认）", Location = new Point(208, 518), Size = new Size(190, 30) };
            StyleGhost(btnThinkRemove);
            pT.Controls.AddRange(new Control[] { btnThinkApply, btnThinkRemove });

            // ================= 页 4：权限模式（启动器） =================
            var p3 = pages[3];
            PageHeader(p3, "权限模式（启动方式）", "kimi 的权限模式是会话级设置（v2 没有 [permission] mode 配置键）—— 用固定参数的启动器来固定它");
            var pc = Card(p3, 16, 80, 676, 330, "创建固定权限模式的启动器", "在 bin 目录生成 kimi-auto / kimi-yolo 命令，并在桌面创建快捷方式");
            rbAuto = new RadioButton { Text = "无人值守（--auto，从不询问）—— 挂机跑长任务", Location = new Point(20, 56), AutoSize = true, Checked = true };
            var rbAutoN = new Label { Text = "一切自动运行、不再打断你；请只在可信项目里使用", Location = new Point(42, 80), AutoSize = true, ForeColor = TextSub };
            rbYolo = new RadioButton { Text = "按需询问（--yolo，常规自动、高风险询问）—— 推荐日常", Location = new Point(20, 110), AutoSize = true };
            var rbYoloN = new Label { Text = "常规编辑与命令自动执行，高风险操作仍会询问", Location = new Point(42, 134), AutoSize = true, ForeColor = TextSub };
            rbManual = new RadioButton { Text = "总是询问（默认启动即是，无需启动器）—— 重要项目", Location = new Point(20, 164), AutoSize = true };
            var lblPC = new Label { Text = "已有会话会记住自己被设置过的模式；会话内也可用 /permissions 随时改。", Location = new Point(20, 194), AutoSize = true, ForeColor = TextSub };
            var lblPC2 = new Label { Text = "此前写入的 [permission] mode 是无效配置（v2 会忽略），可用下方按钮清理。", Location = new Point(20, 218), AutoSize = true, ForeColor = TextSub };
            var btnMakeLauncher = new Button { Text = "创建启动器 + 桌面快捷方式", Location = new Point(20, 248), Size = new Size(280, 40) };
            pc.Controls.AddRange(new Control[] { rbAuto, rbAutoN, rbYolo, rbYoloN, rbManual, lblPC, lblPC2 });
            pc.Controls.Add(btnMakeLauncher);
            var btnCleanPerm = new Button { Text = "清理无效的 [permission] 段", Location = new Point(16, 424), Size = new Size(240, 32) };
            StyleGhost(btnCleanPerm);
            p3.Controls.Add(btnCleanPerm);
            var lblPn = new Label { Text = "启动器写入 " + Path.Combine(Path.GetDirectoryName(cfgPath), "bin") + "；桌面快捷方式按所选模式命名。", Location = new Point(16, 466), AutoSize = true, ForeColor = TextSub };
            p3.Controls.Add(lblPn);


            // ================= 页 4：界面偏好 =================
            var p4 = pages[4];
            PageHeader(p4, "界面偏好 (tui.toml)", "调整 kimi 终端界面的外观与行为，重启 kimi 后生效");
            var c6 = Card(p4, 16, 80, 676, 474, "外观与行为", "写入 tui.toml（完整路径见帮助页与日志）");
            int y4 = 56;
            Action<string, Control, string> row = delegate(string name, Control a, string desc)
            {
                var l = new Label { Text = name, Location = new Point(20, y4 + 4), AutoSize = true, ForeColor = TextMain };
                a.Location = new Point(180, y4);
                c6.Controls.Add(l); c6.Controls.Add(a);
                if (desc != null && desc.Length > 0)
                {
                    var d = new Label { Text = desc, Location = new Point(370, y4 + 4), AutoSize = true, ForeColor = TextSub };
                    c6.Controls.Add(d);
                }
                y4 += 34;
            };
            cboTheme = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown, Width = 140 };
            cboTheme.Items.AddRange(new object[] { "auto", "dark", "light" });
            row("主题 theme:", cboTheme, "auto 跟随终端 / dark / light");
            cboTuiMode = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 140 };
            cboTuiMode.Items.AddRange(new object[] { "regular", "fullscreen" });
            row("布局 tui_mode:", cboTuiMode, "fullscreen 为实验性全屏模式");
            cboMermaid = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 140 };
            cboMermaid.Items.AddRange(new object[] { "final", "off" });
            row("Mermaid 图表:", cboMermaid, "final=渲染成图，off=保留源码");
            chkLatex = new CheckBox { Text = "渲染 LaTeX 公式", AutoSize = true, ForeColor = TextMain };
            row("LaTeX:", chkLatex, null);
            chkPasteBurst = new CheckBox { Text = "禁用非括号化粘贴连发", AutoSize = true, ForeColor = TextMain };
            row("粘贴:", chkPasteBurst, "粘贴多行异常时再开启");
            chkCacheHint = new CheckBox { Text = "缓存过期提醒对话框", AutoSize = true, ForeColor = TextMain };
            row("缓存提醒:", chkCacheHint, "关闭后不再弹出续费提示");
            chkSurvey = new CheckBox { Text = "隐藏会话评分问卷", AutoSize = true, ForeColor = TextMain };
            row("评分问卷:", chkSurvey, null);
            txtEditor = new TextBox { Width = 200 };
            row("外部编辑器:", txtEditor, "留空用 $VISUAL/$EDITOR，Ctrl-G 调用");
            chkNotif = new CheckBox { Text = "桌面通知", AutoSize = true, ForeColor = TextMain };
            row("通知:", chkNotif, null);
            cboNotifCond = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 110 };
            cboNotifCond.Items.AddRange(new object[] { "unfocused", "always" });
            cboNotifCond.Location = new Point(chkNotif.Right + 8, y4 - 34);
            c6.Controls.Add(cboNotifCond);
            chkAutoUpgrade = new CheckBox { Text = "后台自动安装更新", AutoSize = true, ForeColor = TextMain };
            row("自动更新:", chkAutoUpgrade, null);
            var lblSt = new Label { Text = "底栏槽位:", Location = new Point(20, y4 + 4), AutoSize = true, ForeColor = TextMain };
            txtStatusItems = new TextBox { Location = new Point(180, y4), Width = 440 };
            c6.Controls.Add(lblSt); c6.Controls.Add(txtStatusItems);
            y4 += 40;
            btnSaveTui = new Button { Text = "写入 tui.toml", Location = new Point(20, y4), Size = new Size(180, 34) };
            StylePrimary(btnSaveTui);
            c6.Controls.Add(btnSaveTui);

            // ================= 页 5：高级 =================
            var p5 = pages[5];
            PageHeader(p5, "高级", "实验性功能与任意配置段的直接编辑，普通用户可以不用");
            var c7 = Card(p5, 16, 80, 676, 142, "实验性 flags [experimental]", "一行一个，来自官方公告或 /settings");
            txtFlags = new TextBox { Location = new Point(20, 32), Size = new Size(636, 80), Multiline = true, ScrollBars = ScrollBars.Vertical };
            c7.Controls.Add(txtFlags);
            var c8 = Card(p5, 16, 232, 676, 264, "直接编辑任意 TOML 段", "段名 + 每行 key = value（如 hooks、workspace 等高级段）");
            txtRawSection = new TextBox { Location = new Point(20, 54), Width = 280 };
            var btnDelSec = new Button { Text = "删除该段", Location = new Point(316, 52), Size = new Size(110, 26) };
            StyleGhost(btnDelSec);
            txtRawBody = new TextBox { Location = new Point(20, 86), Size = new Size(636, 120), Multiline = true, ScrollBars = ScrollBars.Vertical };
            btnSaveRaw = new Button { Text = "写入该段", Location = new Point(20, 216), Size = new Size(140, 30) };
            StylePrimary(btnSaveRaw);
            c8.Controls.AddRange(new Control[] { txtRawSection, btnDelSec, txtRawBody, btnSaveRaw });
            var lblT5 = new Label { Text = "写入前请确认段名正确；表数组 [[段名]] 不在此支持范围。", Location = new Point(18, 508), AutoSize = true, ForeColor = TextSub };
            p5.Controls.Add(lblT5);

            // ================= 页 6：帮助 =================
            var p6 = pages[6];
            PageHeader(p6, "使用帮助", "常见问题与排错对照");
            var help = new TextBox
            {
                Location = new Point(16, 80), Size = new Size(676, 470),
                Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
                BackColor = Color.White, BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Microsoft YaHei UI", 9F)
            };
            help.Text = "【配置文件位置】\r\n"
                + "  " + cfgPath + "\r\n"
                + "  " + tuiPath + "\r\n"
                + "  （设置了 KIMI_CODE_HOME 环境变量时会用它的位置）\r\n\r\n"
                + "【如何启动 kimi】\r\n"
                + "  新开一个终端窗口 → cd 到你的项目目录 → 输入 kimi 回车\r\n"
                + "  第一次使用在 kimi 里输入 /login 登录，或 /provider 添加供应商\r\n\r\n"
                + "【常见报错对照】\r\n"
                + "  Model \"...\" is not configured in config.toml\r\n"
                + "      → config.toml 里 default_model 指向的别名不存在；用本工具重新写入即可\r\n"
                + "  401 Invalid token\r\n"
                + "      → API Key 失效，去服务商后台重新生成，回到第①步重新写入\r\n"
                + "  403 预扣费失败 / 余额不足\r\n"
                + "      → 中转账户余额不够，充值即可，配置无需改动\r\n"
                + "  400 max_tokens 不能超过 65536\r\n"
                + "      → 把\"最大输出\"调到 65536 或更小\r\n"
                + "  Git Bash not found\r\n"
                + "      → 安装 Git for Windows，或重开终端让 KIMI_SHELL_PATH 生效\r\n"
                + "  无法连接 / Connection error\r\n"
                + "      → Base URL 写错、服务商宕机或被墙，可在浏览器打开服务商面板确认\r\n\r\n"
                + "【小提示】\r\n"
                + "  · 本工具写的是全局配置，对所有项目目录生效\r\n"
                + "  · 重复写入同一供应商 ID = 整体更新，不会产生重复配置\r\n"
                + "  · 修改后新开的 kimi 会话生效；当前会话输入 /reload 可立即应用";
            p6.Controls.Add(help);

            // ---------- 事件 ----------
            btnFetch.Click += OnFetch;
            btnTest.Click += OnTest;
            btnApply.Click += OnApply;
            txtSearch.TextChanged += delegate { RenderModels(); };
            btnAll.Click += delegate { SetAllVisible(true); };
            btnNone.Click += delegate { SetAllVisible(false); };
            btnReload.Click += delegate { LoadConfigToUi(); Log("已重新读取配置。"); };
            btnDelProvider.Click += OnDeleteProvider;
            btnSaveThink.Click += delegate
            {
                try
                {
                    var blocks = OpenConfig();
                    var th = new List<Kv>();
                    if (chkThinking.Checked) th.Add(new Kv("enabled", "true"));
                    var eff = cboEffort.Text.Trim();
                    if (eff.Length > 0) th.Add(new Kv("effort", TomlConfig.Q(eff)));
                    var keep = txtKeep.Text.Trim();
                    if (keep.Length > 0) th.Add(new Kv("keep", keep));
                    if (th.Count > 0) TomlConfig.UpsertSection(blocks, "thinking", th); else TomlConfig.RemoveSection(blocks, "thinking");
                    var sec = txtSecModel.Text.Trim();
                    if (sec.Length > 0)
                    {
                        var sm = new List<Kv> { new Kv("model", TomlConfig.Q(sec)) };
                        if (chkSecForce.Checked) sm.Add(new Kv("force", "true"));
                        TomlConfig.UpsertSection(blocks, "secondary_model", sm);
                    }
                    else TomlConfig.RemoveSection(blocks, "secondary_model");
                    TomlConfig.Save(blocks, cfgPath);
                    Log("已写入 thinking/secondary_model ✓");
                }
                catch (Exception ex) { Log("写入失败: " + ex.Message); MessageBox.Show(ex.Message, "错误"); }
            };
            btnMakeLauncher.Click += delegate
            {
                try
                {
                    string mode = rbYolo.Checked ? "yolo" : "auto";
                    var binDir = Path.Combine(Path.GetDirectoryName(cfgPath), "bin");
                    Directory.CreateDirectory(binDir);
                    var cmdPath = Path.Combine(binDir, "kimi-" + mode + ".cmd");
                    var shim = Path.Combine(binDir, "kimi.cmd");
                    var body = "@echo off\r\n" + (File.Exists(shim)
                        ? "call \"%~dp0kimi.cmd\" --" + mode + " %*\r\n"
                        : "\"%USERPROFILE%\\.kimi-code\\bin\\kimi.cmd\" --" + mode + " %*\r\n") + "exit /b %ERRORLEVEL%\r\n";
                    File.WriteAllText(cmdPath, body, new UTF8Encoding(false));
                    dynamic link = System.Activator.CreateInstance(System.Type.GetTypeFromProgID("WScript.Shell"));
                    var name = "Kimi " + (mode == "auto" ? "无人值守" : "按需询问");
                    var lnk = link.CreateShortcut(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory) + "\\" + name + ".lnk");
                    lnk.TargetPath = cmdPath;
                    lnk.WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                    lnk.Description = "Kimi Code " + (mode == "auto" ? "无人值守（--auto）" : "按需询问（--yolo）");
                    lnk.Save();
                    Log("已创建启动器 " + cmdPath + " 与桌面快捷方式 ✓");
                    MessageBox.Show("已创建：\r\n  命令 kimi-" + mode + "（全局可用）\r\n  桌面快捷方式 " + name + "\r\n\r\n双击快捷方式或在任意目录运行 kimi-" + mode + " 即以该模式启动。", "完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex) { Log("创建失败: " + ex.Message); MessageBox.Show("创建失败:\r\n" + ex.Message, "错误"); }
            };
            btnCleanPerm.Click += delegate
            {
                try
                {
                    var blocks = OpenConfig();
                    int removed = 0;
                    var had = TomlConfig.Find(blocks, "permission");
                    if (had != null)
                    {
                        var keys = had.Items.Where(k => k.Key != "#raw").ToList();
                        if (keys.All(k => k.Key == "mode")) { TomlConfig.RemoveSection(blocks, "permission"); removed = 1; }
                        else TomlConfig.RemoveKey(had, "mode");
                    }
                    TomlConfig.Save(blocks, cfgPath);
                    Log(removed > 0 ? "已清理无效的 [permission] 段 ✓" : "没有需要清理的 [permission] mode 键 ✓");
                }
                catch (Exception ex) { Log("清理失败: " + ex.Message); MessageBox.Show(ex.Message, "错误"); }
            };
            btnSaveTui.Click += delegate
            {
                try
                {
                    List<TomlBlock> blocks;
                    if (File.Exists(tuiPath)) blocks = TomlConfig.Parse(tuiPath);
                    else blocks = new List<TomlBlock> { new TomlBlock { Name = "" } };
                    var root = TomlConfig.Find(blocks, "");
                    root.Items.RemoveAll(delegate(Kv kv)
                    {
                        return kv.Key == "theme" || kv.Key == "tui_mode" || kv.Key == "render_latex" || kv.Key == "disable_paste_burst"
                            || kv.Key == "cache_expiry_hint" || kv.Key == "disable_feedback_survey";
                    });
                    if (cboTheme.Text.Trim().Length > 0) root.Items.Add(new Kv("theme", TomlConfig.Q(cboTheme.Text.Trim())));
                    if (cboTuiMode.Text.Length > 0) root.Items.Add(new Kv("tui_mode", TomlConfig.Q(cboTuiMode.Text)));
                    root.Items.Add(new Kv("render_latex", chkLatex.Checked ? "true" : "false"));
                    root.Items.Add(new Kv("disable_paste_burst", chkPasteBurst.Checked ? "true" : "false"));
                    root.Items.Add(new Kv("cache_expiry_hint", chkCacheHint.Checked ? "true" : "false"));
                    root.Items.Add(new Kv("disable_feedback_survey", chkSurvey.Checked ? "true" : "false"));
                    var ed = txtEditor.Text.Trim();
                    TomlConfig.UpsertSection(blocks, "editor", ed.Length > 0
                        ? new List<Kv> { new Kv("command", TomlConfig.Q(ed)) }
                        : new List<Kv>());
                    TomlConfig.UpsertSection(blocks, "notifications", new List<Kv>
                    {
                        new Kv("enabled", chkNotif.Checked ? "true" : "false"),
                        new Kv("notification_condition", TomlConfig.Q(cboNotifCond.Text.Length > 0 ? cboNotifCond.Text : "unfocused"))
                    });
                    TomlConfig.UpsertSection(blocks, "upgrade", new List<Kv> { new Kv("auto_install", chkAutoUpgrade.Checked ? "true" : "false") });
                    TomlConfig.UpsertSection(blocks, "markdown", new List<Kv> { new Kv("mermaid", TomlConfig.Q(cboMermaid.Text.Length > 0 ? cboMermaid.Text : "final")) });
                    var itemsRaw = txtStatusItems.Text.Trim();
                    if (itemsRaw.Length > 0)
                    {
                        var items = itemsRaw.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToList();
                        TomlConfig.UpsertSection(blocks, "status_line", new List<Kv> { new Kv("items", TomlConfig.JoinStringArray(items)) });
                    }
                    else TomlConfig.RemoveSection(blocks, "status_line");
                    TomlConfig.Save(blocks, tuiPath);
                    Log("已写入 " + tuiPath + " ✓（重启 kimi 生效）");
                    MessageBox.Show("界面偏好已写入，重启 kimi 后生效。", "完成");
                }
                catch (Exception ex) { Log("写入失败: " + ex.Message); MessageBox.Show(ex.Message, "错误"); }
            };
            btnThinkApply.Click += delegate
            {
                var efforts = new List<string>();
                if (chkTEffOff.Checked) efforts.Add("off");
                if (chkTEffLow.Checked) efforts.Add("low");
                if (chkTEffMedium.Checked) efforts.Add("medium");
                if (chkTEffHigh.Checked) efforts.Add("high");
                if (efforts.Count == 0) { MessageBox.Show("请至少勾选一个思考档位"); return; }
                var picked = new List<string>();
                foreach (var it in clbThinkAliases.CheckedItems) picked.Add(((string)it).Split(new[] { "  (" }, StringSplitOptions.None)[0]);
                if (picked.Count == 0) { MessageBox.Show("请至少勾选一个模型别名"); return; }
                try
                {
                    var blocks = OpenConfig();
                    int n = 0;
                    foreach (var name in picked)
                    {
                        var b = TomlConfig.Find(blocks, "models." + name);
                        if (b == null) continue;
                        var providerId = TomlConfig.GetValue(b, "provider") ?? "";
                        var modelId = TomlConfig.GetValue(b, "model") ?? "";
                        TomlConfig.SetKey(b, "supportEfforts", TomlConfig.JoinStringArray(efforts));
                        if (chkTAdaptive.Checked) TomlConfig.SetKey(b, "adaptiveThinking", "true");
                        else TomlConfig.RemoveKey(b, "adaptiveThinking");
                        var oe = txtOffEffort.Text.Trim();
                        if (oe.Length > 0) TomlConfig.SetKey(b, "offEffort", TomlConfig.Q(oe));
                        else TomlConfig.RemoveKey(b, "offEffort");
                        if (providerId.Length > 0 && modelId.Length > 0)
                        {
                            var pn = "providers." + providerId + ".models";
                            foreach (var pb in blocks)
                                if (pb.Name == pn && pb.IsArrayTable && TomlConfig.GetValue(pb, "model") == modelId)
                                    TomlConfig.SetKey(pb, "support_efforts", TomlConfig.JoinStringArray(efforts));
                        }
                        n++;
                    }
                    TomlConfig.Save(blocks, cfgPath);
                    Log("已为 " + n + " 个别名写入思考支持 " + TomlConfig.JoinStringArray(efforts) + " ✓");
                    MessageBox.Show("写入成功！新开一个 kimi 会话，即可在会话内切换思考强度。", "完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    LoadConfigToUi();
                }
                catch (Exception ex) { Log("写入失败: " + ex.Message); MessageBox.Show(ex.Message, "错误"); }
            };
            btnThinkRemove.Click += delegate
            {
                var picked = new List<string>();
                foreach (var it in clbThinkAliases.CheckedItems) picked.Add(((string)it).Split(new[] { "  (" }, StringSplitOptions.None)[0]);
                if (picked.Count == 0) { MessageBox.Show("请至少勾选一个模型别名"); return; }
                try
                {
                    var blocks = OpenConfig();
                    int n = 0;
                    foreach (var name in picked)
                    {
                        var b = TomlConfig.Find(blocks, "models." + name);
                        if (b == null) continue;
                        var providerId = TomlConfig.GetValue(b, "provider") ?? "";
                        var modelId = TomlConfig.GetValue(b, "model") ?? "";
                        TomlConfig.RemoveKey(b, "supportEfforts");
                        TomlConfig.RemoveKey(b, "adaptiveThinking");
                        TomlConfig.RemoveKey(b, "offEffort");
                        if (providerId.Length > 0 && modelId.Length > 0)
                        {
                            var pn = "providers." + providerId + ".models";
                            foreach (var pb in blocks)
                                if (pb.Name == pn && pb.IsArrayTable && TomlConfig.GetValue(pb, "model") == modelId)
                                    TomlConfig.RemoveKey(pb, "support_efforts");
                        }
                        n++;
                    }
                    TomlConfig.Save(blocks, cfgPath);
                    Log("已移除 " + n + " 个别名的思考声明 ✓");
                    LoadConfigToUi();
                }
                catch (Exception ex) { Log("移除失败: " + ex.Message); MessageBox.Show(ex.Message, "错误"); }
            };
            btnSaveRaw.Click += delegate
            {
                try
                {
                    var name = txtRawSection.Text.Trim();
                    if (name.Length == 0) { MessageBox.Show("请填写段名"); return; }
                    var blocks = OpenConfig();
                    var items = new List<Kv>();
                    foreach (var l in txtRawBody.Lines)
                    {
                        var m = Regex.Match(l, "^\\s*([A-Za-z0-9_\\-\\.]+)\\s*=\\s*(.+?)\\s*$");
                        if (m.Success) items.Add(new Kv(m.Groups[1].Value, m.Groups[2].Value));
                    }
                    TomlConfig.UpsertSection(blocks, name, items);
                    TomlConfig.Save(blocks, cfgPath);
                    Log("已写入 [" + name + "] ✓");
                }
                catch (Exception ex) { Log("写入失败: " + ex.Message); MessageBox.Show(ex.Message, "错误"); }
            };
            btnDelSec.Click += delegate
            {
                try
                {
                    var name = txtRawSection.Text.Trim();
                    if (name.Length == 0) { MessageBox.Show("请填写段名"); return; }
                    var blocks = OpenConfig();
                    TomlConfig.RemoveSection(blocks, name);
                    TomlConfig.Save(blocks, cfgPath);
                    Log("已删除 [" + name + "] ✓");
                }
                catch (Exception ex) { Log("失败: " + ex.Message); MessageBox.Show(ex.Message, "错误"); }
            };
            btnOpenCfg.Click += delegate
            {
                try { System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + cfgPath + "\""); }
                catch { }
            };

            ShowPage(0);
            LoadConfigToUi();
            Log("全局配置: " + cfgPath);

        }

        internal void NavForTest(int idx) { ShowPage(idx); }

        private void ShowPage(int idx)
        {
            for (int i = 0; i < pages.Count; i++) pages[i].Visible = (i == idx);
            for (int i = 0; i < navButtons.Count; i++)
            {
                if (i == idx) { navButtons[i].BackColor = AccentLight; navButtons[i].ForeColor = Accent; navButtons[i].Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold); }
                else { navButtons[i].BackColor = Color.White; navButtons[i].ForeColor = TextMain; navButtons[i].Font = new Font("Microsoft YaHei UI", 9.5F); }
            }
        }

        private void PageHeader(Panel p, string title, string sub)
        {
            var t = new Label { Text = title, Font = new Font("Microsoft YaHei UI", 12.5F, FontStyle.Bold), ForeColor = TextMain, Location = new Point(14, 12), AutoSize = true };
            var s = new Label { Text = sub, Font = new Font("Microsoft YaHei UI", 9F), ForeColor = TextSub, Location = new Point(16, 44), AutoSize = true };
            p.Controls.Add(t);
            p.Controls.Add(s);
        }

        private static Panel Card(Panel page, int x, int y, int w, int h, string title, string desc)
        {
            var card = new Panel { Location = new Point(x, y), Size = new Size(w, h), BackColor = Color.White };
            var t = new Label { Text = title, Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold), ForeColor = TextMain, Location = new Point(18, 10), AutoSize = true };
            card.Controls.Add(t);
            if (desc != null && desc.Length > 0)
            {
                var d = new Label { Text = desc, Font = new Font("Microsoft YaHei UI", 8.5F), ForeColor = TextSub, Location = new Point(18, 32), AutoSize = true };
                card.Controls.Add(d);
            }
            card.Paint += delegate(object s, PaintEventArgs e)
            {
                e.Graphics.DrawRectangle(new Pen(Color.FromArgb(228, 231, 236)), 0, 0, card.Width - 1, card.Height - 1);
            };
            page.Controls.Add(card);
            return card;
        }

        private static void CardLabel(Panel card, string name, int x, int y)
        {
            var l = new Label { Text = name, Location = new Point(x, y), AutoSize = true, ForeColor = TextMain };
            card.Controls.Add(l);
        }

        private void FieldLabel(Panel page, string name, int x, int y, string desc)
        {
            var l = new Label { Text = name, Location = new Point(x, y), AutoSize = true, ForeColor = TextMain };
            page.Controls.Add(l);
            if (desc != null)
            {
                var tip = new ToolTip();
                tip.SetToolTip(l, desc);
                tip.SetToolTip(page, desc);
            }
        }

        private void StylePrimary(Button b)
        {
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderSize = 0;
            b.BackColor = Accent;
            b.ForeColor = Color.White;
            b.Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold);
            b.Cursor = Cursors.Hand;
        }

        private void StyleGhost(Button b)
        {
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderColor = Color.FromArgb(209, 213, 219);
            b.BackColor = Color.White;
            b.ForeColor = TextMain;
            b.Cursor = Cursors.Hand;
        }

        private List<TomlBlock> OpenConfig()
        {
            List<TomlBlock> blocks;
            if (File.Exists(cfgPath)) blocks = TomlConfig.Parse(cfgPath);
            else { Directory.CreateDirectory(Path.GetDirectoryName(cfgPath)); blocks = new List<TomlBlock> { new TomlBlock { Name = "" } }; }
            return blocks;
        }

        private void LoadConfigToUi()
        {
            try
            {
                if (File.Exists(cfgPath))
                {
                    var blocks = TomlConfig.Parse(cfgPath);
                    lstProviders.Items.Clear();
                    clbThinkAliases.Items.Clear();
                    foreach (var b in blocks)
                    {
                        if (!b.Name.StartsWith("models.") || b.IsArrayTable) continue;
                        var an = b.Name.Substring("models.".Length);
                        var mdl = TomlConfig.GetValue(b, "model") ?? "";
                        var eff = TomlConfig.GetValue(b, "supportEfforts");
                        clbThinkAliases.Items.Add(an + "  (" + mdl + ")" + (eff != null ? "  [已声明: " + eff + "]" : "  [未声明]"), true);
                    }
                    foreach (var b in blocks)
                        if (b.Name.StartsWith("providers.") && !b.IsArrayTable && !b.Name.Contains("."))
                            lstProviders.Items.Add(b.Name.Substring("providers.".Length));
                    var th = TomlConfig.Find(blocks, "thinking");
                    var sec = TomlConfig.Find(blocks, "secondary_model");
                    if (th != null)
                    {
                        var en = TomlConfig.GetValue(th, "enabled");
                        if (en != null) chkThinking.Checked = en == "true";
                        cboEffort.Text = TomlConfig.GetValue(th, "effort") ?? "";
                        txtKeep.Text = TomlConfig.GetValue(th, "keep") ?? "";
                    }
                    if (sec != null) txtSecModel.Text = TomlConfig.GetValue(sec, "model") ?? "";
                }
                if (File.Exists(tuiPath))
                {
                    var tb = TomlConfig.Parse(tuiPath);
                    var r = TomlConfig.Find(tb, "");
                    if (r != null)
                    {
                        cboTheme.Text = TomlConfig.GetValue(r, "theme") ?? "";
                        var tm = TomlConfig.GetValue(r, "tui_mode");
                        if (tm == "fullscreen") cboTuiMode.SelectedIndex = 1; else if (tm == "regular") cboTuiMode.SelectedIndex = 0;
                        var la = TomlConfig.GetValue(r, "render_latex");
                        if (la != null) chkLatex.Checked = la == "true";
                        var pbv = TomlConfig.GetValue(r, "disable_paste_burst");
                        if (pbv != null) chkPasteBurst.Checked = pbv == "true";
                        var chv = TomlConfig.GetValue(r, "cache_expiry_hint");
                        if (chv != null) chkCacheHint.Checked = chv == "true";
                        var sv = TomlConfig.GetValue(r, "disable_feedback_survey");
                        if (sv != null) chkSurvey.Checked = sv == "true";
                    }
                    var md = TomlConfig.Find(tb, "markdown");
                    if (md != null) { var mm = TomlConfig.GetValue(md, "mermaid"); if (mm != null) cboMermaid.Text = mm; }
                    var ed = TomlConfig.Find(tb, "editor");
                    if (ed != null) txtEditor.Text = TomlConfig.GetValue(ed, "command") ?? "";
                    var no = TomlConfig.Find(tb, "notifications");
                    if (no != null)
                    {
                        var nv = TomlConfig.GetValue(no, "enabled");
                        if (nv != null) chkNotif.Checked = nv == "true";
                        var nc = TomlConfig.GetValue(no, "notification_condition");
                        if (nc != null) cboNotifCond.Text = nc;
                    }
                    var up = TomlConfig.Find(tb, "upgrade");
                    if (up != null) { var av = TomlConfig.GetValue(up, "auto_install"); if (av != null) chkAutoUpgrade.Checked = av == "true"; }
                    var st = TomlConfig.Find(tb, "status_line");
                    if (st != null && st.Items.Count > 0)
                    {
                        var arr = TomlConfig.ParseStringArray(st.Items[0].Value);
                        txtStatusItems.Text = string.Join(", ", arr.ToArray());
                    }
                }
            }
            catch (Exception ex) { Log("读取配置失败: " + ex.Message); }
        }

        private void OnDeleteProvider(object sender, EventArgs e)
        {
            var id = lstProviders.SelectedItem as string;
            if (id == null) { MessageBox.Show("请先在列表中选中一个供应商"); return; }
            if (MessageBox.Show("删除供应商 \"" + id + "\" 及其全部模型别名？", "确认", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            try
            {
                var blocks = OpenConfig();
                string pfx = "providers." + id + ".";
                var toRemove = new List<TomlBlock>();
                foreach (var b in blocks)
                {
                    if (b.Name == "providers." + id || b.Name.StartsWith(pfx)) { toRemove.Add(b); continue; }
                    if (b.Name.StartsWith("models.") && !b.IsArrayTable && TomlConfig.GetValue(b, "provider") == id) toRemove.Add(b);
                }
                foreach (var b in toRemove) blocks.Remove(b);
                TomlConfig.Save(blocks, cfgPath);
                LoadConfigToUi();
                Log("已删除供应商 " + id + " ✓");
            }
            catch (Exception ex) { Log("删除失败: " + ex.Message); MessageBox.Show(ex.Message, "错误"); }
        }

        private void Log(string s)
        {
            txtLog.AppendText("[" + DateTime.Now.ToString("HH:mm:ss") + "] " + s + Environment.NewLine);
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
        }

        private void OnFetch(object sender, EventArgs e)
        {
            var baseUrl = txtBase.Text.Trim();
            var key = txtKey.Text.Trim();
            if (baseUrl.Length == 0 || key.Length == 0) { MessageBox.Show("请先填写 Base URL 与 API Key"); return; }
            btnFetch.Enabled = false; btnTest.Enabled = false;
            lblStatus.Text = "正在获取…"; lblStatus.ForeColor = TextSub;
            var self = this;
            Task.Factory.StartNew(delegate { return Api.FetchModels(baseUrl, key); })
                .ContinueWith(delegate(Task<List<string>> t)
            {
                self.BeginInvoke(new Action(delegate
                {
                    btnFetch.Enabled = true; btnTest.Enabled = true;
                    if (t.IsFaulted)
                    {
                        var ex = t.Exception.Flatten().InnerException;
                        lblStatus.Text = "获取失败"; lblStatus.ForeColor = ErrRed;
                        Log("获取模型失败: " + ex.Message);
                        MessageBox.Show("获取模型失败:\r\n" + ex.Message + HintFor(ex.Message), "错误", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                    var ids = t.Result;
                    allIds = ids;
                    checkedIds = new HashSet<string>(ids);
                    cboDefault.Items.Clear();
                    foreach (var id in ids) cboDefault.Items.Add(id);
                    if (ids.Count > 0) cboDefault.SelectedIndex = 0;
                    RenderModels();
                    lblStatus.Text = "✓ 获取到 " + ids.Count + " 个模型"; lblStatus.ForeColor = OkGreen;
                    Log("获取到 " + ids.Count + " 个模型（已全选）。搜索框可过滤；选好默认模型后点写入。");
                }));
            });
        }

        private void OnTest(object sender, EventArgs e)
        {
            var baseUrl = txtBase.Text.Trim();
            var key = txtKey.Text.Trim();
            if (baseUrl.Length == 0 || key.Length == 0) { MessageBox.Show("请先填写 Base URL 与 API Key"); return; }
            btnTest.Enabled = false;
            lblStatus.Text = "正在测试…"; lblStatus.ForeColor = TextSub;
            var self = this;
            Task.Factory.StartNew(delegate { return Api.FetchModels(baseUrl, key); })
                .ContinueWith(delegate(Task<List<string>> t)
            {
                self.BeginInvoke(new Action(delegate
                {
                    btnTest.Enabled = true;
                    if (t.IsFaulted)
                    {
                        var ex = t.Exception.Flatten().InnerException;
                        lblStatus.Text = "✗ 连接失败"; lblStatus.ForeColor = ErrRed;
                        Log("测试连接失败: " + ex.Message);
                        MessageBox.Show("连接失败:\r\n" + ex.Message + HintFor(ex.Message), "错误", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                    lblStatus.Text = "✓ 连接正常，" + t.Result.Count + " 个模型"; lblStatus.ForeColor = OkGreen;
                    Log("测试连接成功，" + t.Result.Count + " 个模型。");
                }));
            });
        }

        private string HintFor(string msg)
        {
            if (msg.IndexOf("401") >= 0) return "\r\n\r\n提示：401 = API Key 无效，请到服务商后台重新生成。";
            if (msg.IndexOf("403") >= 0) return "\r\n\r\n提示：403 = 权限或余额问题，请登录服务商面板确认。";
            if (msg.IndexOf("400") >= 0) return "\r\n\r\n提示：400 = 请求参数问题，常见为模型名或额度限制。";
            if (msg.IndexOf("name") >= 0 && msg.IndexOf("resolve") >= 0) return "\r\n\r\n提示：域名解析失败，请检查 Base URL 拼写与网络。";
            return "";
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

            try
            {
                var blocks = OpenConfig();
                var used = new List<string>();
                foreach (var b in blocks)
                    if (b.Name.StartsWith("models.")) used.Add(b.Name.Substring("models.".Length));
                var models = new List<ModelInfo>();
                foreach (var mid in chosen) models.Add(new ModelInfo { Id = mid, Alias = Api.MakeAlias(mid, used) });
                var defModel = models.First(m => m.Id == def);

                TomlConfig.MergeProvider(blocks, id, baseUrl, apiKey, models, ctx, outp, defModel.Alias, defModel.Id);
                TomlConfig.Save(blocks, cfgPath);
                Log("已写入(全局，对所有目录生效): " + cfgPath);
                Log("供应商: " + id + "  模型数: " + models.Count + "  默认: " + defModel.Alias + " (" + defModel.Id + ")");
                Log("新开终端运行 kimi 即可使用；会话内 /model 可切换模型。");
                MessageBox.Show("写入成功！\r\n\r\n默认模型: " + defModel.Alias + " (" + defModel.Id + ")\r\n\r\n新开一个终端，cd 到项目目录，输入 kimi 即可开始使用。", "完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
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
        private static string DefaultConfigPath()
        {
            var home = Environment.GetEnvironmentVariable("KIMI_CODE_HOME");
            if (string.IsNullOrEmpty(home)) home = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".kimi-code");
            return Path.Combine(home, "config.toml");
        }

        private static int Main(string[] args)
        {
            try { System.Net.ServicePointManager.SecurityProtocol |= (System.Net.SecurityProtocolType)3072 | (System.Net.SecurityProtocolType)12288; } catch { }
            try { Console.OutputEncoding = Encoding.UTF8; } catch { }
            if (args.Length > 0 && args[0] == "--screenshot")
            {
                var dir = args.Length > 1 ? args[1] : ".";
                var f = new MainForm();
                f.StartPosition = FormStartPosition.Manual;
                f.Location = new Point(10, 10);
                f.Show();
                Application.DoEvents();
                for (int pi = 0; pi < 7; pi++)
                {
                    f.NavForTest(pi);
                    Application.DoEvents();
                    using (var bmp = new Bitmap(f.Width, f.Height))
                    {
                        f.DrawToBitmap(bmp, new Rectangle(0, 0, f.Width, f.Height));
                        bmp.Save(Path.Combine(dir, "page" + (pi + 1) + ".png"));
                    }
                }
                f.Close();
                Console.WriteLine("screenshots saved to " + dir);
                return 0;
            }
            if (args.Length > 0 && args[0] == "--fix-permission")
            {
                var path = args.Length > 1 && args[1].Length > 0 ? args[1] : DefaultConfigPath();
                var blocks = TomlConfig.Parse(path);
                var had = TomlConfig.Find(blocks, "permission");
                int removed = 0;
                if (had != null)
                {
                    var keys = had.Items.Where(k => k.Key != "#raw").ToList();
                    if (keys.All(k => k.Key == "mode")) { TomlConfig.RemoveSection(blocks, "permission"); removed = 1; }
                    else TomlConfig.RemoveKey(had, "mode");
                }
                TomlConfig.Save(blocks, path);
                Console.WriteLine(removed > 0 ? "OK 已清理无效的 [permission] 段" : "OK 没有需要清理的 mode 键");
                return 0;
            }
            if (args.Length > 0 && args[0] == "--think-apply")
            {
                var path = args.Length > 1 && args[1].Length > 0 ? args[1] : DefaultConfigPath();
                var efforts = new List<string> { "off", "low", "medium", "high" };
                var blocks = TomlConfig.Parse(path);
                int n = 0;
                foreach (var b in blocks)
                {
                    if (!b.Name.StartsWith("models.") || b.IsArrayTable) continue;
                    var providerId = TomlConfig.GetValue(b, "provider") ?? "";
                    var modelId = TomlConfig.GetValue(b, "model") ?? "";
                    TomlConfig.SetKey(b, "supportEfforts", TomlConfig.JoinStringArray(efforts));
                    TomlConfig.SetKey(b, "offEffort", TomlConfig.Q("none"));
                    if (providerId.Length > 0 && modelId.Length > 0)
                    {
                        var pn = "providers." + providerId + ".models";
                        foreach (var pb in blocks)
                            if (pb.Name == pn && pb.IsArrayTable && TomlConfig.GetValue(pb, "model") == modelId)
                                TomlConfig.SetKey(pb, "support_efforts", TomlConfig.JoinStringArray(efforts));
                    }
                    n++;
                }
                TomlConfig.Save(blocks, path);
                Console.WriteLine("OK 已为 " + n + " 个别名写入 supportEfforts");
                return 0;
            }
            if (args.Length > 0 && args[0] == "--selftest") return SelfTest(args.Length > 1 ? args[1] : null);
            if (args.Length > 0 && args[0] == "--fetchtest")
            {
                var ids = Api.FetchModels(args[1], args[2]);
                Console.WriteLine("模型数: " + ids.Count);
                foreach (var i in ids.Take(5)) Console.WriteLine("  " + i);
                return 0;
            }
            if (args.Length > 0 && args[0] == "--apply")
            {
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
                if (prov == null || aliasCount != 2 || dm != "zz-c-d") { Console.WriteLine("SELFTEST FAIL (provider/alias/default)"); return 1; }

                TomlConfig.UpsertSection(again, "thinking", new List<Kv> { new Kv("enabled", "true"), new Kv("effort", TomlConfig.Q("high")) });
                TomlConfig.UpsertSection(again, "permission", new List<Kv> { new Kv("mode", TomlConfig.Q("yolo")) });
                TomlConfig.Save(again, tmp);
                var third = TomlConfig.Parse(tmp);
                var th = TomlConfig.Find(third, "thinking");
                var pm = TomlConfig.Find(third, "permission");
                if (th == null || TomlConfig.GetValue(th, "effort") != "high" || pm == null || TomlConfig.GetValue(pm, "mode") != "yolo")
                { Console.WriteLine("SELFTEST FAIL (sections)"); return 1; }

                var arr = TomlConfig.ParseStringArray(TomlConfig.JoinStringArray(new List<string> { "model", "context", "a\"b" }));
                if (arr.Count != 3 || arr[2] != "a\"b") { Console.WriteLine("SELFTEST FAIL (array)"); return 1; }
                TomlConfig.SetKey(th, "supportEfforts", TomlConfig.JoinStringArray(new List<string> { "off", "low", "medium", "high" }));
                TomlConfig.SetKey(th, "offEffort", TomlConfig.Q("none"));
                TomlConfig.Save(third, tmp);
                var th3 = TomlConfig.Find(TomlConfig.Parse(tmp), "thinking");
                var effArr = TomlConfig.ParseStringArray(TomlConfig.GetValue(th3, "supportEfforts"));
                if (effArr.Count != 4 || effArr[3] != "high") { Console.WriteLine("SELFTEST FAIL (supportEfforts)"); return 1; }
                if (TomlConfig.GetValue(th3, "offEffort") != "none") { Console.WriteLine("SELFTEST FAIL (offEffort)"); return 1; }

                var dupCount = TomlConfig.Parse(tmp).Count(b => b.Name == "providers.zzselftest");
                if (dupCount != 1) { Console.WriteLine("SELFTEST FAIL (idempotent)"); return 1; }
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
