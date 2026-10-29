/* Local deterministic renderer. Starts a workspace-only HTTP server and uses
   bundled Playwright to feed rendered frames directly into ffmpeg. */
const fs=require('fs'),path=require('path'),http=require('http'),{spawn}=require('child_process');
const root=path.resolve(__dirname,'../../..');
const {chromium}=require(path.join(process.env.USERPROFILE,'.cache/codex-runtimes/codex-primary-runtime/dependencies/node/node_modules/playwright'));
const out=path.join(root,'artifacts/promo/film');fs.mkdirSync(out,{recursive:true});
const mime={'.html':'text/html','.js':'text/javascript','.png':'image/png','.ttf':'font/ttf','.jpg':'image/jpeg'};
const server=http.createServer((req,res)=>{let name=path.resolve(root,'.'+decodeURIComponent(req.url.split('?')[0]));if(!name.startsWith(root+path.sep)){res.writeHead(403);res.end();return}fs.readFile(name,(err,data)=>{if(err){res.writeHead(404);res.end();return}res.setHeader('Content-Type',mime[path.extname(name)]||'application/octet-stream');res.end(data)})});
(async()=>{
 await new Promise(r=>server.listen(0,'127.0.0.1',r));
 const browser=await chromium.launch({headless:true,executablePath:'C:/Program Files/Google/Chrome/Application/chrome.exe',args:['--use-angle=d3d11','--disable-web-security','--allow-file-access-from-files']});
 const page=await browser.newPage({viewport:{width:1920,height:1080},deviceScaleFactor:1});
 page.on('pageerror',e=>console.error('PAGE:',e));
 await page.goto(`http://127.0.0.1:${server.address().port}/tools/promo/film/index.html`);
 await page.waitForFunction(()=>window.filmReady,{timeout:60000});
 if(process.argv.includes('--stills')){
  const requested=process.argv.slice(process.argv.indexOf('--stills')+1).map(Number);
  for(const t of requested.length?requested:[1,3.5,6.5,8.5,10.8,12.4,15.5,18.5,22.5,24.2,28.8]){await page.evaluate(t=>window.renderFrame(t),t);await page.screenshot({path:path.join(out,`frame-${t}.png`)});console.log('still',t)}
 }else{
  const fps=60,total=32*fps;
  const proc=spawn('ffmpeg',['-hide_banner','-loglevel','warning','-y','-f','image2pipe','-vcodec','mjpeg','-framerate',String(fps),'-i','pipe:0','-i',path.join(out,'soundtrack.wav'),'-map','0:v','-map','1:a','-c:v','libx264','-preset','medium','-crf','17','-pix_fmt','yuv420p','-vf','scale=out_color_matrix=bt709:out_range=tv','-color_primaries','bt709','-color_trc','bt709','-colorspace','bt709','-af','volume=-3.75dB','-c:a','aac','-b:a','256k','-movflags','+faststart','-t','32',path.join(out,'MelodayMusic-Product-Film.mp4')],{stdio:['pipe','inherit','inherit']});
  let failed=false;proc.on('exit',c=>{if(c)failed=true});proc.stdin.on('error',e=>{failed=true;console.error(e.message)});
  for(let i=0;i<total;i++){
   if(failed)throw Error('Encoder failed');
   await page.evaluate(t=>window.renderFrame(t),i/fps);
   const data=await page.screenshot({type:'jpeg',quality:96});
   if(!proc.stdin.write(data))await new Promise(r=>proc.stdin.once('drain',r));
   if(i%60===0)console.log(`Rendered ${i}/${total} (${(i/fps).toFixed(0)}s)`);
  }
  proc.stdin.end();await new Promise((resolve,reject)=>proc.on('close',c=>c?reject(Error('ffmpeg '+c)):resolve()));
 }
 await browser.close();server.close();
})().catch(e=>{console.error(e);server.close();process.exit(1)});
