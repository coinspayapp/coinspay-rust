// Builds dist/CoinsPay.cs, the one file a server owner drops into oxide/plugins or carbon/plugins.
// Oxide compiles a plugin from a single file named after its class, so the glue (src/CoinsPay.cs)
// and the core (src/Core/*.cs, namespace CoinsPayRust, usings inside the namespace) are joined.
//   node bin/build.mjs
import { mkdirSync, readFileSync, readdirSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = join(dirname(fileURLToPath(import.meta.url)), '..');
const read = (path) => readFileSync(join(root, path), 'utf8').replace(/\r\n/g, '\n').replace(/^﻿/, '');

const glue = read('src/CoinsPay.cs');
const version = /\[Info\("CoinsPay", "CoinsPay", "([0-9.]+)"\)\]/.exec(glue)?.[1];
if (!version) throw new Error('src/CoinsPay.cs: no [Info] version');

const parts = [glue.trimEnd()];
for (const name of readdirSync(join(root, 'src/Core')).filter((f) => f.endsWith('.cs')).sort()) {
  const source = read(`src/Core/${name}`).trim();
  // Usings must stay inside the namespace, or the joined file would not compile.
  if (!source.startsWith('namespace CoinsPayRust')) throw new Error(`src/Core/${name} must start with "namespace CoinsPayRust"`);
  parts.push(`// ---- ${name}\n${source}`);
}

mkdirSync(join(root, 'dist'), { recursive: true });
writeFileSync(join(root, 'dist/CoinsPay.cs'), parts.join('\n\n') + '\n');
console.log(`dist/CoinsPay.cs ${version}`);
