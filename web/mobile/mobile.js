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

/* ---------- Navigation ---------- */
const SCREENS=["mScan","mCreate","mEdit","mHistory"];
let screen="mScan",back=null,atelierView=null;
function show(id,from){
  leaveAtelier();
  screen=id;back=from||null;
  SCREENS.forEach(s=>$(s).hidden=s!==id);
  const tab=id==="mEdit"?"mCreate":id;
  document.querySelectorAll(".m-tabs button").forEach(b=>{if(b.dataset.go===tab)b.setAttribute("aria-current","page");else b.removeAttribute("aria-current");});
  $("mBack").hidden=!back;
  if(id==="mScan")camStart();else camStop();
  if(id==="mHistory")renderHistory();
}
/* Analyse détaillée ou créateur complet : l'Atelier lui-même */
function showAtelier(mode,from){
  camStop();closeSheet();
  atelierView=mode;back=from;
  document.body.classList.add("m-show-atelier");
  if(mode==="create"&&window.atelierSetMode)window.atelierSetMode("create",false);
  $("mBack").hidden=false;window.scrollTo(0,0);
}
function leaveAtelier(){if(!atelierView)return;atelierView=null;document.body.classList.remove("m-show-atelier");}
$("mBack").addEventListener("click",()=>{const to=back||"mScan";show(to==="mEdit"?"mCreate":to);});
document.querySelectorAll(".m-tabs button").forEach(b=>b.addEventListener("click",()=>show(b.dataset.go)));
// Un bouton de l'analyse détaillée (« Recréer ce code ») passe l'Atelier en création : on suit.
const atelierSetMode=window.atelierSetMode;
if(atelierSetMode)window.atelierSetMode=function(m,save){atelierSetMode(m,save);if(atelierView==="details"&&m==="create")atelierView="create";};

/* ---------- Lecture à la caméra ---------- */
const vid=$("mVideo"),finder=$("mFinder");
let stream=null,loop=0,canvas=null,frozen=false,torchOn=false,lastText="",lastAt=0,shot=null;
async function camStart(){
  if(stream||frozen||screen!=="mScan"||document.hidden)return;
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
document.addEventListener("visibilitychange",()=>{if(document.hidden)camStop();else if(screen==="mScan"&&!atelierView)camStart();});

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
  if(window.atelierLoadImageData){window.atelierLoadImageData(url,T("Photo","Photo"));showAtelier("details","mScan");}
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
  $("mSheet").hidden=false;
}
function closeSheet(){$("mSheet").hidden=true;}
function resume(){closeSheet();frozen=false;lastAt=Date.now();/* le même code, encore devant la caméra, ne repart pas tout de suite */if(screen==="mScan"){if(stream){vid.play().catch(()=>{});tick();}else camStart();}}
$("mAgain").addEventListener("click",resume);
$("mSheet").addEventListener("click",e=>{if(e.target===$("mSheet"))resume();});
$("mCopy").addEventListener("click",async()=>{try{await navigator.clipboard.writeText(sheetItem.text);toast(T("Contenu copié.","Content copied."));}catch(e){toast(T("Copie impossible sur cet appareil.","Copying is not possible on this device."));}});
$("mDetailsBtn").addEventListener("click",()=>{if(!shot||!window.atelierLoadImageData)return;frozen=false;window.atelierLoadImageData(shot,T("Caméra du téléphone","Phone camera"));showAtelier("details","mScan");});
$("mRecreate").addEventListener("click",()=>{
  const it=sheetItem;closeSheet();frozen=false;
  const preset=presetFor(it);
  if(preset){openEditor(preset.id,preset.values,screen);return;}
  if(window.atelierLoad){window.atelierLoad(it.text,{format:it.format,gs1:it.gs1});showAtelier("create",screen);}
});

