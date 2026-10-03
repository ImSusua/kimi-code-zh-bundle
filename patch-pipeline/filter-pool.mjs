// 从 pool.tsv 再剔除明显的 vendor/库内部字符串，得到待翻译清单
import fs from 'node:fs';
const rows = fs.readFileSync('pool.tsv', 'utf8').split('\n').filter(Boolean).map(l => {
  const [i, bt, rawJson] = l.split('\t');
  return { i: +i, bt: bt === 'T', raw: JSON.parse(rawJson) };
});

// JS/常见语言关键字停用词（全由这些词组成的多词小写字符串 → 关键字表，非 UI）
const KW = new Set(['true','false','null','nil','NULL','nullptr','undefined','class','struct','union','enum','interface','trait','final','public','private','protected','static','return','throw','case','switch','catch','for','while','if','else','elif','endif','ifdef','ifndef','include','import','define','undef','warning','error','line','pragma','_Pragma','get','set','args','call','new','do','then','end','fn','let','var','const','in','is','and','or','not','use','strict','asm','extends','implements','infix','infixl','infixr','pre','code','Bool','_Complex','_Imaginary','True','False']);
const isKeywordList = s => {
  if (!/^[A-Za-z_]+( [A-Za-z_]+)+$/.test(s)) return false;
  const ws = s.split(' ');
  return ws.every(w => KW.has(w));
};

const VENDOR_FPS = [
  /^-\/\//i, /^-\//i, /^\/\//, /dtd html/i, /dtd xhtml/i, /dtd w3/i, /dtd netscape/i,
  /^(text|application|image|audio|video|multipart|message|font)\//i,
  /^(ipaddr:|fastify|sonic boom|SonicBoom|ajv\b|schema with id|Unexpected , in|Comments must be separated|Tags and anchors|Tabs are not allowed|Map keys must be unique|Implicit keys|Set items must all|Unresolved alias|Unresolved tag|Anchor cannot|Invalid block scalar|Expected a mapping for this tag|Expected a sequence for this tag)/,
  /^must (be|match|NOT|have)/, /^must match a schema/,
  /^(Bad Request|Bad Gateway|Not Found|Internal Server Error|Unauthorized|Forbidden|Service Unavailable|Not Implemented|Gateway Timeout|Too Many Requests|Not Modified|Moved Permanently|See Other|Temporary Redirect|Permanent Redirect|Multiple Choices|No Content|Created|Accepted|Continue|Switching Protocols|Upgrade Required|Use Proxy|Expectation Failed|Unsupported Media Type|Length Required|Precondition Failed|Payload Too Large|URI Too Long|Misdirected Request|Range Not Satisfiable|Expectation Failed|Precondition Required|Network Authentication Required|Non-Authoritative Information|Reset Content|Partial Content|HTTP\/)/,
  /^(Invalid (max|ttl) value|Illegal (constructor|arguments|callback|len)|incoming request|request completed|ws upgrade rejected|response destroyed before completion|response will send|sleep: ms|flush cb|sonic boom|the worker has exited|default level|breaking|break visit|skip children|invalid HTTP|HTTP tunnel|pageToken|search service|search worker|another process holds|the search index|management connection|Local:    |Unable to read the local server token|server shutting down|before_id and after_id|before_turn and after_turn|fastqueue|Plugin did not start|Warning (name|code|message|opts)|ReadableStream is locked|next\(\) called multiple times|This context has no|Context is not finalized|No active router|Absolute URL for :path|Invalid absolute URL|Missing host header|Unsupported scheme|Invalid host header|Client connection prematurely closed|The user aborted a request|unknown error$)/,
  /^(getConsoleProcessList|Object contains forbidden prototype|invalid agent id|agent_id must be|invalid sha256|zip is too large|zip has too many|zip uncompressed|zip entry escapes|failed to (read|write) zip|failed to write the native binary|invalid semver|Version should be|Path should be|The path could not|The first character of a path|Optional Parameter needs|Method should be|Wildcard must be|Error on Stream found|The regex '|response will send)/,
  /^(Copyright|Licensed under|Permission is hereby|THE SOFTWARE IS PROVIDED|Redistribution and use)/,
  /^(get set|true false|false null|else if|while if|class interface|import include|return throw|enum class|final class|infix |pre code|Plain text$)/,
  /^(Public Domain|W3C|IETF|Netscape|Microsoft|O'Reilly|SoftQuad|Spyglass|Sun Microsystems|WebTechs|Silmaril|AdvaSoft|Metrius)/i,
];
const isVendor = s => VENDOR_FPS.some(re => re.test(s));

const keep = [];
const dropped = [];
for (const r of rows) {
  if (isVendor(r.raw) || isKeywordList(r.raw)) { dropped.push(r); continue; }
  keep.push(r);
}
console.error('keep:', keep.length, 'dropped:', dropped.length);
fs.writeFileSync('pool2.tsv', keep.map(r => `${r.i}\t${r.bt ? 'T' : 'P'}\t${JSON.stringify(r.raw)}`).join('\n'));
fs.writeFileSync('dropped.json', JSON.stringify(dropped));
