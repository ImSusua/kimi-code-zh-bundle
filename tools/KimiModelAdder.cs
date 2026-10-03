// KimiModelAdder - Kimi Code 配置管理器（GUI，覆盖 CLI 可配置的全部设置项）
// Tab1 供应商与模型（获取/搜索/全选/默认模型/删除供应商）
// Tab2 思考与子模型（[thinking] / [secondary_model]）
// Tab3 权限模式（[permission]）
// Tab4 界面 tui.toml（主题/TUI模式/mermaid/latex/编辑器/通知/更新/状态栏…）
// Tab5 高级（实验性 flags / 任意 TOML 段直接编辑）
// 编译（系统自带 csc，C# 5 语法）：
//   csc -target:winexe -out:KimiModelAdder.exe -r:System.dll -r:System.Core.dll
//       -r:System.Drawing.dll -r:System.Windows.Forms.dll -r:System.Web.Extensions.dll
//       -r:System.Net.Http.dll KimiModelAdder.cs
// 命令行：--selftest [config路径]
//         --fetchtest <base_url> <api_key>
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
        public string Value; // 保留原始写法（含引号或数字）
        public Kv(string key, string value) { Key = key; Value = value; }
    }

    internal class TomlBlock
    {
        public string Name = "";            // ""=根, "providers.susu", "models.x", "providers.susu.models"
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

        // 解析 TOML 字符串数组（单行 ["a","b"]）
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

        // 托管写入：providers.<id> 块 + models.<别名> 块 + 根 default_*
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

        // 托管写入：替换/新建一个普通段（保留其余块）；keys 为 null 的键删除
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
        private const string Efforts = "off;low;medium;high";
        private readonly string cfgPath;
        private readonly string tuiPath;

        // Tab1
        private TextBox txtProvider, txtBase, txtKey, txtSearch;
        private CheckBox chkShow;
        private Button btnFetch, btnApply, btnOpenCfg, btnAll, btnNone, btnDelProvider, btnReload;
        private CheckedListBox clbModels;
        private ComboBox cboDefault;
        private NumericUpDown numContext, numOutput;
        private Label lblStatus;
        private ListBox lstProviders;
        // Tab2
        private CheckBox chkThinking, chkSecForce;
        private ComboBox cboEffort;
        private TextBox txtKeep, txtSecModel;
        private Button btnSaveThink;
        // Tab3
        private RadioButton rbManual, rbYolo, rbAuto;
        private Button btnSavePerm;
        // Tab4
        private ComboBox cboTheme, cboTuiMode, cboMermaid, cboNotifCond;
        private TextBox txtEditor, txtStatusItems;
        private CheckBox chkLatex, chkPasteBurst, chkCacheHint, chkSurvey, chkNotif, chkAutoUpgrade;
        private Button btnSaveTui;
        // Tab5
        private TextBox txtFlags, txtRawSection, txtRawBody;
        private Button btnSaveRaw;
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
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(860, 700);
            Font = new Font("Microsoft YaHei UI", 9F);

            var tabs = new TabControl { Location = new Point(8, 8), Size = new Size(844, 596) };

            // ---------- Tab1 供应商与模型 ----------
            var tab1 = new TabPage("供应商与模型");
            var lbl1 = new Label { Text = "供应商 ID:", Location = new Point(14, 14), AutoSize = true };
            txtProvider = new TextBox { Location = new Point(100, 10), Width = 150, Text = "susu" };
            var lbl2 = new Label { Text = "Base URL:", Location = new Point(272, 14), AutoSize = true };
            txtBase = new TextBox { Location = new Point(348, 10), Width = 470 };
            var lbl3 = new Label { Text = "API Key:", Location = new Point(14, 48), AutoSize = true };
            txtKey = new TextBox { Location = new Point(100, 44), Width = 600, UseSystemPasswordChar = true };
            chkShow = new CheckBox { Text = "显示", Location = new Point(716, 44), AutoSize = true };
            chkShow.CheckedChanged += delegate { txtKey.UseSystemPasswordChar = !chkShow.Checked; };
            btnFetch = new Button { Text = "获取模型列表", Location = new Point(14, 78), Size = new Size(130, 30) };
            lblStatus = new Label { Text = "", Location = new Point(154, 84), AutoSize = true, ForeColor = Color.DimGray };
            var lblModels = new Label { Text = "模型（勾选要添加的）:", Location = new Point(14, 116), AutoSize = true };
            txtSearch = new TextBox { Location = new Point(14, 136), Width = 300 };
            btnAll = new Button { Text = "全选", Location = new Point(326, 134), Size = new Size(72, 25) };
            btnNone = new Button { Text = "全不选", Location = new Point(402, 134), Size = new Size(72, 25) };
            clbModels = new CheckedListBox { Location = new Point(14, 166), Size = new Size(460, 260), CheckOnClick = true };
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
            var lblDefault = new Label { Text = "默认模型:", Location = new Point(494, 170), AutoSize = true };
            cboDefault = new ComboBox { Location = new Point(494, 190), Width = 330, DropDownStyle = ComboBoxStyle.DropDownList };
            var lblCtx = new Label { Text = "上下文长度:", Location = new Point(494, 230), AutoSize = true };
            numContext = new NumericUpDown { Location = new Point(494, 250), Width = 130, Maximum = 2000000, Value = 200000, Increment = 1000 };
            var lblOut = new Label { Text = "最大输出 (0=不限):", Location = new Point(494, 288), AutoSize = true };
            numOutput = new NumericUpDown { Location = new Point(494, 308), Width = 130, Maximum = 2000000, Value = 65536, Increment = 1024 };
            btnApply = new Button { Text = "写入全局 config.toml（所有目录生效）", Location = new Point(494, 348), Size = new Size(330, 36) };
            var lblProvList = new Label { Text = "已配置的供应商（选中可删除）:", Location = new Point(494, 398), AutoSize = true };
            lstProviders = new ListBox { Location = new Point(494, 418), Size = new Size(330, 96) };
            btnDelProvider = new Button { Text = "删除选中供应商及其模型", Location = new Point(494, 520), Size = new Size(330, 30) };
            btnOpenCfg = new Button { Text = "打开配置目录", Location = new Point(14, 434), Size = new Size(140, 28) };
            btnReload = new Button { Text = "重新读取配置", Location = new Point(164, 434), Size = new Size(140, 28) };
            var lblG = new Label { Text = "写入的是全局配置文件（KIMI_CODE_HOME 或 %USERPROFILE%\\.kimi-code\\config.toml），对所有工作目录生效。", Location = new Point(14, 556), AutoSize = true, ForeColor = Color.DimGray };
            tab1.Controls.AddRange(new Control[] { lbl1, txtProvider, lbl2, txtBase, lbl3, txtKey, chkShow, btnFetch, lblStatus, lblModels, txtSearch, btnAll, btnNone, clbModels, lblDefault, cboDefault, lblCtx, numContext, lblOut, numOutput, btnApply, lblProvList, lstProviders, btnDelProvider, btnOpenCfg, btnReload, lblG });

            // ---------- Tab2 思考与子模型 ----------
            var tab2 = new TabPage("思考与子模型");
            var gb1 = new GroupBox { Text = "思考 [thinking]", Location = new Point(14, 14), Size = new Size(810, 170) };
            var chkThinkingL = new Label { Text = "enabled:", Location = new Point(20, 36), AutoSize = true };
            chkThinking = new CheckBox { Text = "启用思考（默认由模型决定）", Location = new Point(110, 32), AutoSize = true };
            var lblEffort = new Label { Text = "effort:", Location = new Point(20, 70), AutoSize = true };
            cboEffort = new ComboBox { Location = new Point(110, 66), Width = 140, DropDownStyle = ComboBoxStyle.DropDown };
            cboEffort.Items.AddRange(new object[] { "off", "low", "medium", "high" });
            var lblKeep = new Label { Text = "keep:", Location = new Point(20, 104), AutoSize = true };
            txtKeep = new TextBox { Location = new Point(110, 100), Width = 140 };
            var lblKeepN = new Label { Text = "（压缩上下文时保留的思考预算，如 20000）", Location = new Point(260, 104), AutoSize = true, ForeColor = Color.DimGray };
            gb1.Controls.AddRange(new Control[] { chkThinkingL, chkThinking, lblEffort, cboEffort, lblKeep, txtKeep, lblKeepN });
            var gb2 = new GroupBox { Text = "子智能体次级模型 [secondary_model]", Location = new Point(14, 196), Size = new Size(810, 130) };
            var lblSec = new Label { Text = "model (别名):", Location = new Point(20, 36), AutoSize = true };
            txtSecModel = new TextBox { Location = new Point(130, 32), Width = 300 };
            var lblSec2 = new Label { Text = "留空 = 跟随主模型；填 [models.*] 别名，如 agnes-2-5-flash", Location = new Point(440, 36), AutoSize = true, ForeColor = Color.DimGray };
            var chkSecForce = new CheckBox { Text = "force（强制子智能体使用，忽略会话级覆盖）", Location = new Point(130, 70), AutoSize = true };
            gb2.Controls.AddRange(new Control[] { lblSec, txtSecModel, lblSec2, chkSecForce });
            var btnSave23 = new Button { Text = "写入以上设置", Location = new Point(14, 340), Size = new Size(180, 34) };
            var lbl23 = new Label { Text = "留空的键不会写入；effort 支持任意值（常用 off/low/medium/high）。", Location = new Point(14, 556), AutoSize = true, ForeColor = Color.DimGray };
            tab2.Controls.AddRange(new Control[] { gb1, gb2, btnSave23, lbl23 });

            // ---------- Tab3 权限 ----------
            var tab3 = new TabPage("权限模式");
            var lblP = new Label { Text = "permission.mode —— 控制工具执行的批准策略（对应界面：总是询问 / 按需询问 / 从不询问）:", Location = new Point(14, 20), AutoSize = true };
            var rb1 = new RadioButton { Text = "manual —— 总是询问：执行命令/改文件前都会询问（最安全）", Location = new Point(30, 60), AutoSize = true };
            var rb2 = new RadioButton { Text = "yolo —— 按需询问：常规编辑与命令自动执行，高风险操作仍询问", Location = new Point(30, 90), AutoSize = true };
            var rb3 = new RadioButton { Text = "auto —— 从不询问：一切自动运行（无人值守）", Location = new Point(30, 120), AutoSize = true };
            var pb = new Panel { Location = new Point(14, 44), Size = new Size(800, 110) };
            pb.Controls.AddRange(new Control[] { rb1, rb2, rb3 });
            var btnSaveP = new Button { Text = "写入权限模式", Location = new Point(14, 170), Size = new Size(180, 34) };
            var lblPn = new Label { Text = "写入 [permission] mode = \"…\"；删除该段则恢复默认（manual）。", Location = new Point(14, 556), AutoSize = true, ForeColor = Color.DimGray };
            tab3.Controls.AddRange(new Control[] { lblP, pb, btnSaveP, lblPn });

            // ---------- Tab4 界面 tui.toml ----------
            var tab4 = new TabPage("界面 tui.toml");
            int y = 16;
            Action<string, Control, Control> row = delegate(string name, Control a, Control b)
            {
                var l = new Label { Text = name, Location = new Point(16, y + 4), AutoSize = true, Width = 170 };
                a.Location = new Point(190, y);
                if (b != null) { b.Location = new Point(a.Right + 10, y); tab4.Controls.Add(b); }
                tab4.Controls.Add(l); tab4.Controls.Add(a);
                y += 40;
            };
            cboTheme = new ComboBox { DropDownStyle = ComboBoxStyle.DropDown, Width = 160 };
            cboTheme.Items.AddRange(new object[] { "auto", "dark", "light" });
            row("theme 主题:", cboTheme, null);
            cboTuiMode = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 };
            cboTuiMode.Items.AddRange(new object[] { "regular", "fullscreen" });
            row("tui_mode 布局:", cboTuiMode, null);
            cboMermaid = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 };
            cboMermaid.Items.AddRange(new object[] { "final", "off" });
            row("markdown.mermaid:", cboMermaid, null);
            chkLatex = new CheckBox { Text = "render_latex 渲染 LaTeX 公式", AutoSize = true };
            row("", chkLatex, null);
            chkPasteBurst = new CheckBox { Text = "disable_paste_burst 禁用非括号化粘贴连发", AutoSize = true };
            row("", chkPasteBurst, null);
            chkCacheHint = new CheckBox { Text = "cache_expiry_hint 缓存过期提醒对话框", AutoSize = true };
            row("", chkCacheHint, null);
            chkSurvey = new CheckBox { Text = "disable_feedback_survey 隐藏评分问卷", AutoSize = true };
            row("", chkSurvey, null);
            txtEditor = new TextBox { Width = 380 };
            row("editor.command 外部编辑器:", txtEditor, null);
            var lblEd = new Label { Text = "（留空用 $VISUAL/$EDITOR）", Location = new Point(580, y - 36), AutoSize = true, ForeColor = Color.DimGray };
            tab4.Controls.Add(lblEd);
            chkNotif = new CheckBox { Text = "notifications.enabled 桌面通知", AutoSize = true };
            cboNotifCond = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 140 };
            cboNotifCond.Items.AddRange(new object[] { "unfocused", "always" });
            row("", chkNotif, cboNotifCond);
            chkAutoUpgrade = new CheckBox { Text = "upgrade.auto_install 后台自动安装更新", AutoSize = true };
            row("", chkAutoUpgrade, null);
            var lblSt = new Label { Text = "status_line.items 底栏槽位:", Location = new Point(16, y + 4), AutoSize = true, Width = 170 };
            txtStatusItems = new TextBox { Location = new Point(190, y), Width = 620 };
            y += 40;
            tab4.Controls.Add(lblSt); tab4.Controls.Add(txtStatusItems);
            btnSaveTui = new Button { Text = "写入 tui.toml", Location = new Point(16, y + 10), Size = new Size(180, 34) };
            var lblT4 = new Label { Text = "写入 " + tuiPath + "（客户端界面偏好，与 config.toml 分离）", Location = new Point(14, 556), AutoSize = true, ForeColor = Color.DimGray };
            tab4.Controls.Add(btnSaveTui); tab4.Controls.Add(lblT4);

            // ---------- Tab5 高级 ----------
            var tab5 = new TabPage("高级");
            var lblExp = new Label { Text = "实验性 flags（[experimental]，一行一个）:", Location = new Point(14, 14), AutoSize = true };
            txtFlags = new TextBox { Location = new Point(14, 36), Size = new Size(810, 90), Multiline = true, ScrollBars = ScrollBars.Vertical };
            var lblRaw = new Label { Text = "任意 TOML 段直改（覆盖 hooks、workspace 等所有高级段）—— 段名 + 每行 key = value:", Location = new Point(14, 140), AutoSize = true };
            txtRawSection = new TextBox { Location = new Point(14, 162), Width = 300 };
            txtRawBody = new TextBox { Location = new Point(14, 192), Size = new Size(810, 220), Multiline = true, ScrollBars = ScrollBars.Vertical };
            var btnDelSec = new Button { Text = "删除该段", Location = new Point(330, 158), Size = new Size(110, 26) };
            var btnSaveRaw = new Button { Text = "写入该段", Location = new Point(14, 420), Size = new Size(180, 32) };
            var lblT5 = new Label { Text = "示例：段名 hooks，正文见官方文档 hooks 配置（TOML 表数组需用 [[段名]]，本框只支持普通段）。", Location = new Point(14, 556), AutoSize = true, ForeColor = Color.DimGray };
            tab5.Controls.AddRange(new Control[] { lblExp, txtFlags, lblRaw, txtRawSection, btnDelSec, txtRawBody, btnSaveRaw, lblT5 });

            tabs.TabPages.AddRange(new TabPage[] { tab1, tab2, tab3, tab4, tab5 });
            Controls.Add(tabs);

            txtLog = new TextBox { Location = new Point(8, 608), Size = new Size(844, 84), Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BackColor = Color.White };
            Controls.Add(txtLog);

            // ---------- 事件 ----------
            btnFetch.Click += OnFetch;
            btnApply.Click += OnApply;
            txtSearch.TextChanged += delegate { RenderModels(); };
            btnAll.Click += delegate { SetAllVisible(true); };
            btnNone.Click += delegate { SetAllVisible(false); };
            btnReload.Click += delegate { LoadConfigToUi(); };
            btnDelProvider.Click += OnDeleteProvider;
            btnSave23.Click += delegate
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
                    Log("已写入 thinking/secondary_model → " + cfgPath);
                }
                catch (Exception ex) { Log("写入失败: " + ex.Message); MessageBox.Show(ex.Message, "错误"); }
            };
            btnSaveP.Click += delegate
            {
                try
                {
                    var blocks = OpenConfig();
                    string mode = rb1.Checked ? "manual" : (rb2.Checked ? "yolo" : "auto");
                    TomlConfig.UpsertSection(blocks, "permission", new List<Kv> { new Kv("mode", TomlConfig.Q(mode)) });
                    TomlConfig.Save(blocks, cfgPath);
                    Log("已写入 [permission] mode = \"" + mode + "\"");
                }
                catch (Exception ex) { Log("写入失败: " + ex.Message); MessageBox.Show(ex.Message, "错误"); }
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
                    Log("已写入 " + tuiPath + "（重启 Kimi Code 生效）");
                }
                catch (Exception ex) { Log("写入失败: " + ex.Message); MessageBox.Show(ex.Message, "错误"); }
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
                    Log("已写入 [" + name + "] → " + cfgPath);
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
                    Log("已删除 [" + name + "]");
                }
                catch (Exception ex) { Log("失败: " + ex.Message); MessageBox.Show(ex.Message, "错误"); }
            };
            btnOpenCfg.Click += delegate
            {
                try { System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + cfgPath + "\""); }
                catch { }
            };

            Log("全局配置: " + cfgPath);
            Log("界面配置: " + tuiPath);
            LoadConfigToUi();
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
                    foreach (var b in blocks)
                        if (b.Name.StartsWith("providers.") && !b.IsArrayTable && !b.Name.Contains("."))
                            lstProviders.Items.Add(b.Name.Substring("providers.".Length));
                    var th = TomlConfig.Find(blocks, "thinking");
                    var pm = TomlConfig.Find(blocks, "permission");
                    var sec = TomlConfig.Find(blocks, "secondary_model");
                    if (th != null)
                    {
                        var en = TomlConfig.GetValue(th, "enabled");
                        cboEffort.Text = TomlConfig.GetValue(th, "effort") ?? "";
                        txtKeep.Text = TomlConfig.GetValue(th, "keep") ?? "";
                    }
                    if (pm != null)
                    {
                        var mode = TomlConfig.GetValue(pm, "mode");
                        if (mode == "yolo") rbYolo.Checked = true;
                        else if (mode == "auto") rbAuto.Checked = true;
                        else rbManual.Checked = true;
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
                        if (tm == "fullscreen") cboTuiMode.SelectedIndex = 1; else cboTuiMode.SelectedIndex = 0;
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
                    if (st != null)
                    {
                        var raw = st.Items.Count > 0 ? st.Items[0].Value : null;
                        var arr = TomlConfig.ParseStringArray(raw);
                        txtStatusItems.Text = string.Join(", ", arr.ToArray());
                    }
                }
                Log("已读取当前配置。");
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
                Log("已删除供应商 " + id);
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
            btnFetch.Enabled = false;
            lblStatus.Text = "正在获取…";
            var self = this;
            Task.Factory.StartNew(delegate { return Api.FetchModels(baseUrl, key); })
                .ContinueWith(delegate(Task<List<string>> t)
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
                    Log("获取到 " + ids.Count + " 个模型（已全选）。搜索框过滤，选好默认模型后写入。");
                }));
            });
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
        public static TextBox TxtLogRef; // 供 Log 使用（GUI 内通过实例方法）
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

                // 段写入/删除 + 字符串数组
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

                // 幂等
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
