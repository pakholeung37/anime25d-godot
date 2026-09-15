'use strict';
const fs=require('node:fs'),path=require('node:path'),os=require('node:os'),{spawnSync}=require('node:child_process');
const root=path.resolve(__dirname,'..'),target=fs.mkdtempSync(path.join(os.tmpdir(),'anime25d-consumer-'));
const godot=process.env.GODOT_BIN||'/Applications/Godot_mono.app/Contents/MacOS/Godot';
fs.cpSync(path.join(root,'tests/consumer'),target,{recursive:true});
fs.cpSync(path.join(root,'addons/anime25d'),path.join(target,'addons/anime25d'),{recursive:true});
fs.cpSync(path.join(root,'demo/models/sample-a'),path.join(target,'models/sample-a'),{recursive:true});
function run(command,args){
  const result=spawnSync(command,args,{cwd:target,encoding:'utf8',timeout:120000});
  fs.appendFileSync(path.join(root,'artifacts/consumer.log'),(result.stdout||'')+(result.stderr||''));
  if(result.status!==0||/\bERROR:/.test(result.stderr||''))throw Error((result.stdout||'')+(result.stderr||'')+String(result.error||''));
}
try{
  run('dotnet',['build','--nologo']);
  run(godot,['--headless','--editor','--path',target,'--import']);
  run(godot,['--headless','--path',target]);
  fs.writeFileSync(path.join(root,'artifacts/consumer-test.json'),JSON.stringify({status:'passed',project:target},null,2));
  console.log('Independent addon consumer passed: '+target);
}catch(error){console.error(error);process.exitCode=1;}
