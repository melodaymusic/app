import * as THREE from 'three';
import {RoomEnvironment} from 'three/addons/environments/RoomEnvironment.js';

// Deterministic, frame-addressable product film. Every visible element is animated
// geometry or a live canvas texture; no screenshots or pre-rendered UI footage.
const W=1920,H=1080,PI=Math.PI;
const clamp=(x,a=0,b=1)=>Math.min(b,Math.max(a,x));
const lerp=(a,b,t)=>a+(b-a)*t;
const smooth=x=>{x=clamp(x);return x*x*(3-2*x)};
const ease=x=>1-Math.pow(1-clamp(x),4);
const at=(t,a,b)=>clamp((t-a)/(b-a));
const mix=(a,b,t)=>a.map((v,i)=>lerp(v,b[i],t));
const rgb=a=>`rgb(${a.map(Math.round).join(',')})`;
const brand=['#ed255c','#9749ff','#2ed8c1','#9acb35','#fac51c'];
const renderer=new THREE.WebGLRenderer({antialias:true,alpha:false,preserveDrawingBuffer:true,powerPreference:'high-performance'});
renderer.setSize(W,H);renderer.setPixelRatio(1);renderer.outputColorSpace=THREE.SRGBColorSpace;
renderer.toneMapping=THREE.ACESFilmicToneMapping;renderer.toneMappingExposure=1.2;
document.body.prepend(renderer.domElement);
const overlay=document.querySelector('#type'),ctx=overlay.getContext('2d');
const scene=new THREE.Scene();
const camera=new THREE.PerspectiveCamera(36,W/H,.1,150);camera.position.set(0,0,15);
scene.environment=new THREE.PMREMGenerator(renderer).fromScene(new RoomEnvironment(),.04).texture;
const key=new THREE.DirectionalLight(0xffffff,5);key.position.set(-3,6,8);scene.add(key);
const rim=new THREE.DirectionalLight(0x9db9ff,3);rim.position.set(5,2,-4);scene.add(rim);
const fill=new THREE.DirectionalLight(0xffd2b0,1.5);fill.position.set(-6,-2,3);scene.add(fill);

const canvas=(w,h)=>Object.assign(document.createElement('canvas'),{width:w,height:h});
function texture(c){const t=new THREE.CanvasTexture(c);t.colorSpace=THREE.SRGBColorSpace;t.anisotropy=8;return t}
function text(c,s,x,y,size=40,weight=700,color='#fff',align='left'){
 c.font=`${weight} ${size}px Nunito`;c.fillStyle=color;c.textAlign=align;c.fillText(s,x,y);
}
function rounded(c,x,y,w,h,r,fill){c.beginPath();c.roundRect(x,y,w,h,r);c.fillStyle=fill;c.fill()}
function line(c,x1,y1,x2,y2,color,width=2){c.beginPath();c.moveTo(x1,y1);c.lineTo(x2,y2);c.strokeStyle=color;c.lineWidth=width;c.stroke()}
function mark(c,x,y,w,color){c.save();c.translate(x,y);c.scale(w/944,w/944);c.translate(-122,-1089);c.strokeStyle=color;c.lineWidth=92;c.lineCap='round';c.lineJoin='round';c.stroke(new Path2D('M168 1355 L262 1355 C318 1355 340 1310 365 1250 C395 1178 415 1135 455 1135 C495 1135 510 1180 525 1240 C545 1320 560 1428 600 1428 C640 1428 655 1340 675 1280 C695 1220 710 1194 740 1194 C772 1194 790 1235 810 1280 C830 1325 850 1355 895 1355 L1020 1355'));c.restore()}
function play(c,x,y,r,playing=true){c.fillStyle='#fff';c.beginPath();c.arc(x,y,r,0,2*PI);c.fill();c.fillStyle='#141417';if(playing){c.fillRect(x-r*.3,y-r*.4,r*.19,r*.8);c.fillRect(x+r*.1,y-r*.4,r*.19,r*.8)}else{c.beginPath();c.moveTo(x-r*.2,y-r*.4);c.lineTo(x+r*.4,y);c.lineTo(x-r*.2,y+r*.4);c.fill()}}