/* ---------- Historique ---------- */
const HKEY="scanalyse-history";
function history(){try{return JSON.parse(localStorage.getItem(HKEY)||"[]");}catch(e){return[];}}
function addHistory(it){const h=history();h.unshift(it);try{localStorage.setItem(HKEY,JSON.stringify(h.slice(0,50)));}catch(e){}}
function renderHistory(){
  const h=history();
  $("mList").innerHTML=h.length?h.map((it,i)=>`<li><button type="button" data-i="${i}"><code>${esc(it.text.length>120?it.text.slice(0,120)+"…":it.text)}</code><small>${esc(fmtName(it.format))} · ${esc(new Date(it.at).toLocaleString(EN?"en-GB":"fr-FR",{day:"2-digit",month:"2-digit",hour:"2-digit",minute:"2-digit"}))}</small></button></li>`).join("")
    :`<li class="m-empty">${esc(T("Aucune lecture pour l'instant. Les codes lus à la caméra apparaissent ici.","No reads yet. Codes read with the camera show up here."))}</li>`;
  $("mClear").hidden=!h.length;
  $("mList").querySelectorAll("button[data-i]").forEach(b=>b.addEventListener("click",()=>openSheet(h[+b.dataset.i],false)));
}
$("mClear").addEventListener("click",()=>{if(!confirm(T("Effacer tout l'historique de ce téléphone ?","Clear the whole history on this phone?")))return;try{localStorage.removeItem(HKEY);}catch(e){}renderHistory();});

/* ---------- Créer ---------- */
const ICON={
  link:'<path d="M10 14a4 4 0 0 0 5.7 0l3-3a4 4 0 0 0-5.7-5.7l-1 1M14 10a4 4 0 0 0-5.7 0l-3 3a4 4 0 0 0 5.7 5.7l1-1"/>',
  text:'<path d="M5 6h14M5 12h14M5 18h9"/>',
  wifi:'<path d="M2 9a15 15 0 0 1 20 0M5 13a10 10 0 0 1 14 0M8.5 16.5a5 5 0 0 1 7 0"/><circle cx="12" cy="20" r="1"/>',
  bars:'<path d="M4 5v14M7 5v14M10 5v14M14 5v14M17 5v14M20 5v14"/>',
  dm:'<rect x="4" y="4" width="16" height="16" rx="1"/><path d="M4 20V4h16M8 8h2v2H8zM13 9h3M9 14h2M14 13h2v3h-2"/>',
  gs1:'<path d="M3 7V5a2 2 0 0 1 2-2h2M17 3h2a2 2 0 0 1 2 2v2M21 17v2a2 2 0 0 1-2 2h-2M7 21H5a2 2 0 0 1-2-2v-2M8 8h3v3H8zM13 13h3v3h-3zM13 8h3M8 13v3"/>'
};
const TYPES=[
  {id:"url",fmt:"QRCode",icon:"link",fr:["Lien","QR vers un site web"],en:["Link","QR to a website"]},
  {id:"wifi",fmt:"QRCode",icon:"wifi",fr:["Wi-Fi","Se connecter en scannant"],en:["Wi-Fi","Connect by scanning"]},
  {id:"text",fmt:"QRCode",icon:"text",fr:["Texte","QR avec un texte libre"],en:["Text","QR with free text"]},
  {id:"ean13",fmt:"EAN13",icon:"bars",hrt:true,fr:["EAN-13","Article en magasin"],en:["EAN-13","Retail item"]},
  {id:"code128",fmt:"Code128",icon:"bars",hrt:true,fr:["Code 128","Étiquette interne, stock"],en:["Code 128","Internal label, stock"]},
  {id:"gs1128",fmt:"Code128",icon:"gs1",gs1:true,hrt:true,fr:["GS1-128","GTIN, lot, date, série"],en:["GS1-128","GTIN, batch, date, serial"]},
  {id:"datamatrix",fmt:"DataMatrix",icon:"dm",fr:["Data Matrix","Petites pièces, marquage"],en:["Data Matrix","Small parts, marking"]},
  {id:"gs1dm",fmt:"DataMatrix",icon:"gs1",gs1:true,fr:["GS1 DataMatrix","Santé, traçabilité"],en:["GS1 DataMatrix","Healthcare, traceability"]},
];
const byId=Object.fromEntries(TYPES.map(t=>[t.id,t]));
const name=t=>(EN?t.en:t.fr)[0];
$("mTiles").innerHTML=TYPES.map(t=>`<button type="button" class="m-tile" data-t="${t.id}"><svg viewBox="0 0 24 24" aria-hidden="true">${ICON[t.icon]}</svg><b>${esc(name(t))}</b><small>${esc((EN?t.en:t.fr)[1])}</small></button>`).join("");
$("mTiles").querySelectorAll(".m-tile").forEach(b=>b.addEventListener("click",()=>openEditor(b.dataset.t,null,"mCreate")));
$("mAll").addEventListener("click",()=>showAtelier("create","mCreate"));

