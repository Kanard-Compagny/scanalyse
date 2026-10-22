
/* ================= Cohérence d'une étiquette complète ================= */
// Quand une image contient plusieurs codes (étiquette logistique, carton, palette), on rassemble leurs
// données GS1 et on vérifie qu'elles se tiennent : mêmes valeurs d'un code à l'autre, associations
// obligatoires (règles GS1), ordre des dates, poids net ≤ poids brut…
const LBL_GTIN_FORMATS={EAN13:13,EAN8:8,UPCA:12,UPCE:8,ITF14:14,ITF:14};
// AI qui exigent l'identification de l'article (01 ou 02, ou 03 / 8006 / 8026).
const LBL_NEEDS_ITEM=/^(10|11|13|15|16|17|20|22|30|3[1-6]\d\d)$/;
const LBL_DATES=[["11","Date de production"],["13","Date d'emballage"],["16","Date limite de vente"],["15","DDM"],["17","Date d'expiration"]];

/* Éléments GS1 lus dans un code (null si le code n'en porte pas). Les EAN/UPC/ITF-14 donnent un GTIN (AI 01). */
function lblElements(r){
  const t=r.text||"";
  if(GS1SYM.test(r.symId||"")||/^\(\d{2,4}\)/.test(t)){const els=parseGS1Elements(t);if(els&&els.length)return els.map(([a,v])=>[a,v]);}
  const n=LBL_GTIN_FORMATS[r.format];
  if(n){let d=(r.format==="UPCE"&&r.upce&&/^\d{12}$/.test(r.upce))?r.upce:t;
    if(/^\d+$/.test(d)&&(d.length===n||(r.format==="ITF"&&d.length===14))&&validGtin(d))return [["01",d.padStart(14,"0")]];}
  return null;
}
function lblName(ai){return aiInfo(ai).name;}
function lblCode(i){return `code ${i+1}`;}
function lblList(ix){return ix.length===1?lblCode(ix[0]):"codes "+ix.slice(0,-1).map(i=>i+1).join(", ")+" et "+(ix[ix.length-1]+1);}
function lblShow(ai,v){
  const info=aiInfo(ai);
  if(info.kind==="date"){const d=gs1Date(v);return d.date?d.text:v;}
  if(info.kind==="dec"){const n=+ai[3];return (parseInt(v,10)/Math.pow(10,n)).toLocaleString("fr-FR",{minimumFractionDigits:n,maximumFractionDigits:n});}
  if(info.kind==="int")return String(parseInt(v,10));
  return v;
}
/* Nombre de caractères de données d'un GS1-128 (FNC1 compris) : la norme limite à 48. */
function lbl128Length(els){
  let n=0;els.forEach(([a,v],i)=>{n+=a.length+v.length;if(!FIXED[a.slice(0,2)]&&i<els.length-1)n+=1;});return n;
}

