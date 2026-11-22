// Rebuilds src/BlueHarvest.Sim.Tests/Content/Fixtures/packs/narrative with the Narrative Studio's own code,
// so the C# tests load exactly what the Studio writes. Run from the repo root:
//   node tools/narrative-studio/make-test-fixture.js
// It loads the Studio's script with a stand-in for the browser, enters three entries, then exports the two
// ready ones as a pack ("Export ready pack ZIP", unzipped) and the draft on its own ("Entry YAML").
'use strict';
const fs = require('fs'), path = require('path');
const here = __dirname, root = path.join(here, '..', '..');
const html = fs.readFileSync(path.join(here, 'BlueHarvest_Narrative_Studio.html'), 'utf8');
let js = html.slice(html.indexOf('<script>') + 8, html.lastIndexOf('</script>'));
const hook = 'init();\n})();';
if (!js.includes(hook)) throw Error('The Studio script no longer ends with init(); update make-test-fixture.js.');
js = js.replace(hook, 'init();globalThis.__studio={store:()=>store,normalize,issuesFor,exportDefinition,packFiles};\n})();');

// Just enough browser for the Studio to start.
const elements = {};
const element = id => elements[id] || (elements[id] = { id, value: '', innerHTML: '', textContent: '', style: {}, dataset: {}, addEventListener() {}, click() {}, remove() {} });
globalThis.document = { getElementById: element, createElement: () => element('_'), addEventListener() {}, body: { appendChild() {} } };
globalThis.localStorage = { getItem: () => null, setItem() {} };
globalThis.confirm = () => true;
new Function(js)();
const S = globalThis.__studio;

const dialogue = S.normalize({
  id: 'MEN-01-TALK-001', form: 'dialogue', status: 'ready', title: 'Ask about the eastern paddock', facility: 'MEN',
  tags: ['containment'], source: { kind: 'npc', ref: 'MEN-01' },
  dialogue: { start_node: 'start', nodes: [
    { id: 'start', speaker: 'Dr. Sera Okonkwo', text: 'The animals refuse to enter that part of the enclosure.', ending: false,
      choices: [
        { id: 'c1', text: 'What frightened them?', to: 'explanation' },
        { id: 'c2', text: "I'll investigate on my own.", to: '__end__', reveals_facts: ['FACT-MEN-001'] }] },
    { id: 'explanation', speaker: 'Dr. Sera Okonkwo', text: "I don't yet know.\nThey may have seen something we haven't.", ending: true, choices: [] }] },
});
const terminal = S.normalize({
  id: 'NAR-TERM-0001', form: 'document', presentation: 'terminal', status: 'ready', title: 'Enclosure monitor',
  source: { kind: 'device', ref: 'DEV-ADM-001' }, // written bare; the Studio exports it as core:DEV-ADM-001
  document: { author: 'Enclosure Security', recipient: 'Maintenance', subject: 'EVENT REPORT / 16:22', severity: 'warning',
    body: 'WARNING: "CAMERA" CLOCK\n\nFrame timestamps preserved.' },
});
const draft = S.normalize({
  id: 'NAR-TXT-0001', form: 'document', presentation: 'note', status: 'draft', title: 'Shift change', facility: 'ADM', tags: ['draft'],
  document: { author: 'Night shift', body: 'Left the keys where we agreed. Not writing down where.' },
});
S.store().entries = [dialogue, terminal, draft];
for (const e of S.store().entries) {
  const errors = S.issuesFor(e).filter(i => i.level === 'error');
  if (errors.length) throw Error(`${e.id}: ${errors.map(i => i.text).join('; ')}`);
}

const files = S.packFiles();
files.push(['narrative/documents/nar-txt-0001.yaml', S.exportDefinition(draft)]);
const out = path.join(root, 'src', 'BlueHarvest.Sim.Tests', 'Content', 'Fixtures', 'packs', 'narrative');
fs.rmSync(out, { recursive: true, force: true });
for (const [name, text] of files) {
  const file = path.join(out, name);
  fs.mkdirSync(path.dirname(file), { recursive: true });
  fs.writeFileSync(file, text);
}
console.log(`Wrote ${files.length} files to ${path.relative(root, out)}:`);
for (const [name] of files) console.log('  ' + name);