// The exact curve used by MelodayMark.cs, extruded as a polished 3D tube.
const curve=new THREE.CurvePath();
const v=(x,y)=>new THREE.Vector3((x-594)/130,(1280-y)/130,0);
let prev=[168,1355];
const L=(x,y)=>{curve.add(new THREE.LineCurve3(v(...prev),v(x,y)));prev=[x,y]};
const C=(a,b,c,d,e,f)=>{curve.add(new THREE.CubicBezierCurve3(v(...prev),v(a,b),v(c,d),v(e,f)));prev=[e,f]};
L(262,1355);C(318,1355,340,1310,365,1250);C(395,1178,415,1135,455,1135);C(495,1135,510,1180,525,1240);C(545,1320,560,1428,600,1428);C(640,1428,655,1340,675,1280);C(695,1220,710,1194,740,1194);C(772,1194,790,1235,810,1280);C(830,1325,850,1355,895,1355);L(1020,1355);
const logoGeo=new THREE.TubeGeometry(curve,480,.354,28,false);
const colors=[];
for(let i=0;i<logoGeo.attributes.position.count;i++){
 const x=clamp((logoGeo.attributes.position.getX(i)+3.63)/7.26)*4,j=Math.min(3,Math.floor(x));
 const col=new THREE.Color(brand[j]).lerp(new THREE.Color(brand[j+1]),x-j);colors.push(col.r,col.g,col.b);
}
logoGeo.setAttribute('color',new THREE.Float32BufferAttribute(colors,3));
const logoMat=new THREE.MeshPhysicalMaterial({vertexColors:true,metalness:.6,roughness:.23,clearcoat:1,clearcoatRoughness:.12});
const logo=new THREE.Group(),tube=new THREE.Mesh(logoGeo,logoMat);logo.add(tube);
for(const [p,col] of [[curve.getPoint(0),brand[0]],[curve.getPoint(1),brand[4]]]){const cap=new THREE.Mesh(new THREE.SphereGeometry(.354,32,24),new THREE.MeshPhysicalMaterial({color:col,metalness:.6,roughness:.23,clearcoat:1}));cap.position.copy(p);logo.add(cap)}
scene.add(logo);

const periods=[
 {name:'Dawn',bg:[45,35,80],sun:[255,110,75],x:.5,y:1.15,title:['Hushed','Indie Folk','Dawn']},
 {name:'Morning',bg:[60,165,225],sun:[255,200,0],x:.5,y:.5,title:['Bright','Feel-Good Pop','Morning']},
 {name:'Afternoon',bg:[30,90,220],sun:[255,200,0],x:.5,y:.1,title:['Sunlit','Dance Pop','Afternoon']},
 {name:'Evening',bg:[135,55,105],sun:[255,110,75],x:.5,y:.9,title:['Warm','Neo Soul','Evening']},
 {name:'Night',bg:[75,65,120],sun:[160,225,255],x:.5,y:.236,title:['Dreamy','Ambient','Night']},
];
function dayState(pos){pos=clamp(pos,0,3.999);const i=Math.floor(pos),p=smooth(pos-i),a=periods[i],b=periods[i+1];return {bg:mix(a.bg,b.bg,p),sun:mix(a.sun,b.sun,p),x:lerp(a.x,b.x,p),y:lerp(a.y,b.y,p),title:p<.5?a.title:b.title,name:p<.5?a.name:b.name}}
function drawCover(c,x,y,size,pos,t,labels=true){
 const s=dayState(pos);c.save();c.beginPath();c.roundRect(x,y,size,size,size*.028);c.clip();c.fillStyle=rgb(s.bg);c.fillRect(x,y,size,size);
 const d=size*.8;
 [1,.75,.5,.25].forEach((r,i)=>{c.beginPath();c.arc(x+s.x*size,y+s.y*size,d*r*.5*(1+.025*Math.sin(t*.8+i*.7)),0,PI*2);c.fillStyle=rgb(mix(s.bg,s.sun,[.30,.40,.65,1][i]));c.fill()});
 if(labels){s.title.forEach((word,i)=>text(c,word,x+size*.89,y+size*(.185+i*.097),size*.094,850,'#fff','right'));text(c,'Meloday',x+size*.11,y+size*.888,size*.087,750);mark(c,x+size*.075,y+size*.06,size*.13,'#fff')}
 c.restore();
}

