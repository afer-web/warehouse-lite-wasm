import { copyFile, mkdir } from 'node:fs/promises';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const __filename = fileURLToPath(import.meta.url);
const __dirname = dirname(__filename);
const projectRoot = join(__dirname, '..');
const dist = join(projectRoot, 'node_modules', 'sql.js', 'dist');
const targetDir = join(projectRoot, 'wwwroot', 'sqlite');

await mkdir(targetDir, { recursive: true });

await copyFile(join(dist, 'sql-wasm.js'), join(targetDir, 'sql-wasm.js'));
await copyFile(join(dist, 'sql-wasm.wasm'), join(targetDir, 'sqlite-wasm.wasm'));

console.log('[MiniWms] sql.js assets copied to wwwroot/sqlite');
