/* ===== Scanalyse sur téléphone : lire à la caméra, créer, historique ===== */
(function(){
const $=id=>document.getElementById(id);
const esc=s=>String(s).replace(/[&<>"']/g,c=>({"&":"&amp;","<":"&lt;",">":"&gt;",'"':"&quot;","'":"&#39;"}[c]));
document.body.classList.add("m-app");
window.atelierNoSample=true;

/* ---------- Langue : celle du téléphone, ou celle choisie ---------- */
let lang=null;try{lang=localStorage.getItem("scanalyse-lang");}catch(e){}
if(!lang)lang=/^fr\b/i.test(navigator.language||"fr")?"fr":"en";
const EN=lang==="en";
const T=(fr,en)=>EN?en:fr;
if(EN){
  document.documentElement.lang="en";
  document.querySelectorAll("[data-en]").forEach(e=>{e.textContent=e.dataset.en;});
  if(window.atelierSetLanguage&&window.SCANALYSE_EN)window.atelierSetLanguage(window.SCANALYSE_EN);
  $("mLang").textContent="Français";$("mLang").lang="fr";$("mSite").href="../en/";
}
$("mLang").addEventListener("click",()=>{try{localStorage.setItem("scanalyse-lang",EN?"fr":"en");}catch(e){}location.reload();});

let toastTimer=0;
function toast(t){const e=$("mToast");e.textContent=t;e.classList.add("show");clearTimeout(toastTimer);toastTimer=setTimeout(()=>e.classList.remove("show"),2200);}
const fmtName=f=>{const n=window.atelierFormatName?window.atelierFormatName(f):f;return window.atelierT?window.atelierT(n):n;};
const aiName=ai=>{const n=window.atelierAiName?window.atelierAiName(ai):"";return window.atelierT?window.atelierT(n):n;};

/* ---------- Navigation : onglets, sous-écrans, retour (bouton, geste, touche Retour d'Android) ---------- */
const SCREENS=["mScan","mCreate","mTypes","mHistory","mAtelier"];
const TAB_OF={mScan:"mScan",mCreate:"mCreate",mTypes:"mCreate",mHistory:"mHistory"};
const wrap=document.querySelector(".wrap");if(wrap)$("mAtelier").appendChild(wrap);
let stack=[{s:"mScan"}],screen="mScan";
function render(){
  const top=stack[stack.length-1];screen=top.s;
  SCREENS.forEach(id=>$(id).hidden=id!==screen);
  const tab=top.tab||TAB_OF[screen]||"mScan";
  document.querySelectorAll(".m-tabs button").forEach(b=>{if(b.dataset.go===tab)b.setAttribute("aria-current","page");else b.removeAttribute("aria-current");});
  $("mBack").hidden=stack.length<2;
  $("mTitle").textContent=top.title||"Scanalyse";
  if(screen==="mScan"&&$("mSheet").hidden)camStart();else camStop();
  if(screen==="mHistory")renderHistory();
  if(screen==="mTypes")renderTypes(top.cat);
  if(screen==="mAtelier"&&window.atelierSetMode){const m=top.mode==="details"?"scan":"create";if(!$((m==="scan")?"view-scan":"view-create")||$((m==="scan")?"view-scan":"view-create").hidden)atelierSetMode(m,false);}
}
function go(s,extra){stack.push({s,...extra});try{history.pushState({depth:stack.length},"");}catch(e){}render();const el=$(s);if(el.classList.contains("m-page")&&!(extra&&extra.keepScroll))el.scrollTop=0;}
function tab(s){closeSheet(true);stack=[{s}];try{history.replaceState({depth:1},"");}catch(e){}render();$(s).scrollTop=0;}
function back(){if(!$("mSheet").hidden||stack.length>1)history.back();}
window.addEventListener("popstate",()=>{
  if(!$("mSheet").hidden){closeSheet(true);resume(true);return;}
  if(stack.length>1){stack.pop();render();}
});
try{history.replaceState({depth:1},"");}catch(e){}
$("mBack").addEventListener("click",back);
document.querySelectorAll(".m-tabs button").forEach(b=>b.addEventListener("click",()=>tab(b.dataset.go)));

/* Glisser du bord gauche vers la droite : retour, comme dans les applications (application installée seulement :
   dans le navigateur, le geste appartient déjà au navigateur) */
const STANDALONE=matchMedia("(display-mode: standalone)").matches||navigator.standalone===true;
if(STANDALONE){
  let sx=0,sy=0,dx=0,track=false,el=null;
  document.addEventListener("touchstart",e=>{const t=e.touches[0];track=t.clientX<28&&(stack.length>1||!$("mSheet").hidden);if(!track)return;
    sx=t.clientX;sy=t.clientY;dx=0;el=$("mSheet").hidden?$(screen):$("mSheet").firstElementChild;},{passive:true});
  document.addEventListener("touchmove",e=>{if(!track)return;const t=e.touches[0];dx=t.clientX-sx;
    if(Math.abs(t.clientY-sy)>60&&dx<40){track=false;el.style.transform="";return;}
    if(dx>0)el.style.transform=`translateX(${dx}px)`;},{passive:true});
  document.addEventListener("touchend",()=>{if(!track)return;track=false;const done=dx>window.innerWidth*0.28;
    el.classList.add("m-back-anim");el.style.transform=done?"translateX(100%)":"";
    setTimeout(()=>{el.classList.remove("m-back-anim");el.style.transform="";if(done)back();},180);});
}
/* « Recréer ce code » dans l'analyse détaillée : l'Atelier passe en création, l'écran suit. */
const atelierSetMode=window.atelierSetMode;
if(atelierSetMode)window.atelierSetMode=function(m,save){atelierSetMode(m,save);
  const top=stack[stack.length-1];if(top.s==="mAtelier"&&top.mode==="details"&&m==="create"){go("mAtelier",{mode:"create",tab:"mCreate",title:T("Créer un code","Create a code")});}};

/* ---------- Lecture à la caméra ---------- */
const vid=$("mVideo"),finder=$("mFinder");
let stream=null,loop=0,canvas=null,frozen=false,torchOn=false,lastText="",lastAt=0,shot=null;
async function camStart(){
  if(stream||frozen||screen!=="mScan"||document.hidden)return;
  lastAt=Date.now();/* le dernier code lu, encore devant l'objectif, ne rouvre pas sa fiche */
  if(!(navigator.mediaDevices&&navigator.mediaDevices.getUserMedia)){camOff(T("Ce navigateur ne donne pas accès à la caméra. Analyse une photo à la place.","This browser gives no camera access. Analyze a photo instead."),false);return;}
  try{
    const engine=window.atelierEngine?window.atelierEngine():Promise.resolve();
    stream=await navigator.mediaDevices.getUserMedia({audio:false,video:{facingMode:{ideal:"environment"},width:{ideal:1920},height:{ideal:1080}}});
    vid.srcObject=stream;await vid.play().catch(()=>{});
    $("mCamOff").hidden=true;
    const track=stream.getVideoTracks()[0],caps=track&&track.getCapabilities?track.getCapabilities():{};
    $("mTorch").hidden=!caps.torch;torchOn=false;$("mTorch").classList.remove("on");
    await engine;
    tick();
  }catch(e){
    stream=null;
    camOff(e&&e.name==="NotAllowedError"
      ?T("Accès à la caméra refusé. Autorise-le dans les réglages du navigateur pour ce site, ou analyse une photo.","Camera access denied. Allow it in the browser settings for this site, or analyze a photo.")
      :T("Caméra indisponible. Analyse une photo à la place.","Camera unavailable. Analyze a photo instead."),true);
  }
}
function camOff(msg,retry){$("mCamMsg").textContent=msg;$("mCamStart").hidden=!retry;$("mCamOff").hidden=false;}
function camStop(){clearTimeout(loop);if(stream){stream.getTracks().forEach(t=>t.stop());stream=null;}vid.srcObject=null;}
$("mCamStart").addEventListener("click",()=>{frozen=false;camStart();});
$("mTorch").addEventListener("click",async()=>{const t=stream&&stream.getVideoTracks()[0];if(!t)return;torchOn=!torchOn;
  try{await t.applyConstraints({advanced:[{torch:torchOn}]});$("mTorch").classList.toggle("on",torchOn);}catch(e){torchOn=false;}});
document.addEventListener("visibilitychange",()=>{if(document.hidden)camStop();else if(screen==="mScan"&&$("mSheet").hidden)camStart();});

async function tick(){
  if(!stream||frozen)return;
  try{
    const w=vid.videoWidth,h=vid.videoHeight;
    if(w&&window.ZXingWASM){
      const k=Math.min(1,1280/Math.max(w,h));canvas=canvas||document.createElement("canvas");
      canvas.width=Math.round(w*k);canvas.height=Math.round(h*k);
      const x=canvas.getContext("2d",{willReadFrequently:true});x.drawImage(vid,0,0,canvas.width,canvas.height);
      const r=await ZXingWASM.readBarcodes(x.getImageData(0,0,canvas.width,canvas.height),{tryHarder:true,maxNumberOfSymbols:1,textMode:"HRI"});
      const b=r&&r[0];
      if(b&&b.isValid!==false&&b.text){
        const now=Date.now();
        if(!(b.text===lastText&&now-lastAt<2500)){lastText=b.text;lastAt=now;found(b);return;}
        lastAt=now;
      }
    }
  }catch(e){}
  loop=setTimeout(tick,140);
}
function found(b){
  frozen=true;clearTimeout(loop);
  try{shot=canvas.toDataURL("image/jpeg",.9);}catch(e){shot=null;}
  finder.classList.add("hit");setTimeout(()=>finder.classList.remove("hit"),500);
  if(navigator.vibrate)navigator.vibrate(40);
  vid.pause();
  const item={text:b.text,format:b.format||"",gs1:b.contentType==="GS1"||/^\(\d{2,4}\)\d/.test(b.text),at:Date.now()};
  addHistory(item);openSheet(item,true);
}
$("mPhoto").addEventListener("change",e=>{
  const f=e.target.files&&e.target.files[0];e.target.value="";if(!f)return;
  const url=URL.createObjectURL(f);
  if(window.atelierLoadImageData){window.atelierLoadImageData(url,T("Photo","Photo"));go("mAtelier",{mode:"details",tab:"mScan",title:T("Analyse détaillée","Detailed analysis")});}
});

/* ---------- Fiche du résultat ---------- */
let sheetItem=null;
function openSheet(item,live){
  sheetItem=item;
  $("mSheetType").textContent=fmtName(item.format)||T("Code","Code");
  $("mSheetMeta").textContent=new Date(item.at).toLocaleString(EN?"en-GB":"fr-FR",{hour:"2-digit",minute:"2-digit",day:"2-digit",month:"2-digit"});
  $("mSheetValue").textContent=item.text;
  const els=item.gs1&&window.atelierParseGs1?window.atelierParseGs1(item.text):null;
  $("mSheetAi").innerHTML=els?els.map(([ai,v])=>`<dt>(${esc(ai)})</dt><dd>${esc(v)}<small>${esc(aiName(ai))}</small></dd>`).join(""):"";
  const isUrl=/^https?:\/\/\S+$/i.test(item.text.trim());
  $("mOpen").hidden=!isUrl;if(isUrl)$("mOpen").href=item.text.trim();
  $("mDetailsBtn").hidden=!(live&&shot);
  $("mAgain").textContent=live?T("Lire un autre code","Scan another code"):T("Fermer","Close");
  $("mSheet").hidden=false;try{history.pushState({sheet:true},"");}catch(e){}
}
/* silent : fermeture déjà due à un retour (geste, touche) ou à un changement d'onglet */
function closeSheet(silent){if($("mSheet").hidden)return;$("mSheet").hidden=true;if(!silent){try{history.back();}catch(e){}}}
function resume(fromHistory){if(!fromHistory)closeSheet();frozen=false;lastAt=Date.now();/* le même code, encore devant la caméra, ne repart pas tout de suite */
  if(screen==="mScan"){if(stream){vid.play().catch(()=>{});tick();}else camStart();}}
$("mAgain").addEventListener("click",()=>resume());
$("mSheet").addEventListener("click",e=>{if(e.target===$("mSheet"))resume();});
$("mCopy").addEventListener("click",async()=>{try{await navigator.clipboard.writeText(sheetItem.text);toast(T("Contenu copié.","Content copied."));}catch(e){toast(T("Copie impossible sur cet appareil.","Copying is not possible on this device."));}});
$("mDetailsBtn").addEventListener("click",()=>{if(!shot||!window.atelierLoadImageData)return;$("mSheet").hidden=true;frozen=false;
  window.atelierLoadImageData(shot,T("Caméra du téléphone","Phone camera"));
  stack.push({s:"mAtelier",mode:"details",tab:"mScan",title:T("Analyse détaillée","Detailed analysis")});try{history.replaceState({depth:stack.length},"");}catch(e){}render();$("mAtelier").scrollTop=0;});
$("mRecreate").addEventListener("click",()=>{
  const it=sheetItem;$("mSheet").hidden=true;frozen=false;if(!window.atelierLoad)return;
  window.atelierLoad(it.text,{format:it.format,gs1:it.gs1});
  stack.push({s:"mAtelier",mode:"create",tab:"mCreate",title:typeTitle()});try{history.replaceState({depth:stack.length},"");}catch(e){}render();$("mAtelier").scrollTop=0;});

/* ---------- Historique ---------- */
const HKEY="scanalyse-history";
function hist(){try{return JSON.parse(localStorage.getItem(HKEY)||"[]");}catch(e){return[];}}
function addHistory(it){const h=hist();h.unshift(it);try{localStorage.setItem(HKEY,JSON.stringify(h.slice(0,50)));}catch(e){}}
function renderHistory(){
  const h=hist();
  $("mList").innerHTML=h.length?h.map((it,i)=>`<li><button type="button" data-i="${i}"><code>${esc(it.text.length>120?it.text.slice(0,120)+"…":it.text)}</code><small>${esc(fmtName(it.format))} · ${esc(new Date(it.at).toLocaleString(EN?"en-GB":"fr-FR",{day:"2-digit",month:"2-digit",hour:"2-digit",minute:"2-digit"}))}</small></button></li>`).join("")
    :`<li class="m-empty">${esc(T("Aucune lecture pour l'instant. Les codes lus à la caméra apparaissent ici.","No reads yet. Codes read with the camera show up here."))}</li>`;
  $("mClear").hidden=!h.length;
  $("mList").querySelectorAll("button[data-i]").forEach(b=>b.addEventListener("click",()=>openSheet(h[+b.dataset.i],false)));
}
$("mClear").addEventListener("click",()=>{if(!confirm(T("Effacer tout l'historique de ce téléphone ?","Clear the whole history on this phone?")))return;try{localStorage.removeItem(HKEY);}catch(e){}renderHistory();});

/* ---------- Créer : les mêmes types et le même éditeur que sur PC, présentés par catégories ---------- */
const CAT_ICON={qr:'<path d="M3 3h7v7H3zM14 3h7v7h-7zM3 14h7v7H3zM6 6h1v1H6zM17 6h1v1h-1zM6 17h1v1H6zM14 14h3v3M21 14v7h-7M17 21v-3"/>',
  "2d":'<rect x="4" y="4" width="16" height="16" rx="1"/><path d="M4 20V4h16M8 8h2v2H8zM13 9h3M9 14h2M14 13h2v3h-2"/>',
  retail:'<path d="M4 5v14M7 5v14M10 5v14M14 5v14M17 5v14M20 5v14"/>',
  ind:'<path d="M3 21V9l6 4V9l6 4V5h6v16zM7 17h2M12 17h2M17 17h2"/>'};
const GO='<svg class="go" viewBox="0 0 24 24" aria-hidden="true"><path d="M9 5l7 7-7 7"/></svg>';
const tr=t=>window.atelierT?window.atelierT(t):t;
function cats(){return[...document.querySelectorAll(".tp-cats button[data-cat]")].map(b=>({cat:b.dataset.cat,name:b.querySelector("b").textContent,desc:b.querySelector("span").textContent}));}
function renderCats(){
  $("mCats").innerHTML=cats().map(c=>`<button type="button" class="m-cat" data-cat="${c.cat}"><svg class="ic" viewBox="0 0 24 24" aria-hidden="true">${CAT_ICON[c.cat]||""}</svg><span><b>${esc(c.name)}</b><small>${esc(c.desc)}</small></span>${GO}</button>`).join("");
  $("mCats").querySelectorAll(".m-cat").forEach(b=>b.addEventListener("click",()=>{
    const c=b.dataset.cat;
    if(c==="qr"){openType("qr");return;}
    go("mTypes",{cat:c,title:T("Créer un code","Create a code")});
  }));
}
function renderTypes(cat){
  const c=cats().find(x=>x.cat===cat)||{name:"",desc:""};
  $("mTypesTitle").textContent=c.name;$("mTypesDesc").textContent=c.desc;
  const list=(window.atelierCreateTypes?window.atelierCreateTypes():[]).filter(t=>t.cat===cat);
  $("mTypesList").innerHTML=list.map(t=>`<button type="button" class="m-type" data-id="${t.id}"><span><b>${esc(tr(t.label))}</b><small>${esc(tr(t.usage))}</small></span>${GO}</button>`).join("");
  $("mTypesList").querySelectorAll(".m-type").forEach(b=>b.addEventListener("click",()=>openType(b.dataset.id)));
}
function typeTitle(){const id=window.atelierCreateCurrent?window.atelierCreateCurrent():"";const t=(window.atelierCreateTypes?window.atelierCreateTypes():[]).find(x=>x.id===id);return t?tr(t.label):T("Créer un code","Create a code");}
function openType(id){
  if(window.atelierSetCreateType)window.atelierSetCreateType(id);
  go("mAtelier",{mode:"create",tab:"mCreate",title:typeTitle()});
}
/* Dans l'éditeur : afficher le code en grand (pour le faire scanner) et le partager, en tête de l'aperçu */
function codeImage(){
  const std=$("stdMode")&&!$("stdMode").hidden;
  const box=std?$("stdPreview"):$("qr");if(!box)return Promise.resolve(null);
  const cv=box.querySelector("canvas");if(cv)return Promise.resolve(cv.toDataURL("image/png"));
  const svg=box.querySelector("svg");if(!svg)return Promise.resolve(null);
  return Promise.resolve("data:image/svg+xml;charset=utf-8,"+encodeURIComponent(new XMLSerializer().serializeToString(svg)));
}
function addQuick(aside){
  if(!aside||aside.querySelector(".m-quick"))return;
  const q=document.createElement("div");q.className="m-quick";
  q.innerHTML=`<button type="button" class="m-btn m-primary">${esc(T("Afficher en grand","Show full screen"))}</button>`;
  aside.prepend(q);
  q.querySelector("button").addEventListener("click",async()=>{const src=await codeImage();if(!src){toast(T("Rien à afficher : vérifie le contenu.","Nothing to show: check the content."));return;}
    $("mFullImg").src=src;$("mFullscreen").hidden=false;try{wake=await navigator.wakeLock.request("screen");}catch(e){}});
}
document.querySelectorAll("#view-create aside.preview").forEach(addQuick);
let wake=null;
$("mFullscreen").addEventListener("click",()=>{$("mFullscreen").hidden=true;if(wake){wake.release().catch(()=>{});wake=null;}});
renderCats();

/* ---------- Départ ---------- */
render();
})();