// An independent animated front surface on a bevelled physical cover.
function roundedBox(w,h,r,depth){const s=new THREE.Shape();s.moveTo(-w/2+r,-h/2);s.lineTo(w/2-r,-h/2);s.quadraticCurveTo(w/2,-h/2,w/2,-h/2+r);s.lineTo(w/2,h/2-r);s.quadraticCurveTo(w/2,h/2,w/2-r,h/2);s.lineTo(-w/2+r,h/2);s.quadraticCurveTo(-w/2,h/2,-w/2,h/2-r);s.lineTo(-w/2,-h/2+r);s.quadraticCurveTo(-w/2,-h/2,-w/2+r,-h/2);return new THREE.ExtrudeGeometry(s,{depth,bevelEnabled:true,bevelSize:.025,bevelThickness:.025,bevelSegments:3,steps:1,curveSegments:16})}
const coverCanvas=canvas(1024,1024),coverCtx=coverCanvas.getContext('2d'),coverTex=texture(coverCanvas);
const cover=new THREE.Group();
const coverBody=new THREE.Mesh(roundedBox(4.8,4.8,.13,.12),new THREE.MeshPhysicalMaterial({color:0x24242a,metalness:.65,roughness:.24}));cover.add(coverBody);
const coverFace=new THREE.Mesh(new THREE.PlaneGeometry(4.8,4.8),new THREE.MeshBasicMaterial({map:coverTex,transparent:true}));coverFace.position.z=.151;cover.add(coverFace);scene.add(cover);

const playerCanvas=canvas(1100,150),pc=playerCanvas.getContext('2d'),playerTex=texture(playerCanvas);
const player=new THREE.Mesh(new THREE.PlaneGeometry(6.7,.914),new THREE.MeshBasicMaterial({map:playerTex,transparent:true}));scene.add(player);
function playerDraw(t,pos){pc.clearRect(0,0,1100,150);rounded(pc,1,1,1098,148,36,'#16161b');pc.strokeStyle='#ffffff20';pc.lineWidth=2;pc.stroke();drawCover(pc,24,24,102,pos,t,false);text(pc,'Meloday',151,65,28,800);text(pc,dayState(pos).name+' · Made for you',151,105,22,600,'#a8a8b4');play(pc,615,75,37,t>7.7);for(let i=0;i<27;i++){const h=(.18+.82*Math.abs(Math.sin(i*.8+t*4)*Math.cos(i*.23-t*2)))*46;rounded(pc,723+i*10,75-h/2,4,h,2,i<22?'#bba2fc':'#50505e')}}

// Two streams of actual moving 3D waveform bars, converging on the same beat.
const barsGroup=new THREE.Group();scene.add(barsGroup);
const nBars=165;
const barMeshes=[[],[]];
const barGeometry=new THREE.CapsuleGeometry(.021,1,3,6);
for(let row=0;row<2;row++)for(let i=0;i<nBars;i++){
 const u=i/(nBars-1),col=new THREE.Color(row?'#47e0cf':'#ff7f66').lerp(new THREE.Color(row?'#ac8bff':'#ffcc68'),u);
 const b=new THREE.Mesh(barGeometry,new THREE.MeshStandardMaterial({color:col,metalness:.35,roughness:.28,emissive:col,emissiveIntensity:.24}));barsGroup.add(b);barMeshes[row].push(b);
}
const mixRing=new THREE.Mesh(new THREE.TorusGeometry(.38,.018,8,100),new THREE.MeshBasicMaterial({color:0xffffff}));barsGroup.add(mixRing);

