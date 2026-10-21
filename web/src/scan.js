(function(){
const $=id=>document.getElementById(id);
const IN_CLAUDE=!!(window.claude&&window.claude.use);
// Intégré dans le programme Windows Scanalyse (WebView2) : pont vers le banc d'essai
const DESKTOP=!!(window.chrome&&window.chrome.webview);
if(DESKTOP)document.documentElement.classList.add("desktop");
const esc=s=>String(s).replace(/[&<>"']/g,c=>({"&":"&amp;","<":"&lt;",">":"&gt;",'"':"&quot;","'":"&#39;"}[c]));

/* ================= Onglets ================= */
const tabs={create:$("tabCreate"),scan:$("tabScan")};
function setMode(m,save){
  const scan=m==="scan";
  if(!scan&&CAM.stream)camStop(false);
  $("view-create").hidden=scan; $("view-scan").hidden=!scan;
  tabs.create.setAttribute("aria-selected",String(!scan)); tabs.scan.setAttribute("aria-selected",String(scan));
  $("headDesc").textContent=scan
    ?"Lis un QR code ou un code-barres depuis une image, une capture d'écran ou un collage, et obtiens son contenu détaillé et ses caractéristiques techniques."
    :"Contenu, couleurs, formes des modules et des repères, logo central. L'aperçu se met à jour en direct et vérifie que le code reste lisible.";
  if(save){try{localStorage.setItem("atelier-mode",m);}catch(e){}}
  try{history.replaceState(null,"",scan?"#analyser":"#creer");}catch(e){}
  if(scan&&!src) loadSample(true);
  if(DESKTOP){try{window.chrome.webview.postMessage(JSON.stringify({type:"mode",mode:scan?"scan":"create"}));}catch(e){}}
}
tabs.create.addEventListener("click",()=>setMode("create",true));
tabs.scan.addEventListener("click",()=>setMode("scan",true));
window.atelierSetMode=setMode;
window.atelierEngine=()=>engine();
window.atelierParseGs1=t=>parseGS1Elements(t);
// Appelé par le programme Windows : image venant du presse-papiers ou de l'outil Capture d'écran de Windows
window.atelierLoadImageData=function(dataUrl,label){
  fetch(dataUrl).then(r=>r.blob()).then(b=>{setMode("scan",false);loadBlob(b,label||"Image");}).catch(()=>showMessage("Image illisible","L'image reçue n'a pas pu être ouverte."));
};

/* ================= Moteurs de décodage ================= */
let enginePromise=null, engineName="";
function engine(){
  if(enginePromise) return enginePromise;
  enginePromise=(async()=>{
    if(window.ZXingWASM){
      try{
        const b64=($("zxwasm")||{}).textContent||"";
        const opts={fireImmediately:true,overrides:{}};
        if(b64.trim()){const bin=atob(b64.trim());const u=new Uint8Array(bin.length);for(let i=0;i<bin.length;i++)u[i]=bin.charCodeAt(i);opts.overrides.wasmBinary=u.buffer;}
        await ZXingWASM.prepareZXingModule(opts);
        engineName="ZXing-C++ (WebAssembly)";
        return zxingRead;
      }catch(e){console.warn("ZXing indisponible",e);}
    }
    if("BarcodeDetector" in window){
      try{const fm=await BarcodeDetector.getSupportedFormats();if(fm.length){engineName="détecteur intégré au navigateur (analyse réduite)";return nativeRead;}}catch(e){}
    }
    if(window.jsQR){engineName="jsQR (QR codes uniquement, analyse réduite)";return jsqrRead;}
    throw new Error("no-engine");
  })();
  return enginePromise;
}
const ZX_OPTS={tryHarder:true,tryRotate:true,tryInvert:true,tryDownscale:true,maxNumberOfSymbols:16,textMode:"HRI"};
async function zxingRead(img,extra){
  const r=await ZXingWASM.readBarcodes(img,{...ZX_OPTS,...(extra||{})});
  return normalize(r);
}
function normalize(r){
  return r.filter(x=>x.isValid!==false).map(x=>{
    let extra={};try{extra=x.extra?JSON.parse(x.extra):{};}catch(e){}
    const p=x.position;
    return {format:x.format,text:x.text,bytes:x.bytes,hasECI:x.hasECI,symId:x.symbologyIdentifier||"",
      pts:[p.topLeft,p.topRight,p.bottomRight,p.bottomLeft],rotation:x.rotation,mirrored:x.isMirrored,inverted:x.isInverted,
      ec:extra.ECLevel||x.ecLevel||"",version:extra.Version||x.version||"",mask:extra.DataMask,uec:extra.UEC,addon:extra.EanAddOn,upce:extra.UPCE,
      readerInit:extra.ReaderInit||x.readerInit,lines:x.lineCount,seq:x.sequenceSize>0?{size:x.sequenceSize,index:x.sequenceIndex,id:x.sequenceId}:null,extra};
  });
}
const NATIVE_MAP={qr_code:"QRCode",data_matrix:"DataMatrix",aztec:"Aztec",pdf417:"PDF417",ean_13:"EAN13",ean_8:"EAN8",upc_a:"UPCA",upc_e:"UPCE",code_128:"Code128",code_39:"Code39",code_93:"Code93",codabar:"Codabar",itf:"ITF"};
async function nativeRead(img){
  const d=new BarcodeDetector();const r=await d.detect(img);
  return r.map(x=>({format:NATIVE_MAP[x.format]||x.format,text:x.rawValue,bytes:new TextEncoder().encode(x.rawValue),symId:"",
    pts:x.cornerPoints.length===4?x.cornerPoints:[{x:x.boundingBox.left,y:x.boundingBox.top},{x:x.boundingBox.right,y:x.boundingBox.top},{x:x.boundingBox.right,y:x.boundingBox.bottom},{x:x.boundingBox.left,y:x.boundingBox.bottom}],extra:{}}));
}
async function jsqrRead(img){
  const out=[];const tries=[img];
  for(const im of tries){
    const r=jsQR(im.data,im.width,im.height,{inversionAttempts:"attemptBoth"});
    if(r){const L=r.location;out.push({format:"QRCode",text:r.data,bytes:new Uint8Array(r.binaryData),symId:"",
      pts:[L.topLeftCorner,L.topRightCorner,L.bottomRightCorner,L.bottomLeftCorner],version:String(r.version),extra:{}});}
  }
  return out;
}

/* ================= Source et canevas ================= */
let srcBlob=null, srcOrigW=1, src=null, srcLabel="", lum=null, region=null, results=[], focusIdx=-1, drag=null, selecting=false;
const cv=$("scanCanvas"), stage=$("scanStage");
const MAXSRC=4096;
function setSource(drawable,w,h,label,keepBlob){
  if(CAM.stream)camStop(true);
  if(!keepBlob)srcBlob=null;
  const k=Math.min(1,MAXSRC/Math.max(w,h));
  const c=document.createElement("canvas");c.width=Math.max(1,Math.round(w*k));c.height=Math.max(1,Math.round(h*k));
  const x=c.getContext("2d",{willReadFrequently:true});x.fillStyle="#fff";x.fillRect(0,0,c.width,c.height);x.drawImage(drawable,0,0,c.width,c.height);
  src=c;srcLabel=label;region=null;focusIdx=-1;
  const d=x.getImageData(0,0,c.width,c.height).data;lum=new Uint8Array(c.width*c.height);
  for(let i=0,j=0;i<d.length;i+=4,j++)lum[j]=(d[i]*299+d[i+1]*587+d[i+2]*114)/1000;
  const dk=Math.min(1,1600/Math.max(c.width,c.height));cv.width=Math.round(c.width*dk);cv.height=Math.round(c.height*dk);
  cv.style.imageRendering=c.width<500?"pixelated":"auto";
  cv.hidden=false;$("scanEmpty").hidden=true;$("scanReset").hidden=true;
  draw();analyze();
}
function draw(){
  if(!src) return;
  const x=cv.getContext("2d"),k=cv.width/src.width;
  x.clearRect(0,0,cv.width,cv.height);x.drawImage(src,0,0,cv.width,cv.height);
  const accent=getComputedStyle(document.documentElement).getPropertyValue("--accent").trim()||"#145c43";
  const sc=cv.width/((cv.getBoundingClientRect().width)||cv.width);const lw=2*sc;
  if(region){
    x.save();x.fillStyle="rgba(10,12,25,.45)";x.beginPath();x.rect(0,0,cv.width,cv.height);x.rect(region.x*k,region.y*k,region.w*k,region.h*k);x.fill("evenodd");
    x.setLineDash([lw*3,lw*2]);x.strokeStyle="#fff";x.lineWidth=lw;x.strokeRect(region.x*k,region.y*k,region.w*k,region.h*k);x.restore();
  }
  results.forEach((r,i)=>{
    x.save();x.beginPath();r.pts.forEach((p,j)=>j?x.lineTo(p.x*k,p.y*k):x.moveTo(p.x*k,p.y*k));x.closePath();
    x.lineWidth=i===focusIdx?lw*2:lw*1.4;x.strokeStyle="#fff";x.stroke();x.lineWidth=i===focusIdx?lw*1.3:lw*.8;x.strokeStyle=accent;x.stroke();
    x.fillStyle=accent;x.globalAlpha=i===focusIdx?.22:.12;x.fill();x.globalAlpha=1;
    const R=12*sc,p=r.pts[0];const cx=Math.min(Math.max(p.x*k,R),cv.width-R),cy=Math.min(Math.max(p.y*k,R),cv.height-R);
    x.beginPath();x.arc(cx,cy,R,0,7);x.fillStyle=accent;x.fill();x.lineWidth=2*sc;x.strokeStyle="#fff";x.stroke();
    x.fillStyle="#fff";x.font=`700 ${Math.round(R*1.1)}px ui-monospace,monospace`;x.textAlign="center";x.textBaseline="middle";x.fillText(String(i+1),cx,cy+1);x.restore();
  });
  if(drag&&drag.cur){
    const a=drag.start,b=drag.cur;x.save();x.setLineDash([6*sc,4*sc]);x.lineWidth=2*sc;x.strokeStyle=accent;x.fillStyle="rgba(120,140,255,.15)";
    x.fillRect(Math.min(a.x,b.x)*k,Math.min(a.y,b.y)*k,Math.abs(b.x-a.x)*k,Math.abs(b.y-a.y)*k);x.strokeRect(Math.min(a.x,b.x)*k,Math.min(a.y,b.y)*k,Math.abs(b.x-a.x)*k,Math.abs(b.y-a.y)*k);x.restore();
  }
}
function toSrc(e){const r=cv.getBoundingClientRect();return{x:Math.max(0,Math.min(src.width,(e.clientX-r.left)/r.width*src.width)),y:Math.max(0,Math.min(src.height,(e.clientY-r.top)/r.height*src.height))};}
cv.addEventListener("pointerdown",e=>{
  if(!src) return; if(e.pointerType!=="mouse"&&!selecting) return; if(e.button>0) return;
  e.preventDefault();cv.setPointerCapture(e.pointerId);drag={start:toSrc(e),cur:null};
});
cv.addEventListener("pointermove",e=>{if(!drag) return;drag.cur=toSrc(e);draw();});
cv.addEventListener("pointerup",e=>{
  if(!drag) return;const a=drag.start,b=drag.cur||a;drag=null;
  const w=Math.abs(b.x-a.x),h=Math.abs(b.y-a.y);
  if(w>10&&h>10){region={x:Math.round(Math.min(a.x,b.x)),y:Math.round(Math.min(a.y,b.y)),w:Math.round(w),h:Math.round(h)};$("scanReset").hidden=false;analyze();}
  else{
    // clic simple : met en avant le code cliqué
    const i=results.findIndex(r=>inPoly(a,r.pts));if(i>=0){setFocus(i,true);}else draw();
  }
});
cv.addEventListener("pointercancel",()=>{drag=null;draw();});
function inPoly(p,pts){let c=false;for(let i=0,j=pts.length-1;i<pts.length;j=i++){const a=pts[i],b=pts[j];if(((a.y>p.y)!==(b.y>p.y))&&(p.x<(b.x-a.x)*(p.y-a.y)/(b.y-a.y)+a.x))c=!c;}return c;}
$("scanReset").addEventListener("click",()=>{region=null;$("scanReset").hidden=true;analyze();});
$("scanSelect").addEventListener("click",()=>{selecting=!selecting;$("scanSelect").classList.toggle("on",selecting);$("scanSelect").setAttribute("aria-pressed",String(selecting));stage.classList.toggle("selecting",selecting);
  $("scanInfo").textContent=selecting?"Trace un rectangle autour du code à analyser.":"Trace un rectangle sur l'image pour n'analyser qu'une zone.";});
function setFocus(i,scroll){focusIdx=i;draw();document.querySelectorAll(".res").forEach((el,j)=>el.classList.toggle("focus",j===i));
  if(scroll){const el=document.querySelectorAll(".res")[i];if(el)el.scrollIntoView({behavior:matchMedia("(prefers-reduced-motion: reduce)").matches?"auto":"smooth",block:"nearest"});}}

window.addEventListener("resize",()=>{if(src)draw();});
/* ================= Entrées : fichier, glisser, coller, capture ================= */
async function loadBlob(blob,label){
  try{
    let bmp;
    if(window.createImageBitmap){try{bmp=await createImageBitmap(blob);}catch(e){bmp=null;}}
    if(!bmp){bmp=await new Promise((res,rej)=>{const im=new Image();im.onload=()=>res(im);im.onerror=rej;im.src=URL.createObjectURL(blob);});}
    srcBlob=blob;srcOrigW=bmp.width||bmp.naturalWidth;
    setSource(bmp,bmp.width||bmp.naturalWidth,bmp.height||bmp.naturalHeight,label,true);
  }catch(e){showMessage("Image illisible","Ce format n'est pas pris en charge par le navigateur (par exemple HEIC). Convertis l'image en PNG ou JPG, ou fais une capture d'écran.");}
}
$("scanFile").addEventListener("change",e=>{const f=e.target.files[0];if(f)loadBlob(f,f.name);e.target.value="";});
["dragenter","dragover"].forEach(t=>stage.addEventListener(t,e=>{e.preventDefault();stage.classList.add("over");}));
["dragleave","drop"].forEach(t=>stage.addEventListener(t,e=>{e.preventDefault();stage.classList.remove("over");}));
stage.addEventListener("drop",e=>{const f=[...e.dataTransfer.files].find(f=>/^image\//.test(f.type)||/\.(png|jpe?g|gif|webp|bmp|avif|svg)$/i.test(f.name));if(f)loadBlob(f,f.name);});
document.addEventListener("paste",e=>{
  if($("view-scan").hidden) return;
  const it=[...(e.clipboardData||{}).items||[]].find(i=>i.type.startsWith("image/"));
  if(it){e.preventDefault();loadBlob(it.getAsFile(),"Image collée");}
});
if(!IN_CLAUDE&&navigator.clipboard&&navigator.clipboard.read){
  $("scanPaste").hidden=false;
  $("scanPaste").addEventListener("click",async()=>{
    try{const items=await navigator.clipboard.read();for(const it of items){const t=it.types.find(t=>t.startsWith("image/"));if(t){loadBlob(await it.getType(t),"Image collée");return;}}
      showMessage("Pas d'image dans le presse-papiers","Copie d'abord une image ou fais une capture avec Win + Maj + S.");}
    catch(e){showMessage("Accès au presse-papiers refusé","Utilise Ctrl + V directement sur la page.");}
  });
}
$("scanScreen").addEventListener("click",async()=>{
  if(!(navigator.mediaDevices&&navigator.mediaDevices.getDisplayMedia)){
    showMessage("Capture d'écran indisponible ici",IN_CLAUDE?"Dans Claude, la page n'a pas accès à l'écran. Fais Win + Maj + S, sélectionne la zone, puis Ctrl + V sur cette page.":"Ce navigateur ne permet pas la capture d'écran. Fais Win + Maj + S puis Ctrl + V.");return;}
  let stream;
  try{stream=await navigator.mediaDevices.getDisplayMedia({video:{displaySurface:"monitor",width:{ideal:7680},height:{ideal:4320},frameRate:{ideal:5}},audio:false});}
  catch(e){showMessage("Capture annulée",IN_CLAUDE?"Dans Claude, la page n'a pas accès à l'écran. Fais Win + Maj + S, sélectionne la zone, puis Ctrl + V sur cette page.":"Aucun écran n'a été partagé. Tu peux aussi faire Win + Maj + S puis Ctrl + V.");return;}
  try{
    const v=document.createElement("video");v.muted=true;v.playsInline=true;v.srcObject=stream;await v.play();
    await new Promise(r=>setTimeout(r,350));
    const c=document.createElement("canvas");c.width=v.videoWidth;c.height=v.videoHeight;c.getContext("2d").drawImage(v,0,0);
    stream.getTracks().forEach(t=>t.stop());
    setSource(c,c.width,c.height,"Capture d'écran");
    vOpen("Capture prête : zoome avec la molette, puis trace un rectangle autour du code.");
  }catch(e){stream.getTracks().forEach(t=>t.stop());showMessage("Capture impossible","Réessaie, ou utilise Win + Maj + S puis Ctrl + V.");}
});
function loadSample(silent){
  const c=document.querySelector("#qr canvas");
  if(c&&c.width){setSource(c,c.width,c.height,"Code créé : QR personnalisé");}
  else if(!silent) showMessage("Aucun QR dans l'atelier","Crée d'abord un QR code dans l'onglet Créer.");
}
$("scanSample").addEventListener("click",()=>loadSample(false));
$("qrAnalyse").addEventListener("click",()=>{setMode("scan",false);loadSample(false);});
if(DESKTOP)$("scanEmpty").innerHTML="<b>Dépose une image ici</b>ou utilise Importer, Coller ou Capturer dans la barre du haut.";

/* ================= Visionneuse plein écran (zoom + sélection) ================= */
const V={el:$("viewer"),cv:$("vCanvas"),area:$("vArea"),z:1,ox:0,oy:0,pan:false,ptrs:new Map(),act:null,space:false};
function vOpen(hint){
  if(!src) return;
  V.el.hidden=false;document.body.style.overflow="hidden";
  $("vHint").textContent=hint||"Trace un rectangle autour du code à analyser.";
  vResize();vFit();
  setTimeout(()=>$("vClose").focus(),0);
}
function vClose(){V.el.hidden=true;document.body.style.overflow="";V.ptrs.clear();V.act=null;$("scanZoom").focus();}
function vResize(){const r=V.area.getBoundingClientRect(),d=window.devicePixelRatio||1;V.cv.width=Math.max(1,Math.round(r.width*d));V.cv.height=Math.max(1,Math.round(r.height*d));V.cv.style.width=r.width+"px";V.cv.style.height=r.height+"px";vDraw();}
function vFit(){const w=V.cv.width,h=V.cv.height;V.z=Math.min(w/src.width,h/src.height)*.96;V.ox=(w-src.width*V.z)/2;V.oy=(h-src.height*V.z)/2;vDraw();}
function vZoomAt(f,cx,cy){const nz=Math.min(24,Math.max(.03,V.z*f));const k=nz/V.z;V.ox=cx-(cx-V.ox)*k;V.oy=cy-(cy-V.oy)*k;V.z=nz;vDraw();}
function vToSrc(e){const r=V.cv.getBoundingClientRect(),d=V.cv.width/r.width;return{x:((e.clientX-r.left)*d-V.ox)/V.z,y:((e.clientY-r.top)*d-V.oy)/V.z};}
function vLocal(e){const r=V.cv.getBoundingClientRect(),d=V.cv.width/r.width;return{x:(e.clientX-r.left)*d,y:(e.clientY-r.top)*d};}
function vDraw(){
  if(V.el.hidden||!src) return;
  const x=V.cv.getContext("2d"),d=window.devicePixelRatio||1;
  x.setTransform(1,0,0,1,0,0);x.fillStyle=getComputedStyle(document.body).getPropertyValue("--bg")||"#111";x.fillRect(0,0,V.cv.width,V.cv.height);
  x.setTransform(V.z,0,0,V.z,V.ox,V.oy);x.imageSmoothingEnabled=V.z<2;x.imageSmoothingQuality="high";x.drawImage(src,0,0);
  const accent=getComputedStyle(document.documentElement).getPropertyValue("--accent").trim()||"#145c43";const lw=2*d/V.z;
  results.forEach((r,i)=>{x.beginPath();r.pts.forEach((p,j)=>j?x.lineTo(p.x,p.y):x.moveTo(p.x,p.y));x.closePath();x.lineWidth=lw*2;x.strokeStyle="#fff";x.stroke();x.lineWidth=lw;x.strokeStyle=accent;x.stroke();
    const R=12*d/V.z,p=r.pts[0];x.beginPath();x.arc(p.x,p.y,R,0,7);x.fillStyle=accent;x.fill();x.fillStyle="#fff";x.font=`700 ${R*1.1}px ui-monospace,monospace`;x.textAlign="center";x.textBaseline="middle";x.fillText(String(i+1),p.x,p.y+R*.08);});
  if(V.act&&V.act.type==="sel"&&V.act.cur){const a=V.act.start,b=V.act.cur;x.save();x.setLineDash([6*d/V.z,4*d/V.z]);x.lineWidth=lw;x.strokeStyle=accent;x.fillStyle="rgba(120,140,255,.18)";
    x.fillRect(Math.min(a.x,b.x),Math.min(a.y,b.y),Math.abs(b.x-a.x),Math.abs(b.y-a.y));x.strokeRect(Math.min(a.x,b.x),Math.min(a.y,b.y),Math.abs(b.x-a.x),Math.abs(b.y-a.y));x.restore();}
  $("vZoomV").textContent=Math.round(V.z/d*100)+" %";
}
V.cv.addEventListener("wheel",e=>{e.preventDefault();vZoomAt(Math.pow(1.0012,-e.deltaY),...Object.values(vLocal(e)));},{passive:false});
V.cv.addEventListener("pointerdown",e=>{
  V.cv.setPointerCapture(e.pointerId);V.ptrs.set(e.pointerId,vLocal(e));
  if(V.ptrs.size===2){const [a,b]=[...V.ptrs.values()];V.act={type:"pinch",dist:Math.hypot(a.x-b.x,a.y-b.y),mid:{x:(a.x+b.x)/2,y:(a.y+b.y)/2}};vDraw();return;}
  const panning=V.pan||V.space||e.button===1||e.button===2;
  V.act=panning?{type:"pan",last:vLocal(e)}:{type:"sel",start:vToSrc(e),cur:null};
  V.cv.classList.toggle("panning",panning);
});
V.cv.addEventListener("pointermove",e=>{
  if(!V.ptrs.has(e.pointerId)) return;V.ptrs.set(e.pointerId,vLocal(e));
  if(!V.act) return;
  if(V.act.type==="pinch"&&V.ptrs.size===2){const [a,b]=[...V.ptrs.values()];const dist=Math.hypot(a.x-b.x,a.y-b.y),mid={x:(a.x+b.x)/2,y:(a.y+b.y)/2};
    V.ox+=mid.x-V.act.mid.x;V.oy+=mid.y-V.act.mid.y;vZoomAt(dist/V.act.dist,mid.x,mid.y);V.act.dist=dist;V.act.mid=mid;return;}
  if(V.act.type==="pan"){const p=vLocal(e);V.ox+=p.x-V.act.last.x;V.oy+=p.y-V.act.last.y;V.act.last=p;vDraw();return;}
  if(V.act.type==="sel"){V.act.cur=vToSrc(e);vDraw();}
});
function vUp(e){
  V.ptrs.delete(e.pointerId);V.cv.classList.remove("panning");
  if(!V.act) return;const act=V.act;
  if(act.type==="pinch"){if(V.ptrs.size===0)V.act=null;return;}
  V.act=null;
  if(act.type==="sel"&&act.cur){
    const cl=(v,mx)=>Math.max(0,Math.min(mx,v));
    const x0=cl(Math.min(act.start.x,act.cur.x),src.width),y0=cl(Math.min(act.start.y,act.cur.y),src.height),x1=cl(Math.max(act.start.x,act.cur.x),src.width),y1=cl(Math.max(act.start.y,act.cur.y),src.height);
    if(x1-x0>6&&y1-y0>6){region={x:Math.round(x0),y:Math.round(y0),w:Math.round(x1-x0),h:Math.round(y1-y0)};$("scanReset").hidden=false;vClose();analyze();return;}
  }
  vDraw();
}
V.cv.addEventListener("pointerup",vUp);V.cv.addEventListener("pointercancel",vUp);
V.cv.addEventListener("contextmenu",e=>e.preventDefault());
$("vZoomIn").addEventListener("click",()=>vZoomAt(1.4,V.cv.width/2,V.cv.height/2));
$("vZoomOut").addEventListener("click",()=>vZoomAt(1/1.4,V.cv.width/2,V.cv.height/2));
$("vFit").addEventListener("click",vFit);
$("v100").addEventListener("click",()=>{const d=window.devicePixelRatio||1;vZoomAt(d/V.z,V.cv.width/2,V.cv.height/2);});
$("vPan").addEventListener("click",()=>{V.pan=!V.pan;$("vPan").setAttribute("aria-pressed",String(V.pan));$("vPan").classList.toggle("on",V.pan);V.cv.classList.toggle("pan-mode",V.pan);});
$("vAll").addEventListener("click",()=>{region=null;$("scanReset").hidden=true;vClose();analyze({deep:true});});
$("vClose").addEventListener("click",vClose);
window.addEventListener("resize",()=>{if(!V.el.hidden)vResize();});
document.addEventListener("keydown",e=>{
  if(V.el.hidden) return;
  if(e.key==="Escape"){e.preventDefault();vClose();}
  else if(e.key===" "){if(!V.space){V.space=true;V.cv.classList.add("pan-mode");}e.preventDefault();}
  else if(e.key==="+"||e.key==="="){vZoomAt(1.4,V.cv.width/2,V.cv.height/2);}
  else if(e.key==="-"){vZoomAt(1/1.4,V.cv.width/2,V.cv.height/2);}
  else if(e.key==="0"){vFit();}
});
document.addEventListener("keyup",e=>{if(e.key===" "){V.space=false;if(!V.pan)V.cv.classList.remove("pan-mode");}});
$("scanZoom").addEventListener("click",()=>vOpen());
cv.addEventListener("dblclick",()=>vOpen());

/* ================= Recherche approfondie par tuiles ================= */
async function tileScan(read,id,onProgress){
  const T=900,S=600,W=src.width,H=src.height,out=[];
  const xs=[],ys=[];for(let x=0;;x+=S){xs.push(Math.min(x,Math.max(0,W-T)));if(x+T>=W)break;}for(let y=0;;y+=S){ys.push(Math.min(y,Math.max(0,H-T)));if(y+T>=H)break;}
  const tiles=[];ys.forEach(y=>xs.forEach(x=>tiles.push({x,y,w:Math.min(T,W-x),h:Math.min(T,H-y)})));
  let n=0;
  for(const t of tiles){
    for(const k of [2,1]){
      const pad=12;const c=document.createElement("canvas");c.width=Math.round(t.w*k)+pad*2;c.height=Math.round(t.h*k)+pad*2;
      const x=c.getContext("2d",{willReadFrequently:true});x.fillStyle=edgeColor(t);x.fillRect(0,0,c.width,c.height);x.imageSmoothingEnabled=true;x.imageSmoothingQuality="high";
      x.drawImage(src,t.x,t.y,t.w,t.h,pad,pad,Math.round(t.w*k),Math.round(t.h*k));
      const img=x.getImageData(0,0,c.width,c.height);
      let f=[];try{f=await read(img,{tryDownscale:false});}catch(e){}
      if(id!==runId) return null;
      f.forEach(o=>{o.pts=o.pts.map(p=>({x:t.x+(p.x-pad)/k,y:t.y+(p.y-pad)/k}));out.push(o);});
      if(f.length) break;
      // code repéré mais illisible : on relance la cascade complète sur sa zone
      if(window.ZXingWASM&&engineName.startsWith("ZXing")){
        let bad=[];try{bad=(await ZXingWASM.readBarcodes(img,{...ZX_OPTS,tryDownscale:false,returnErrors:true})).filter(z=>!z.isValid&&z.position);}catch(e){}
        for(const z of bad){
          const P=[z.position.topLeft,z.position.topRight,z.position.bottomRight,z.position.bottomLeft].map(p=>({x:t.x+(p.x-pad)/k,y:t.y+(p.y-pad)/k}));
          const xs=P.map(p=>p.x),ys=P.map(p=>p.y),mg=Math.max(12,(Math.max(...xs)-Math.min(...xs))*.15);
          const x0=Math.max(0,Math.floor(Math.min(...xs)-mg)),y0=Math.max(0,Math.floor(Math.min(...ys)-mg)),x1=Math.min(W,Math.ceil(Math.max(...xs)+mg)),y1=Math.min(H,Math.ceil(Math.max(...ys)+mg));
          if(x1-x0<8||y1-y0<8) continue;
          const g=await cascade({x:x0,y:y0,w:x1-x0,h:y1-y0},read,id,true);if(g===null) return null;
          g.forEach(o=>out.push(o));
        }
        if(out.length) break;
      }
    }
    onProgress(++n,tiles.length);
  }
  return out;
}
function center(o){return{x:o.pts.reduce((s,p)=>s+p.x,0)/4,y:o.pts.reduce((s,p)=>s+p.y,0)/4};}
function mergeFound(a,b){const out=[...a];b.forEach(o=>{const c=center(o);if(!out.some(q=>q.text===o.text&&q.format===o.format&&Math.hypot(center(q).x-c.x,center(q).y-c.y)<60))out.push(o);});return out;}

/* ================= Analyse ================= */
let runId=0;
async function analyze(opts){
  opts=opts||{};
  if(!src) return;
  const id=++runId;
  $("scanResults").innerHTML=`<div class="summary"><b>Analyse en cours…</b></div>`;
  const r=region||{x:0,y:0,w:src.width,h:src.height};
  let read;
  try{read=await engine();}
  catch(e){showMessage("Moteur de lecture indisponible","Le module de décodage n'a pas pu démarrer dans ce navigateur. Utilise la version autonome de l'Atelier QR dans Chrome, Edge, Opera ou Firefox.");return;}
  $("scanEngine").textContent=`Moteur : ${engineName}. Analyse 100 % locale : l'image ne quitte pas ton appareil.`;
  const t0=performance.now();
  // Cascade d'essais : fichier d'origine, puis plusieurs échelles et méthodes de seuillage
  const m=Math.max(r.w,r.h);const bg=edgeColor(r);const isZX=engineName.startsWith("ZXing");
  let found=[];
  if(isZX&&!region&&srcBlob){
    try{const f=await ZXingWASM.readBarcodes(srcBlob,ZX_OPTS);const sk=src.width/srcOrigW;
      found=(await normalize(f)).map(o=>({...o,pts:o.pts.map(p=>({x:p.x*sk,y:p.y*sk}))}));}catch(e){}
    if(id!==runId) return;
  }
  if(!found.length){const f=await cascade(r,read,id,isZX);if(f===null)return;found=f;}
  if(id!==runId) return;
  if(!region&&(opts.deep||m>1100)){
    const sum=$("scanResults").querySelector(".summary b");
    const more=await tileScan(read,id,(i,n)=>{if(sum)sum.textContent=`Recherche approfondie… ${i}/${n} zones`;});
    if(more===null) return;
    found=mergeFound(found,more);
  }
  // supprime les doublons (même texte, même position)
  results=found.filter((f,i)=>found.findIndex(g=>g.text===f.text&&g.format===f.format&&Math.hypot(g.pts[0].x-f.pts[0].x,g.pts[0].y-f.pts[0].y)<10)===i);
  results.sort((a,b)=>(a.pts[0].y-b.pts[0].y)||(a.pts[0].x-b.pts[0].x));
  for(const f of results){try{f.stagenow=await decodeStageNow(f.bytes);}catch(e){f.stagenow=null;}}
  if(id!==runId) return;
  focusIdx=-1;draw();render(performance.now()-t0,r);
}
async function cascade(r,read,id,isZX){
  const m=Math.max(r.w,r.h);const bg=edgeColor(r);let found=[];
  const scales=[m>3000?3000/m:1];
  if(m<1600)scales.push(1.5,2,3,1.25);
  if(m>500)scales.push(.75,.5);
  const bins=isZX?[{},{binarizer:"FixedThreshold"},{binarizer:"GlobalHistogram"},{tryDenoise:true}]:[{}];
  outer: for(const bz of bins){
    for(const k of scales){
      if(found.length) break outer;
      if(k*m>4200) continue;
      const pad=Math.round(m*k*.06)+6;
      const c=document.createElement("canvas");c.width=Math.round(r.w*k)+pad*2;c.height=Math.round(r.h*k)+pad*2;
      const x=c.getContext("2d",{willReadFrequently:true});
      x.fillStyle=bg;x.fillRect(0,0,c.width,c.height);x.imageSmoothingEnabled=true;x.imageSmoothingQuality="high";
      x.drawImage(src,r.x,r.y,r.w,r.h,pad,pad,Math.round(r.w*k),Math.round(r.h*k));
      let f=[];try{f=await read(x.getImageData(0,0,c.width,c.height),bz);}catch(e){console.warn(e);}
      if(id!==runId) return null;
      if(f.length){f.forEach(o=>{o.pts=o.pts.map(p=>({x:r.x+(p.x-pad)/k,y:r.y+(p.y-pad)/k}));});found=f;}
      if(!isZX) break;
    }
  }
  return found;
}
function edgeColor(r){
  let s=0,n=0;const W=src.width;const add=(x,y)=>{s+=lum[y*W+x];n++;};
  const step=Math.max(1,Math.round((r.w+r.h)/200));
  for(let xx=r.x;xx<r.x+r.w;xx+=step){add(xx,r.y);add(xx,Math.min(src.height-1,r.y+r.h-1));}
  for(let yy=r.y;yy<r.y+r.h;yy+=step){add(r.x,yy);add(Math.min(W-1,r.x+r.w-1),yy);}
  const v=Math.round(s/n);return `rgb(${v},${v},${v})`;
}

/* ================= Mesures sur l'image ================= */
function bbox(pts){const xs=pts.map(p=>p.x),ys=pts.map(p=>p.y);return{x0:Math.max(0,Math.floor(Math.min(...xs))),y0:Math.max(0,Math.floor(Math.min(...ys))),x1:Math.min(src.width-1,Math.ceil(Math.max(...xs))),y1:Math.min(src.height-1,Math.ceil(Math.max(...ys)))};}
function contrast(pts){
  const b=bbox(pts),W=src.width,h=new Uint32Array(256);let n=0;
  const step=Math.max(1,Math.round(Math.sqrt((b.x1-b.x0)*(b.y1-b.y0)/40000)));
  for(let y=b.y0;y<=b.y1;y+=step)for(let x=b.x0;x<=b.x1;x+=step){h[lum[y*W+x]]++;n++;}
  const pct=q=>{let a=0;for(let i=0;i<256;i++){a+=h[i];if(a>=n*q)return i;}return 255;};
  const lo=pct(.05),hi=pct(.95);return{lo,hi,sc:Math.round((hi-lo)/255*100)};
}
function modulesOf(r){
  const v=parseInt(r.version,10);
  if(r.format==="QRCode"||r.format==="QRCodeModel2"||r.format==="QRCodeModel1") return v?17+4*v:0;
  if(r.format==="MicroQRCode"){const m=parseInt(String(r.version).replace(/\D/g,""),10);return m?9+2*m:0;}
  if(r.format==="DataMatrix"||r.format==="RMQRCode"){const mm=String(r.version).match(/(\d+)\s*x\s*(\d+)/i);return mm?+mm[2]:0;}
  if(r.format==="Aztec"||r.format==="AztecCode"){return 0;}
  return 0;
}
function sideLen(pts){return(Math.hypot(pts[1].x-pts[0].x,pts[1].y-pts[0].y)+Math.hypot(pts[2].x-pts[3].x,pts[2].y-pts[3].y))/2;}
function quietZone(r,modPx,need,ct){
  if(!modPx||!need) return null;
  const b=bbox(r.pts),q=Math.round(modPx*need),W=src.width,H=src.height;
  if(b.x0-q<0||b.y0-q<0||b.x1+q>=W||b.y1+q>=H) return {state:"edge"};
  const bgLight=!r.inverted, mid=(ct.lo+ct.hi)/2;let ok=0,n=0;
  const step=Math.max(1,Math.round(modPx/3)),inset=Math.max(1,Math.round(modPx*.4));
  const test=(x,y)=>{const L=lum[y*W+x];n++;if(bgLight?L>mid:L<mid)ok++;};
  for(let x=b.x0-q;x<=b.x1+q;x+=step){for(let d=inset;d<=q;d+=step){test(x,b.y0-d);test(x,b.y1+d);}}
  for(let y=b.y0;y<=b.y1;y+=step){for(let d=inset;d<=q;d+=step){test(b.x0-d,y);test(b.x1+d,y);}}
  return {state:ok/n>=.97?"ok":"busy",ratio:ok/n};
}

/* ================= Référentiels ================= */
const FMT={QRCode:"QR Code",QRCodeModel1:"QR Code modèle 1",QRCodeModel2:"QR Code",MicroQRCode:"Micro QR Code",RMQRCode:"rMQR (QR rectangulaire)",DataMatrix:"Data Matrix",Aztec:"Aztec",AztecCode:"Aztec",AztecRune:"Aztec Rune",PDF417:"PDF417",CompactPDF417:"PDF417 compact",MicroPDF417:"MicroPDF417",MaxiCode:"MaxiCode",
  EAN13:"EAN-13",EAN8:"EAN-8",UPCA:"UPC-A",UPCE:"UPC-E",ISBN:"EAN-13 (ISBN)",EAN5:"EAN-5",EAN2:"EAN-2",Code128:"Code 128",Code39:"Code 39",Code39Std:"Code 39",Code39Ext:"Code 39 étendu",Code32:"Code 32 (pharma italien)",PZN:"PZN (pharma allemand)",Code93:"Code 93",Codabar:"Codabar",ITF:"ITF (2 parmi 5 entrelacé)",ITF14:"ITF-14",
  DataBar:"GS1 DataBar",DataBarOmni:"GS1 DataBar",DataBarStk:"GS1 DataBar empilé",DataBarStkOmni:"GS1 DataBar empilé omnidirectionnel",DataBarLtd:"GS1 DataBar Limited",DataBarExp:"GS1 DataBar Expanded",DataBarExpStk:"GS1 DataBar Expanded empilé",DXFilmEdge:"DX Film Edge",Telepen:"Telepen"};
const MATRIX=/QR|DataMatrix|Aztec|PDF417|MaxiCode/;
const SYMID={Q0:"QR Code modèle 1",Q1:"QR Code modèle 2",Q2:"QR Code avec ECI",Q3:"GS1 QR Code (FNC1 en 1re position)",Q4:"GS1 QR Code avec ECI",Q5:"QR Code (FNC1 en 2e position, AIM)",Q6:"QR Code AIM avec ECI",
  d1:"Data Matrix ECC 200",d2:"GS1 DataMatrix (FNC1 en 1re position)",d3:"Data Matrix (FNC1 en 2e position)",d4:"Data Matrix avec ECI",d5:"GS1 DataMatrix avec ECI",
  C0:"Code 128 standard",C1:"GS1-128 (FNC1 en 1re position)",C2:"Code 128 (FNC1 en 2e position)",C4:"Code 128 concaténé",E0:"EAN-13 / UPC-A",E1:"EAN-13 + EAN-2",E2:"EAN-13 + EAN-5",E3:"EAN-13 avec supplément",E4:"EAN-8",
  I0:"ITF sans contrôle",I1:"ITF avec chiffre de contrôle vérifié",A0:"Code 39 sans contrôle",A1:"Code 39 avec contrôle vérifié",A4:"Code 39 étendu",e0:"GS1 DataBar",L0:"PDF417",L1:"PDF417 (codage étendu)",L2:"PDF417 (codage de base)",z0:"Aztec",z1:"GS1 Aztec",z3:"Aztec avec ECI",G0:"Code 93",F0:"Codabar",U0:"MaxiCode",U1:"MaxiCode"};
const GS1SYM=/^\](Q3|Q4|d2|d5|C1|e0|z1|J1)/;
const EC_PCT={L:"≈ 7 %",M:"≈ 15 %",Q:"≈ 25 %",H:"≈ 30 %"};
const GS1PFX=[[0,19,"États-Unis / Canada"],[20,29,"usage interne (poids ou prix variable, codes magasin)"],[30,39,"États-Unis (médicaments)"],[40,49,"usage interne restreint"],[50,59,"coupons (États-Unis)"],[60,139,"États-Unis / Canada"],[200,299,"usage interne (poids ou prix variable, codes magasin)"],[300,379,"France et Monaco"],[380,380,"Bulgarie"],[383,383,"Slovénie"],[385,385,"Croatie"],[387,387,"Bosnie-Herzégovine"],[389,389,"Monténégro"],[400,440,"Allemagne"],[450,459,"Japon"],[460,469,"Russie"],[471,471,"Taïwan"],[474,474,"Estonie"],[475,475,"Lettonie"],[477,477,"Lituanie"],[479,479,"Sri Lanka"],[480,480,"Philippines"],[481,481,"Biélorussie"],[482,482,"Ukraine"],[484,484,"Moldavie"],[485,485,"Arménie"],[486,486,"Géorgie"],[487,487,"Kazakhstan"],[489,489,"Hong Kong"],[490,499,"Japon"],[500,509,"Royaume-Uni"],[520,521,"Grèce"],[528,528,"Liban"],[529,529,"Chypre"],[531,531,"Macédoine du Nord"],[535,535,"Malte"],[539,539,"Irlande"],[540,549,"Belgique et Luxembourg"],[560,560,"Portugal"],[569,569,"Islande"],[570,579,"Danemark"],[590,590,"Pologne"],[594,594,"Roumanie"],[599,599,"Hongrie"],[600,601,"Afrique du Sud"],[611,611,"Maroc"],[613,613,"Algérie"],[619,619,"Tunisie"],[622,622,"Égypte"],[628,628,"Arabie saoudite"],[629,629,"Émirats arabes unis"],[640,649,"Finlande"],[690,699,"Chine"],[700,709,"Norvège"],[729,729,"Israël"],[730,739,"Suède"],[750,750,"Mexique"],[754,755,"Canada"],[760,769,"Suisse et Liechtenstein"],[770,771,"Colombie"],[773,773,"Uruguay"],[775,775,"Pérou"],[778,779,"Argentine"],[780,780,"Chili"],[789,790,"Brésil"],[800,839,"Italie"],[840,849,"Espagne et Andorre"],[850,850,"Cuba"],[858,858,"Slovaquie"],[859,859,"Tchéquie"],[860,860,"Serbie"],[868,869,"Turquie"],[870,879,"Pays-Bas"],[880,880,"Corée du Sud"],[885,885,"Thaïlande"],[888,888,"Singapour"],[890,890,"Inde"],[893,893,"Vietnam"],[899,899,"Indonésie"],[900,919,"Autriche"],[930,939,"Australie"],[940,949,"Nouvelle-Zélande"],[950,950,"GS1 Global Office"],[955,955,"Malaisie"],[958,958,"Macao"],[960,969,"GS1 (numéros GTIN-8 abrégés)"],[977,977,"ISSN (presse, périodiques)"],[978,979,"ISBN (livres) / ISMN (partitions)"],[980,980,"bons de remboursement"],[981,984,"coupons"],[990,999,"coupons"]];
function gs1Prefix(d13){const p=parseInt(d13.slice(0,3),10);const f=GS1PFX.find(([a,b])=>p>=a&&p<=b);return f?f[2]:"préfixe non attribué";}
function checkDigit(body){let s=0;for(let i=body.length-1,w=3;i>=0;i--,w=4-w)s+=(+body[i])*w;return(10-s%10)%10;}
function validGtin(d){return /^\d{8,18}$/.test(d)&&checkDigit(d.slice(0,-1))===+d.slice(-1);}
const ISO3166={"004":"Afghanistan","008":"Albanie","012":"Algérie","032":"Argentine","036":"Australie","040":"Autriche","056":"Belgique","076":"Brésil","100":"Bulgarie","124":"Canada","152":"Chili","156":"Chine","170":"Colombie","191":"Croatie","196":"Chypre","203":"Tchéquie","208":"Danemark","233":"Estonie","246":"Finlande","250":"France","276":"Allemagne","300":"Grèce","344":"Hong Kong","348":"Hongrie","352":"Islande","356":"Inde","360":"Indonésie","372":"Irlande","376":"Israël","380":"Italie","392":"Japon","410":"Corée du Sud","428":"Lettonie","440":"Lituanie","442":"Luxembourg","458":"Malaisie","470":"Malte","484":"Mexique","504":"Maroc","528":"Pays-Bas","554":"Nouvelle-Zélande","578":"Norvège","604":"Pérou","608":"Philippines","616":"Pologne","620":"Portugal","642":"Roumanie","643":"Russie","682":"Arabie saoudite","702":"Singapour","703":"Slovaquie","705":"Slovénie","710":"Afrique du Sud","724":"Espagne","752":"Suède","756":"Suisse","764":"Thaïlande","788":"Tunisie","792":"Turquie","804":"Ukraine","784":"Émirats arabes unis","818":"Égypte","826":"Royaume-Uni","840":"États-Unis","704":"Vietnam","999":"pays non précisé"};
const AI={"00":["SSCC (n° de colis ou palette)","sscc"],"01":["GTIN (article)","gtin"],"02":["GTIN des articles contenus","gtin"],"03":["GTIN (article de base)","gtin"],
"10":["Numéro de lot"],"11":["Date de production","date"],"12":["Date d'échéance","date"],"13":["Date d'emballage","date"],"15":["Date de durabilité minimale (DDM)","date"],"16":["Date limite de vente","date"],"17":["Date d'expiration (DLC)","date"],
"20":["Variante de produit"],"21":["Numéro de série"],"22":["Version de produit (santé)"],"30":["Quantité variable","int"],"37":["Nombre d'unités contenues","int"],
"235":["Extension de série tierce"],"240":["Référence article additionnelle"],"241":["Référence client"],"242":["Variante sur commande"],"243":["Code de composant d'emballage"],"250":["Numéro de série secondaire"],"251":["Référence de l'entité source"],"253":["GDTI (document)"],"254":["Extension de GLN"],"255":["GCN (coupon)"],
"400":["N° de commande client"],"401":["GINC (n° d'envoi)"],"402":["GSIN (n° d'expédition)"],"403":["Code de routage"],"410":["Livrer à (GLN)","gln"],"411":["Facturer à (GLN)","gln"],"412":["Acheté auprès de (GLN)","gln"],"413":["Destinataire final (GLN)","gln"],"414":["Emplacement physique (GLN)","gln"],"415":["Facturant (GLN)","gln"],"416":["Lieu de production (GLN)","gln"],"417":["Partie (GLN)","gln"],
"420":["Code postal de livraison"],"421":["Code postal avec pays"],"422":["Pays d'origine","country"],"423":["Pays de transformation initiale","countries"],"424":["Pays de transformation","country"],"425":["Pays de démontage","countries"],"426":["Pays de toute la chaîne","country"],"427":["Subdivision du pays d'origine"],
"7001":["Code OTAN (NSN)"],"7002":["Classification carcasse (UN/ECE)"],"7003":["Date et heure d'expiration","datetime"],"7004":["Puissance active"],"7005":["Zone de capture"],"7006":["Date de premier gel","date"],"7007":["Date de récolte"],"7008":["Espèce (pêche)"],"7009":["Type d'engin de pêche"],"7010":["Méthode de production"],"7020":["Lot de remise à neuf"],"7021":["État fonctionnel"],"7022":["État de révision"],"7023":["GIAI de l'assemblage"],"7240":["Protocole"],
"8001":["Produit en rouleau (dimensions)"],"8002":["N° de téléphone mobile"],"8003":["GRAI (actif réutilisable)"],"8004":["GIAI (actif individuel)"],"8005":["Prix unitaire"],"8006":["ITIP (pièce d'un ensemble)"],"8007":["IBAN"],"8008":["Date et heure de production"],"8009":["Indicateur de capteur optique"],"8010":["CPID (composant)"],"8011":["N° de série CPID"],"8012":["Version logicielle"],"8013":["GMN (modèle de produit)"],"8017":["GSRN prestataire"],"8018":["GSRN bénéficiaire"],"8019":["SRIN (instance de service)"],"8020":["N° de bulletin de paiement"],"8026":["ITIP des pièces contenues"],"8030":["Signature numérique"],"8110":["Coupon (Amérique du Nord)"],"8111":["Points de fidélité"],"8112":["Coupon papier (Amérique du Nord)"],"8200":["URL de l'emballage étendu"],"90":["Information convenue entre partenaires"]};
const MEAS={"310":["Poids net","kg"],"311":["Longueur","m"],"312":["Largeur","m"],"313":["Hauteur","m"],"314":["Surface","m²"],"315":["Volume net","l"],"316":["Volume net","m³"],"320":["Poids net","lb"],"330":["Poids brut","kg"],"331":["Longueur logistique","m"],"332":["Largeur logistique","m"],"333":["Hauteur logistique","m"],"334":["Surface logistique","m²"],"335":["Volume brut","l"],"336":["Volume logistique","m³"],"337":["Masse surfacique","kg/m²"],"390":["Montant à payer","monnaie locale"],"391":["Montant à payer","devise ISO"],"392":["Prix (article à prix variable)","monnaie locale"],"393":["Prix (article à prix variable)","devise ISO"],"394":["Remise du coupon","%"],"395":["Montant par unité de mesure","monnaie locale"]};
const FIXED={"00":18,"01":14,"02":14,"03":14,"04":16,"11":6,"12":6,"13":6,"14":6,"15":6,"16":6,"17":6,"18":6,"19":6,"20":2,"31":6,"32":6,"33":6,"34":6,"35":6,"36":6,"41":13};
function aiLength(s){
  for(const n of [4,3,2]){const k=s.slice(0,n);if(AI[k])return n;}
  const two=s.slice(0,2);
  if(/^(3[1-6]|39|43|70|72|8\d)$/.test(two)) return 4;
  if(/^(23|24|25|40|41|42|71)$/.test(two)) return 3;
  return 2;
}
function aiInfo(ai){
  if(AI[ai]) return {name:AI[ai][0],kind:AI[ai][1]};
  const m=MEAS[ai.slice(0,3)];
  if(m&&ai.length===4) return {name:`${m[0]} (${m[1]})`,kind:/^39[13]/.test(ai)?"money":"dec"};
  if(/^9[1-9]$/.test(ai)) return {name:"Information interne de l'entreprise"};
  if(/^703\d$/.test(ai)) return {name:"N° d'approbation du transformateur"};
  return {name:"Identifiant d'application "+ai};
}
function gs1Date(v){
  if(!/^\d{6}$/.test(v)) return {text:v};
  const yy=+v.slice(0,2),mm=+v.slice(2,4),dd=+v.slice(4,6),now=new Date().getFullYear();
  let y=2000+yy;if(y>now+50)y-=100;else if(y<now-49)y+=100;
  const day=dd===0?new Date(y,mm,0).getDate():dd;const d=new Date(y,mm-1,day);
  return {text:d.toLocaleDateString("fr-FR",{day:"2-digit",month:"long",year:"numeric"})+(dd===0?" (fin de mois)":""),date:d};
}
function parseGS1Elements(text){
  const out=[];
  if(/^\(\d{2,4}\)/.test(text)){
    const re=/\((\d{2,4})\)([^(]*)/g;let m;while((m=re.exec(text)))out.push([m[1],m[2]]);
    return out.length?out:null;
  }
  let s=text.replace(/^\][A-Za-z]\d/,"");
  if(!/^\d{2}/.test(s)) return null;
  while(s.length){
    if(s[0]==="\x1d"){s=s.slice(1);continue;}
    const n=aiLength(s),ai=s.slice(0,n);if(!/^\d+$/.test(ai)) return out.length?out:null;
    s=s.slice(n);const fx=FIXED[ai.slice(0,2)];let v;
    if(fx){v=s.slice(0,fx);s=s.slice(fx);}else{const g=s.indexOf("\x1d");v=g<0?s:s.slice(0,g);s=g<0?"":s.slice(g+1);}
    out.push([ai,v]);
  }
  return out.length?out:null;
}
function describeAI(ai,v,alerts){
  const info=aiInfo(ai);let val=esc(v);
  switch(info.kind){
    case "gtin":{const ok=validGtin(v);val=`<code>${esc(v)}</code> ${ok?'<span class="pill good">clé valide</span>':'<span class="pill bad">clé invalide</span>'}`;
      if(ok&&v.length===14)val+=`<br><span class="hint">Indicateur ${v[0]} · préfixe GS1 : ${esc(gs1Prefix(v.slice(1)))}</span>`;
      if(!ok)alerts.push(["bad",`Le chiffre de contrôle du GTIN ${v} est faux : le code a été mal généré ou mal lu.`]);break;}
    case "sscc":{const ok=validGtin(v);val=`<code>${esc(v)}</code> ${ok?'<span class="pill good">clé valide</span>':'<span class="pill bad">clé invalide</span>'}<br><span class="hint">Extension ${v[0]} · préfixe GS1 : ${esc(gs1Prefix(v.slice(1)))}</span>`;break;}
    case "gln":{const ok=validGtin(v);val=`<code>${esc(v)}</code> ${ok?'<span class="pill good">clé valide</span>':'<span class="pill bad">clé invalide</span>'}`;break;}
    case "date":{const d=gs1Date(v);val=`${esc(d.text)} <span class="hint">(${esc(v)})</span>`;
      if(d.date&&(ai==="17"||ai==="15")){const today=new Date();today.setHours(0,0,0,0);
        if(d.date<today)alerts.push(["bad",`${ai==="17"?"Date d'expiration":"DDM"} dépassée : ${d.text}.`]);
        else{const days=Math.round((d.date-today)/864e5);if(days<=30)alerts.push(["warn",`${ai==="17"?"Expiration":"DDM"} dans ${days} jour${days>1?"s":""} (${d.text}).`]);}}
      break;}
    case "datetime":{const d=gs1Date(v.slice(0,6));val=`${esc(d.text)} à ${esc(v.slice(6,8))} h ${esc(v.slice(8,10)||"00")} <span class="hint">(${esc(v)})</span>`;break;}
    case "dec":{const n=+ai[3];const num=parseInt(v,10)/Math.pow(10,n);val=`${num.toLocaleString("fr-FR",{minimumFractionDigits:n,maximumFractionDigits:n})} <span class="hint">(${esc(v)})</span>`;break;}
    case "money":{const n=+ai[3];const cur=v.slice(0,3),num=parseInt(v.slice(3),10)/Math.pow(10,n);val=`${num.toLocaleString("fr-FR",{minimumFractionDigits:n,maximumFractionDigits:n})} <span class="hint">(devise ISO ${esc(cur)})</span>`;break;}
    case "int":val=String(parseInt(v,10));break;
    case "country":val=`${esc(ISO3166[v]||"code pays "+v)} <span class="hint">(${esc(v)})</span>`;break;
    case "countries":val=(v.match(/\d{3}/g)||[]).map(c=>ISO3166[c]||c).join(", ")+` <span class="hint">(${esc(v)})</span>`;break;
  }
  return [`${esc(info.name)} <span class="hint">AI ${esc(ai)}</span>`,val];
}


/* ================= Profils Zebra StageNow ================= */
async function decodeStageNow(bytes){
  if(!bytes||bytes.length<8||bytes[0]!==83||bytes[1]!==78||bytes[2]!==63) return null;
  const info={part:bytes[4],total:bytes[5],calls:[],script:""};
  let t="";for(let i=6;i<bytes.length;i++)t+=String.fromCharCode(bytes[i]);
  let bin;try{bin=atob(t.replace(/[^A-Za-z0-9+/=]/g,""));}catch(e){return info;}
  const u=new Uint8Array(bin.length);for(let i=0;i<bin.length;i++)u[i]=bin.charCodeAt(i);
  let at=-1;for(let i=0;i<Math.min(32,u.length-2);i++)if(u[i]===0x1f&&u[i+1]===0x8b&&u[i+2]===8){at=i;break;}
  if(at<0||!window.DecompressionStream) return info;
  try{
    const ds=new Blob([u.slice(at)]).stream().pipeThrough(new DecompressionStream("gzip"));
    info.script=await new Response(ds).text();
  }catch(e){return info;}
  info.calls=parseCalls(info.script);
  return info;
}
function parseCalls(src){
  const out=[];let i=0;const n=src.length;
  const idre=/[A-Za-z_][\w.]*\s*\(/y;
  while(i<n){
    idre.lastIndex=i;const mm=idre.exec(src);
    if(!mm){i++;continue;}
    const name=mm[0].replace(/\s*\($/,"");i=idre.lastIndex;const args=[];let cur="",depth=0;
    while(i<n){
      const ch=src[i];
      if(ch==='"'||ch==="'"){let j=i+1,str="";while(j<n&&src[j]!==ch){if(src[j]==="\\"&&j+1<n){str+=src[j+1];j+=2;}else str+=src[j++];}cur+=str;i=j+1;continue;}
      if(ch==="("){depth++;cur+=ch;i++;continue;}
      if(ch===")"){if(depth===0){args.push(cur.trim());i++;break;}depth--;cur+=ch;i++;continue;}
      if(ch===","&&depth===0){args.push(cur.trim());cur="";i++;continue;}
      cur+=ch;i++;
    }
    const c={csp:name,args,json:null,raw:args.join(", "),generic:true};
    if(name==="zh"){c.csp=args[0]||"zh";c.generic=false;try{c.json=JSON.parse(args[1]);}catch(e){}c.raw=args[1]||"";}
    out.push(c);
  }
  return out;
}
function describeStageNow(sn,rows,alerts){
  rows.push(["Type","Profil de configuration Zebra StageNow"]);
  if(sn.total>1)rows.push(["Partie",`${sn.part} sur ${sn.total}`]);
  if(!sn.script){
    alerts.push(["info",sn.total>1?"Profil réparti sur plusieurs codes : scanne toutes les parties avec StageNow sur le terminal Zebra.":"Contenu compressé non décodable ici (profil chiffré ou partiel)."]);return;}
  const ACT={Add:"Ajouter",Connect:"Se connecter",Remove:"Supprimer",Disconnect:"Se déconnecter",Enable:"Activer",Disable:"Désactiver",Install:"Installer",Uninstall:"Désinstaller"};
  let pwd=false;
  sn.calls.forEach((c,i)=>{
    const zd=(c.json&&c.json.zd)||{};const act=zd["48"];let d="";
    if(/wi-?fi/i.test(c.csp)){
      const n=zd["0"]||{};const parts=[];
      if(n["49"])parts.push(`réseau <code>${esc(n["49"])}</code>`);
      const sec=n["176"];if(sec){if(sec["82"])parts.push(`clé : ${esc(sec["82"])}`);if(sec["88"]){parts.push(`mot de passe <code>${esc(sec["88"])}</code>`);pwd=true;}}
      const ip=n["177"];if(ip){parts.push(`IP fixe <code>${esc(ip["98"]||"?")}</code>`+(ip["101"]?` / ${esc(ip["101"])}`:"")+(ip["99"]?`, passerelle ${esc(ip["99"])}`:"")+(ip["102"]?`, DNS ${esc(ip["102"])}`:""));}
      d=parts.join(" · ");
    }
    if(c.generic){
      c.args.forEach(u=>{const cred=u.match(/^[a-z]+:\/\/([^\/@:]+):([^\/@]+)@/i);if(cred){pwd=true;alerts.push(["warn",`Une adresse du profil contient un identifiant (${cred[1]}) et un mot de passe en clair.`]);}});
      const label=/download/i.test(c.csp)?"Télécharger":c.csp;
      const shown=c.args.map(a=>`<code>${esc(/^[a-z]+:\/\//i.test(a)?a.replace(/^([a-z]+:\/\/[^\/@:]+:)[^\/@]+@/i,"$1•••@"):(a.length>120?a.slice(0,120)+"…":a))}</code>`);
      rows.push([`Étape ${i+1} <span class="hint">${esc(c.csp)}</span>`,`<b>${esc(label)}</b>${shown.length?" · "+shown.join(" → "):""}`]);
      return;
    }
    rows.push([`Étape ${i+1} <span class="hint">${esc(c.csp)}</span>`,`${act?`<b>${esc(ACT[act]||act)}</b>${d?" · ":""}`:""}${d||(act?"":"paramètres ci-dessous")}`]);
  });
  if(pwd&&sn.calls.some(c=>/wi-?fi/i.test(c.csp)))alerts.push(["warn","Ce code contient un mot de passe Wi-Fi lisible par n'importe quel lecteur : ne le diffuse pas au-delà des personnes qui préparent les terminaux."]);
  alerts.push(["info","Profil StageNow décompressé localement. Les champs non reconnus restent visibles dans « Profil décompressé »."]);
}

/* ================= Interprétation du contenu ================= */
const SHORTENERS=/^(bit\.ly|tinyurl\.com|t\.co|goo\.gl|ow\.ly|is\.gd|buff\.ly|rebrand\.ly|cutt\.ly|qrco\.de|s\.id|shorturl\.at|rb\.gy|tiny\.cc|t\.ly|lnkd\.in|bl\.ink|qr\.io|q-r\.to|me-qr\.com|qr-code-generator\.com|l\.ead\.me|scan\.page|qrfy\.io)$/i;
const DL_KEYS=["01","00","253","255","401","402","414","417","8003","8004","8006","8010","8013","8017","8018"];
function unescWifi(s){return s.replace(/\\([\\;,:"])/g,"$1");}
function splitUnesc(s,sep){const out=[];let cur="";for(let i=0;i<s.length;i++){if(s[i]==="\\"&&i+1<s.length){cur+=s[i]+s[i+1];i++;}else if(s[i]===sep){out.push(cur);cur="";}else cur+=s[i];}out.push(cur);return out;}
function interpret(text,r){
  const t=text||"";const rows=[],alerts=[];let kind="Texte",link=null;
  const T=t.trim();
  let gs1=(GS1SYM.test(r.symId)||/^\(\d{2,4}\)\d/.test(T))?parseGS1Elements(T):null;
  let gs1Unflagged=false;
  if(!gs1){const T2=T.replace(/<GS>/g,"\x1d");
    if(/\x1d/.test(T2)&&/^\x1d?\d{4}/.test(T2)){const g=parseGS1Elements(T2.replace(/^\x1d/,""));
      if(g&&g.every(([ai])=>!/^Identifiant/.test(aiInfo(ai).name))){gs1=g;gs1Unflagged=true;}}}
  if(r.stagenow){kind="Profil Zebra StageNow";describeStageNow(r.stagenow,rows,alerts);}
  else if(gs1&&gs1.length){
    kind="Données GS1 (traçabilité)";
    if(gs1Unflagged)alerts.push(["warn",`Structure GS1 reconnue, mais le code n'est pas marqué GS1 (identifiant ${r.symId||"standard"} sans FNC1 en tête). Les systèmes GS1 risquent de le refuser : active le mode GS1 dans le logiciel d'étiquetage.`]);
    gs1.forEach(([ai,v])=>rows.push(describeAI(ai,v,alerts)));
    alerts.push(["info","Chaîne d'éléments GS1 : identifiants d'application (AI) normalisés, lus par les systèmes logistiques et de caisse."]);
  }
  else if(/^otpauth:\/\//i.test(T)){
    kind="Double authentification (2FA)";
    try{const u=new URL(T);const label=decodeURIComponent(u.pathname.replace(/^\/+/,""));
      rows.push(["Type",esc(u.hostname.toUpperCase())],["Compte",esc(label)],["Émetteur",esc(u.searchParams.get("issuer")||"—")],["Algorithme",esc(u.searchParams.get("algorithm")||"SHA1")],["Chiffres",esc(u.searchParams.get("digits")||"6")],["Période",esc((u.searchParams.get("period")||"30")+" s")]);
    }catch(e){}
    alerts.push(["bad","Ce code contient une clé secrète de connexion. Ne le partage pas et ne le publie pas."]);
  }
  else if(/^WIFI:/i.test(T)){
    kind="Réseau Wi-Fi";const f={};splitUnesc(T.slice(5),";").forEach(p=>{const i=p.indexOf(":");if(i>0)f[p.slice(0,i).toUpperCase()]=unescWifi(p.slice(i+1));});
    rows.push(["Nom du réseau (SSID)",`<code>${esc(f.S||"")}</code>`],["Sécurité",esc({WPA:"WPA / WPA2 / WPA3",WEP:"WEP (obsolète)",nopass:"Aucune (réseau ouvert)","":"non précisée"}[f.T||""]||f.T)],["Mot de passe",f.P?`<code>${esc(f.P)}</code>`:"—"],["Réseau masqué",f.H==="true"?"oui":"non"]);
    if(f.T==="WEP")alerts.push(["warn","WEP est un chiffrement obsolète et facile à casser."]);
    if(f.T==="nopass"||!f.P)alerts.push(["warn","Réseau sans mot de passe : le trafic n'est pas chiffré."]);
  }
  else if(/^BEGIN:VCARD/i.test(T)){
    kind="Carte de visite (vCard)";
    const lines=T.replace(/\r?\n[ \t]/g,"").split(/\r?\n/);
    const L={FN:"Nom",N:"Nom structuré",ORG:"Organisation",TITLE:"Fonction",TEL:"Téléphone",EMAIL:"E-mail",ADR:"Adresse",URL:"Site web",NOTE:"Note",BDAY:"Anniversaire",VERSION:"Version vCard"};
    lines.forEach(l=>{const m=l.match(/^([A-Z-]+)((?:;[^:]*)?):(.*)$/i);if(!m)return;const k=m[1].toUpperCase();if(!L[k]||k==="N")return;
      let v=m[3].replace(/\\n/gi," ").replace(/\\([,;\\])/g,"$1");if(k==="ADR")v=v.split(";").filter(Boolean).join(", ");
      const typ=(m[2].match(/TYPE=([^;:]+)/i)||[])[1];rows.push([L[k]+(typ?` <span class="hint">${esc(typ.toLowerCase())}</span>`:""),esc(v)]);});
  }
  else if(/^MECARD:/i.test(T)){
    kind="Carte de visite (MeCard)";const L={N:"Nom",TEL:"Téléphone",EMAIL:"E-mail",ADR:"Adresse",URL:"Site web",NOTE:"Note",BDAY:"Anniversaire",ORG:"Organisation"};
    splitUnesc(T.slice(7),";").forEach(p=>{const i=p.indexOf(":");if(i>0){const k=p.slice(0,i).toUpperCase();if(L[k])rows.push([L[k],esc(unescWifi(p.slice(i+1)).replace(/,/g,", "))]);}});
  }
  else if(/BEGIN:VEVENT/i.test(T)){
    kind="Événement d'agenda";const g=k=>(T.match(new RegExp("^"+k+"[^:\\n]*:(.*)$","mi"))||[])[1];
    const fd=v=>{if(!v)return"—";const m=v.match(/(\d{4})(\d{2})(\d{2})(?:T(\d{2})(\d{2}))?/);if(!m)return esc(v);const d=new Date(+m[1],+m[2]-1,+m[3],+(m[4]||0),+(m[5]||0));return esc(d.toLocaleString("fr-FR",{dateStyle:"full",timeStyle:m[4]?"short":undefined}));};
    rows.push(["Titre",esc(g("SUMMARY")||"—")],["Début",fd(g("DTSTART"))],["Fin",fd(g("DTEND"))],["Lieu",esc(g("LOCATION")||"—")]);if(g("DESCRIPTION"))rows.push(["Description",esc(g("DESCRIPTION").replace(/\\n/g," "))]);
  }
  else if(/^mailto:/i.test(T)){
    kind="E-mail";const [to,q]=T.slice(7).split("?");const p=new URLSearchParams(q||"");
    rows.push(["Destinataire",esc(decodeURIComponent(to))]);if(p.get("cc"))rows.push(["Copie",esc(p.get("cc"))]);if(p.get("subject"))rows.push(["Objet",esc(p.get("subject"))]);if(p.get("body"))rows.push(["Message",esc(p.get("body"))]);
  }
  else if(/^MATMSG:/i.test(T)){
    kind="E-mail";const f={};splitUnesc(T.slice(7),";").forEach(p=>{const i=p.indexOf(":");if(i>0)f[p.slice(0,i).toUpperCase()]=unescWifi(p.slice(i+1));});
    rows.push(["Destinataire",esc(f.TO||"")],["Objet",esc(f.SUB||"—")],["Message",esc(f.BODY||"—")]);
  }
  else if(/^tel:/i.test(T)){kind="Numéro de téléphone";const n=T.slice(4).replace(/[^\d+]/g,"");rows.push(["Numéro",`<code>${esc(T.slice(4))}</code>`]);if(/^(\+338|08)9/.test(n))alerts.push(["warn","Numéro surtaxé (08 9x) : appel facturé au prix fort."]);}
  else if(/^(SMSTO|sms):/i.test(T)){
    kind="SMS";let num,body;if(/^SMSTO:/i.test(T)){const p=T.slice(6);const i=p.indexOf(":");num=i<0?p:p.slice(0,i);body=i<0?"":p.slice(i+1);}else{const [n,q]=T.slice(4).split("?");num=n;body=new URLSearchParams(q||"").get("body")||"";}
    rows.push(["Numéro",`<code>${esc(num)}</code>`],["Message",esc(body||"—")]);
  }
  else if(/^geo:/i.test(T)){
    kind="Position géographique";const m=T.slice(4).match(/^(-?[\d.]+),(-?[\d.]+)(?:,(-?[\d.]+))?/);
    if(m){rows.push(["Latitude",esc(m[1])],["Longitude",esc(m[2])]);if(m[3])rows.push(["Altitude",esc(m[3])+" m"]);link={href:`https://www.openstreetmap.org/?mlat=${m[1]}&mlon=${m[2]}#map=16/${m[1]}/${m[2]}`,label:"Voir sur la carte"};}
  }
  else if(/^BCD\r?\n/.test(T)){
    kind="Virement SEPA (QR EPC)";const l=T.split(/\r?\n/);
    const amt=(l[7]||"").match(/^([A-Z]{3})([\d.]+)/);
    rows.push(["Bénéficiaire",esc(l[5]||"—")],["IBAN",`<code>${esc((l[6]||"").replace(/(.{4})/g,"$1 ").trim())}</code>`],["BIC",esc(l[4]||"—")],["Montant",amt?`${(+amt[2]).toLocaleString("fr-FR",{minimumFractionDigits:2})} ${esc(amt[1])}`:"libre"],["Référence",esc(l[9]||l[10]||"—")],["Motif",esc(l[10]&&l[9]?l[10]:(l[8]||"—"))],["Version",esc(l[1]||"")]);
    if(l[6]&&!ibanOk(l[6]))alerts.push(["bad","La clé de contrôle de l'IBAN est invalide."]);
    alerts.push(["warn","Vérifie toujours le bénéficiaire et l'IBAN avant de payer : un QR de paiement peut être collé par-dessus l'original."]);
  }
  else if(/^SPC\r?\n/.test(T)){kind="QR-facture suisse";const ib=T.match(/\b(CH|LI)\d{2}[0-9A-Z]{17}\b/);if(ib)rows.push(["IBAN",`<code>${esc(ib[0])}</code>`]);alerts.push(["info","Format suisse de paiement (Swiss QR Code)."]);}
  else if(/^DC0[1-4]/.test(T)){kind="2D-Doc (document sécurisé français)";rows.push(["Version",esc(T.slice(2,4))],["Autorité de certification",esc(T.slice(4,8))],["Certificat",esc(T.slice(8,12))]);
    const day=parseInt(T.slice(12,16),16);if(!isNaN(day)&&day<0xFFFF){const d=new Date(2000,0,1);d.setDate(d.getDate()+day);rows.push(["Date d'émission",esc(d.toLocaleDateString("fr-FR"))]);}
    alerts.push(["info","La signature du 2D-Doc n'est pas vérifiée ici. Utilise l'application officielle de l'ANTS pour authentifier le document."]);}
  else if(/^HC1:/.test(T)){kind="Certificat sanitaire européen (EU DCC)";alerts.push(["info","Contenu compressé et signé : il faut une application dédiée pour le lire."]);}
  else if(/^(bitcoin|ethereum|litecoin):/i.test(T)){kind="Paiement en cryptomonnaie";rows.push(["Adresse",`<code>${esc(T.split(/[:?]/)[1])}</code>`]);alerts.push(["warn","Un paiement en cryptomonnaie est irréversible. Vérifie l'adresse."]);}
  else if(/^(https?:\/\/|www\.)/i.test(T)){
    let u;try{u=new URL(/^www\./i.test(T)?"https://"+T:T);}catch(e){u=null;}
    if(u){
      const dl=digitalLink(u);
      kind=dl?"Lien GS1 Digital Link":"Lien web";link={href:u.href,label:"Ouvrir le lien"};
      rows.push(["Protocole",u.protocol==="https:"?'HTTPS <span class="pill good">chiffré</span>':'HTTP <span class="pill warn">non chiffré</span>'],["Domaine",`<code>${esc(u.hostname)}</code>`]);
      if(u.port)rows.push(["Port",esc(u.port)]);
      if(u.pathname&&u.pathname!=="/")rows.push(["Chemin",`<code>${esc(decodeURIComponent(u.pathname))}</code>`]);
      const ps=[...u.searchParams];if(ps.length)rows.push(["Paramètres",ps.map(([k,v])=>`<code>${esc(k)}</code> = ${esc(v)}`).join("<br>")]);
      if(u.hash)rows.push(["Ancre",esc(u.hash)]);
      const utm=ps.filter(([k])=>/^utm_/i.test(k));if(utm.length)alerts.push(["info",`Lien de suivi marketing (${utm.map(([k])=>k).join(", ")}).`]);
      if(dl){dl.forEach(([ai,v])=>rows.push(describeAI(ai,v,alerts)));alerts.push(["info","Lien GS1 Digital Link : la même étiquette sert au passage en caisse et à une page web produit."]);}
      if(u.protocol==="http:")alerts.push(["warn","Lien non chiffré (http) : préfère https."]);
      if(SHORTENERS.test(u.hostname))alerts.push(["warn","Lien raccourci ou redirigé : la destination finale n'est pas visible avant ouverture."]);
      if(/^\d+\.\d+\.\d+\.\d+$/.test(u.hostname)||u.hostname.includes(":"))alerts.push(["warn","Le lien pointe vers une adresse IP plutôt qu'un nom de domaine : prudence."]);
      if(/(^|\.)xn--/.test(u.hostname))alerts.push(["warn","Nom de domaine internationalisé (punycode) : vérifie qu'il n'imite pas un site connu."]);
      if(u.username||T.includes("@")&&/^https?:\/\/[^/]*@/i.test(T))alerts.push(["bad","Le lien contient un « @ » avant le domaine : technique classique pour masquer la vraie destination."]);
      if(/\.(apk|exe|msi|bat|scr|dmg|zip|js|vbs)(\?|$)/i.test(u.pathname))alerts.push(["bad","Le lien télécharge directement un fichier exécutable ou une archive."]);
      alerts.push(["ok","Le lien n'a pas été ouvert : l'analyse est faite localement."]);
    }
  }
  else if(/^(market|itms-apps|intent):/i.test(T)){kind="Lien vers une appli";}
  else if(/^[{[]/.test(T)){try{const j=JSON.parse(T);kind="Données JSON";rows.push(["Structure",Array.isArray(j)?`tableau de ${j.length} éléments`:`objet : ${Object.keys(j).slice(0,12).map(esc).join(", ")}`]);}catch(e){}}
  else if(/^\d+$/.test(T)){kind="Nombre";}
  // Codes de la grande distribution
  if(/^(EAN13|EAN8|UPCA|UPCE|ISBN|ITF14)$/.test(r.format)||(r.format==="ITF"&&T.length===14)){
    const d=T.replace(/\D/g,"");const g=r.format==="UPCA"?"0"+d:d;
    kind=r.format==="ISBN"||/^97[89]/.test(d)?"Livre (ISBN)":/^977/.test(d)?"Presse (ISSN)":"Article (GTIN)";
    rows.push(["GTIN",`<code>${esc(d)}</code> ${validGtin(d)?'<span class="pill good">clé valide</span>':'<span class="pill bad">clé invalide</span>'}`]);
    if(g.length===13)rows.push(["Préfixe GS1",`${esc(g.slice(0,3))} : ${esc(gs1Prefix(g))}`]);
    if(g.length===14)rows.push(["Indicateur logistique",esc(g[0])],["Préfixe GS1",esc(gs1Prefix(g.slice(1)))]);
    if(/^978/.test(d)&&d.length===13){const b=d.slice(3,12);let s=0;for(let i=0;i<9;i++)s+=(10-i)*+b[i];const c=(11-s%11)%11;rows.push(["ISBN-10",`<code>${b}${c===10?"X":c}</code>`]);}
    if(r.upce)rows.push(["UPC-E d'origine",`<code>${esc(r.upce)}</code>`]);
    if(r.addon)rows.push(["Supplément",`<code>${esc(r.addon)}</code>`+(r.addon.length===5&&/^97[89]/.test(d)?" <span class=\"hint\">(prix conseillé, codage éditeur)</span>":"")]);
    if(g.length===13&&/^2/.test(g))alerts.push(["info","Préfixe 2 : code interne au magasin, souvent pour les produits pesés ou à prix variable."]);
    else if(g.length>=13)alerts.push(["info","Le préfixe GS1 indique le pays de l'organisation qui a attribué le numéro, pas le pays de fabrication."]);
  }
  return {kind,rows,alerts,link};
}
function digitalLink(u){
  const seg=u.pathname.split("/").filter(Boolean);const i=seg.findIndex((s,j)=>DL_KEYS.includes(s)&&j+1<seg.length);
  if(i<0) return null;const out=[];
  for(let j=i;j+1<seg.length;j+=2){if(!/^\d{2,4}$/.test(seg[j]))break;out.push([seg[j],decodeURIComponent(seg[j+1])]);}
  u.searchParams.forEach((v,k)=>{if(/^\d{2,4}$/.test(k))out.push([k,v]);});
  return out.length&&out[0][0]&&/^(01|00|414|8004|8003|253|255|401|402|417|8006|8010|8013|8017|8018)$/.test(out[0][0])?out:null;
}
function ibanOk(ib){const s=ib.replace(/\s/g,"").toUpperCase();if(!/^[A-Z]{2}\d{2}[A-Z0-9]{8,30}$/.test(s))return false;const r=(s.slice(4)+s.slice(0,4)).replace(/[A-Z]/g,c=>c.charCodeAt(0)-55);let m=0;for(const ch of r)m=(m*10+ +ch)%97;return m===1;}

/* ================= Rendu des résultats ================= */
function hexdump(bytes){
  const b=bytes&&bytes.length?bytes:new Uint8Array();const lines=[];const max=Math.min(b.length,512);
  for(let i=0;i<max;i+=16){const ch=[...b.slice(i,i+16)];
    lines.push(i.toString(16).padStart(4,"0")+"  "+ch.map(x=>x.toString(16).padStart(2,"0")).join(" ").padEnd(48)+"  "+ch.map(x=>x===29?"␝":x>=32&&x<127?String.fromCharCode(x):"·").join(""));}
  if(b.length>max)lines.push(`… ${b.length-max} octets supplémentaires`);
  return lines.join("\n");
}
function charsetOf(r){
  if(r.hasECI) return "ECI déclaré (jeu de caractères explicite)";
  const b=r.bytes||[];let hi=false;for(const x of b)if(x>127){hi=true;break;}
  if(!hi) return "ASCII";
  try{new TextDecoder("utf-8",{fatal:true}).decode(b);return "UTF-8 (détecté)";}catch(e){return "ISO-8859-1 / autre (détecté)";}
}
function render(ms,reg){
  const box=$("scanResults");
  const zone=region?`zone de ${reg.w} × ${reg.h} px`:`image de ${src.width} × ${src.height} px`;
  if(!results.length){
    box.innerHTML=`<div class="noresult"><h3>Aucun code détecté</h3><p class="hint" style="margin:0">${esc(srcLabel)} · ${zone}</p>
      <ul><li>Trace un rectangle serré autour du code, avec un peu de marge blanche.</li><li>Évite le flou, les reflets et les angles trop prononcés.</li><li>Un code trop petit à l'écran : zoome avant la capture.</li><li>Un QR très stylisé ou un logo trop grand peut empêcher toute lecture.</li></ul>
      <div class="acts"><button class="btn small primary" type="button" data-zoomsel>Agrandir et sélectionner</button>${region?"":`<button class="btn small" type="button" data-deep>Recherche approfondie</button>`}</div></div>`;
    box.querySelector("[data-zoomsel]").addEventListener("click",()=>vOpen());
    if(DESKTOP){try{window.chrome.webview.postMessage(JSON.stringify({type:"decoded",source:srcLabel,items:[]}));}catch(e){}}
    const dp=box.querySelector("[data-deep]");if(dp)dp.addEventListener("click",()=>analyze({deep:true}));
    return;
  }
  const kinds={};
  let html=`<div class="summary"><b>${results.length} code${results.length>1?"s":""} détecté${results.length>1?"s":""}</b><span>${esc(srcLabel)} · ${zone} · ${Math.round(ms)} ms</span>${region?"":`<button class="btn small" type="button" data-deep>Chercher d'autres codes</button>`}</div>`;
  results.forEach((r,i)=>{
    const it=interpret(r.text,r);kinds[i]=it;
    const fmt=FMT[r.format]||r.format;const isM=MATRIX.test(r.format);
    const ct=contrast(r.pts);const side=sideLen(r.pts);const nMod=modulesOf(r);const modPx=nMod?side/nMod:0;
    const qzNeed=/^(QRCode|QRCodeModel2|QRCodeModel1)$/.test(r.format)?4:r.format==="MicroQRCode"?2:/DataMatrix/.test(r.format)?1:0;
    const qz=quietZone(r,modPx,qzNeed,ct);
    const scGrade=ct.sc>=70?["A","good"]:ct.sc>=55?["B","good"]:ct.sc>=40?["C","warn"]:ct.sc>=20?["D","warn"]:["F","bad"];
    let health=["Lecture fiable","good"];
    if(r.uec!=null&&r.uec<.25)health=["Code abîmé","bad"];else if(r.uec!=null&&r.uec<.6)health=["Lecture fragile","warn"];else if(ct.sc<40)health=["Contraste faible","warn"];
    const spec=[];
    spec.push(["Symbologie",esc(fmt)+(r.symId?` <span class="hint">· identifiant <code>${esc(r.symId)}</code>${SYMID[r.symId.slice(1)]?" : "+esc(SYMID[r.symId.slice(1)]):""}</span>`:"")]);
    if(r.version){spec.push(["Version / taille",esc(r.version)+(nMod&&/QR/.test(r.format)?` <span class="hint">(${nMod} × ${nMod} modules)</span>`:"")]);}
    if(r.ec)spec.push(["Correction d'erreur",esc(/%$/.test(r.ec)?r.ec+" de la capacité":r.ec)+(EC_PCT[r.ec]?` <span class="hint">${EC_PCT[r.ec]} de données récupérables</span>`:"")]);
    if(r.mask!=null&&r.mask!=="")spec.push(["Masque de données",esc(String(r.mask))]);
    if(r.uec!=null){const u=Math.round(r.uec*100);const col=u>=60?"var(--ok)":u>=25?"var(--warn)":"var(--bad)";
      spec.push(["Marge de correction restante",`<span class="meter" style="--v:${u}%;--mc:${col}"><i></i>${u} %</span> <span class="hint">part de la correction d'erreur encore inutilisée</span>`]);}
    spec.push(["Encodage",esc(charsetOf(r))]);
    spec.push(["Longueur",`${[...(r.text||"")].length} caractères · ${(r.bytes||[]).length} octets`]);
    const orient=[];if(r.rotation)orient.push(`rotation ${r.rotation}°`);if(r.mirrored)orient.push("image en miroir");if(r.inverted)orient.push("couleurs inversées (clair sur foncé)");
    spec.push(["Orientation",orient.length?esc(orient.join(" · ")):"normale"]);
    spec.push(["Taille dans l'image",`${Math.round(side)} px de côté`+(modPx?` · ≈ ${modPx.toFixed(1)} px par module`:"")]);
    spec.push(["Contraste de symbole",`${ct.sc} % <span class="pill ${scGrade[1]}">${scGrade[0]}</span> <span class="hint">estimation indicative, pas une vérification ISO 15415/15416</span>`]);
    if(qz) spec.push(["Zone de silence",qz.state==="ok"?`conforme (≥ ${qzNeed} module${qzNeed>1?"s":""}) <span class="pill good">OK</span>`:qz.state==="edge"?`non vérifiable (code au bord de l'image)`:`encombrée <span class="pill warn">à vérifier</span>`]);
    if(!isM&&r.lines)spec.push(["Lignes de lecture concordantes",esc(String(r.lines))]);
    if(r.seq)spec.push(["Séquence (structured append)",`symbole ${r.seq.index+1} sur ${r.seq.size||"?"}`+(r.seq.id?` · id ${esc(r.seq.id)}`:"")]);
    if(r.readerInit)spec.push(["Programmation lecteur","oui (code de configuration de scanner)"]);
    const al=it.alerts.filter((a,j,arr)=>arr.findIndex(b=>b[1]===a[1])===j);
    if(modPx&&modPx<2.5)al.push(["warn",`Modules très petits dans l'image (≈ ${modPx.toFixed(1)} px). Pour une impression, prévois au moins 0,3 mm par module.`]);
    if(qz&&qz.state==="busy")al.push(["warn","La marge autour du code semble encombrée : certains lecteurs peuvent échouer."]);
    if(r.inverted)al.push(["warn","Code inversé : de nombreux lecteurs (caisses, vieux smartphones) ne le lisent pas."]);
    if(r.uec!=null&&r.uec<.25)al.push(["bad","Presque toute la correction d'erreur est consommée : le code est abîmé, masqué (logo trop grand) ou mal imprimé."]);
    html+=`<article class="res" data-i="${i}">
      <div class="res-head"><span class="num">${i+1}</span><h3>${esc(it.kind)}</h3><span class="pill">${esc(fmt)}</span><span class="pill ${health[1]}">${health[0]}</span></div>
      <pre class="content">${esc(r.text||"(vide)").replace(/\x1d/g,'<span class="hint">␝</span>')}</pre>
      <div class="acts"><button class="btn small" type="button" data-copy="${i}">Copier le contenu</button>${it.link?`<a class="btn small" href="${esc(it.link.href)}" target="_blank" rel="noopener noreferrer">${esc(it.link.label)}</a>`:""}<button class="btn small" type="button" data-recreate="${i}" title="Ouvre « Créer un code » avec le même type et le même contenu">Recréer ce code</button>${DESKTOP?`<button class="btn small primary" type="button" data-bench="${i}">Comparer avec une lecture douchette</button>`:""}</div>
      ${al.length?`<div class="alerts">${al.map(([t,m])=>`<div class="alert ${t}">${esc(m)}</div>`).join("")}</div>`:""}
      ${it.rows.length?`<h4>Ce que contient le code</h4><dl class="kv">${it.rows.map(([k,v])=>`<dt>${k}</dt><dd>${v}</dd>`).join("")}</dl>`:""}
      <h4>Caractéristiques techniques</h4><dl class="kv">${spec.map(([k,v])=>`<dt>${k}</dt><dd>${v}</dd>`).join("")}</dl>
      ${r.stagenow&&r.stagenow.script?`<details class="raw"><summary>Profil décompressé</summary><pre>${esc(r.stagenow.calls.length?r.stagenow.calls.map(c=>c.csp+(c.json?"\n"+JSON.stringify(c.json,null,2):"("+c.args.join(", ")+")")).join("\n\n"):r.stagenow.script)}</pre></details>`:""}
      <details class="raw"><summary>Données brutes (hexadécimal)</summary><pre class="hex">${esc(hexdump(r.bytes))}</pre></details>
    </article>`;
  });
  box.innerHTML=html;
  if(DESKTOP){try{window.chrome.webview.postMessage(JSON.stringify({type:"decoded",source:srcLabel,
    items:results.map(r=>({format:FMT[r.format]||r.format,text:ControlCharactersVisual(r.text||"")}))}));}catch(e){}}
  const dp=box.querySelector("[data-deep]");if(dp)dp.addEventListener("click",()=>analyze({deep:true}));
  box.querySelectorAll(".res").forEach(el=>{el.addEventListener("mouseenter",()=>setFocus(+el.dataset.i,false));});
  box.querySelectorAll("[data-copy]").forEach(b=>b.addEventListener("click",()=>{const t=results[+b.dataset.copy].text||"";
    const done=()=>{b.textContent="Copié";setTimeout(()=>b.textContent="Copier le contenu",1800);};
    (navigator.clipboard?navigator.clipboard.writeText(t):Promise.reject()).then(done,()=>{const pre=b.closest(".res").querySelector(".content");const rg=document.createRange();rg.selectNodeContents(pre);const s=getSelection();s.removeAllRanges();s.addRange(rg);b.textContent="Texte sélectionné : Ctrl+C";});}));
  box.querySelectorAll("[data-bench]").forEach(b=>b.addEventListener("click",()=>{const r=results[+b.dataset.bench];
    // Données brutes, telles qu'un scanner les transmet (GS réel, pas la forme lisible « (01)… »)
    let raw=r.text||"";if(r.bytes&&r.bytes.length){try{raw=new TextDecoder("utf-8",{fatal:true}).decode(r.bytes);}catch(e){raw=String.fromCharCode(...r.bytes);}}
    window.chrome.webview.postMessage(JSON.stringify({type:"compare",text:raw,display:r.text||"",format:FMT[r.format]||r.format||"",symbologyIdentifier:r.symId||"",image:cropImage(r)}));}));
  box.querySelectorAll("[data-recreate]").forEach(b=>b.addEventListener("click",()=>{const r=results[+b.dataset.recreate];if(window.atelierLoad){setMode("create",true);window.atelierLoad(r.text||"",{format:r.format,gs1:GS1SYM.test(r.symId||"")});window.scrollTo({top:0});}}));
}
function ControlCharactersVisual(t){return t.replace(/[\x00-\x1f]/g,c=>"<"+["NUL","SOH","STX","ETX","EOT","ENQ","ACK","BEL","BS","HT","LF","VT","FF","CR","SO","SI","DLE","DC1","DC2","DC3","DC4","NAK","SYN","ETB","CAN","EM","SUB","ESC","FS","GS","RS","US"][c.charCodeAt(0)]+">");}
/* Vignette du code (zone de l'image autour du code), pour le banc d'essai */
function cropImage(r){
  try{const b=bbox(r.pts);const m=Math.max(12,Math.round((b.x1-b.x0+b.y1-b.y0)*.08));
    const x0=Math.max(0,b.x0-m),y0=Math.max(0,b.y0-m),x1=Math.min(src.width,b.x1+m),y1=Math.min(src.height,b.y1+m);
    const w=x1-x0,h=y1-y0,k=Math.min(4,600/Math.max(w,h));
    const c=document.createElement("canvas");c.width=Math.round(w*k);c.height=Math.round(h*k);
    const x=c.getContext("2d");x.imageSmoothingEnabled=k<1;x.drawImage(src,x0,y0,w,h,0,0,c.width,c.height);return c.toDataURL("image/png");}
  catch(e){return "";}
}
function showMessage(title,msg){$("scanResults").innerHTML=`<div class="noresult"><h3>${esc(title)}</h3><p style="margin:0">${esc(msg)}</p></div>`;}

/* ================= Démarrage ================= */
let start="create";
try{start=localStorage.getItem("atelier-mode")||"create";}catch(e){}
if(/^#analy/i.test(location.hash))start="scan";else if(/^#cr/i.test(location.hash))start="create";
// attend que l'aperçu de l'atelier soit dessiné pour l'exemple
setTimeout(()=>setMode(start==="scan"?"scan":"create",false),start==="scan"?400:0);
setTimeout(()=>engine().catch(()=>{}),1500);
})();
