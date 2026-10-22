
/* ================= Caméra en direct ================= */
// Lecture en continu depuis une webcam ou une caméra document : les codes sont encadrés en direct,
// puis l'image est figée et analysée en détail (comme une image importée).
const CAM={stream:null,timer:0,busy:false,seen:new Map(),video:$("camVideo"),ov:$("camOverlay"),work:document.createElement("canvas"),
  small:document.createElement("canvas"),sharp:0,tick:0,wait:null};
// Netteté : 99e centile du laplacien rapporté au contraste, mesuré au centre de l'image réduite à 480 px.
// Repères : au-dessus de 1,4 les barres sont franches ; sous 0,9 le flou dépasse la largeur d'une barre fine.
const SHARP_OK=1.4, SHARP_LOW=.9;
function camSharpness(source,w,h){
  const s=CAM.small,W=480,H=Math.max(1,Math.round(h*W/w));s.width=W;s.height=H;
  const x=s.getContext("2d",{willReadFrequently:true});x.drawImage(source,0,0,W,H);
  const x0=Math.round(W*.15),y0=Math.round(H*.15),cw=Math.round(W*.7),ch=Math.round(H*.7);
  const d=x.getImageData(x0,y0,cw,ch).data;const g=new Float32Array(cw*ch);
  let sum=0,sq=0;for(let i=0,j=0;j<g.length;i+=4,j++){const v=(d[i]*299+d[i+1]*587+d[i+2]*114)/1000;g[j]=v;sum+=v;sq+=v*v;}
  const n=g.length,sd=Math.sqrt(Math.max(0,sq/n-(sum/n)*(sum/n)))+1;
  const hist=new Uint32Array(1021);let cnt=0;
  for(let y=1;y<ch-1;y++)for(let xx=1;xx<cw-1;xx++){const j=y*cw+xx;const l=Math.abs(4*g[j]-g[j-1]-g[j+1]-g[j-cw]-g[j+cw]);hist[Math.min(1020,l|0)]++;cnt++;}
  let c=0,p=0;const lim=cnt*.99;for(let i=0;i<1021;i++){c+=hist[i];if(c>=lim){p=i;break;}}
  return p/sd;
}
function camShowSharp(v){
  const el=$("camSharp");const pct=Math.max(4,Math.min(100,v/2*100));
  const st=v>=SHARP_OK?["ok","Image nette"]:v>=SHARP_LOW?["mid","Un peu floue : tiens le code immobile"]:["bad","Image floue : recule un peu (20 à 30 cm)"];
  el.className="cam-sharp "+st[0];el.style.setProperty("--v",pct+"%");el.querySelector("span").textContent=CAM.wait?"Attente d'une image nette…":st[1];
}
const CAM_KEY="atelier-camera";
const camErr=e=>{
  const n=e&&e.name||"";
  if(n==="NotAllowedError"||n==="SecurityError")return DESKTOP
    ?"Windows bloque l'accès : Paramètres Windows › Confidentialité et sécurité › Caméra, active « Autoriser les applications de bureau à accéder à votre caméra »."
    :"L'accès à la caméra a été refusé. Autorise-le dans les réglages du navigateur (icône à gauche de l'adresse), puis réessaie.";
  if(n==="NotFoundError"||n==="OverconstrainedError")return"Aucune caméra trouvée. Branche une webcam ou une caméra document, puis réessaie.";
  if(n==="NotReadableError")return"La caméra est déjà utilisée par une autre application (Teams, Zoom, l'appli Caméra…). Ferme-la puis réessaie.";
  return"La caméra n'a pas pu démarrer.";
};
async function camList(current){
  let devs=[];try{devs=(await navigator.mediaDevices.enumerateDevices()).filter(d=>d.kind==="videoinput");}catch(e){}
  $("camSelect").innerHTML=devs.map((d,i)=>`<option value="${esc(d.deviceId)}">${esc(d.label||("Caméra "+(i+1)))}</option>`).join("");
  if(current)$("camSelect").value=current;
}
async function camStart(deviceId,preferLabel){
  if(!(navigator.mediaDevices&&navigator.mediaDevices.getUserMedia)){showMessage("Caméra indisponible ici",IN_CLAUDE?"Dans Claude, la page n'a pas accès à la caméra. Prends une photo du code, puis importe-la.":"Ce navigateur ne donne pas accès à la caméra.");return;}
  camStop(true);
  setMode("scan",false);
  if(!deviceId){try{deviceId=localStorage.getItem(CAM_KEY)||"";}catch(e){}}
  const want=w=>({audio:false,video:{...(w?{deviceId:{exact:w}}:{facingMode:{ideal:"environment"}}),width:{ideal:1920},height:{ideal:1080},frameRate:{ideal:30}}});
  let stream;
  try{stream=await navigator.mediaDevices.getUserMedia(want(deviceId));}
  catch(e){
    if(deviceId&&(e.name==="OverconstrainedError"||e.name==="NotFoundError")){try{stream=await navigator.mediaDevices.getUserMedia(want(""));}catch(e2){showMessage("Caméra",camErr(e2));return;}}
    else{showMessage("Caméra",camErr(e));return;}
  }
  CAM.stream=stream;
  const track=stream.getVideoTracks()[0];const id=(track.getSettings&&track.getSettings().deviceId)||deviceId||"";
  try{if(id)localStorage.setItem(CAM_KEY,id);}catch(e){}
  // Mise au point continue quand la caméra la propose (utile pour les petits codes 1D).
  try{const caps=track.getCapabilities?track.getCapabilities():{};if(caps.focusMode&&caps.focusMode.includes("continuous"))await track.applyConstraints({advanced:[{focusMode:"continuous"}]});}catch(e){}
  await camList(id);
  // Caméra demandée par son nom (page Douchettes du programme) : on bascule dessus si ce n'est pas celle ouverte.
  if(preferLabel){const want=preferLabel.toLowerCase();const opt=[...$("camSelect").options].find(o=>o.textContent.toLowerCase().includes(want)||want.includes(o.textContent.toLowerCase()));
    if(opt&&opt.value&&opt.value!==id){stream.getTracks().forEach(t=>t.stop());CAM.stream=null;return camStart(opt.value);}}
  CAM.video.srcObject=stream;try{await CAM.video.play();}catch(e){}
  CAM.seen.clear();
  stage.classList.add("live");cv.hidden=true;$("scanEmpty").hidden=true;
  CAM.video.hidden=false;CAM.ov.hidden=false;$("camLive").hidden=false;$("camBar").hidden=false;$("camSharp").hidden=false;CAM.wait=null;CAM.tick=0;
  $("camLive").innerHTML=`<span class="wait">Présente un code devant la caméra…</span>`;
  $("scanResults").innerHTML="";
  CAM.timer=setInterval(camTick,140);
}
function camStop(silent){
  clearInterval(CAM.timer);CAM.timer=0;
  if(CAM.stream){CAM.stream.getTracks().forEach(t=>t.stop());CAM.stream=null;}
  CAM.video.srcObject=null;CAM.video.hidden=true;CAM.ov.hidden=true;$("camLive").hidden=true;$("camBar").hidden=true;$("camSharp").hidden=true;stage.classList.remove("live");
  CAM.wait=null;$("camFreeze").textContent="Figer et analyser";
  if(!silent){if(src){cv.hidden=false;}else{$("scanEmpty").hidden=false;}}
}
async function camTick(){
  const v=CAM.video;if(CAM.busy||!CAM.stream||!v.videoWidth)return;
  CAM.busy=true;
  try{
    const k=Math.min(1,1280/v.videoWidth);const w=Math.round(v.videoWidth*k),h=Math.round(v.videoHeight*k);
    const c=CAM.work;c.width=w;c.height=h;const x=c.getContext("2d",{willReadFrequently:true});x.drawImage(v,0,0,w,h);
    CAM.sharp=camSharpness(c,w,h);camShowSharp(CAM.sharp);
    // « Figer » demandé sur une image floue : on garde la plus nette des 2 secondes suivantes.
    if(CAM.wait){
      if(CAM.sharp>CAM.wait.best){CAM.wait.best=CAM.sharp;CAM.wait.frame=camGrab();}
      if(CAM.sharp>=SHARP_OK||Date.now()>CAM.wait.until){const f=CAM.wait.frame;const ok=CAM.wait.best>=SHARP_OK;CAM.wait=null;camFreezeFrame(f,ok?"":" (image la plus nette obtenue)");return;}
    }
    const read=await engine();
    let found=await read(x.getImageData(0,0,w,h),{tryHarder:true,tryRotate:true,tryInvert:false,tryDownscale:true,maxNumberOfSymbols:8});
    // Une image sur trois : nouvel essai sur l'image accentuée (codes un peu flous).
    if(!found.length&&++CAM.tick%3===0){try{found=await read(enhanceImage(x.getImageData(0,0,w,h),2.2,2),{tryHarder:true,tryRotate:true,maxNumberOfSymbols:8});}catch(e){}}
    camDraw(found,w,h);
    // Un code vu sur deux images de suite est considéré comme lu (évite les lectures fantômes).
    const now=Date.now();let stable=null;
    found.forEach(f=>{const key=f.format+"|"+f.text;const prev=CAM.seen.get(key);CAM.seen.set(key,{n:(prev&&now-prev.t<600?prev.n:0)+1,t:now,f});if(CAM.seen.get(key).n>=2&&!stable)stable=f;});
    $("camLive").innerHTML=found.length?found.map(f=>`<span><b>${esc(f.format)}</b>${esc(ControlCharactersVisual(f.text))}</span>`).join(""):`<span class="wait">Présente un code devant la caméra…</span>`;
    if(stable&&$("camAuto").checked){camBeep();camFreeze(stable);}
  }catch(e){}
  finally{CAM.busy=false;}
}
function camDraw(found,w,h){
  const o=CAM.ov,r=o.getBoundingClientRect(),d=window.devicePixelRatio||1;
  o.width=Math.max(1,Math.round(r.width*d));o.height=Math.max(1,Math.round(r.height*d));
  const x=o.getContext("2d");x.clearRect(0,0,o.width,o.height);
  // La vidéo est affichée « contain » dans le cadre : même mise à l'échelle pour les encadrés.
  const s=Math.min(o.width/w,o.height/h),ox=(o.width-w*s)/2,oy=(o.height-h*s)/2;
  const accent=getComputedStyle(document.documentElement).getPropertyValue("--accent").trim()||"#145c43";
  found.forEach(f=>{x.beginPath();f.pts.forEach((p,j)=>{const X=ox+p.x*s,Y=oy+p.y*s;j?x.lineTo(X,Y):x.moveTo(X,Y);});x.closePath();
    x.lineWidth=4*d;x.strokeStyle="#fff";x.stroke();x.lineWidth=2.5*d;x.strokeStyle="#2FBF85";x.stroke();x.fillStyle=accent;x.globalAlpha=.18;x.fill();x.globalAlpha=1;});
}
function camGrab(){
  const v=CAM.video;if(!v.videoWidth)return null;
  const c=document.createElement("canvas");c.width=v.videoWidth;c.height=v.videoHeight;c.getContext("2d").drawImage(v,0,0);return c;
}
/* « Figer et analyser » : tout de suite si l'image est nette, sinon on attend une image nette (2 s au plus). */
function camFreezeRequest(){
  if(!CAM.stream)return;
  if(CAM.sharp>=SHARP_OK){camFreezeFrame(camGrab(),"");return;}
  CAM.wait={until:Date.now()+2000,best:CAM.sharp,frame:camGrab()};
  $("camFreeze").textContent="Attente d'une image nette…";camShowSharp(CAM.sharp);
}
function camFreeze(hit){camFreezeFrame(camGrab(),"");void hit;}
function camFreezeFrame(c,note){
  if(!c)return;
  const label=$("camSelect").selectedOptions[0]?$("camSelect").selectedOptions[0].textContent:"Caméra";
  camStop(true);
  setSource(c,c.width,c.height,"Caméra : "+label+note);
  if(DESKTOP){try{window.chrome.webview.postMessage(JSON.stringify({type:"camera",state:"frozen"}));}catch(e){}}
}
let camAudio=null;
function camBeep(){try{camAudio=camAudio||new (window.AudioContext||window.webkitAudioContext)();const o=camAudio.createOscillator(),g=camAudio.createGain();
  o.frequency.value=1800;g.gain.value=.06;o.connect(g);g.connect(camAudio.destination);o.start();o.stop(camAudio.currentTime+.07);}catch(e){}}
$("scanCam").addEventListener("click",()=>CAM.stream?camStop(false):camStart(""));
$("camStop").addEventListener("click",()=>camStop(false));
$("camFreeze").addEventListener("click",camFreezeRequest);
$("camSelect").addEventListener("change",e=>camStart(e.target.value));
document.addEventListener("visibilitychange",()=>{if(document.hidden&&CAM.stream)camStop(false);});
window.atelierStartCamera=label=>camStart("",label||"");
window.atelierStopCamera=()=>camStop(false);