// A live composited version of Home: its artwork, player, navigation selection,
// pointer, expanding cover and progress are redrawn for every frame.
const uiCanvas=canvas(1600,1000),uc=uiCanvas.getContext('2d'),uiTex=texture(uiCanvas);
const ui=new THREE.Group();
const frame=new THREE.Mesh(roundedBox(12,7.5,.17,.10),new THREE.MeshPhysicalMaterial({color:0x27272b,metalness:.8,roughness:.25}));ui.add(frame);
const face=new THREE.Mesh(new THREE.PlaneGeometry(12,7.5),new THREE.MeshBasicMaterial({map:uiTex,transparent:true}));face.position.z=.133;ui.add(face);scene.add(ui);
const artNames=['dance-pop','australian-electronic','cool-jazz','indie-pop'];
const arts=await Promise.all(artNames.map(name=>new Promise(resolve=>{const im=new Image();im.onload=()=>resolve(im);im.onerror=()=>resolve(null);im.src=`/src/MelodayMusic.App/Assets/Genres/${name}.png`})));
function drawUI(t){
 uc.clearRect(0,0,1600,1000);rounded(uc,0,0,1600,1000,22,'#111114');
 rounded(uc,12,42,225,942,22,'#1d1d21');mark(uc,30,12,23,'#b9b9c4');text(uc,'Meloday Music',65,30,18,650,'#b9b9c4');
 text(uc,'Music',35,90,24,850);const nav=['Home','Made for You','Browse','Radio','Trends'];nav.forEach((s,i)=>{if(i===0)rounded(uc,25,118+i*58,198,45,12,'#414147');text(uc,['⌂','✧','▦','◉','↗'][i],40,150+i*58,24,650,'#dbdbe1');text(uc,s,78,149+i*58,20,650,'#eeeeef')});
 text(uc,'LIBRARY',37,475,16,800,'#777783');['Pinned','Playlists','Albums','Artists','Songs'].forEach((s,i)=>text(uc,s,43,519+i*48,20,650,'#b9b9c3'));
 const scroll=80*smooth(at(t,22.7,24.7));uc.save();uc.beginPath();uc.rect(251,49,1349,800);uc.clip();uc.translate(0,-scroll);
 text(uc,'Good morning',283,106,22,650,'#a8a8b4');text(uc,'Made for You',283,164,49,850);
 const selected=smooth(at(t,22,22.7));
 for(let i=0;i<4;i++){
  const x=283+i*324,y=203-(i===0?selected*13:0),s=292+(i===0?selected*8:0);
  if(i===0)drawCover(uc,x,y,s,1,t);
  else{uc.save();uc.beginPath();uc.roundRect(x,y,s,s,14);uc.clip();if(arts[i-1])uc.drawImage(arts[i-1],x,y,s,s);else{uc.fillStyle=['#813cd9','#179494','#ca484c'][i-1];uc.fillRect(x,y,s,s)}uc.restore()}
  text(uc,['Meloday','Pop Essentials','Electronic Essentials','Jazz Essentials'][i],x,539,23,800);text(uc,['A mix for your morning','Your library, rediscovered','Get into your flow','Make a little room'][i],x,573,18,600,'#9999a5');
  if(i===0){play(uc,x+s-44,y+s-43,30,t>22.9)}
 }
 text(uc,'Live',283,662,36,850);
 for(let i=0;i<4;i++){const x=283+i*324;const g=uc.createLinearGradient(x,690,x+290,960);g.addColorStop(0,['#dc2356','#139e55','#2b4aca','#00a8a0'][i]);g.addColorStop(1,['#690028','#044729','#10164f','#005350'][i]);rounded(uc,x,695,292,240,14,g);uc.save();uc.beginPath();uc.roundRect(x,695,292,240,14);uc.clip();for(let r=0;r<3;r++){uc.beginPath();for(let j=0;j<100;j++){const xx=x+j*3,yy=813+Math.sin(j*.08+t*1.7+i+r*.6)*35+r*14;j?uc.lineTo(xx,yy):uc.moveTo(xx,yy)}uc.strokeStyle='#ffffff16';uc.lineWidth=22;uc.stroke()}uc.restore();mark(uc,x+20,714,32,'#fff');text(uc,'Live',x+62,737,21,850);text(uc,['Pulse','Hits','Library','Deep Cuts'][i],x+24,905,28,800)}
 uc.restore();rounded(uc,255,866,1328,116,28,'#303035');drawCover(uc,273,881,84,1,t,false);text(uc,'Meloday',376,915,23,800);text(uc,'Bright Feel-Good Pop Morning',376,946,18,600,'#b9b9c3');play(uc,891,917,33,t>22.9);text(uc,'‹',805,929,40);text(uc,'›',952,929,40);rounded(uc,1032,914,245,5,3,'#5d5d66');rounded(uc,1032,914,35+(t-21)*12,5,3,'#fafaff');
 // Cursor follows a bezier-like arc, clicks Play, then disappears naturally.
 const p=smooth(at(t,21.6,22.7)),cx=lerp(1230,535,p),cy=lerp(650,470,p),alpha=1-smooth(at(t,23.1,23.4));
 uc.save();uc.globalAlpha=alpha;uc.translate(cx,cy);uc.rotate(-.18);uc.beginPath();uc.moveTo(0,0);uc.lineTo(0,30);uc.lineTo(8,22);uc.lineTo(14,36);uc.lineTo(20,33);uc.lineTo(13,19);uc.lineTo(25,19);uc.closePath();uc.fillStyle='#fff';uc.shadowColor='#000';uc.shadowBlur=4;uc.fill();uc.strokeStyle='#111';uc.lineWidth=2;uc.stroke();uc.restore();
 const click=Math.sin(PI*at(t,22.7,23.2));if(click>0){uc.beginPath();uc.arc(535,470,20+45*at(t,22.7,23.2),0,2*PI);uc.strokeStyle=`rgba(255,255,255,${click*.65})`;uc.lineWidth=3;uc.stroke()}
}

