'use strict';
const fs=require('node:fs'),path=require('node:path'),http=require('node:http');
const {chromium}=require('playwright');const {PNG}=require('pngjs');
const {createReference}=require('./reference.cjs');
const root=path.resolve(__dirname,'..');
const source=fs.readFileSync(path.join(__dirname,'vendor/anime25d/app.js'),'utf8');
// The actual shaders, texture setup and draw-order/stencil pass come from the pinned source.
const setup=source.slice(source.indexOf('function sh('),source.indexOf('// ---------- model state'));
const mkTex=source.slice(source.indexOf('function mkTex('),source.indexOf('function disposeLayers('));
const binding=source.slice(source.indexOf('function bindLayer('),source.indexOf('function render('));
const render=source.slice(source.indexOf('function render('),source.indexOf('function tick('));
const pageHtml=`<!doctype html><html><body style="margin:0;background:transparent"><canvas id="cv"></canvas><script>
const cv=document.getElementById('cv');
const gl=cv.getContext('webgl',{alpha:true,stencil:true,antialias:true,premultipliedAlpha:true,preserveDrawingBuffer:true});
if(!gl)throw Error('WebGL unavailable');
${setup}\n${mkTex}\n${binding}\n${render}
let CW=1280,CH=1280,layers=[];
function deform(){} // Positions below were computed by the original CPU functions in the oracle.
function fadeAlpha(L){return L.referenceAlpha/L.opacity;}
window.capture=async(payload)=>{
 for(const L of layers){gl.deleteTexture(L.tex);gl.deleteBuffer(L.vboPos);gl.deleteBuffer(L.vboUV);gl.deleteBuffer(L.ibo);}
 layers=[];CW=payload.w;CH=payload.h;cv.width=CW;cv.height=CH;
 for(const L of payload.layers){
   const image=new Image();image.src=L.url;await image.decode();
   const off=document.createElement('canvas');off.width=image.width;off.height=image.height;const ctx=off.getContext('2d');ctx.drawImage(image,0,0);
   L.tex=mkTex(ctx.getImageData(0,0,off.width,off.height));L.cur=new Float32Array(L.positions);
   L.vboPos=gl.createBuffer();gl.bindBuffer(gl.ARRAY_BUFFER,L.vboPos);gl.bufferData(gl.ARRAY_BUFFER,L.cur,gl.DYNAMIC_DRAW);
   L.vboUV=gl.createBuffer();gl.bindBuffer(gl.ARRAY_BUFFER,L.vboUV);gl.bufferData(gl.ARRAY_BUFFER,new Float32Array(L.uv),gl.STATIC_DRAW);
   L.ibo=gl.createBuffer();gl.bindBuffer(gl.ELEMENT_ARRAY_BUFFER,L.ibo);gl.bufferData(gl.ELEMENT_ARRAY_BUFFER,new Uint16Array(L.indices),gl.STATIC_DRAW);L.nIdx=L.indices.length;
   layers.push(L);
 }
 render({});gl.finish();if(gl.getError()!==gl.NO_ERROR)throw Error('WebGL draw failed');
 return cv.toDataURL('image/png');
};</script></body></html>`;
function compare(reference,actual){
  const a=PNG.sync.read(reference),b=PNG.sync.read(actual);
  if(a.width!==b.width||a.height!==b.height)throw Error('Image size differs.');
  let sum=0,pixels=0,bad=0,max=0;const diff=new PNG({width:a.width,height:a.height});
  // Compare premultiplied RGBA so undefined RGB in transparent pixels does not dominate.
  for(let i=0;i<a.data.length;i+=4){
    if(a.data[i+3]===0&&b.data[i+3]===0)continue;
    pixels++;let peak=0;
    for(let c=0;c<4;c++){
      const av=c===3?a.data[i+c]:a.data[i+c]*a.data[i+3]/255;
      // Godot readback is premultiplied; reference canvas.toDataURL is straight alpha.
      const bv=b.data[i+c];const d=Math.abs(av-bv);sum+=d;peak=Math.max(peak,d);
      diff.data[i+c]=c===3?255:Math.min(255,d*8);
    }
    max=Math.max(max,peak);if(peak>12)bad++;
  }
  return {stats:{meanChannelError:sum/(pixels*4),fractionPixelsOver12:bad/pixels,maxError:max,visiblePixels:pixels},diff:PNG.sync.write(diff)};
}
(async()=>{
  const server=http.createServer((req,res)=>{
    const url=new URL(req.url,'http://127.0.0.1');
    if(url.pathname==='/'){res.setHeader('Content-Type','text/html');return res.end(pageHtml);}
    const file=path.resolve(root,'.'+decodeURIComponent(url.pathname));
    if(!file.startsWith(root+path.sep)||!fs.existsSync(file)){res.statusCode=404;return res.end();}
    res.setHeader('Content-Type',file.endsWith('.png')?'image/png':'application/octet-stream');fs.createReadStream(file).pipe(res);
  });
  await new Promise(resolve=>server.listen(0,'127.0.0.1',resolve));
  let browser;
  try{
    browser=await chromium.launch({channel:'chrome',headless:true});
    const page=await browser.newPage({viewport:{width:1280,height:1280}});page.on('pageerror',error=>{throw error;});
    await page.goto('http://127.0.0.1:'+server.address().port);
    const data=JSON.parse(fs.readFileSync(path.join(root,'artifacts/reference.json')));
    const out=path.join(root,'artifacts/reference-images');fs.mkdirSync(out,{recursive:true});
    const results=[];
    for(const model of data.models){
      if(model.name==='long-blink')continue;
      const reference=createReference(model.rig);
      for(const test of model.cases){
        if(!['neutral','turn','closed','wink','crossfade','reordered','animation60'].includes(test.name))continue;
        const checkpoint=test.checkpoints.at(-1),byName=new Map(reference.layers.map(l=>[l.name,l]));
        const layers=checkpoint.parts.map(p=>{
          const l=byName.get(p.name),definition=model.rig.layers.find(d=>d.name===p.name);
          const {nx,ny}=require('./vendor/anime25d/runtime.js').meshSize(l.w,l.h,(l.phys?30:42)*Math.max(0.6,model.rig.canvas.w/768));
          const indices=[];for(let j=0;j<ny;j++)for(let i=0;i<nx;i++){const a=j*(nx+1)+i,b=a+1,c=a+nx+1,d=c+1;indices.push(a,b,c,b,d,c);}
          const uv=[];for(let j=0;j<=ny;j++)for(let i=0;i<=nx;i++)uv.push(i/nx,j/ny);
          return {name:l.name,bn:l.bn,side:l.side,visible:true,opacity:l.opacity,referenceAlpha:p.alpha,positions:p.positions,uv,indices,url:'/demo/models/'+model.name+'/'+String(definition.texture).padStart(2,'0')+'.png'};
        });
        const encoded=await page.evaluate(payload=>window.capture(payload),{w:model.rig.canvas.w,h:model.rig.canvas.h,layers});
        const png=Buffer.from(encoded.split(',')[1],'base64'),filename=model.name+'-'+test.name+'.png';fs.writeFileSync(path.join(out,filename),png);
        const actual=fs.readFileSync(path.join(root,'artifacts/godot',filename)),result=compare(png,actual);
        fs.writeFileSync(path.join(out,'diff-'+filename),result.diff);
        results.push({name:filename,...result.stats});console.log(filename,JSON.stringify(result.stats));
      }
    }
    fs.writeFileSync(path.join(root,'artifacts/image-comparison.json'),JSON.stringify(results,null,2));
    if(results.some(r=>r.meanChannelError>0.25||r.fractionPixelsOver12>0.0025))throw Error('Visual parity threshold exceeded; inspect difference images.');
  }finally{await browser?.close();await new Promise(resolve=>server.close(resolve));}
})().catch(error=>{console.error(error);process.exitCode=1;});
