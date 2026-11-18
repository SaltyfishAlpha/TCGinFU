// Decompile Godot 4.4 GDScript binary-token buffers (.gdc, GDSC v100) back to source.
//
// Buffer layout (after zstd):
//   u32 identifier_count, u32 constant_count, u32 token_line_count, u32 _unused, u32 token_count
//   identifiers[]  : u32 len, then len u32 chars each XOR 0xb6b6b6b6
//   constants[]    : encode_variant() stream
//   line map[]     : (u32 token_index, u32 line)   x token_line_count
//   column map[]   : (u32 token_index, u32 column) x token_line_count
//   tokens[]       : (u32 type_and_payload, u32 line) x token_count
//                    type = v & 0x7f, payload = v >> 8
const fs = require('fs');
const path = require('path');
const zlib = require('zlib');

const TOKENS = [
  'EMPTY', 'ANNOTATION', 'IDENTIFIER', 'LITERAL',
  'LESS', 'LESS_EQUAL', 'GREATER', 'GREATER_EQUAL', 'EQUAL_EQUAL', 'BANG_EQUAL',
  'AND', 'OR', 'NOT', 'AMPERSAND_AMPERSAND', 'PIPE_PIPE', 'BANG',
  'AMPERSAND', 'PIPE', 'TILDE', 'CARET', 'LESS_LESS', 'GREATER_GREATER',
  'PLUS', 'MINUS', 'STAR', 'STAR_STAR', 'SLASH', 'PERCENT',
  'EQUAL', 'PLUS_EQUAL', 'MINUS_EQUAL', 'STAR_EQUAL', 'STAR_STAR_EQUAL',
  'SLASH_EQUAL', 'PERCENT_EQUAL', 'LESS_LESS_EQUAL', 'GREATER_GREATER_EQUAL',
  'AMPERSAND_EQUAL', 'PIPE_EQUAL', 'CARET_EQUAL',
  'IF', 'ELIF', 'ELSE', 'FOR', 'WHILE', 'BREAK', 'CONTINUE', 'PASS', 'RETURN',
  'MATCH', 'WHEN', 'AS', 'ASSERT', 'AWAIT', 'BREAKPOINT', 'CLASS', 'CLASS_NAME',
  'CONST', 'ENUM', 'EXTENDS', 'FUNC', 'IN', 'IS', 'NAMESPACE', 'PRELOAD',
  'SELF', 'SIGNAL', 'STATIC', 'SUPER', 'TRAIT', 'VAR', 'VOID', 'YIELD',
  'BRACKET_OPEN', 'BRACKET_CLOSE', 'BRACE_OPEN', 'BRACE_CLOSE',
  'PARENTHESIS_OPEN', 'PARENTHESIS_CLOSE',
  'COMMA', 'SEMICOLON', 'PERIOD', 'PERIOD_PERIOD', 'COLON', 'DOLLAR',
  'FORWARD_ARROW', 'UNDERSCORE', 'NEWLINE', 'INDENT', 'DEDENT',
  'CONST_PI', 'CONST_TAU', 'CONST_INF', 'CONST_NAN',
  'VCS_CONFLICT_MARKER', 'BACKTICK', 'QUESTION_MARK', 'ERROR', 'TK_EOF',
];

