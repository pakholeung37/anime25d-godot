'use strict';
// Offline converter. Node built-ins only; the runtime never reads a PSD.
const fs = require('node:fs'), path = require('node:path'), zlib = require('node:zlib');
const ag = require('./vendor/anime25d/ag-psd.min.js');
const R = require('./vendor/anime25d/rigger.js');
const RT = require('./vendor/anime25d/runtime.js');
const GP = require('./vendor/anime25d/genericparts.js');
ag.initializeCanvas(undefined, (width, height) => ({width, height, data:new Uint8ClampedArray(width*height*4)}));

function crc32(buffer) {
  let crc = 0xffffffff;
  for (const byte of buffer) { crc ^= byte; for(let bit=0;bit<8;bit++) crc = (crc>>>1)^((crc&1)?0xedb88320:0); }
  return (crc^0xffffffff)>>>0;
}
function chunk(type, data) {
  const name = Buffer.from(type), size = Buffer.alloc(4), crc = Buffer.alloc(4);
  size.writeUInt32BE(data.length); crc.writeUInt32BE(crc32(Buffer.concat([name,data])));
  return Buffer.concat([size,name,data,crc]);
}
function png(img) {
  const head=Buffer.alloc(13); head.writeUInt32BE(img.width);head.writeUInt32BE(img.height,4);head[8]=8;head[9]=6;
  const stride=img.width*4, rows=Buffer.alloc((stride+1)*img.height);
  for(let y=0;y<img.height;y++) Buffer.from(img.data.buffer,img.data.byteOffset+y*stride,stride).copy(rows,y*(stride+1)+1);
  return Buffer.concat([Buffer.from([137,80,78,71,13,10,26,10]),chunk('IHDR',head),chunk('IDAT',zlib.deflateSync(rows)),chunk('IEND',Buffer.alloc(0))]);
}
function readPsd(file) {
  const bytes=fs.readFileSync(file), buffer=bytes.buffer.slice(bytes.byteOffset,bytes.byteOffset+bytes.byteLength);
  RT.validateHeader(buffer);
  const opts={useImageData:true,skipThumbnail:true,skipCompositeImageData:true};
  R.validatePsd(ag.readPsd(bytes,{...opts,skipLayerImageData:true}));
  return ag.readPsd(bytes,opts);
}
function convert(file, output) {
  const generic={eyeL:GP.get('eyeL'),eyeR:GP.get('eyeR'),mouth:GP.get('mouth')};
  const root=path.dirname(file), eyeFile=path.join(root,'eye_close.psd'), mouthFile=path.join(root,'mouth_close.psd');
  if(fs.existsSync(eyeFile)) { const split=R.splitImgLR(R.flattenPsdToImg(readPsd(eyeFile)));if(split){generic.eyeL=split.l;generic.eyeR=split.r;} }
  if(fs.existsSync(mouthFile)) generic.mouth=R.flattenPsdToImg(readPsd(mouthFile));
  const psd=readPsd(file); R.cleanPsdLayers(psd);
  const rig=R.buildRig(psd,{generic});
  fs.mkdirSync(output,{recursive:true});
  const textures=[];
  for(const [i,layer] of rig.layers.entries()) {
    const filename=String(i).padStart(2,'0')+'.png';
    fs.writeFileSync(path.join(output,filename),png(layer.img));
    // Match WebGL's premultiply-before-filtering. Godot imports straight PNG into premultiplied texture.
    fs.writeFileSync(path.join(output,filename+'.import'), '[remap]\nimporter="texture"\ntype="CompressedTexture2D"\n\n[params]\ncompress/mode=0\nmipmaps/generate=false\nprocess/fix_alpha_border=false\nprocess/premult_alpha=true\n');
    textures.push(filename); delete layer.img;
    layer.texture=i;
  }
  const manifest=require('./rig-schema.cjs').toRuntime({format:'anime25d-rig',version:1,name:path.basename(file),...rig});
  fs.writeFileSync(path.join(output,'model.rig.json'),JSON.stringify(manifest,null,2)+'\n');
  let resource='[gd_resource type="Resource" script_class="AnimeRigModel" load_steps='+ (textures.length+2)+' format=3]\n\n';
  resource+='[ext_resource type="Script" path="res://demo/SampleRig/AnimeRigModel.cs" id="1"]\n';
  textures.forEach((name,i)=>resource+='[ext_resource type="Texture2D" path="'+name+'" id="t'+i+'"]\n');
  resource+='\n[resource]\nscript = ExtResource("1")\nManifest = '+JSON.stringify(JSON.stringify(manifest))+'\n';
  resource+='Textures = Array[Texture2D](['+textures.map((_,i)=>'ExtResource("t'+i+'")').join(', ')+'])\n';
  fs.writeFileSync(path.join(output,'model.tres'),resource);
  console.log(`${file} → ${output}: ${rig.layers.length} parts, ${rig.canvas.w}×${rig.canvas.h}`);
  return manifest;
}
if(require.main===module) {
  const [file,output]=process.argv.slice(2);
  if(!file||!output) { console.error('Usage: node tools/convert.cjs input.psd output-directory');process.exitCode=1; }
  else convert(path.resolve(file),path.resolve(output));
}
module.exports={convert,png};
