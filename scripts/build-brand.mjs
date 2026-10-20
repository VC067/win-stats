import { createRequire } from 'node:module';
import { copyFile, mkdir, writeFile } from 'node:fs/promises';
import { dirname, resolve } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const outputDir = resolve(root, 'assets/brand');
const args = process.argv.slice(2);
const conceptArg = args.indexOf('--concept');
const concept = conceptArg >= 0 ? args[conceptArg + 1] : 'pulse';
const sharpArg = args.indexOf('--sharp-module');
const sharpPath = sharpArg >= 0 ? args[sharpArg + 1] : null;
const validConcepts = new Set(['pulse', 'bars', 'window']);

if (!validConcepts.has(concept)) throw new Error('--concept must be pulse, bars, or window');
if (sharpArg >= 0 && !sharpPath) throw new Error('--sharp-module requires a module path');

const require = createRequire(import.meta.url);
const sharp = sharpPath ? (await import(pathToFileURL(resolve(sharpPath)).href)).default : require('sharp');

const descriptions = {
  pulse: 'A white angular pulse forming a W inside a cobalt rounded square.',
  bars: 'Three white rounded vertical bars of different heights inside a cobalt rounded square.',
  window: 'A white monitor frame with a top separator and ascending bars inside a cobalt rounded square.',
};

const wrap = (name, description, contents) => `<svg xmlns="http://www.w3.org/2000/svg" width="256" height="256" viewBox="0 0 256 256"><title>${name}</title><desc>${description}</desc><rect width="256" height="256" rx="48" fill="#0A84FF"/>${contents}</svg>`;
const logos = {
  pulse: wrap('Win Stats Pulse', descriptions.pulse, '<path d="M48 137h34l18-42 30 70 24-51 18 23h36" fill="none" stroke="#FFFFFF" stroke-width="16" stroke-linecap="round" stroke-linejoin="round"/>'),
  bars: wrap('Win Stats Bars', descriptions.bars, '<rect x="56" y="120" width="32" height="72" rx="12" fill="#FFFFFF"/><rect x="104" y="64" width="32" height="128" rx="12" fill="#FFFFFF"/><rect x="152" y="96" width="32" height="96" rx="12" fill="#FFFFFF"/>'),
  window: wrap('Win Stats Monitor', descriptions.window, '<rect x="48" y="56" width="160" height="144" rx="20" fill="none" stroke="#FFFFFF" stroke-width="12"/><path d="M54 92h148" stroke="#FFFFFF" stroke-width="10"/><rect x="78" y="148" width="24" height="34" rx="8" fill="#FFFFFF"/><rect x="116" y="128" width="24" height="54" rx="8" fill="#FFFFFF"/><rect x="154" y="108" width="24" height="74" rx="8" fill="#FFFFFF"/>'),
};

await mkdir(outputDir, { recursive: true });
await mkdir(resolve(root, 'scripts'), { recursive: true });
for (const name of ['pulse', 'bars', 'window']) {
  await writeFile(resolve(outputDir, `concept-${name}.svg`), `${logos[name]}\n`, 'utf8');
}
await writeFile(resolve(outputDir, 'logo.svg'), `${logos[concept]}\n`, 'utf8');

const renderSvg = async (svg, width, height) => sharp(Buffer.from(svg)).resize(width, height).png().toBuffer();
const conceptsSvg = `<svg xmlns="http://www.w3.org/2000/svg" width="1200" height="480" viewBox="0 0 1200 480"><rect width="1200" height="480" fill="#FFFFFF"/><g fill="#29313D" font-family="Arial, sans-serif" font-size="28" text-anchor="middle"><text x="200" y="390">01 Pulse</text><text x="600" y="390">02 Bars</text><text x="1000" y="390">03 Monitor</text></g></svg>`;
const conceptLayers = await Promise.all(['pulse', 'bars', 'window'].map(async (name, index) => ({
  input: await renderSvg(logos[name], 160, 160),
  left: 120 + index * 400,
  top: 112,
})));
await sharp(Buffer.from(conceptsSvg)).composite(conceptLayers).png().toFile(resolve(outputDir, 'concepts.png'));

await renderSvg(logos[concept], 512, 512).then((png) => writeFile(resolve(outputDir, 'logo.png'), png));

const socialSvg = `<svg xmlns="http://www.w3.org/2000/svg" width="1280" height="640" viewBox="0 0 1280 640"><rect width="1280" height="640" fill="#F5F8FC"/><circle cx="1110" cy="86" r="220" fill="#E9F2FF"/><circle cx="165" cy="585" r="210" fill="#EAF3FF"/><text x="530" y="268" fill="#18212F" font-family="Arial, sans-serif" font-size="74" font-weight="700">Win Stats</text><text x="534" y="342" fill="#4C596B" font-family="Arial, sans-serif" font-size="34">Your PC, at a glance.</text><text x="534" y="420" fill="#697789" font-family="Arial, sans-serif" font-size="22">Windows 11 · CPU · RAM · GPU · Disk · Network</text></svg>`;
const socialLayers = [
  { input: await renderSvg(logos[concept], 320, 320), left: 118, top: 160 },
];
await sharp(Buffer.from(socialSvg)).composite(socialLayers).png().toFile(resolve(outputDir, 'social-preview.png'));

const iconDir = resolve(root, 'src/StatsBar/Assets');
const siteDir = resolve(root, 'website/assets/brand');
await mkdir(iconDir, { recursive: true });
await mkdir(siteDir, { recursive: true });
const sizes = [16, 24, 32, 48, 64, 128, 256];
const frames = await Promise.all(sizes.map(size => renderSvg(logos[concept], size, size)));
const directory = Buffer.alloc(6 + sizes.length * 16);
directory.writeUInt16LE(1, 2);
directory.writeUInt16LE(sizes.length, 4);
let offset = directory.length;
frames.forEach((frame, index) => {
  const entry = 6 + index * 16;
  directory[entry] = sizes[index] === 256 ? 0 : sizes[index];
  directory[entry + 1] = directory[entry];
  directory.writeUInt16LE(1, entry + 4);
  directory.writeUInt16LE(32, entry + 6);
  directory.writeUInt32LE(frame.length, entry + 8);
  directory.writeUInt32LE(offset, entry + 12);
  offset += frame.length;
});
const iconPath = resolve(iconDir, 'WinStats.ico');
await writeFile(iconPath, Buffer.concat([directory, ...frames]));
await copyFile(iconPath, resolve(siteDir, 'favicon.ico'));
for (const name of ['logo.svg', 'social-preview.png'])
  await copyFile(resolve(outputDir, name), resolve(siteDir, name));