const TEXT = {
  LESS: '<', LESS_EQUAL: '<=', GREATER: '>', GREATER_EQUAL: '>=',
  EQUAL_EQUAL: '==', BANG_EQUAL: '!=', AND: 'and', OR: 'or', NOT: 'not',
  AMPERSAND_AMPERSAND: '&&', PIPE_PIPE: '||', BANG: '!', AMPERSAND: '&',
  PIPE: '|', TILDE: '~', CARET: '^', LESS_LESS: '<<', GREATER_GREATER: '>>',
  PLUS: '+', MINUS: '-', STAR: '*', STAR_STAR: '**', SLASH: '/', PERCENT: '%',
  EQUAL: '=', PLUS_EQUAL: '+=', MINUS_EQUAL: '-=', STAR_EQUAL: '*=',
  STAR_STAR_EQUAL: '**=', SLASH_EQUAL: '/=', PERCENT_EQUAL: '%=',
  LESS_LESS_EQUAL: '<<=', GREATER_GREATER_EQUAL: '>>=', AMPERSAND_EQUAL: '&=',
  PIPE_EQUAL: '|=', CARET_EQUAL: '^=',
  IF: 'if', ELIF: 'elif', ELSE: 'else', FOR: 'for', WHILE: 'while',
  BREAK: 'break', CONTINUE: 'continue', PASS: 'pass', RETURN: 'return',
  MATCH: 'match', WHEN: 'when', AS: 'as', ASSERT: 'assert', AWAIT: 'await',
  BREAKPOINT: 'breakpoint', CLASS: 'class', CLASS_NAME: 'class_name',
  CONST: 'const', ENUM: 'enum', EXTENDS: 'extends', FUNC: 'func', IN: 'in',
  IS: 'is', NAMESPACE: 'namespace', PRELOAD: 'preload', SELF: 'self',
  SIGNAL: 'signal', STATIC: 'static', SUPER: 'super', TRAIT: 'trait',
  VAR: 'var', VOID: 'void', YIELD: 'yield',
  BRACKET_OPEN: '[', BRACKET_CLOSE: ']', BRACE_OPEN: '{', BRACE_CLOSE: '}',
  PARENTHESIS_OPEN: '(', PARENTHESIS_CLOSE: ')',
  COMMA: ',', SEMICOLON: ';', PERIOD: '.', PERIOD_PERIOD: '..', COLON: ':',
  DOLLAR: '$', FORWARD_ARROW: '->', UNDERSCORE: '_',
  CONST_PI: 'PI', CONST_TAU: 'TAU', CONST_INF: 'INF', CONST_NAN: 'NAN',
  BACKTICK: '`', QUESTION_MARK: '?',
};

// ---------------------------------------------------------------- variants
const VT = ['NIL', 'BOOL', 'INT', 'FLOAT', 'STRING', 'VECTOR2', 'VECTOR2I',
  'RECT2', 'RECT2I', 'VECTOR3', 'VECTOR3I', 'TRANSFORM2D', 'VECTOR4',
  'VECTOR4I', 'PLANE', 'QUATERNION', 'AABB', 'BASIS', 'TRANSFORM3D',
  'PROJECTION', 'COLOR', 'STRING_NAME', 'NODE_PATH', 'RID', 'OBJECT',
  'CALLABLE', 'SIGNAL', 'DICTIONARY', 'ARRAY', 'PACKED_BYTE_ARRAY',
  'PACKED_INT32_ARRAY', 'PACKED_INT64_ARRAY', 'PACKED_FLOAT32_ARRAY',
  'PACKED_FLOAT64_ARRAY', 'PACKED_STRING_ARRAY', 'PACKED_VECTOR2_ARRAY',
  'PACKED_VECTOR3_ARRAY', 'PACKED_COLOR_ARRAY', 'PACKED_VECTOR4_ARRAY'];

const FLAG_64 = 1 << 16;

class Reader {
  constructor(buf, pos = 0) { this.b = buf; this.p = pos; }
  u32() { const v = this.b.readUInt32LE(this.p); this.p += 4; return v; }
  i32() { const v = this.b.readInt32LE(this.p); this.p += 4; return v; }
  i64() { const v = this.b.readBigInt64LE(this.p); this.p += 8; return v; }
  f32() { const v = this.b.readFloatLE(this.p); this.p += 4; return v; }
  f64() { const v = this.b.readDoubleLE(this.p); this.p += 8; return v; }
  str() {
    const len = this.u32();
    const s = this.b.subarray(this.p, this.p + len).toString('utf8');
    this.p += len + ((4 - (len % 4)) % 4);
    return s;
  }
}

