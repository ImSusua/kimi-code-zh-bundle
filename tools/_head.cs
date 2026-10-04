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
        private TextBox txtLog;

        private List<string> allIds = new List<string>();
        private HashSet<string> checkedIds = new HashSet<string>();

        public MainForm()
        {
            var home = Environment.GetEnvironmentVariable("KIMI_CODE_HOME");
            if (string.IsNullOrEmpty(home)) home = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".kimi-code");
            cfgPath = Path.Combine(home, "config.toml");
            tuiPath = Path.Combine(home, "tui.toml");

