'use strict';
// Offline compatibility boundary; the runtime consumes explicit semantic roles.
const roleNames = {
    face: 'Face', neck: 'Neck', irides: 'Iris', eyewhite: 'EyeWhite', eye_close: 'ClosedEye',
    eye_close2: 'AlternateClosedEye', eyebrow: 'Eyebrow', mouth_open: 'OpenMouth', mouth_close: 'ClosedMouth',
    'front hair': 'Fringe', topwear: 'UpperClothing', handwear: 'ArmClothing'
};
const fades = { eyeOpen: 'OpenEye', eyeClose: 'ClosedEye', eyeClose2: 'AlternateClosedEye', mouthOpen: 'OpenMouth', mouthClose: 'ClosedMouth' };
function toRuntime(manifest) {
    if (manifest.version === 2) return structuredClone(manifest);
    if (manifest.version !== 1) throw Error('Unsupported rig version.');
    const result = structuredClone(manifest);
    result.version = 2;
    for (const layer of result.layers) {
        let name = layer.name.replace(/_(l|r)$/, '');
        if (name !== 'eye_close2') name = name.replace(/_\d+$/, '');
        layer.role = roleNames[name] || 'Generic';
        layer.side = layer.side === 'L' ? 'Left' : layer.side === 'R' ? 'Right' : 'None';
        layer.fade = fades[layer.fade] || 'None';
        layer.physicsMesh = layer.phys != null;
        delete layer.phys;
    }
    return result;
}
function toReference(manifest) {
    const result = structuredClone(manifest);
    if (result.version === 1) return result;
    if (result.version !== 2) throw Error('Unsupported rig version.');
    result.version = 1;
    for (const layer of result.layers) {
        layer.side = { Left: 'L', Right: 'R' }[layer.side] || null;
        layer.fade = Object.keys(fades).find(key => fades[key] === layer.fade) || null;
        // The pinned reference only tests this tag for presence; hair topology comes from strands.
        layer.phys = layer.physicsMesh ? 'hair' : null;
        delete layer.role;
        delete layer.physicsMesh;
    }
    return result;
}
module.exports = { toRuntime, toReference };