function decodeVariant(r) {
  const head = r.u32();
  const type = head & 0xffff;
  const f64 = (head & FLAG_64) !== 0;
  switch (VT[type]) {
    case 'NIL': return { t: 'nil' };
    case 'BOOL': return { t: 'bool', v: r.u32() !== 0 };
    case 'INT': return { t: 'int', v: f64 ? r.i64() : r.i32() };
    case 'FLOAT': return { t: 'float', v: f64 ? r.f64() : r.f32() };
    case 'STRING': return { t: 'str', v: r.str() };
    case 'STRING_NAME': return { t: 'strname', v: r.str() };
    case 'NODE_PATH': return { t: 'nodepath', v: r.str() };
    case 'VECTOR2': return { t: 'vec2', v: [r.f32(), r.f32()] };
    case 'VECTOR2I': return { t: 'vec2i', v: [r.i32(), r.i32()] };
    case 'VECTOR3': return { t: 'vec3', v: [r.f32(), r.f32(), r.f32()] };
    case 'COLOR': return { t: 'color', v: [r.f32(), r.f32(), r.f32(), r.f32()] };
    case 'ARRAY': {
      const n = r.u32() & 0x7fffffff;
      const out = [];
      for (let i = 0; i < n; i++) out.push(decodeVariant(r));
      return { t: 'array', v: out };
    }
    case 'DICTIONARY': {
      const n = r.u32() & 0x7fffffff;
      const out = [];
      for (let i = 0; i < n; i++) {
        const k = decodeVariant(r);
        const v = decodeVariant(r);
        out.push([k, v]);
      }
      return { t: 'dict', v: out };
    }
    case 'PACKED_STRING_ARRAY': {
      const n = r.u32(); const out = [];
      for (let i = 0; i < n; i++) out.push(r.str());
      return { t: 'pstrarray', v: out };
    }
    case 'PACKED_INT32_ARRAY': {
      const n = r.u32(); const out = [];
      for (let i = 0; i < n; i++) out.push(r.i32());
      return { t: 'pintarray', v: out };
    }
    default:
      throw new Error('unhandled variant type ' + type + ' (' + VT[type] + ') at ' + r.p);
  }
}

