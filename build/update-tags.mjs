// Refresh the embedded community tags. Optional argument: a saved /api/v1/dxdata response.
import fs from "node:fs/promises";

const source = "https://miruku.dxrating.net/api/v1/dxdata";
const catalog = process.argv[2]
  ? JSON.parse(await fs.readFile(process.argv[2], "utf8"))
  : await (async () => {
    const response = await fetch(source, { headers: { "User-Agent": "MaiPlannerMod", Accept: "application/json" } });
    if (!response.ok) throw new Error("Catalog HTTP " + response.status);
    return response.json();
  })();
const groupNames = { "配置": "PatternsTag", "难度": "DifficultyTag", "评价": "EvaluationTag" };
const groups = new Map(catalog.tagGroups.map(g => [g.id, groupNames[g.localized_name?.["zh-Hans"]]]));
const tags = catalog.tags.map(t => ({ name: t.localized_name?.["zh-Hans"], group: groups.get(t.group_id) }));
if (tags.some(t => !t.name || !t.group)) throw new Error("Unknown tag or group");
const tagIndex = new Map(catalog.tags.map((t, index) => [t.id, index]));
const diffIndex = { basic: 0, advanced: 1, expert: 2, master: 3, remaster: 4 };
const sheets = new Map();
for (const song of catalog.songs) {
  for (const sheet of song.sheets) {
    // Use explicit game IDs only. Never match STD/DX or missing IDs by title.
    if (Number.isInteger(sheet.internalId) && sheet.internalId > 0 && diffIndex[sheet.difficulty] != null) {
      const key = sheet.internalId + ":" + diffIndex[sheet.difficulty];
      const entry = { key, type: sheet.type, difficulty: sheet.difficulty };
      if (sheets.has(sheet.id) && sheets.get(sheet.id).key !== key) throw new Error("Ambiguous sheet ID");
      sheets.set(sheet.id, entry);
    }
  }
}
const charts = {};
let skipped = 0;
for (const row of catalog.tagSongs) {
  const sheet = sheets.get(row.sheet_id);
  const index = tagIndex.get(row.tag_id);
  if (!sheet || sheet.type !== row.sheet_type || sheet.difficulty !== row.sheet_difficulty) { skipped++; continue; }
  if (index == null) throw new Error("Unknown tag ID");
  const list = charts[sheet.key] ||= [];
  if (!list.includes(index)) list.push(index);
}
if (Object.keys(charts).length < 1000) throw new Error("Tag catalog unexpectedly small");
const doc = { source, updatedAt: catalog.updatedAt, tags, charts };
await fs.writeFile(new URL("../web/community-tags.json", import.meta.url), JSON.stringify(doc) + "\n");
console.log(`${Object.keys(charts).length} tagged charts, ${tags.length} tags; ${skipped} assignments without exact game IDs omitted`);
