'use strict';
const fs=require('node:fs'),path=require('node:path'),vm=require('node:vm');
const vendor=path.join(__dirname,'vendor/anime25d');
const source=fs.readFileSync(path.join(vendor,'app.js'),'utf8');
const html=fs.readFileSync(path.join(vendor,'index.html'),'utf8');
const defaults=vm.runInNewContext('('+source.match(/const P = (\{[\s\S]*?\});/)[1]+')');
const sliders=vm.runInNewContext('('+source.match(/const sliders = (\{[\s\S]*?\});/)[1]+')');
const presets=vm.runInNewContext('('+source.match(/const presets=(\{[\s\S]*?\});/)[1]+')');
const ranges={};
for(const [id,key] of Object.entries(sliders)){
  const tag=html.match(new RegExp('<input[^>]+id="'+id+'"[^>]*>'))[0];
  ranges[key]=['min','max'].map(a=>Number(tag.match(new RegExp(a+'="([^"]+)"'))[1]));
}
function between(start,end){const a=source.indexOf(start),b=source.indexOf(end,a);if(a<0||b<0)throw Error('Reference source boundary missing.');return source.slice(a,b);}
const functions=between('function prepareLayers(','function applyRig(')+between('function clamp(','// ---------- main loop ----------')+between('function animate(','function bindLayer(');
function seeded(seed){return ()=>{seed=(Math.imul(seed,1664525)+1013904223)>>>0;return seed/4294967296;};}
function createReference(rig,seed=1234){
  const math=Object.create(Math);math.random=seeded(seed);
  const gl=new Proxy({MAX_TEXTURE_SIZE:1,MAX_VIEWPORT_DIMS:2,NO_ERROR:0,getParameter:k=>k===1?16384:[16384,16384],getError:()=>0,createBuffer:()=>({})},{get:(obj,k)=>k in obj?obj[k]:(()=>{})});
  const scope={Math:math,gl,rig:structuredClone(rig),RT:require('./vendor/anime25d/runtime.js'),Rigger:require('./vendor/anime25d/rigger.js'),Float32Array,Uint16Array,
    mkTex:()=>({}),disposeLayers:()=>{},T:{...defaults},cur:{...defaults},parameterRanges:ranges,
    auto:{idle:true,blink:true,rand:true,talk:true,mouse:false,phys:true,cam:false,mic:false},
    A:rig.anchors,CW:rig.canvas.w,CH:rig.canvas.h,FS:rig.anchors.faceScale,NP:rig.anchors.neckPivot,BP:rig.anchors.bodyPivot,FC:{x:rig.anchors.face.cx,y:rig.anchors.face.cy},
    bounce:{x:0,v:0,dy:0},blinkT:-1,blinkVariant:1,blinkBounceStarted:false,irisBounceT:-1,nextBlink:1800,
    rnd:{ax:0,ay:0,az:0,bd:0,ex:0,ey:0},nextRnd:0,talkOn:false,talkV:0,talkTgt:0,nextTalkState:0,nextSyl:0,activePreset:null,
    camPhysScale:1,mouse:{in:false,x:0,y:0},hasEyeClose2:rig.layers.some(p=>p.fade==='eyeClose2'),time:0,frame:null};
  const a=rig.anchors;scope.CHEST={cx:a.neckPivot.cx,cy:a.neckBottom+(a.face.y1-a.face.y0)*0.60,rx:Math.max(1,(a.face.x1-a.face.x0)*0.60),ry:Math.max(1,(a.face.y1-a.face.y0)*0.45)};
  vm.createContext(scope);vm.runInContext(functions+'\nlayers=prepareLayers(rig);',scope);
  return scope;
}
function step(scope,delta){scope.delta=Math.max(0,Math.min(0.05,delta));vm.runInContext('time+=delta*1000;frame=animate(time,delta);for(const L of layers){L.alpha=L.visible?fadeAlpha(L,frame)*L.opacity:0;if(L.alpha>=0.004)deform(L,frame);}',scope);}
function snapshot(scope){
  vm.runInContext('for(const L of layers){L.alpha=L.visible?fadeAlpha(L,frame)*L.opacity:0;if(L.alpha>=0.004)deform(L,frame);}',scope);
  return {time:scope.time,blinkVariant:scope.blinkVariant,values:scope.frame,bounce:scope.bounce.dy,
    parts:scope.layers.map(l=>({name:l.name,alpha:l.alpha,positions:Array.from(l.cur)}))};
}
function casesFor(rig){
  const result=[{name:'neutral',pose:{},frames:0},{name:'turn',pose:{angleX:0.85,angleY:-0.65,angleZ:0.7,body:-0.5,eyeX:1,eyeY:-1,mouthOpen:0.8},frames:0},
    {name:'closed',pose:{eyeOpenL:0,eyeOpenR:0,mouthOpen:0,eyeCAng:0.7,eyeCY:0.2,mouthCAng:-0.4},frames:0},
    {name:'wink',pose:{eyeOpenL:0,eyeOpenR:1,eyeX:1,irisScale:1.3,mouthOpen:0.4},frames:0},
    {name:'crossfade',pose:{eyeOpenL:0.31,eyeOpenR:0.36,mouthOpen:0.35},frames:0}];
  for(const [key,limits] of Object.entries(ranges)) for(const [i,value] of limits.entries()) result.push({name:key+(i?'Max':'Min'),pose:{[key]:value},frames:0});
  for(const fps of [10,30,60,144])result.push({name:'animation'+fps,pose:{},fps,frames:fps*8,animated:true});
  result.push({name:'presetSmile',preset:'smile',pose:{},fps:60,frames:180,animated:true});
  result.push({name:'physicsOff',pose:{angleX:1},fps:60,frames:120,animated:true,physics:false});
  result.push({name:'mouse',pose:{},fps:60,frames:120,animated:true,mouse:{x:0.8,y:-0.6}});
  result.push({name:'reordered',pose:{eyeX:1,eyeY:-1,irisScale:1.3},frames:0,reverse:true});
  return result.map(c=>{
    const s=createReference(rig);Object.assign(s.T,c.pose);Object.assign(s.cur,c.pose);
    if(!c.animated)Object.assign(s.auto,{idle:false,blink:false,rand:false,talk:false,phys:false});
    if(c.physics===false)s.auto.phys=false;
    if(c.preset){Object.assign(s.T,presets[c.preset]);s.activePreset=c.preset;}
    if(c.mouse){s.auto.mouse=true;Object.assign(s.mouse,c.mouse,{in:true});}
    if(c.reverse)s.layers.reverse();
    const checkpoints=[];
    if(!c.frames){step(s,0);checkpoints.push({frame:0,...snapshot(s)});}
    for(let f=1;f<=c.frames;f++){step(s,1/c.fps);if(f%Math.max(1,Math.floor(c.fps/4))===0||f===c.frames)checkpoints.push({frame:f,...snapshot(s)});}
    return {...c,checkpoints};
  });
}
if(require.main===module){
  const root=path.resolve(__dirname,'..'),out=path.join(root,'artifacts');fs.mkdirSync(out,{recursive:true});
  fs.writeFileSync(path.join(out,'.gdignore'),'');
  const all=[];
  for(const name of ['sample-a','sample-b']){
    const rig=require('./rig-schema.cjs').toReference(JSON.parse(fs.readFileSync(path.join(root,'demo/models',name,'model.rig.json'))));
    all.push({name,rig,cases:casesFor(rig)});
    // Synthetic alternate close layers exercise the original 20% long-blink path.
    if(name==='sample-a'){
      const extended=structuredClone(rig);extended.layers.push(...rig.layers.filter(l=>l.fade==='eyeClose').map((l,i)=>({...l,name:'eye_close2_'+l.side.toLowerCase(),fade:'eyeClose2',z:rig.layers.length+i})));
      const s=createReference(extended);const checkpoints=[];
      for(let frame=1;frame<=1800;frame++){step(s,1/60);if(frame%10===0)checkpoints.push({frame,...snapshot(s)});}
      all.push({name:'long-blink',rig:extended,cases:[{name:'longBlink',pose:{},animated:true,fps:60,frames:1800,checkpoints}]});
    }
  }
  fs.writeFileSync(path.join(out,'reference.json'),JSON.stringify({defaults,ranges,models:all}));
  console.log('Generated original-JavaScript reference: '+all.reduce((n,m)=>n+m.cases.length,0)+' cases.');
}
module.exports={createReference,step,snapshot,defaults,ranges,presets,functions,seeded};
