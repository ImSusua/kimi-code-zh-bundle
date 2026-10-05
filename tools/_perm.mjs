// 权限页重构：会话级权限的正确固定方式 = 启动器；并清理无效 [permission] 段
import fs from 'node:fs';
let s = fs.readFileSync('KimiModelAdder.cs', 'utf8');
const R = (a, b) => {
  if (!s.includes(a)) { console.error('MISS:', JSON.stringify(a.slice(0, 80))); process.exit(1); }
  s = s.replace(a, b);
};

// 1. 页 4 内容整体替换
const start = s.indexOf(`            // ================= 页 4：权限模式 =================`);
const endAnchor = `            p3.Controls.AddRange(new Control[] { btnSavePerm, lblPn });`;
const end = s.indexOf(endAnchor);
if (start < 0 || end < 0) { console.error('page4 markers not found', start, end); process.exit(1); }
const newPage = `            // ================= 页 4：权限模式（启动器） =================
            var p3 = pages[3];
            PageHeader(p3, "权限模式（启动方式）", "kimi 的权限模式是会话级设置（v2 没有 [permission] mode 配置键）—— 用固定参数的启动器来固定它");
            var pc = Card(p3, 16, 80, 676, 250, "创建固定权限模式的启动器", "在 bin 目录生成 kimi-auto / kimi-yolo 命令，并在桌面创建快捷方式");
            rbAuto = new RadioButton { Text = "无人值守（--auto，从不询问）—— 挂机跑长任务", Location = new Point(20, 38), AutoSize = true, Checked = true };
            var rbAutoN = new Label { Text = "一切自动运行、不再打断你；请只在可信项目里使用", Location = new Point(42, 62), AutoSize = true, ForeColor = TextSub };
            rbYolo = new RadioButton { Text = "按需询问（--yolo，常规自动、高风险询问）—— 推荐日常", Location = new Point(20, 92), AutoSize = true };
            var rbYoloN = new Label { Text = "常规编辑与命令自动执行，高风险操作仍会询问", Location = new Point(42, 116), AutoSize = true, ForeColor = TextSub };
            rbManual = new RadioButton { Text = "总是询问（默认启动即是，无需启动器）—— 重要项目", Location = new Point(20, 146), AutoSize = true };
            var lblPC = new Label { Text = "已有会话会记住自己被设置过的模式；会话内也可用 /permissions 随时改。", Location = new Point(20, 176), AutoSize = true, ForeColor = TextSub };
            var lblPC2 = new Label { Text = "此前写入的 [permission] mode 是无效配置（v2 会忽略），可用下面的按钮清理。", Location = new Point(20, 200), AutoSize = true, ForeColor = TextSub };
            var btnMakeLauncher = new Button { Text = "创建启动器 + 桌面快捷方式", Location = new Point(20, 196), Size = new Size(280, 40) };
            pc.Controls.AddRange(new Control[] { rbAuto, rbAutoN, rbYolo, rbYoloN, rbManual, lblPC, lblPC2 });
            pc.Controls.Add(btnMakeLauncher);
            var btnCleanPerm = new Button { Text = "清理无效的 [permission] 段", Location = new Point(16, 344), Size = new Size(240, 32) };
            StyleGhost(btnCleanPerm);
            p3.Controls.Add(btnCleanPerm);
            var lblPn = new Label { Text = "启动器写入 " + Path.Combine(Path.GetDirectoryName(cfgPath), "bin") + "；桌面快捷方式按所选模式命名。", Location = new Point(16, 384), AutoSize = true, ForeColor = TextSub };
            p3.Controls.Add(lblPn);
`;
s = s.slice(0, start) + newPage + s.slice(end + endAnchor.length);

// 2. LoadConfigToUi：去掉 rb* 的 [permission] mode 读取
R(`                    var pm = TomlConfig.Find(blocks, "permission");
`, ``);
R(`                    if (pm != null)
                    {
                        var mode = TomlConfig.GetValue(pm, "mode");
                        if (mode == "yolo") rbYolo.Checked = true;
                        else if (mode == "auto") rbAuto.Checked = true;
                        else rbManual.Checked = true;
                    }
`, ``);
// 3. 旧 btnSavePerm 处理器替换为 启动器创建 + 清理
R(`            btnSavePerm.Click += delegate
            {
                try
                {
                    var blocks = OpenConfig();
                    string mode = rbManual.Checked ? "manual" : (rbYolo.Checked ? "yolo" : "auto");
                    TomlConfig.UpsertSection(blocks, "permission", new List<Kv> { new Kv("mode", TomlConfig.Q(mode)) });
                    TomlConfig.Save(blocks, cfgPath);
                    Log("已写入 [permission] mode = \\"" + mode + "\\" ✓");
                    MessageBox.Show("已写入权限模式：" + mode + "\\r\\n新开的 kimi 会话生效。", "完成");
                }
                catch (Exception ex) { Log("写入失败: " + ex.Message); MessageBox.Show(ex.Message, "错误"); }
            };`,
`            btnMakeLauncher.Click += delegate
            {
                try
                {
                    string mode = rbYolo.Checked ? "yolo" : "auto";
                    var binDir = Path.Combine(Path.GetDirectoryName(cfgPath), "bin");
                    Directory.CreateDirectory(binDir);
                    var cmdPath = Path.Combine(binDir, "kimi-" + mode + ".cmd");
                    var shim = Path.Combine(binDir, "kimi.cmd");
                    var body = "@echo off\\r\\n" + (File.Exists(shim)
                        ? "call \\"%~dp0kimi.cmd\\" --" + mode + " %*\\r\\n"
                        : "\\"%USERPROFILE%\\\\.kimi-code\\\\bin\\\\kimi.cmd\\" --" + mode + " %*\\r\\n") + "exit /b %ERRORLEVEL%\\r\\n";
                    File.WriteAllText(cmdPath, body, new UTF8Encoding(false));
                    dynamic link = System.Activator.CreateInstance(System.Type.GetTypeFromProgID("WScript.Shell"));
                    var name = "Kimi " + (mode == "auto" ? "无人值守" : "按需询问");
                    var lnk = link.CreateShortcut(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory) + "\\\\" + name + ".lnk");
                    lnk.TargetPath = cmdPath;
                    lnk.WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                    lnk.Description = "Kimi Code " + (mode == "auto" ? "无人值守（--auto）" : "按需询问（--yolo）");
                    lnk.Save();
                    Log("已创建启动器 " + cmdPath + " 与桌面快捷方式 ✓");
                    MessageBox.Show("已创建：\\r\\n  命令 kimi-" + mode + "（全局可用）\\r\\n  桌面快捷方式 " + name + "\\r\\n\\r\\n双击快捷方式或在任意目录运行 kimi-" + mode + " 即以该模式启动。", "完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex) { Log("创建失败: " + ex.Message); MessageBox.Show("创建失败:\\r\\n" + ex.Message, "错误"); }
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
            };`);

// 4. CLI --fix-permission
R(`            if (args.Length > 0 && args[0] == "--think-apply")`,
`            if (args.Length > 0 && args[0] == "--fix-permission")
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
            if (args.Length > 0 && args[0] == "--think-apply")`);

fs.writeFileSync('KimiModelAdder.cs', s);
console.log('permission page rebuilt');