function labelCheck(list){
  const codes=list.map((r,i)=>({i,r,els:lblElements(r)}));
  const gs=codes.filter(c=>c.els);
  if(!gs.length)return null;
  const notes=[];// [gravité, message] — gravité : ok, info, warn, bad
  const byAi=new Map();
  gs.forEach(c=>c.els.forEach(([a,v])=>{if(!byAi.has(a))byAi.set(a,[]);byAi.get(a).push({i:c.i,v});}));

  // 1. Une même donnée présente dans plusieurs codes doit être identique partout.
  byAi.forEach((occ,ai)=>{
    const ix=[...new Set(occ.map(o=>o.i))];if(ix.length<2)return;
    const vals=[...new Set(occ.map(o=>o.v))];
    if(vals.length===1){notes.push(["ok",`${lblName(ai)} identique dans les ${lblList(ix)} : ${lblShow(ai,vals[0])}.`]);return;}
    if(ai==="01"&&new Set(occ.map(o=>o.v.slice(1,13))).size===1){
      notes.push(["info",`${lblName(ai)} : même article à des niveaux d'emballage différents (${occ.map(o=>`${lblCode(o.i)} indicateur ${o.v[0]}`).join(", ")}). Normal sur un carton qui porte aussi le code de l'unité consommateur.`]);return;}
    notes.push(["bad",`${lblName(ai)} différent selon le code : ${occ.map(o=>`${lblCode(o.i)} = ${lblShow(ai,o.v)}`).join(" ; ")}.`]);
  });

  // 2. GTIN contenu (02) et code article (EAN, 01) présents sur la même étiquette.
  const c02=byAi.get("02")||[],c01=byAi.get("01")||[];
  c02.forEach(a=>{
    const others=c01.filter(b=>b.i!==a.i);
    const same=others.filter(b=>b.v===a.v).map(b=>b.i),level=others.filter(b=>b.v!==a.v&&b.v.slice(1,13)===a.v.slice(1,13)).map(b=>b.i);
    const diff=others.filter(b=>b.v.slice(1,13)!==a.v.slice(1,13));
    if(same.length)notes.push(["ok",`Le GTIN des articles contenus (02, ${lblCode(a.i)}) correspond à l'article ${same.length>1?"des":"du"} ${lblList(same)}.`]);
    if(level.length)notes.push(["info",`Le GTIN contenu (02, ${lblCode(a.i)}) et le${level.length>1?"s":""} ${lblList(level)} désignent le même article à un autre niveau d'emballage.`]);
    diff.forEach(b=>notes.push(["warn",`Le GTIN contenu (02, ${lblCode(a.i)} = ${a.v}) ne correspond pas à l'article du ${lblCode(b.i)} (${b.v}).`]));
  });

  // 3. Associations obligatoires et interdites (spécifications générales GS1).
  const has=a=>byAi.has(a);
  gs.forEach(c=>{const s=new Set(c.els.map(e=>e[0]));
    if(s.has("01")&&s.has("02"))notes.push(["bad",`${lblCode(c.i)} : les AI 01 et 02 ne peuvent pas figurer dans le même code.`]);
    if(s.has("01")&&s.has("37"))notes.push(["warn",`${lblCode(c.i)} : la quantité contenue (37) s'emploie avec le GTIN contenu (02), pas avec le GTIN de l'article (01).`]);});
  if(has("02")&&!has("37"))notes.push(["bad","GTIN contenu (02) sans nombre d'unités contenues (37) : GS1 impose les deux ensemble."]);
  if(has("37")&&!has("02")&&!has("8026"))notes.push(["bad","Nombre d'unités contenues (37) sans GTIN contenu (02)."]);
  if(has("02")&&!has("00"))notes.push(["warn","GTIN contenu (02) sans SSCC (00) : l'AI 02 est réservé aux unités logistiques identifiées par un SSCC."]);
  if(has("21")&&!has("01")&&!has("03")&&!has("8006"))notes.push(["bad","Numéro de série (21) sans GTIN (01) : un numéro de série n'a de sens que rattaché à un article."]);
  const orphan=[...byAi.keys()].filter(a=>LBL_NEEDS_ITEM.test(a));
  if(orphan.length&&!has("01")&&!has("02")&&!has("03")&&!has("8006")&&!has("8026"))
    notes.push(["bad",`${orphan.map(lblName).join(", ")} sans GTIN (01 ou 02) : ces données doivent accompagner l'identification de l'article.`]);
  const ssccs=[...new Set((byAi.get("00")||[]).map(o=>o.v))];
  if(ssccs.length===1&&gs.length>=1)notes.push(["ok",`Unité logistique identifiée par le SSCC ${ssccs[0]}.`]);

  // 4. Ordre des dates : production ≤ emballage ≤ vente ≤ DDM / expiration.
  const dates=LBL_DATES.map(([a,n])=>{const o=(byAi.get(a)||[])[0];const d=o?gs1Date(o.v).date:null;return d?{a,n,d}:null;}).filter(Boolean);
  for(let x=0;x<dates.length;x++)for(let y=x+1;y<dates.length;y++){
    const A=dates[x],B=dates[y];
    if(!(A.a==="15"&&B.a==="17")&&A.d>B.d)notes.push(["warn",`${A.n} (${A.d.toLocaleDateString("fr-FR")}) postérieure à la ${B.n.toLowerCase()} (${B.d.toLocaleDateString("fr-FR")}).`]);
  }

  // 5. Poids net ≤ poids brut (kg).
  const kg=re=>{const k=[...byAi.keys()].find(a=>re.test(a));if(!k)return null;const v=byAi.get(k)[0].v;return parseInt(v,10)/Math.pow(10,+k[3]);};
  const net=kg(/^310\d$/),gross=kg(/^330\d$/);
  if(net!=null&&gross!=null){
    if(net>gross)notes.push(["bad",`Poids net (${net.toLocaleString("fr-FR")} kg) supérieur au poids brut (${gross.toLocaleString("fr-FR")} kg).`]);
    else notes.push(["ok",`Poids net (${net.toLocaleString("fr-FR")} kg) ≤ poids brut (${gross.toLocaleString("fr-FR")} kg).`]);}

  // 6. Longueur maximale d'un GS1-128.
  gs.forEach(c=>{if(c.r.format==="Code128"&&GS1SYM.test(c.r.symId||"")){const n=lbl128Length(c.els);
    if(n>48)notes.push(["warn",`${lblCode(c.i)} (GS1-128) porte ${n} caractères de données : la norme en autorise 48 au maximum par symbole. Répartis les données sur deux codes.`]);}});

  // 7. Codes non GS1 qui reprennent une donnée GS1 (référence interne, lot en Code 39…).
  codes.filter(c=>!c.els&&c.r.text).forEach(c=>{
    for(const [ai,occ] of byAi){const o=occ.find(o=>o.v===c.r.text);if(o){notes.push(["info",`Le ${lblCode(c.i)} (${FMT[c.r.format]||c.r.format}) reprend le ${lblName(ai).toLowerCase()} du ${lblCode(o.i)}.`]);break;}}});

  if(list.length<2&&!notes.some(n=>n[0]==="bad"||n[0]==="warn"))return null;
  const worst=notes.some(n=>n[0]==="bad")?"bad":notes.some(n=>n[0]==="warn")?"warn":"good";
  return {codes,gs,byAi,notes,worst};
}