const FIELDS={
  url:[{k:"url",type:"url",fr:"Adresse du site",en:"Website address",ph:"https://",def:"https://kanard-compagny.github.io/scanalyse/"}],
  text:[{k:"text",type:"area",fr:"Texte",en:"Text",def:"Scanalyse"}],
  wifi:[{k:"ssid",fr:"Nom du réseau (SSID)",en:"Network name (SSID)",def:""},{k:"pass",fr:"Mot de passe",en:"Password",def:""},
    {k:"sec",type:"select",fr:"Sécurité",en:"Security",opts:[["WPA","WPA / WPA2 / WPA3"],["WEP","WEP"],["nopass",EN?"None (open)":"Aucune (ouvert)"]],def:"WPA"},
    {k:"hidden",type:"check",fr:"Réseau masqué",en:"Hidden network",def:false}],
  ean13:[{k:"digits",type:"digits",fr:"Numéro",en:"Number",hint:EN?"12 digits: the check digit is added. 13 digits: it is checked.":"12 chiffres : la clé est ajoutée. 13 chiffres : elle est vérifiée.",def:"300123456789"}],
  code128:[{k:"text",fr:"Contenu",en:"Content",def:"LOT-2026-ABC"}],
  datamatrix:[{k:"text",fr:"Contenu",en:"Content",def:"SCANALYSE-0001"}],
  gs1128:"gs1",gs1dm:"gs1"
};
const GS1_FIELDS=[{k:"01",type:"digits",fr:"GTIN (01)",en:"GTIN (01)",hint:EN?"13 or 14 digits, check digit checked.":"13 ou 14 chiffres, clé vérifiée.",def:"03453120000011"},
  {k:"10",fr:"Lot (10)",en:"Batch (10)",def:"LOT42"},{k:"17",type:"date",fr:"Date d'expiration (17)",en:"Expiry date (17)",def:""},
  {k:"21",fr:"Numéro de série (21)",en:"Serial number (21)",def:""}];
