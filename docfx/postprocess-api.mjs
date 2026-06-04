// Post-process the Markdown that DocFxMarkdownGen writes into website/docs/api.
//
// Why: the generator emits inline cross-reference links WITHOUT a file
// extension, e.g. `[ActionException](../CodeFactory/ActionException)`. Docusaurus
// only resolves relative links to the correct doc permalink when they end in
// `.md` (it matches the target file in the doc graph). Without `.md`, the link
// is treated as a raw URL and resolved with trailing-slash math — which drops
// the `api` segment on namespace summary pages (served at /docs/api/<NS> while
// their source file lives at <NS>/<NS>.md). Appending `.md` makes Docusaurus
// map every link to the real page, immune to that routing quirk and to the
// backtick-named generic-type files.
//
// Run after `dfmg`, before `npm run build`.

import { readdir, readFile, writeFile } from 'node:fs/promises';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';

const apiDir = join(fileURLToPath(new URL('.', import.meta.url)), '..', 'website', 'docs', 'api');

// Match a Markdown link target that is a relative path (starts with ./ or ../),
// capturing the path and an optional #anchor. We append `.md` to the path part
// when it doesn't already have a markdown extension.
const linkRe = /\]\((\.\.?\/[^)#]+?)(#[^)]*)?\)/g;

// A fully-qualified BCL type name: `System.*` or `Microsoft.*`.
const BCL_NAME = '(?:System|Microsoft)\\.[A-Za-z0-9_.]+';

// Generic BCL type: outer name followed by `<...>` (rendered type args) or
// `%60N` (DocFxMarkdownGen's encoded arity artifact, e.g. IReadOnlyList%601).
// The whole span is linked to the OUTER type's arity-suffixed Learn page; the
// `<...>` part stays as plain display text (inner type args are not linked).
const BCL_GENERIC = new RegExp(`(?<!\\[)\`(${BCL_NAME})(<[^\`]*>|%60\\d+)\`(?!\\]\\()`, 'g');

// Non-generic BCL type: a code span that is exactly one fully-qualified type,
// e.g. `System.String`. Excludes `<`/`%` so it never overlaps BCL_GENERIC.
const BCL_TYPE = new RegExp(`(?<!\\[)\`(${BCL_NAME})\`(?!\\]\\()`, 'g');

// Build the canonical Microsoft Learn .NET API URL for a fully-qualified type.
// Convention: lowercase the dotted name; generic arity is suffixed with -N.
// e.g. System.String -> .../api/system.string
//      System.Threading.Tasks.Task (arity 2) -> .../api/system.threading.tasks.task-2
function msLearnUrl(typeName, arity = 0) {
  const slug = typeName.toLowerCase() + (arity > 0 ? `-${arity}` : '');
  return `https://learn.microsoft.com/en-us/dotnet/api/${slug}`;
}

// Count top-level type arguments in an angle-bracket group like `<A, B<C,D>>`
// (nested commas don't count) -> the generic arity.
function genericArity(angle) {
  const inner = angle.slice(1, -1);
  let depth = 0;
  let commas = 0;
  for (const ch of inner) {
    if (ch === '<') depth++;
    else if (ch === '>') depth--;
    else if (ch === ',' && depth === 0) commas++;
  }
  return commas + 1;
}

// Link BCL type code spans to Microsoft Learn, but never inside fenced code
// blocks (declarations show C# keywords, not these spans, but be safe).
function linkBclTypes(content) {
  let inFence = false;
  return content
    .split('\n')
    .map((line) => {
      if (/^\s*```/.test(line)) {
        inFence = !inFence;
        return line;
      }
      if (inFence) return line;
      // Generics first (they contain `<`/`%`, which BCL_TYPE excludes).
      let out = line.replace(BCL_GENERIC, (_m, outer, suffix) => {
        const arity = suffix.startsWith('<') ? genericArity(suffix) : Number(suffix.slice(3));
        // Keep rendered type args as display text; drop the %60N artifact.
        const display = suffix.startsWith('<') ? `${outer}${suffix}` : outer;
        return `[\`${display}\`](${msLearnUrl(outer, arity)})`;
      });
      out = out.replace(BCL_TYPE, (_m, type) => `[\`${type}\`](${msLearnUrl(type)})`);
      return out;
    })
    .join('\n');
}

async function* walk(dir) {
  for (const entry of await readdir(dir, { withFileTypes: true })) {
    const full = join(dir, entry.name);
    if (entry.isDirectory()) yield* walk(full);
    else if (entry.name.endsWith('.md')) yield full;
  }
}

let files = 0;
let rewrites = 0;
let dropped = 0;
let bclLinks = 0;

for await (const file of walk(apiDir)) {
  const original = await readFile(file, 'utf8');
  let updated = original.replace(linkRe, (match, path, anchor = '') => {
    if (/\.mdx?$/i.test(path)) return match; // already has an extension
    rewrites++;
    return `](${path}.md${anchor})`;
  });

  // Link BCL parameter/return/base types to Microsoft Learn.
  const beforeBcl = updated;
  updated = linkBclTypes(updated);
  if (updated !== beforeBcl) {
    bclLinks +=
      (beforeBcl.match(BCL_GENERIC) || []).length + (beforeBcl.match(BCL_TYPE) || []).length;
  }

  // Drop index entries with empty link text, e.g. `* [](./Foo/Foo.md)`. These are
  // emitted for namespaces that contain only sub-namespaces (no documented types),
  // so DocFxMarkdownGen never generates the target page — the link is both empty
  // and broken.
  updated = updated.replace(/^\s*[-*] \[\]\([^)]*\)\s*$\n?/gm, () => {
    dropped++;
    return '';
  });

  if (updated !== original) {
    await writeFile(file, updated);
    files++;
  }
}

console.log(
  `postprocess-api: appended .md to ${rewrites} link(s), linked ${bclLinks} BCL type(s) to Microsoft Learn, ` +
    `dropped ${dropped} empty entry(ies) across ${files} file(s).`,
);
