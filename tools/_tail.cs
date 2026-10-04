
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

        private void Card(Panel page, int x, int y, int w, int h, string title, string desc)
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
                    foreach (var b in blocks)
                        if (b.Name.StartsWith("providers.") && !b.IsArrayTable && !b.Name.Contains("."))
                            lstProviders.Items.Add(b.Name.Substring("providers.".Length));
                    var th = TomlConfig.Find(blocks, "thinking");
                    var pm = TomlConfig.Find(blocks, "permission");
                    var sec = TomlConfig.Find(blocks, "secondary_model");
                    if (th != null)
                    {
                        var en = TomlConfig.GetValue(th, "enabled");
                        if (en != null) chkThinking.Checked = en == "true";
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
                for (int pi = 0; pi < 6; pi++)
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