let cur=null,vals={},renderSeq=0,lastPng=null;
function fieldsOf(id){const f=FIELDS[id];return f==="gs1"?GS1_FIELDS:f;}
function openEditor(id,values,from){
  cur=id;const t=byId[id];
  vals={};fieldsOf(id).forEach(f=>vals[f.k]=values&&values[f.k]!==undefined?values[f.k]:f.def);
  $("mEditTitle").textContent=name(t);
  $("mForm").innerHTML=fieldsOf(id).map(f=>{
    const lbl=`${esc(EN?f.en:f.fr)}${f.hint?`<small>${esc(f.hint)}</small>`:""}`;
    if(f.type==="area")return`<label>${lbl}<textarea name="${f.k}">${esc(vals[f.k])}</textarea></label>`;
    if(f.type==="select")return`<label>${lbl}<select name="${f.k}">${f.opts.map(([v,l])=>`<option value="${v}"${v===vals[f.k]?" selected":""}>${esc(l)}</option>`).join("")}</select></label>`;
    if(f.type==="check")return`<label class="m-row"><input type="checkbox" name="${f.k}"${vals[f.k]?" checked":""}>${esc(EN?f.en:f.fr)}</label>`;
    const it=f.type==="digits"?' inputmode="numeric" pattern="[0-9]*"':f.type==="url"?' type="url" inputmode="url"':f.type==="date"?' type="date"':"";
    return`<label>${lbl}<input name="${f.k}"${it} value="${esc(vals[f.k])}"${f.ph?` placeholder="${esc(f.ph)}"`:""} autocapitalize="off" autocorrect="off" spellcheck="false"></label>`;
  }).join("");
  $("mForm").querySelectorAll("input,textarea,select").forEach(e=>e.addEventListener("input",()=>{vals[e.name]=e.type==="checkbox"?e.checked:e.value;draw();}));
  show("mEdit",from||"mCreate");$("mEdit").scrollTop=0;
  draw();
}
$("mForm").addEventListener("submit",e=>e.preventDefault());
function check(d){let s=0;for(let i=0;i<d.length;i++)s+=+d[d.length-1-i]*(i%2?1:3);return(10-s%10)%10;}
const wifiEsc=s=>String(s).replace(/([\\;,:"])/g,"\\$1");
/* Données à encoder, ou une erreur expliquée */
function data(){
  const t=byId[cur];
  if(cur==="url"){const u=(vals.url||"").trim();return u?{d:u}:{err:T("Saisis une adresse.","Enter an address.")};}
  if(cur==="text"||cur==="code128"||cur==="datamatrix"){return vals.text?{d:vals.text}:{err:T("Le contenu est vide.","The content is empty.")};}
  if(cur==="wifi"){if(!vals.ssid)return{err:T("Saisis le nom du réseau.","Enter the network name.")};
    return{d:`WIFI:T:${vals.sec};S:${wifiEsc(vals.ssid)};${vals.sec==="nopass"?"":`P:${wifiEsc(vals.pass||"")};`}${vals.hidden?"H:true;":""};`};}
  if(cur==="ean13"){let d=(vals.digits||"").replace(/\s/g,"");
    if(!/^\d+$/.test(d))return{err:T("Seuls les chiffres sont acceptés.","Only digits are accepted.")};
    if(d.length===12)d+=check(d);else if(d.length===13){if(check(d.slice(0,12))!==+d[12])return{err:T(`Clé de contrôle fausse : ${d[12]} au lieu de ${check(d.slice(0,12))}.`,`Wrong check digit: ${d[12]} instead of ${check(d.slice(0,12))}.`)};}
    else return{err:T(`12 ou 13 chiffres attendus (${d.length} saisis).`,`12 or 13 digits expected (${d.length} entered).`)};
    return{d};}
  if(t.gs1){let g=(vals["01"]||"").replace(/\s/g,"");
    if(!/^\d{13,14}$/.test(g))return{err:T("GTIN : 13 ou 14 chiffres.","GTIN: 13 or 14 digits.")};
    g=g.padStart(14,"0");if(check(g.slice(0,13))!==+g[13])return{err:T(`GTIN : clé de contrôle fausse (${check(g.slice(0,13))} attendu).`,`GTIN: wrong check digit (${check(g.slice(0,13))} expected).`)};
    let s=`(01)${g}`;
    const dt=vals["17"];if(dt){const m=/^(\d{4})-(\d{2})-(\d{2})$/.exec(dt);if(m)s+=`(17)${m[1].slice(2)}${m[2]}${m[3]}`;}
    for(const ai of ["10","21"]){const v=(vals[ai]||"").trim();if(v){if(v.length>20)return{err:T(`(${ai}) : 20 caractères au plus.`,`(${ai}): 20 characters at most.`)};s+=`(${ai})${v}`;}}
    return{d:s,gs1:true};}
  return{err:"?"};
}
async function draw(){
  const id=++renderSeq,t=byId[cur],r=data();
  const chk=$("mCheck");chk.className="m-check";
  if(r.err){chk.textContent=r.err;chk.classList.add("bad");$("mCode").removeAttribute("src");lastPng=null;return;}
  try{
    if(window.atelierEngine)await window.atelierEngine();
    const out=await ZXingWASM.writeBarcode(r.d,{format:t.fmt,scale:6,withHRT:!!t.hrt,withQuietZones:true,options:r.gs1?"gs1":""});
    if(id!==renderSeq)return;
    if(out.error){chk.textContent=out.error.replace(/^Error \d+:\s*/,"").replace(/\s*\(retval.*$/,"");chk.classList.add("bad");return;}
    const blob=out.image instanceof Blob?out.image:new Blob([out.svg],{type:"image/svg+xml"});
    const png=await toPng(blob);if(id!==renderSeq)return;
    lastPng=png;$("mCode").src=URL.createObjectURL(png);
    // Relecture : le code produit doit redonner le contenu saisi
    const back=await ZXingWASM.readBarcodes(png,{tryHarder:true,textMode:"HRI",maxNumberOfSymbols:1});
    if(id!==renderSeq)return;
    const ok=back&&back[0]&&back[0].text===r.d;
    chk.textContent=ok?T("Vérifié : le code se relit correctement.","Verified: the code reads back correctly."):T("Attention : la relecture ne redonne pas exactement le contenu saisi.","Warning: reading it back does not give exactly the content entered.");
    chk.classList.add(ok?"ok":"bad");
  }catch(e){chk.textContent=T("Création impossible.","Could not create the code.");chk.classList.add("bad");}
}
function toPng(blob){
  return new Promise((res,rej)=>{const img=new Image(),u=URL.createObjectURL(blob);
    img.onload=()=>{const c=document.createElement("canvas");c.width=img.naturalWidth;c.height=img.naturalHeight;const x=c.getContext("2d");x.fillStyle="#fff";x.fillRect(0,0,c.width,c.height);
      x.imageSmoothingEnabled=false;x.drawImage(img,0,0);URL.revokeObjectURL(u);c.toBlob(b=>b?res(b):rej(new Error("png")),"image/png");};
    img.onerror=()=>{URL.revokeObjectURL(u);rej(new Error("img"));};img.src=u;});
}
const fileName=()=>`${cur}-${new Date().toISOString().slice(0,10)}.png`;
$("mSave").addEventListener("click",()=>{if(!lastPng)return;const a=document.createElement("a");a.href=URL.createObjectURL(lastPng);a.download=fileName();document.body.appendChild(a);a.click();a.remove();});
const canShare=!!(navigator.canShare&&navigator.canShare({files:[new File([""],"t.png",{type:"image/png"})]}));
$("mShare").hidden=!canShare;
$("mShare").addEventListener("click",async()=>{if(!lastPng)return;try{await navigator.share({files:[new File([lastPng],fileName(),{type:"image/png"})]});}catch(e){}});
let wake=null;
$("mFull").addEventListener("click",async()=>{if(!lastPng)return;$("mFullImg").src=$("mCode").src;$("mFullscreen").hidden=false;
  try{wake=await navigator.wakeLock.request("screen");}catch(e){}});
$("mFullscreen").addEventListener("click",()=>{$("mFullscreen").hidden=true;if(wake){wake.release().catch(()=>{});wake=null;}});

/* Recréer : le type simple correspondant si possible */
function presetFor(it){
  const f=it.format||"";
  if(it.gs1&&window.atelierParseGs1){const els=window.atelierParseGs1(it.text)||[];const v={};els.forEach(([a,x])=>v[a]=x);
    if(v["17"]&&/^\d{6}$/.test(v["17"]))v["17"]=`20${v["17"].slice(0,2)}-${v["17"].slice(2,4)}-${v["17"].slice(4,6)}`;
    if(els.every(([a])=>["01","10","17","21"].includes(a)))return{id:/DataMatrix/.test(f)?"gs1dm":"gs1128",values:v};return null;}
  if(f==="EAN13")return{id:"ean13",values:{digits:it.text}};
  if(f==="Code128")return{id:"code128",values:{text:it.text}};
  if(f==="DataMatrix")return{id:"datamatrix",values:{text:it.text}};
  if(/^QRCode/.test(f)){if(/^https?:\/\//i.test(it.text))return{id:"url",values:{url:it.text}};if(!/^WIFI:/i.test(it.text))return{id:"text",values:{text:it.text}};}
  return null;
}

/* ---------- Départ ---------- */
show("mScan");
})();
