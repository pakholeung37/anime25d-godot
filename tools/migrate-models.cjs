'use strict';
const fs = require('node:fs'), path = require('node:path');
const { toRuntime } = require('./rig-schema.cjs');
for (const directory of process.argv.slice(2)) {
    const json = path.join(directory, 'model.rig.json');
    const resource = path.join(directory, 'model.tres');
    const manifest = toRuntime(JSON.parse(fs.readFileSync(json, 'utf8')));
    const text = fs.readFileSync(resource, 'utf8');
    if (!/^Manifest = .*$/m.test(text)) throw Error('Resource has no manifest: ' + resource);
    fs.writeFileSync(json, JSON.stringify(manifest, null, 2) + '\n');
    fs.writeFileSync(resource, text.replace(/^Manifest = .*$/m, () => 'Manifest = ' + JSON.stringify(JSON.stringify(manifest))));
    console.log('Migrated semantic rig: ' + directory);
}