function q(s) {
  return '"' + s.replace(/\\/g, '\\\\').replace(/"/g, '\\"')
    .replace(/\n/g, '\\n').replace(/\t/g, '\\t').replace(/\r/g, '\\r') + '"';
}

function litText(c) {
  switch (c.t) {
    case 'nil': return 'null';
    case 'bool': return c.v ? 'true' : 'false';
    case 'int': return String(c.v);
    case 'float': {
      const n = c.v;
      return Number.isInteger(n) ? n.toFixed(1) : String(n);
    }
    case 'str': return q(c.v);
    case 'strname': return '&' + q(c.v);
    case 'nodepath': return '^' + q(c.v);
    case 'vec2': return `Vector2(${c.v.join(', ')})`;
    case 'vec2i': return `Vector2i(${c.v.join(', ')})`;
    case 'vec3': return `Vector3(${c.v.join(', ')})`;
    case 'color': return `Color(${c.v.join(', ')})`;
    case 'array': return '[' + c.v.map(litText).join(', ') + ']';
    case 'dict': return '{' + c.v.map(([k, v]) => litText(k) + ': ' + litText(v)).join(', ') + '}';
    case 'pstrarray': return 'PackedStringArray(' + c.v.map(q).join(', ') + ')';
    case 'pintarray': return 'PackedInt32Array(' + c.v.join(', ') + ')';
    default: return '<?' + c.t + '>';
  }
}

// ------------------------------------------------------------------ parse
function parse(file) {
  let buf = fs.readFileSync(file);
  if (buf.subarray(0, 4).toString('latin1') !== 'GDSC') throw new Error('not GDSC');
  const decompressed = buf.readUInt32LE(8);
  buf = decompressed === 0 ? buf.subarray(12) : zlib.zstdDecompressSync(buf.subarray(12));

  const r = new Reader(buf);
  const identCount = r.u32();
  const constCount = r.u32();
  const lineCount = r.u32();
  r.u32(); // unused
  const tokenCount = r.u32();

  const identifiers = [];
  for (let i = 0; i < identCount; i++) {
    const len = r.u32();
    let s = '';
    for (let j = 0; j < len; j++) s += String.fromCodePoint(r.u32() ^ 0xb6b6b6b6);
    identifiers.push(s);
  }

  const constants = [];
  for (let i = 0; i < constCount; i++) constants.push(decodeVariant(r));

  const lineOf = new Map();
  for (let i = 0; i < lineCount; i++) { const t = r.u32(); lineOf.set(t, r.u32()); }
  const colOf = new Map();
  for (let i = 0; i < lineCount; i++) { const t = r.u32(); colOf.set(t, r.u32()); }

  const tokens = [];
  for (let i = 0; i < tokenCount; i++) {
    const v = r.u32();
    const line = r.u32();
    const type = TOKENS[v & 0x7f];
    const payload = v >> 8;
    tokens.push({ type, payload, line, index: i });
  }
  return { identifiers, constants, tokens, colOf, lineOf };
}

// -------------------------------------------------------------- rendering
const NO_SPACE_BEFORE = new Set(['COMMA', 'COLON', 'SEMICOLON', 'PERIOD',
  'PARENTHESIS_CLOSE', 'BRACKET_CLOSE', 'PARENTHESIS_OPEN', 'BRACKET_OPEN']);
const NO_SPACE_AFTER = new Set(['PERIOD', 'PARENTHESIS_OPEN', 'BRACKET_OPEN',
  'DOLLAR']);
// `(` / `[` only hug the previous token when they are a call / subscript.
const CALLABLE_PREV = new Set(['IDENTIFIER', 'PARENTHESIS_CLOSE',
  'BRACKET_CLOSE', 'SELF', 'SUPER', 'LITERAL', 'BRACE_CLOSE']);

function tokenText(t, ctx) {
  switch (t.type) {
    case 'IDENTIFIER': return ctx.identifiers[t.payload];
    case 'ANNOTATION': {
      const n = ctx.identifiers[t.payload];
      return n.startsWith('@') ? n : '@' + n;
    }
    case 'LITERAL': return litText(ctx.constants[t.payload]);
    case 'ERROR': return '<ERROR ' + litText(ctx.constants[t.payload]) + '>';
    case 'TK_EOF': case 'EMPTY': case 'NEWLINE': case 'INDENT': case 'DEDENT':
      return null;
    default:
      if (TEXT[t.type] === undefined) throw new Error('no text for ' + t.type);
      return TEXT[t.type];
  }
}

function render(ctx) {
  const lines = [];
  let cur = null;
  let curLine = -1;

  for (const t of ctx.tokens) {
    const text = tokenText(t, ctx);
    if (text === null) continue;

    if (t.line !== curLine) {
      if (cur) lines.push(cur);
      curLine = t.line;
      const col = ctx.colOf.get(t.index);
      const depth = col === undefined ? 0 : Math.floor((col - 1) / 4);
      cur = { line: t.line, indent: depth, parts: [] };
    }

    const parts = cur.parts;
    const prev = parts.length ? parts[parts.length - 1] : null;
    let sep = ' ';
    if (!prev) sep = '';
    else if (NO_SPACE_AFTER.has(prev.type)) sep = '';
    else if (t.type === 'PERIOD' || t.type === 'COMMA' || t.type === 'COLON'
      || t.type === 'SEMICOLON' || t.type === 'PARENTHESIS_CLOSE'
      || t.type === 'BRACKET_CLOSE') sep = '';
    else if ((t.type === 'PARENTHESIS_OPEN' || t.type === 'BRACKET_OPEN')
      && CALLABLE_PREV.has(prev.type)) sep = '';
    parts.push({ type: t.type, text, sep });
  }
  if (cur) lines.push(cur);

  // Re-insert blank lines so the output keeps the original line numbering feel.
  const out = [];
  let expected = 1;
  for (const l of lines) {
    while (expected < l.line) { out.push(''); expected++; }
    out.push('\t'.repeat(l.indent) + l.parts.map(p => p.sep + p.text).join('').trimStart());
    expected = l.line + 1;
  }
  return out.join('\n') + '\n';
}

// ------------------------------------------------------------------- main
const inDir = process.argv[2];
const outDir = process.argv[3];
fs.mkdirSync(outDir, { recursive: true });
for (const f of fs.readdirSync(inDir).filter(x => x.endsWith('.gdc'))) {
  const ctx = parse(path.join(inDir, f));
  const src = render(ctx);
  const dest = path.join(outDir, f.replace(/\.gdc$/, '.gd'));
  fs.writeFileSync(dest, src, 'utf8');
  console.log(f, '->', path.basename(dest),
    `(${ctx.tokens.length} tokens, ${ctx.identifiers.length} idents, ${ctx.constants.length} consts)`);
}
