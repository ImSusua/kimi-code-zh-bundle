// offEffort 支持：GUI 字段 + 写入/移除 + --think-apply 默认 none + 自检
import fs from 'node:fs';
let s = fs.readFileSync('KimiModelAdder.cs', 'utf8');
const R = (a, b) => {
  if (!s.includes(a)) { console.error('MISS:', JSON.stringify(a.slice(0, 70))); process.exit(1); }
  s = s.replace(a, b);
};

// 1. 字段
R(`        private CheckedListBox clbThinkAliases;
        private CheckBox chkTEffOff, chkTEffLow, chkTEffMedium, chkTEffHigh, chkTAdaptive;`,
`        private CheckedListBox clbThinkAliases;
        private CheckBox chkTEffOff, chkTEffLow, chkTEffMedium, chkTEffHigh, chkTAdaptive;
        private TextBox txtOffEffort;`);

// 2. 卡片 2 加 offEffort 输入框
R(`            chkTAdaptive = new CheckBox { Text = "adaptiveThinking（模型自适应思考，实验性）", Location = new Point(20, 86), AutoSize = true };
            cT2.Controls.AddRange(new Control[] { chkTEffOff, chkTEffLow, chkTEffMedium, chkTEffHigh, chkTAdaptive });`,
`            chkTAdaptive = new CheckBox { Text = "adaptiveThinking（模型自适应思考，实验性）", Location = new Point(20, 86), AutoSize = true };
            var lblOff = new Label { Text = "offEffort:", Location = new Point(20, 118), AutoSize = true, ForeColor = TextMain };
            txtOffEffort = new TextBox { Location = new Point(120, 114), Width = 120, Text = "none" };
            var lblOffN = new Label { Text = "关闭思考时发送的档位（模型\\u201c默认思考\\u201d时必须声明，如 none）；留空 = 不写", Location = new Point(250, 118), AutoSize = true, ForeColor = TextSub };
            cT2.Controls.AddRange(new Control[] { chkTEffOff, chkTEffLow, chkTEffMedium, chkTEffHigh, chkTAdaptive, lblOff, txtOffEffort, lblOffN });`);
R(`var cT2 = Card(pT, 16, 360, 676, 140,`, `var cT2 = Card(pT, 16, 360, 676, 170,`);

// 3. 写入处理器：offEffort
R(`                        TomlConfig.SetKey(b, "supportEfforts", TomlConfig.JoinStringArray(efforts));
                        if (chkTAdaptive.Checked) TomlConfig.SetKey(b, "adaptiveThinking", "true");
                        else TomlConfig.RemoveKey(b, "adaptiveThinking");`,
`                        TomlConfig.SetKey(b, "supportEfforts", TomlConfig.JoinStringArray(efforts));
                        if (chkTAdaptive.Checked) TomlConfig.SetKey(b, "adaptiveThinking", "true");
                        else TomlConfig.RemoveKey(b, "adaptiveThinking");
                        var oe = txtOffEffort.Text.Trim();
                        if (oe.Length > 0) TomlConfig.SetKey(b, "offEffort", TomlConfig.Q(oe));
                        else TomlConfig.RemoveKey(b, "offEffort");`);

// 4. 移除处理器：offEffort
R(`                        TomlConfig.RemoveKey(b, "supportEfforts");
                        TomlConfig.RemoveKey(b, "adaptiveThinking");`,
`                        TomlConfig.RemoveKey(b, "supportEfforts");
                        TomlConfig.RemoveKey(b, "adaptiveThinking");
                        TomlConfig.RemoveKey(b, "offEffort");`);

// 5. --think-apply：默认写 offEffort=none
R(`                    TomlConfig.SetKey(b, "supportEfforts", TomlConfig.JoinStringArray(efforts));
                    if (providerId.Length > 0 && modelId.Length > 0)
                    {
                        var pn = "providers." + providerId + ".models";
                        foreach (var pb in blocks)
                            if (pb.Name == pn && pb.IsArrayTable && TomlConfig.GetValue(pb, "model") == modelId)
                                TomlConfig.SetKey(pb, "support_efforts", TomlConfig.JoinStringArray(efforts));
                    }
                    n++;`,
`                    TomlConfig.SetKey(b, "supportEfforts", TomlConfig.JoinStringArray(efforts));
                    TomlConfig.SetKey(b, "offEffort", TomlConfig.Q("none"));
                    if (providerId.Length > 0 && modelId.Length > 0)
                    {
                        var pn = "providers." + providerId + ".models";
                        foreach (var pb in blocks)
                            if (pb.Name == pn && pb.IsArrayTable && TomlConfig.GetValue(pb, "model") == modelId)
                                TomlConfig.SetKey(pb, "support_efforts", TomlConfig.JoinStringArray(efforts));
                    }
                    n++;`);

// 6. SelfTest：offEffort 往返
R(`                var effArr = TomlConfig.ParseStringArray(TomlConfig.GetValue(th3, "supportEfforts"));
                if (effArr.Count != 4 || effArr[3] != "high") { Console.WriteLine("SELFTEST FAIL (supportEfforts)"); return 1; }`,
`                var effArr = TomlConfig.ParseStringArray(TomlConfig.GetValue(th3, "supportEfforts"));
                if (effArr.Count != 4 || effArr[3] != "high") { Console.WriteLine("SELFTEST FAIL (supportEfforts)"); return 1; }
                if (TomlConfig.GetValue(th3, "offEffort") != "none") { Console.WriteLine("SELFTEST FAIL (offEffort)"); return 1; }`);

fs.writeFileSync('KimiModelAdder.cs', s);
console.log('offEffort integrated');
