// 上下文定向替换（仅作用于唯一/低风险片段），在 apply-patch 之后运行
import fs from 'node:fs';
const FILE = process.argv[2] || 'main-patched.mjs';
let s = fs.readFileSync(FILE, 'utf8');

const REPLACES = [
  // 状态面板/设置菜单 label
  ['label: "Model"', 'label: "模型"'],
  ['label: "Directory"', 'label: "目录"'],
  ['label: "Permissions"', 'label: "权限"'],
  ['label: "Plan mode"', 'label: "计划模式"'],
  ['label: "Tower mode"', 'label: "Tower 模式"'],
  ['label: "Session"', 'label: "会话"'],
  ['label: "Title"', 'label: "标题"'],
  ['label: "Warning"', 'label: "警告"'],
  // 状态面板 on/off/none/not set 值
  ['value: planMode ? "on" : "off"', 'value: planMode ? "开" : "关"'],
  ['value: towerMode ? "on" : "off"', 'value: towerMode ? "开" : "关"'],
  ['options.sessionId : "none"', 'options.sessionId : "无"'],
  ['if (model.trim().length === 0) return "not set";', 'if (model.trim().length === 0) return "未设置";'],
  // 帮助面板 Enter 快捷键
  ['description: "Submit"', 'description: "提交"'],

];

let total = 0;
for (const [from, to] of REPLACES) {
  let count = 0, idx = 0;
  while ((idx = s.indexOf(from, idx)) !== -1) { s = s.slice(0, idx) + to + s.slice(idx + from.length); idx += to.length; count++; }
  total += count;
  console.error(count.toString().padStart(2), JSON.stringify(from.slice(0, 60)));
}
fs.writeFileSync(FILE, s);
console.error('surgery total replacements:', total);
