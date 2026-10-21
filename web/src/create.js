(function(){
const $=id=>document.getElementById(id);
const IN_CLAUDE=!!(window.claude&&window.claude.use);
const DESKTOP=!!(window.chrome&&window.chrome.webview);
const esc=s=>String(s).replace(/[&<>"']/g,c=>({"&":"&amp;","<":"&lt;",">":"&gt;",'"':"&quot;","'":"&#39;"}[c]));

/* ================= Types de codes ================= */
const TYPES=[
  {id:"qr",cat:"qr",label:"QR Code personnalisé",info:"Couleurs, formes, logo central. Liens, Wi-Fi, cartes de visite, paiements."},
  {id:"datamatrix",cat:"2d",label:"Data Matrix",fmt:"DataMatrix",input:"text",square:true,info:"<b>Jusqu'à 1 556 octets.</b> Industrie, marquage direct de pièces, petites étiquettes."},
  {id:"gs1datamatrix",cat:"2d",label:"GS1 DataMatrix",fmt:"DataMatrix",input:"gs1",gs1:true,square:true,info:"<b>Données GS1 (AI).</b> Santé (sérialisation, UDI), traçabilité industrielle."},
  {id:"gs1qr",cat:"2d",label:"GS1 QR Code",fmt:"QRCode",input:"gs1",gs1:true,info:"<b>Données GS1 (AI) dans un QR.</b> Traçabilité lisible par smartphone."},
  {id:"pdf417",cat:"2d",label:"PDF417",fmt:"PDF417",input:"text",info:"<b>Jusqu'à 1 850 caractères.</b> Cartes d'embarquement, permis, profils de configuration de terminaux."},
  {id:"aztec",cat:"2d",label:"Aztec",fmt:"Aztec",input:"text",info:"<b>Sans zone de silence.</b> Billets de transport, cartes d'embarquement."},
  {id:"microqr",cat:"2d",label:"Micro QR Code",fmt:"MicroQRCode",input:"text",info:"<b>35 chiffres au plus.</b> Marquage de très petites pièces."},
  {id:"rmqr",cat:"2d",label:"rMQR (QR rectangulaire)",fmt:"RMQRCode",input:"text",info:"<b>Format allongé.</b> Bords de cartons, tubes, surfaces étroites."},
  {id:"ean13",cat:"retail",label:"EAN-13",fmt:"EAN13",input:"digits",len:[12,13],hrt:true,info:"<b>12 chiffres + clé.</b> Articles en point de vente (GTIN-13). Clé calculée automatiquement."},
  {id:"ean8",cat:"retail",label:"EAN-8",fmt:"EAN8",input:"digits",len:[7,8],hrt:true,info:"<b>7 chiffres + clé.</b> Petits articles en point de vente."},
  {id:"upca",cat:"retail",label:"UPC-A",fmt:"UPCA",input:"digits",len:[11,12],hrt:true,info:"<b>11 chiffres + clé.</b> Articles en point de vente (Amérique du Nord)."},
  {id:"upce",cat:"retail",label:"UPC-E",fmt:"UPCE",input:"digits",len:[7,8],hrt:true,info:"<b>Forme courte d'un UPC-A</b> (commence par 0 ou 1). Petits articles."},
  {id:"itf14",cat:"retail",label:"ITF-14",fmt:"ITF14",input:"digits",len:[13,14],hrt:true,info:"<b>13 chiffres + clé.</b> Cartons d'expédition (GTIN-14)."},
  {id:"databar",cat:"retail",label:"GS1 DataBar",fmt:"DataBar",input:"digits",len:[13,14],info:"<b>GTIN-14 compact.</b> Fruits et légumes, petits articles."},
  {id:"databarexp",cat:"retail",label:"GS1 DataBar Expanded",fmt:"DataBarExp",input:"gs1",gs1:true,info:"<b>GTIN + poids, prix, dates.</b> Produits frais à poids variable, coupons."},
  {id:"code128",cat:"ind",label:"Code 128",fmt:"Code128",input:"text",hrt:true,info:"<b>ASCII complet.</b> Étiquettes internes, logistique, inventaire."},
  {id:"gs1128",cat:"ind",label:"GS1-128",fmt:"Code128",input:"gs1",gs1:true,hrt:true,info:"<b>Données GS1 (AI).</b> Étiquettes logistiques (SSCC, lot, dates, poids)."},
  {id:"code39",cat:"ind",label:"Code 39",fmt:"Code39",input:"text",hrt:true,upper:true,info:"<b>Majuscules, chiffres, - . espace $ / + %.</b> Industrie, automobile, badges."},
  {id:"code93",cat:"ind",label:"Code 93",fmt:"Code93",input:"text",hrt:true,info:"<b>ASCII complet, plus compact que le Code 39.</b> Industrie."},
  {id:"codabar",cat:"ind",label:"Codabar",fmt:"Codabar",input:"text",hrt:true,info:"<b>Chiffres et - $ : / . +, encadrés par A, B, C ou D.</b> Bibliothèques, banques du sang."},
  {id:"itf",cat:"ind",label:"ITF (2 parmi 5 entrelacé)",fmt:"ITF",input:"digits",hrt:true,even:true,info:"<b>Nombre pair de chiffres.</b> Cartons, entrepôts."}
];
const DEFAULTS={datamatrix:"SCANALYSE-0001",pdf417:"Scanalyse · PDF417 · exemple",aztec:"SCANALYSE-AZTEC",microqr:"12345",rmqr:"SCANALYSE",
  ean13:"300123456789",ean8:"9638507",upca:"03600029145",upce:"0123456",itf14:"1345312000001",databar:"0950600013435",
  code128:"LOT-2026-ABC",code39:"ABC-123",code93:"CODE93",codabar:"A12345B",itf:"12345678"};
const byId=Object.fromEntries(TYPES.map(t=>[t.id,t]));
let current="qr";
const values={};          // contenu saisi, par type
let gs1Rows=null;         // champs GS1 partagés entre les types GS1

/* ================= Identifiants d'application GS1 proposés ================= */
const AIS=[
  ["01","GTIN (article)","gtin"],["02","GTIN des articles contenus","gtin"],["00","SSCC (colis, palette)","sscc"],
  ["10","Numéro de lot","text"],["21","Numéro de série","text"],["11","Date de production","date"],["13","Date d'emballage","date"],
  ["15","Date de durabilité minimale (DDM)","date"],["17","Date d'expiration (DLC)","date"],["30","Quantité variable","num"],
  ["37","Nombre d'unités contenues","num"],["3103","Poids net, kg (3 décimales)","dec"],["3102","Poids net, kg (2 décimales)","dec"],
  ["3302","Poids brut, kg (2 décimales)","dec"],["3922","Prix à payer (2 décimales)","dec"],["400","N° de commande client","text"],
  ["410","Livrer à (GLN)","gln"],["414","Emplacement physique (GLN)","gln"],["422","Pays d'origine (code ISO)","num"],
  ["240","Référence article additionnelle","text"],["241","Référence client","text"],["8200","URL de l'emballage étendu","text"],
  ["90","Information convenue entre partenaires","text"],["91","Information interne","text"]
];
const AI=Object.fromEntries(AIS.map(a=>[a[0],a]));
function check(body){let s=0;for(let i=body.length-1,w=3;i>=0;i--,w=4-w)s+=(+body[i])*w;return(10-s%10)%10;}
function ymd(d){return String(d.getFullYear()%100).padStart(2,"0")+String(d.getMonth()+1).padStart(2,"0")+String(d.getDate()).padStart(2,"0");}
function defaultGs1(){const d=new Date();d.setFullYear(d.getFullYear()+1);return[{ai:"01",v:"03453120000011"},{ai:"17",v:ymd(d)},{ai:"10",v:"LOT42"}];}

/* Valeur normalisée d'un champ GS1 + message (clé calculée, erreur…) */
function normAi(ai,v){
  const kind=(AI[ai]||[0,0,"text"])[2];v=(v||"").trim();
  const digits=/^\d*$/.test(v);
  const full={gtin:14,sscc:18,gln:13}[kind];
  if(full){
    if(!digits)return{v,err:"chiffres uniquement"};
    if(kind==="gtin"&&v.length>=8&&v.length<=13&&v.length!==full-1){v=v.padStart(14,"0");}
    if(v.length===full-1){const c=check(v);return{v:v+c,note:"clé "+c+" ajoutée"};}
    if(v.length===full){return check(v.slice(0,-1))===+v.slice(-1)?{v,note:"clé valide"}:{v,err:"clé invalide (attendue "+check(v.slice(0,-1))+")"};}
    return{v,err:(full-1)+" ou "+full+" chiffres attendus"};
  }
  if(kind==="date"){if(/^\d{4}-\d{2}-\d{2}$/.test(v))v=v.slice(2,4)+v.slice(5,7)+v.slice(8,10);return /^\d{6}$/.test(v)?{v}:{v,err:"date attendue"};}
  if(kind==="dec"){const n=+ai[3];const num=parseFloat(v.replace(",","."));if(isNaN(num))return{v,err:"nombre attendu"};
    const s=String(Math.round(num*Math.pow(10,n)));return s.length<=6?{v:s.padStart(6,"0"),note:num.toLocaleString("fr-FR",{minimumFractionDigits:n})}:{v,err:"valeur trop grande"};}
  if(kind==="num")return digits&&v.length?{v}:{v,err:"chiffres uniquement"};
  return v.length?{v}:{v,err:"valeur vide"};
}
function gs1String(){return gs1Rows.map(r=>"("+r.ai+")"+normAi(r.ai,r.v).v).join("");}

/* ================= Interface ================= */
let cat="qr";
function renderTypes(){
  document.querySelectorAll(".tp-cats button").forEach(b=>b.setAttribute("aria-selected",String(b.dataset.cat===cat)));
  const list=TYPES.filter(t=>t.cat===cat&&cat!=="qr");
  $("tpTypes").innerHTML=list.map(t=>`<label class="chip"><input type="radio" name="ctype" value="${t.id}" ${t.id===current?"checked":""}><span>${esc(t.label)}</span></label>`).join("");
  const t=byId[current];$("tpInfo").innerHTML=(t&&t.cat===cat)?`${esc(t.label)} · ${t.info}`:"";
}
document.querySelectorAll(".tp-cats button").forEach(b=>b.addEventListener("click",()=>{
  cat=b.dataset.cat;
  if(cat==="qr")selectType("qr");else{const first=TYPES.find(t=>t.cat===cat&&t.id===current)||TYPES.find(t=>t.cat===cat);selectType(first.id);}
}));
$("tpTypes").addEventListener("change",e=>{if(e.target.name==="ctype")selectType(e.target.value);});

function selectType(id,keepValues){
  if(!keepValues)saveInput();
  current=byId[id]?id:"qr";const t=byId[current];cat=t.cat;
  renderTypes();
  const qr=current==="qr";
  $("qrMode").hidden=!qr;$("stdMode").hidden=qr;
  if(qr)return;
  ["stdText","stdDigits","stdGs1"].forEach(p=>$(p).hidden=true);
  if(t.input==="text"){$("stdText").hidden=false;$("stdTextInput").value=values[current]??DEFAULTS[current]??"";
    $("stdTextHelp").textContent=t.upper?"Les minuscules sont converties en majuscules.":t.id==="codabar"?"Commence et termine par A, B, C ou D.":"";}
  if(t.input==="digits"){$("stdDigits").hidden=false;$("stdDigitsInput").value=values[current]??DEFAULTS[current]??"";
    $("stdDigitsLabel").textContent=t.len?`Numéro (${t.len[0]} chiffres, ou ${t.len[1]} avec la clé)`:"Numéro (nombre pair de chiffres)";}
  if(t.input==="gs1"){$("stdGs1").hidden=false;if(!gs1Rows)gs1Rows=defaultGs1();renderGs1();}
  $("stdHrtWrap").hidden=!t.hrt;$("stdSquareWrap").hidden=!t.square;
  scheduleRender();
}
function saveInput(){
  const t=byId[current];if(!t||current==="qr")return;
  if(t.input==="text")values[current]=$("stdTextInput").value;
  if(t.input==="digits")values[current]=$("stdDigitsInput").value;
}
window.atelierSetCreateType=function(id){if(window.atelierSetMode)window.atelierSetMode("create",false);selectType(id);};

/* --- Assistant GS1 --- */
$("gs1AddSel").innerHTML=AIS.map(a=>`<option value="${a[0]}">(${a[0]}) ${esc(a[1])}</option>`).join("");
function renderGs1(){
  $("gs1Rows").innerHTML=gs1Rows.map((r,i)=>{
    const a=AI[r.ai]||[r.ai,"AI "+r.ai,"text"];const n=normAi(r.ai,r.v);
    const isDate=a[2]==="date";const val=isDate&&/^\d{6}$/.test(r.v)?`20${r.v.slice(0,2)}-${r.v.slice(2,4)}-${r.v.slice(4,6)}`:r.v;
    return `<div class="gs1-row">
      <label class="f">Champ<select data-i="${i}" data-k="ai">${AIS.map(x=>`<option value="${x[0]}" ${x[0]===r.ai?"selected":""}>(${x[0]}) ${esc(x[1])}</option>`).join("")}${AI[r.ai]?"":`<option value="${esc(r.ai)}" selected>(${esc(r.ai)})</option>`}</select></label>
      <label class="f">Valeur<input data-i="${i}" data-k="v" type="${isDate?"date":"text"}" value="${esc(val)}" spellcheck="false" autocomplete="off"></label>
      <button class="btn" type="button" data-del="${i}" aria-label="Retirer ce champ">Retirer</button>
      ${n.err?`<span class="ko gs1-msg">${esc(n.err)}</span>`:n.note?`<span class="ok gs1-msg">${esc(n.note)}</span>`:""}</div>`;}).join("");
  $("gs1Raw").value=gs1String();
}
$("gs1Rows").addEventListener("change",e=>{const i=+e.target.dataset.i;if(isNaN(i))return;
  if(e.target.dataset.k==="ai"){gs1Rows[i].ai=e.target.value;gs1Rows[i].v="";}else gs1Rows[i].v=e.target.value;
  renderGs1();scheduleRender();});
$("gs1Rows").addEventListener("input",e=>{const i=+e.target.dataset.i;if(isNaN(i)||e.target.dataset.k!=="v"||e.target.type==="date")return;
  gs1Rows[i].v=e.target.value;$("gs1Raw").value=gs1String();scheduleRender();});
$("gs1Rows").addEventListener("click",e=>{const d=e.target.dataset.del;if(d===undefined)return;gs1Rows.splice(+d,1);if(!gs1Rows.length)gs1Rows.push({ai:"01",v:""});renderGs1();scheduleRender();});
$("gs1Add").addEventListener("click",()=>{gs1Rows.push({ai:$("gs1AddSel").value,v:""});renderGs1();scheduleRender();});
$("gs1Raw").addEventListener("change",()=>{const rows=parseHri($("gs1Raw").value);if(rows.length){gs1Rows=rows;renderGs1();scheduleRender();}});
function parseHri(s){const out=[];const re=/\((\d{2,4})\)([^(]*)/g;let m;while((m=re.exec(s)))out.push({ai:m[1],v:m[2]});return out;}

/* --- Réglages d'apparence --- */
function linkColor(id){$(id).addEventListener("input",()=>{$(id+"T").value=$(id).value;scheduleRender();});
  $(id+"T").addEventListener("input",()=>{let v=$(id+"T").value.trim();if(!v.startsWith("#"))v="#"+v;if(/^#[0-9a-f]{6}$/i.test(v)){$(id).value=v.toLowerCase();scheduleRender();}});}
linkColor("stdFg");linkColor("stdBg");
["stdTextInput","stdDigitsInput","stdScale","stdRotate","stdHrt","stdQuiet","stdTransparent","stdSquare","stdDpi","stdDots"].forEach(id=>{
  $(id).addEventListener("input",scheduleRender);$(id).addEventListener("change",scheduleRender);});
$("stdScale").addEventListener("input",()=>{$("stdDots").value=$("stdScale").value;});

/* ================= Génération ================= */
let timer=null,seq=0,last=null;
function scheduleRender(){clearTimeout(timer);timer=setTimeout(renderStd,120);
  document.querySelectorAll('#stdMode input[type=range]').forEach(r=>r.style.setProperty("--p",((r.value-r.min)/(r.max-r.min)*100)+"%"));}
async function engine(){
  if(!window.atelierEngine)throw new Error("moteur absent");
  await window.atelierEngine();
  if(!window.ZXingWASM||!ZXingWASM.writeBarcode)throw new Error("générateur absent");
  return ZXingWASM;
}
/* Données à encoder pour le type courant, ou erreur lisible */
function currentInput(){
  const t=byId[current];
  if(t.input==="gs1"){
    const bad=gs1Rows.map(r=>({r,n:normAi(r.ai,r.v)})).find(x=>x.n.err);
    if(bad)return{err:`Champ (${bad.r.ai}) : ${bad.n.err}.`};
    if(!gs1Rows.length)return{err:"Ajoute au moins un champ GS1."};
    return{data:gs1String(),shown:gs1String()};
  }
  if(t.input==="digits"){
    let d=$("stdDigitsInput").value.replace(/\s/g,"");
    if(!/^\d+$/.test(d))return{err:"Seuls les chiffres sont acceptés."};
    if(t.even&&d.length%2)return{err:"L'ITF exige un nombre pair de chiffres (ajoute un 0 devant)."};
    if(t.len){
      if(d.length===t.len[1]){const c=check(d.slice(0,-1));if(c!==+d.slice(-1))return{err:`Clé de contrôle invalide : ${d.slice(-1)} au lieu de ${c}.`};}
      else if(d.length===t.len[0]){d=d+check(d);}
      else return{err:`${t.len[0]} chiffres (ou ${t.len[1]} avec la clé) attendus : ${d.length} saisis.`};
    }
    return{data:d,shown:d};
  }
  let s=$("stdTextInput").value;if(t.upper)s=s.toUpperCase();
  if(!s.length)return{err:"Le contenu est vide."};
  return{data:s,shown:s};
}
function recolor(svg,fg,bg,transparent){
  const m=svg.match(/<svg[^>]*\swidth="(\d+(?:\.\d+)?)"[^>]*\sheight="(\d+(?:\.\d+)?)"/);
  if(m&&!/viewBox=/.test(svg))svg=svg.replace("<svg ",`<svg viewBox="0 0 ${m[1]} ${m[2]}" `);
  return svg.replace(/<\?xml[^>]*>\s*/,"").replace(/<!DOCTYPE[^>]*>\s*/,"")
    .replace(/fill="#000000"/g,`fill="${fg}"`).replace(/fill="#FFFFFF"/gi,transparent?'fill="none"':`fill="${bg}"`);
}
async function writeFor(typeId,data,opts){
  const t=byId[typeId];const Z=await engine();
  return Z.writeBarcode(data,{format:t.fmt,scale:opts.scale||2,rotate:opts.rotate||0,withHRT:!!(t.hrt&&opts.hrt),
    withQuietZones:opts.quiet!==false,options:t.gs1?"gs1":"",forceSquareDataMatrix:!!(t.square&&opts.square)});
}
function svgToCanvas(svgText,scale,pad,bg){
  return new Promise((res,rej)=>{
    const img=new Image();const url=URL.createObjectURL(new Blob([svgText],{type:"image/svg+xml"}));
    img.onload=()=>{const c=document.createElement("canvas");c.width=Math.round(img.width*scale)+pad*2;c.height=Math.round(img.height*scale)+pad*2;
      const x=c.getContext("2d");if(bg){x.fillStyle=bg;x.fillRect(0,0,c.width,c.height);}x.imageSmoothingEnabled=false;x.drawImage(img,pad,pad,c.width-pad*2,c.height-pad*2);URL.revokeObjectURL(url);res(c);};
    img.onerror=()=>{URL.revokeObjectURL(url);rej(new Error("svg"));};img.src=url;});
}
function lum(h){const c=[1,3,5].map(i=>parseInt(h.substr(i,2),16)/255).map(v=>v<=.03928?v/12.92:Math.pow((v+.055)/1.055,2.4));return .2126*c[0]+.7152*c[1]+.0722*c[2];}
async function renderStd(){
  if(current==="qr")return;
  const id=++seq;const t=byId[current];
  const inp=currentInput();
  $("stdScaleV").textContent=$("stdScale").value+" px";$("stdDotsV").textContent=$("stdDots").value;
  const status=$("stdStatus");
  if(inp.err){status.className="status warn";status.textContent=inp.err;$("stdPreview").innerHTML="";$("stdPayload").textContent="";last=null;printInfo(null);return;}
  $("stdPayload").textContent=inp.shown;
  const fg=$("stdFg").value,bg=$("stdBg").value,tr=$("stdTransparent").checked;
  let r;
  try{r=await writeFor(current,inp.data,{scale:+$("stdScale").value,rotate:+$("stdRotate").value,hrt:$("stdHrt").checked,quiet:$("stdQuiet").checked,square:$("stdSquare").checked});}
  catch(e){status.className="status warn";status.textContent="Générateur indisponible dans ce navigateur.";return;}
  if(id!==seq)return;
  if(r.error){status.className="status warn";status.textContent="Contenu refusé par le "+t.label+" : "+r.error.replace(/^Error \d+:\s*/,"").replace(/\s*\(retval.*$/,"");$("stdPreview").innerHTML="";last=null;printInfo(null);return;}
  const svg=recolor(r.svg,fg,bg,tr);
  $("stdPreview").innerHTML=svg;
  $("stdPreview").classList.toggle("is2d",t.cat==="2d"||t.id==="databar");
  last={svg,data:inp.data,shown:inp.shown,symbol:r.symbol,type:current};
  printInfo(r.symbol);
  // Vérification : on relit le code généré
  const msgs=[];
  const ratio=(Math.max(lum(fg),lum(tr?"#ffffff":bg))+.05)/(Math.min(lum(fg),lum(tr?"#ffffff":bg))+.05);
  if(ratio<3)msgs.push(`Contraste insuffisant (${ratio.toFixed(1)}:1) entre barres et fond.`);
  if(lum(fg)>lum(tr?"#ffffff":bg))msgs.push("Barres plus claires que le fond : beaucoup de lecteurs refusent les codes inversés.");
  if(!$("stdQuiet").checked&&t.id!=="aztec")msgs.push("Sans zones de silence, prévois une marge blanche autour du code à l'impression.");
  try{
    const c=await svgToCanvas(svg,Math.max(1,Math.ceil(4/+$("stdScale").value)),24,tr?"#ffffff":bg);
    const read=await ZXingWASM.readBarcodes(c.getContext("2d").getImageData(0,0,c.width,c.height),{tryHarder:true,tryRotate:true,tryInvert:true,textMode:"HRI"});
    if(id!==seq)return;
    const ok=read.find(x=>x.isValid!==false);
    if(!ok)msgs.unshift("Le code généré n'a pas pu être relu : vérifie les couleurs et le contenu.");
    status.className="status "+(msgs.length?"warn":"ok");
    status.textContent=msgs.length?msgs.join(" "):`Vérifié : relu correctement${ok.symbologyIdentifier?" (identifiant "+ok.symbologyIdentifier+")":""}.`;
  }catch(e){status.className="status "+(msgs.length?"warn":"ok");status.textContent=msgs.join(" ")||"Généré.";}
}
function printInfo(sym){
  const el=$("stdPrint");if(!sym||!sym.width){el.textContent="";return;}
  const dpi=+$("stdDpi").value,dots=+$("stdDots").value,t=byId[current];
  const mm=dots/dpi*25.4;const w=sym.width*mm,h=sym.height*mm;
  const f=v=>v.toLocaleString("fr-FR",{maximumFractionDigits:2});
  let txt=`Module : ${f(mm)} mm (${dots} point${dots>1?"s":""} à ${dpi} dpi). Code : ${f(w)} × ${f(h)} mm hors marges (${sym.width} × ${sym.height} modules).`;
  if(t.cat==="retail"&&/EAN|UPC/.test(t.fmt)){txt+=mm<0.264?" Trop petit pour la grande distribution : GS1 recommande 0,33 mm (0,264 mm au minimum).":" Conforme aux recommandations GS1 pour la distribution (0,264 à 0,66 mm).";}
  if(t.id==="gs1128"||t.id==="itf14"){txt+=mm<0.495?" Pour la logistique, GS1 demande au moins 0,495 mm par module.":" Conforme pour la logistique (au moins 0,495 mm).";}
  if(t.id==="gs1datamatrix"){txt+=mm<0.255?" Module très petit : GS1 recommande au moins 0,255 mm (santé).":"";}
  el.textContent=txt;
}

/* ================= Export ================= */
let toastT;function toast(m){$("stdToast").textContent=m;clearTimeout(toastT);toastT=setTimeout(()=>$("stdToast").textContent="",6000);}
async function saveBlob(blob,name){
  if(IN_CLAUDE){
    let d=null;try{d=await claude.use("downloads");}catch(e){}
    if(d){try{await d.save({filename:name,data:blob});toast("Fichier enregistré : "+name);return;}
      catch(e){if(e&&e.code==="declined"){toast("Téléchargement annulé.");return;}}}
    const r=new FileReader();r.onload=()=>{$("stdFallbackImg").src=r.result;$("stdFallback").hidden=false;};r.readAsDataURL(blob);return;
  }
  const a=document.createElement("a");a.href=URL.createObjectURL(blob);a.download=name;document.body.appendChild(a);a.click();a.remove();
  setTimeout(()=>URL.revokeObjectURL(a.href),4000);toast("Téléchargé : "+name);
}
$("stdDl").addEventListener("click",async()=>{
  if(!last){toast("Rien à exporter : corrige d'abord le contenu.");return;}
  const name=`${last.type}-${(last.shown||"code").replace(/[^A-Za-z0-9-]+/g,"_").slice(0,40)}`;
  if($("stdFmt").value==="svg"){await saveBlob(new Blob([last.svg],{type:"image/svg+xml"}),name+".svg");return;}
  try{const c=await svgToCanvas(last.svg,+$("stdExportScale").value,0,$("stdTransparent").checked?null:$("stdBg").value);
    c.toBlob(b=>saveBlob(b,name+".png"),"image/png");}catch(e){toast("L'export a échoué.");}
});
$("stdCopy").addEventListener("click",()=>{if(!last)return;
  (navigator.clipboard?navigator.clipboard.writeText(last.shown):Promise.reject()).then(()=>toast("Contenu copié."),()=>toast("Copie impossible ici : sélectionne le texte sous l'aperçu."));});

/* Analyse complète du code créé, comme une image reçue d'un client */
$("stdAnalyse").addEventListener("click",async()=>{
  if(!last){toast("Rien à analyser : corrige d'abord le contenu.");return;}
  try{const c=await svgToCanvas(last.svg,2,24,"#ffffff");
    window.atelierLoadImageData(c.toDataURL("image/png"),"Code créé : "+((byId[current]||{}).label||"code"));}
  catch(e){toast("Analyse impossible.");}
});

/* ================= Liens avec l'analyse et le programme Windows ================= */
const FROM_FORMAT={DataMatrix:"datamatrix",PDF417:"pdf417",CompactPDF417:"pdf417",Aztec:"aztec",AztecCode:"aztec",MicroQRCode:"microqr",RMQRCode:"rmqr",
  EAN13:"ean13",ISBN:"ean13",EAN8:"ean8",UPCA:"upca",UPCE:"upce",ITF14:"itf14",ITF:"itf",DataBar:"databar",DataBarOmni:"databar",DataBarExp:"databarexp",
  Code128:"code128",Code39:"code39",Code39Std:"code39",Code93:"code93",Codabar:"codabar"};
const GS1_OF={DataMatrix:"gs1datamatrix",QRCode:"gs1qr",QRCodeModel2:"gs1qr",Code128:"gs1128",DataBarExp:"databarexp"};
const qrLoad=window.atelierLoad;
/* Recrée un code lu dans une image : bon type, bon contenu */
window.atelierLoad=function(text,opts){
  opts=opts||{};const f=opts.format||"";
  let id=opts.gs1?GS1_OF[f]:FROM_FORMAT[f];
  if(!id&&opts.gs1)id="gs1datamatrix";
  if(f==="ITF"&&/^\d{14}$/.test(text))id="itf14";
  if(!id||(!opts.gs1&&/^QRCode/.test(f))){selectType("qr");if(qrLoad)qrLoad(text);return;}
  saveInput();
  const t=byId[id];
  if(t.input==="gs1"){const rows=parseHri(text);gs1Rows=rows.length?rows:defaultGs1();}
  else values[id]=text;
  selectType(id,true);
};
/* Rendu d'un aperçu pour le banc d'essai du programme Windows */
window.atelierRender=async function(reqId,text,typeId){
  const reply=o=>{if(DESKTOP)window.chrome.webview.postMessage(JSON.stringify({type:"rendered",id:reqId,...o}));};
  try{
    const t=byId[typeId];if(!t||!t.fmt){reply({error:"Type inconnu."});return;}
    let data=text;
    if(t.gs1&&!/^\(/.test(data)&&window.atelierParseGs1){const els=window.atelierParseGs1(data)||[];data=els.map(([a,v])=>"("+a+")"+v).join("");}
    const r=await writeFor(typeId,data,{scale:4,hrt:true,quiet:true});
    if(r.error){reply({error:r.error.replace(/^Error \d+:\s*/,"").replace(/\s*\(retval.*$/,"")});return;}
    const c=await svgToCanvas(recolor(r.svg,"#000000","#FFFFFF",false),1,0,"#ffffff");
    reply({image:c.toDataURL("image/png")});
  }catch(e){reply({error:"Aperçu impossible."});}
};

selectType("qr");
})();
