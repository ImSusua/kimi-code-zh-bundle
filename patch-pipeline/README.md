# 汉化补丁管线（patch-pipeline）

Kimi Code CLI 的 TUI/CLI 汉化补丁生成管线。Kimi Code 发新版本后，可用它重新生成汉化版 `dist/main.mjs`。

## 原理

`kimi-code` 的 npm 包 `dist/main.mjs` 是**未压缩可读 bundle**（约 49 万行）。本管线：

1. **extract-strings.mjs** —— 自研扫描器提取全部字符串字面量与模板分块（保留源码级转义）
2. **build-pool.mjs / build-pool-tui.mjs / filter-pool.mjs** —— 构建 UI 候选池并过滤：
   - 风险过滤：出现在 `case` / `===` / `!==` / `['x']` / 对象键等**身份比较上下文**的字符串一律跳过（保证逻辑不变）
   - vendor 指纹过滤：fastify / pino / sonic-boom / busboy / highlight.js / ajv / DTD 公共 ID 等库内部字符串
3. **人工翻译** —— 候选导出为 TSV（`pool-*.tsv`），译文按索引写回 `zh-batch-*.json`
4. **apply-patch.mjs** —— 单遍扫描原位替换：
   - 普通字符串按外层引号补转义（`escForPlain`），模板分块原样写回
   - 模板表达式 `${fn("...")}` 内嵌的字符串同样处理
   - `residual-surgery.mjs` 处理少量需要上下文锚点的定向替换（如 `label: "Model"`）
5. **pty-capture.mjs** —— 用 node-pty 启动 TUI 按场景脚本抓屏（KIMI_CODE_HOME 隔离），配合 ansistrip.mjs 验证汉化效果、迭代补翻

## 针对新版本重新出补丁

```sh
# 0. 准备（需要 Node >= 22.19，Windows 需要 Git Bash）
npm install --global --prefix ./prefix @moonshot-ai/kimi-code@<新版>
cp ./prefix/node_modules/@moonshot-ai/kimi-code/dist/main.mjs main-original.mjs

# 1. 提取（TUI 区块起点可用特征串定位：如 "Esc cancel" 首次出现的偏移）
node extract-strings.mjs main-original.mjs strings-all.json

# 2. 重建候选池（参考 build-pool-tui.mjs 里的过滤规则），与旧 pool 求差集
#    → 新增字符串进 pool-new.tsv，人工翻译出 zh-batch-new.json

# 3. 应用补丁
node apply-patch.mjs          # 读取 pool-*.tsv + zh-batch-*.json → main-patched.mjs
node residual-surgery.mjs main-patched.mjs

# 4. 验证
node main-patched.mjs --version && node main-patched.mjs --help
node pty-capture.mjs scenario.json transcript.bin && node ansistrip.mjs transcript.bin
```

## 文件说明

| 文件 | 作用 |
|---|---|
| `extract-strings.mjs` | 字符串/模板分块提取器 |
| `build-pool.mjs` | 全文件风险上下文扫描（单遍，识别身份比较用法） |
| `build-pool-tui.mjs` | TUI 区域（文件尾段）UI 候选池 + vendor 过滤 |
| `filter-pool.mjs` | vendor/关键字表指纹过滤 |
| `apply-patch.mjs` | 单遍扫描原位替换（含模板内嵌字符串、引号转义修复） |
| `residual-surgery.mjs` | 上下文锚点定向替换 |
| `pty-capture.mjs` / `ansistrip.mjs` | TUI 抓屏与 ANSI 清理 |
| `pool-tui.tsv` / `pool-cli.tsv` / `pool-residual.tsv` | 翻译候选池（索引 \t 类型 \t JSON 原文） |
| `zh-batch-*.json` | 译文（索引 → 中文，v2.1.1 全量） |

> 注意：`main-original.mjs`（官方原版 bundle，约 21MB）不入库，按上面第 0 步从 npm 获取。
