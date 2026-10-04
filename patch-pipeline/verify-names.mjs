// 对比原版与补丁版的全部 name 值，验证工具名未被汉化
import fs from 'node:fs';
function names(file) {
  const s = fs.readFileSync(file, 'utf8');
  const set = new Set();
  const re1 = /name:\s*"((?:[^"\\]|\\.)*)"/g;
  const re2 = /\.name\(\s*"((?:[^"\\]|\\.)*)"/g;
  let m;
  while ((m = re1.exec(s))) set.add(m[1]);
  while ((m = re2.exec(s))) set.add(m[1]);
  return set;
}
const a = names('main-original.mjs');
const b = names('main-patched.mjs');
const cjk = /[\u4e00-\u9fff]/;
const added = [...b].filter(x => !a.has(x));
console.log('原版 name 值数量:', a.size, '| 补丁版:', b.size);
console.log('补丁版新增的 name 值:', added.length);
console.log('其中含中文的:', added.filter(x => cjk.test(x)).length);
console.log('原版有而补丁版丢失的:', [...a].filter(x => !b.has(x)).length);
let diff = 0;
for (const t of ['Bash','Read','Grep','Glob','Edit','Write','TaskOutput','TaskStop','Agent','AgentSwarm','AskUserQuestion','TodoList','WaitFor','UpdateGoal','TowerPlan','TowerSpawn','TowerMerge','ExitPlanMode','EnterPlanMode','ReadMediaFile','WebFetch','WebSearch','CronCreate','CronDelete','Skill']) {
  if (a.has(t) !== b.has(t)) { console.log('DIFF!', t); diff++; }
}
console.log(diff === 0 ? '核心工具名抽查: 全部一致' : '核心工具名存在差异!');
