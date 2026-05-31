import esbuild from 'esbuild';
import { copyFileSync, mkdirSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, resolve } from 'node:path';

const here = dirname(fileURLToPath(import.meta.url));
const outDir = resolve(here, '..', 'Assets', 'StreamingAssets', 'HUD');
mkdirSync(outDir, { recursive: true });

copyFileSync(resolve(here, 'src', 'index.html'), resolve(outDir, 'index.html'));

const watch = process.argv.includes('--watch');

const config = {
  entryPoints: [resolve(here, 'src', 'main.jsx')],
  outdir: outDir,
  bundle: true,
  format: 'iife',
  target: ['chrome120'],
  jsx: 'automatic',
  loader: { '.css': 'css' },
  sourcemap: watch ? 'inline' : false,
  minify: !watch,
  define: { 'process.env.NODE_ENV': watch ? '"development"' : '"production"' },
  entryNames: 'hud',
  logLevel: 'info',
};

if (watch) {
  const ctx = await esbuild.context(config);
  await ctx.watch();
  console.log(`[esbuild] watching → ${outDir}`);
} else {
  await esbuild.build(config);
  console.log(`[esbuild] built → ${outDir}`);
}