function labelCheckHtml(list){
  let c;try{c=labelCheck(list);}catch(e){return "";}
  if(!c)return "";
  const verdict={good:"Étiquette cohérente",warn:"À vérifier",bad:"Incohérences"}[c.worst];
  const cols=c.gs;
  const rows=[...c.byAi.keys()].sort((a,b)=>a.localeCompare(b,"fr",{numeric:true})).map(ai=>{
    const occ=c.byAi.get(ai);const distinct=new Set(occ.map(o=>o.v)).size;
    const mism=distinct>1&&!(ai==="01"&&new Set(occ.map(o=>o.v.slice(1,13))).size===1);
    return `<tr${mism?' class="mism"':""}><th scope="row">${esc(lblName(ai))} <span class="hint">${esc(ai)}</span></th>${cols.map(col=>{
      const e=col.els.find(x=>x[0]===ai);return `<td>${e?esc(lblShow(ai,e[1])):'<span class="hint">—</span>'}</td>`;}).join("")}</tr>`;}).join("");
  const order={bad:0,warn:1,info:2,ok:3};
  const notes=c.notes.slice().sort((a,b)=>order[a[0]]-order[b[0]]);
  return `<section class="lbl">
    <div class="res-head"><h3>Cohérence de l'étiquette</h3><span class="pill ${c.worst}">${verdict}</span></div>
    <p class="hint" style="margin:0">${cols.length} code${cols.length>1?"s":""} porteur${cols.length>1?"s":""} de données GS1 sur ${list.length} : chaque donnée est comparée d'un code à l'autre et aux règles GS1.</p>
    ${notes.length?`<div class="alerts">${notes.map(([t,m])=>`<div class="alert ${t==="ok"?"ok":t}">${esc(m)}</div>`).join("")}</div>`:""}
    <div class="lbl-wrap"><table class="lbl-table"><thead><tr><th scope="col">Donnée</th>${cols.map(col=>`<th scope="col"><button type="button" class="lbl-col" data-goto="${col.i}"><span class="num">${col.i+1}</span>${esc(FMT[col.r.format]||col.r.format)}</button></th>`).join("")}</tr></thead><tbody>${rows}</tbody></table></div>
  </section>`;
}