function type(s,x,y,size,alpha=1,align='center',weight=850,color='#f5f5f7'){
 ctx.save();ctx.globalAlpha=clamp(alpha);text(ctx,s,x,y,size,weight,color,align);ctx.restore();
}
function reveal(s,x,y,size,t,start,opts={}){const p=ease(at(t,start,start+.85));type(s,x,y+(1-p)*34,size,p,opts.align||'center',opts.weight||850,opts.color||'#f5f5f7')}
function stateReset(){logo.visible=cover.visible=player.visible=barsGroup.visible=ui.visible=false;camera.position.set(0,0,15);camera.lookAt(0,0,0);key.intensity=5;renderer.toneMappingExposure=1.2;ctx.clearRect(0,0,W,H)}

window.renderFrame=async function(t){
 stateReset();
 const lightScene=t>=5&&t<14||t>=21&&t<26;
 const bg=lightScene?'#efeff2':'#050507';scene.background=new THREE.Color(bg);
 if(t<5){
  logo.visible=true;const p=ease(at(t,0,4.3));logo.scale.setScalar(lerp(2.65,1,p));logo.position.set(lerp(1.3,0,p),lerp(-.6,.25,p),0);logo.rotation.set(lerp(.42,-.08,p),lerp(-.85,.04,p),lerp(-.16,0,p));
  // A single moving light passes over the curved surface.
  key.position.set(lerp(-8,6,smooth(at(t,0,4))),5,8);key.intensity=lerp(.15,5,ease(at(t,0,.9)));
  reveal('Your music.',960,838,93,t,2.0);reveal('A new rhythm.',960,929,65,t,2.65,{color:'#898994',weight:650});
  if(t>4.45){ctx.fillStyle=`rgba(239,239,242,${smooth(at(t,4.45,5))})`;ctx.fillRect(0,0,W,H)}
 }else if(t<14){
  const u=t-5,pos=lerp(1,3.999,smooth(at(t,7.5,12.4)));
  cover.visible=player.visible=true;coverCtx.clearRect(0,0,1024,1024);drawCover(coverCtx,0,0,1024,pos,t);coverTex.needsUpdate=true;
  const p=ease(at(u,0,1.5));cover.position.set(lerp(-.4,-3.05,p),lerp(-1.3,.15,p),0);cover.rotation.set(lerp(.32,-.04,p),lerp(-.8,.16,p)+Math.sin(u*.55)*.025,lerp(-.12,-.025,p));cover.scale.setScalar(lerp(1.55,1,p));
  playerDraw(t,pos);playerTex.needsUpdate=true;player.position.set(-3.05,-3.0,0);player.rotation.set(-.04,.16,-.025);player.scale.setScalar(.83*ease(at(u,.5,1.7)));
  reveal('A mix for',1150,402,104,t,5.7,{align:'left',color:'#17171c'});reveal('your kind',1150,516,104,t,5.9,{align:'left',color:'#17171c'});reveal('of day.',1150,630,104,t,6.1,{align:'left',color:'#17171c'});
  reveal('MELODAY',1156,287,23,t,5.6,{align:'left',color:'#787882',weight:800});
  const names=['Morning','Afternoon','Evening','Night'];names.forEach((name,i)=>{const active=clamp(1-Math.abs(pos-(i+1))),opacity=ease(at(t,6.8+i*.12,7.5+i*.12));type(name,1157,735+i*43,27,opacity, 'left',active>.5?850:600,active>.5?'#1c1c23':'#a2a2ad');if(active>.5){ctx.save();ctx.globalAlpha=opacity;ctx.beginPath();ctx.arc(1129,725+i*43,4,0,2*PI);ctx.fillStyle='#8355da';ctx.fill();ctx.restore()}});
  // Cover itself moves into the lens to motivate the next scene.
  const exit=smooth(at(t,13.0,14));cover.position.z=exit*10;cover.position.x=lerp(-3.05,0,exit);cover.rotation.y*=1-exit;
  if(t>13.65){ctx.fillStyle=`rgba(5,5,7,${smooth(at(t,13.65,14))})`;ctx.fillRect(0,0,W,H)}
 }else if(t<21){
  const u=t-14,join=smooth(at(u,1.6,5.6));barsGroup.visible=true;barsGroup.rotation.set(.12,lerp(-.36,.07,smooth(at(u,0,6))),-.055);barsGroup.position.y=-.65;
  for(let row=0;row<2;row++)barMeshes[row].forEach((b,i)=>{
   const x=(i/(nBars-1)-.5)*15,phase=i*.61-u*(row?7.2:7.0),beat=Math.pow(Math.max(0,Math.sin(i*.145-u*3.14)),8);
   const env=.17+.36*Math.abs(Math.sin(phase))+.43*beat;
   const crossing=smooth((x+7.5)/15)*join;
   b.position.set(x,(row?-.9:.9)*(1-crossing)+Math.sin(x*.5+u)*.065,Math.sin(x*.45+u*.3)*.17+(row?.05:0));b.scale.y=env*(.6+.4*Math.sin(PI*(i+1)/(nBars+1)));b.material.emissiveIntensity=.12+beat*.5;
  });
  mixRing.position.set(lerp(-6.7,6.8,at(u,.7,6)),0,.32);mixRing.scale.setScalar(1+.12*Math.sin(u*6.28));
  reveal('One song.',960,253,105,t,14.3);reveal('Into the next.',960,367,105,t,14.8,{color:'#9e94ba'});
  reveal('AutoMix',960,899,32,t,15.2,{color:'#d7d7df',weight:750});reveal('Feel the flow.',960,951,27,t,16.2,{color:'#787882',weight:600});
  const exit=smooth(at(u,6.35,7));if(exit){ctx.fillStyle=`rgba(239,239,242,${exit})`;ctx.fillRect(0,0,W,H)}
 }else if(t<26){
  ui.visible=true;drawUI(t);uiTex.needsUpdate=true;const p=ease(at(t,21,22.5));ui.rotation.set(lerp(.18,.03,p),lerp(-.22,-.035,p),lerp(-.065,-.012,p));ui.position.set(lerp(.8,0,p),lerp(-2.1,-.85,p),lerp(1,0,p));ui.scale.setScalar(.9+.035*smooth(at(t,22.5,25.4)));
  reveal('Your Plex library. Rediscovered.',960,180,73,t,21.15,{color:'#17171c'});
  const q=smooth(at(t,25.3,26));if(q){ctx.fillStyle=`rgba(5,5,7,${q})`;ctx.fillRect(0,0,W,H)}
 }else{
  const u=t-26;logo.visible=true;logo.scale.setScalar(.77);logo.position.set(0,.88,0);logo.rotation.set(lerp(.2,0,ease(at(u,0,2))),lerp(.65,0,ease(at(u,0,2))),0);key.position.set(-3+u,6,8);
  reveal('Find your Meloday.',960,745,100,t,26.55);reveal('Meloday Music',960,837,39,t,27.2,{weight:750,color:'#b7b7c2'});reveal('Free & open source. For Windows.',960,919,27,t,27.8,{weight:600,color:'#73737f'});reveal('Made for your Plex library.',960,963,24,t,28.1,{weight:600,color:'#73737f'});
  if(t>31.3){ctx.fillStyle=`rgba(5,5,7,${smooth(at(t,31.3,32))})`;ctx.fillRect(0,0,W,H)}
 }
 renderer.render(scene,camera);
};
await document.fonts.load('850 100px Nunito');await document.fonts.ready;
await window.renderFrame(0);window.filmReady=true;
