
/* ================= Pré-vérification sur image (paramètres des normes ISO/IEC 15416 et 15415) ================= */
// Les mêmes paramètres qu'un vérificateur, mesurés sur l'image analysée. Ce sont des estimations : une photo n'a ni
// l'éclairage normalisé, ni l'étalonnage, ni l'ouverture de mesure d'un vérificateur. Elles servent à trouver la cause
// d'un problème (contraste, engraissement, taches, déformation), pas à remplacer un rapport certifié.
const VGRADE=["F","D","C","B","A"];
const gHi=(v,t)=>v>=t[0]?4:v>=t[1]?3:v>=t[2]?2:v>=t[3]?1:0;
const gLo=(v,t)=>v<=t[0]?4:v<=t[1]?3:v<=t[2]?2:v<=t[3]?1:0;
const vDist=(a,b)=>Math.hypot(b.x-a.x,b.y-a.y);
const pctOf=v=>Math.round(v*100)+" %";
/* Luminance (0 à 1) en un point quelconque, interpolée ; NaN hors de l'image */
function lumAt(x,y){
  const W=src.width,H=src.height;if(!(x>=0&&y>=0&&x<W-1&&y<H-1))return NaN;
  const x0=x|0,y0=y|0,fx=x-x0,fy=y-y0,i=y0*W+x0;
  return((lum[i]*(1-fx)+lum[i+1]*fx)*(1-fy)+(lum[i+W]*(1-fx)+lum[i+W+1]*fx)*fy)/255;
}
/* Point d'un quadrilatère [haut-gauche, haut-droite, bas-droite, bas-gauche] en coordonnées relatives (0 à 1) */
function quadAt(p,u,v){
  const tx=p[0].x+(p[1].x-p[0].x)*u,ty=p[0].y+(p[1].y-p[0].y)*u,bx=p[3].x+(p[2].x-p[3].x)*u,by=p[3].y+(p[2].y-p[3].y)*u;
  return{x:tx+(bx-tx)*v,y:ty+(by-ty)*v};
}
const quantile=(arr,q)=>{const a=Float32Array.from(arr).sort();return a.length?a[Math.min(a.length-1,Math.max(0,Math.round(q*(a.length-1))))]:NaN;};
/* Rangées de modules attendues (0 = foncé), d'après le symbole décodé */
function symbolRow(s,i){const out=[];for(let j=0;j<s.width;j++)out.push(s.data[i*s.width+j]<128?1:0);return out;}
function runsOf(bits){const r=[];for(const b of bits){if(r.length&&r[r.length-1][0]===b)r[r.length-1][1]++;else r.push([b,1]);}return r;}

/* ---------- Codes linéaires (ISO/IEC 15416) : 10 lignes de lecture, réflectance le long de chacune ---------- */
// Les lecteurs ne rendent pas la grille des modules d'un code linéaire : on la recrée en réencodant le contenu lu.
const TWO_WIDTH=/^(Code39|ITF|Codabar)$/;
async function verifyPrepare(list){
  if(!window.ZXingWASM||!ZXingWASM.writeBarcode)return;
  for(const r of list){
    if(MATRIX.test(r.format)||r.symbol||r.addon||!r.text)continue;
    try{const w=await ZXingWASM.writeBarcode(r.text,{format:r.format,scale:1,addQuietZones:false,withHRT:false});
      if(w&&w.symbol&&w.symbol.width){const row=symbolRow(w.symbol,0);while(row.length&&!row[0])row.shift();while(row.length&&!row[row.length-1])row.pop();if(row.length>=15)r.ref=row;}}catch(e){}
  }
}
/* Éléments (barres et espaces) d'un profil, bords placés au point de passage du seuil */
function elementsOf(R,gt){
  const el=[];for(let i=0;i<R.length;i++){if(isNaN(R[i]))continue;const d=R[i]<gt?1:0;const e=el[el.length-1];
    if(e&&e.d===d&&e.end===i-1){e.end=i;e.min=Math.min(e.min,R[i]);e.max=Math.max(e.max,R[i]);}else el.push({d,start:i,end:i,min:R[i],max:R[i]});}
  for(let i=0;i<el.length;i++){const e=el[i];
    const l=i&&el[i-1].end===e.start-1?e.start-1+(gt-R[e.start-1])/((R[e.start]-R[e.start-1])||1e-6):e.start-.5;
    const rr=i<el.length-1&&el[i+1].start===e.end+1?e.end+(gt-R[e.end])/((R[e.end+1]-R[e.end])||1e-6):e.end+.5;
    e.w=rr-l;}
  return el;
}
/* Largeur de module la plus cohérente avec des largeurs mesurées (codes à largeurs multiples, sans référence) */
function fitModules(ws){
  const tot=ws.reduce((a,b)=>a+b,0),mn=quantile(ws,.1);let best=null;
  for(let m=Math.max(ws.length,Math.floor(tot/(mn*1.5)));m<=Math.ceil(tot/(mn*.6));m++){const X=tot/m;let e=0;
    for(const w of ws){const k=Math.max(1,Math.round(w/X));e+=(w/X-k)**2;}if(!best||e<best.e)best={m,e};}
  return best?best.m:0;
}
function verifyLinear(r){
  const p=r.pts,ref=r.ref||null,runs=ref?runsOf(ref):null;
  const left={x:(p[0].x+p[3].x)/2,y:(p[0].y+p[3].y)/2},right={x:(p[1].x+p[2].x)/2,y:(p[1].y+p[2].y)/2};
  let len=vDist(left,right);if(len<20)return null;
  let ux=(right.x-left.x)/len,uy=(right.y-left.y)/len,nx=-uy,ny=ux;
  const mid={x:(left.x+right.x)/2,y:(left.y+right.y)/2};
  const corr=(a,b,lag)=>{let sa=0,sb=0,n=0;lag=lag||0;for(let i=0;i<a.length;i++){const j=i+lag;if(j<0||j>=b.length||isNaN(a[i])||isNaN(b[j]))continue;sa+=a[i];sb+=b[j];n++;}if(n<10)return 0;
    const ma=sa/n,mb=sb/n;let c=0,va=0,vb=0;for(let i=0;i<a.length;i++){const j=i+lag;if(j<0||j>=b.length||isNaN(a[i])||isNaN(b[j]))continue;c+=(a[i]-ma)*(b[j]-mb);va+=(a[i]-ma)**2;vb+=(b[j]-mb)**2;}return c/Math.sqrt(va*vb||1);};
  // Code penché : les lecteurs donnent parfois la position le long de leurs lignes de balayage, pas de l'axe du code.
  // On mesure l'inclinaison des barres en suivant le décalage du profil quand on s'écarte de la ligne centrale.
  {const line=(o,st)=>{const out=[];for(let t=-len/2;t<=len/2;t+=st)out.push(lumAt(mid.x+ux*t+nx*o,mid.y+uy*t+ny*o));return out;};
    const st=.5,P0=line(0,st);let tilt=0,d=3;
    const best=(d,c0,span)=>{let bl=0,bc=-2;const a=line(d,st);for(let L=Math.round((c0-span)/st);L<=Math.round((c0+span)/st);L++){const c=corr(P0,a,L);if(c>bc){bc=c;bl=L;}}return[bl*st,bc];};
    let [lag,c]=best(d,0,d);if(c>.6){tilt=lag/d;
      for(d=9;d<len*.6;d*=2){const[l2,c2]=best(d,tilt*d,2);if(c2<.6)break;tilt=l2/d;}}
    if(Math.abs(tilt)>.02&&Math.abs(tilt)<1.5){const k=1/Math.hypot(1,tilt);const vx=(ux-tilt*nx)*k,vy=(uy-tilt*ny)*k;
      len*=k;ux=vx;uy=vy;nx=-uy;ny=ux;}}
  const prof=(cx,cy,from,to,step,X)=>{const out=[];for(let t=from;t<=to+1e-6;t+=step){let a=0,n=0;
      for(const k of[-.3,0,.3]){const v=lumAt(cx+ux*t+nx*k*X,cy+uy*t+ny*k*X);if(!isNaN(v)){a+=v;n++;}}out.push(n?(r.inverted?1-a/n:a/n):NaN);}return out;};
  // Largeur de module : par la référence, sinon d'après la ligne centrale
  const twoW=TWO_WIDTH.test(r.format);let mods=ref?ref.length:0,X=0;
  if(!mods){const R0=prof(mid.x,mid.y,-len/2-2,len/2+2,.25,1),v0=R0.filter(v=>!isNaN(v));if(v0.length<20)return null;
    const el=elementsOf(R0,(quantile(v0,.99)+quantile(v0,.01))/2),fb=el.findIndex(e=>e.d),lb=el.length-1-[...el].reverse().findIndex(e=>e.d);
    if(fb<0||lb-fb<8)return null;const ws=el.slice(fb,lb+1).map(e=>e.w*.25);
    X=twoW?quantile(ws,.25):len/(mods=fitModules(ws));}
  else X=len/mods;
  if(!(X>0))return null;
  if(X<1.5)return{tooSmall:true,X};
  const qzNeed=/EAN13|UPCA|ISBN|UPCE/.test(r.format)?9:/EAN8/.test(r.format)?7:10;
  // Hauteur des barres : on s'écarte de la ligne centrale tant que le profil reste le même
  const coarse=Math.max(.5,X/3),base=prof(mid.x,mid.y,-len/2,len/2,coarse,X);
  const reach=dir=>{let d=0;const stepH=Math.max(1,X);for(let k=1;k<600;k++){const o=dir*k*stepH;
      if(corr(base,prof(mid.x+nx*o,mid.y+ny*o,-len/2,len/2,coarse,X))<.8)break;d=k*stepH;}return d;};
  const up=reach(-1),down=reach(1),height=up+down;
  if(height<2*X)return null;
  // Profils : 10 lignes réparties entre 10 % et 90 % de la hauteur, chacune débordant dans les zones de silence
  const ext=(qzNeed+1)*X,step=Math.max(.2,X/8),lines=[];
  for(let k=0;k<10;k++){
    const o=-up+height*(.1+.8*k/9),c={x:mid.x+nx*o,y:mid.y+ny*o};
    lines.push(analyseProfile(prof(c.x,c.y,-len/2-ext,len/2+ext,step,X),step,X,{runs,mods,twoW},qzNeed));
  }
  const ok=lines.filter(Boolean);if(ok.length<5)return null;
  const mean=k=>ok.reduce((a,l)=>a+l[k],0)/ok.length;
  const avgG=k=>ok.reduce((a,l)=>a+l.g[k],0)/ok.length;
  const params=[
    ["sc","Contraste de symbole (SC)",pctOf(mean("sc")),avgG("sc")],
    ["rmin","Réflectance minimale",`${Math.round(mean("rmin")*100)} % (maximum toléré ${Math.round(mean("rmax")*50)} %)`,avgG("rmin")],
    ["ec","Contraste de bord minimal (ECmin)",pctOf(mean("ecmin")),avgG("ec")],
    ["mod","Modulation (MOD)",mean("mod").toFixed(2),avgG("mod")],
    ["def","Défauts",mean("def").toFixed(2),avgG("def")],
    ["dec","Décodabilité",mean("dec").toFixed(2),avgG("dec")],
  ];
  if(runs)params.push(["edge","Détermination des bords",ok.every(l=>l.edgeOk)?"tous les éléments trouvés":`éléments manquants ou en trop sur ${ok.filter(l=>!l.edgeOk).length} ligne(s) sur ${ok.length}`,avgG("edge")]);
  const gr=ok.map(l=>l.growth).filter(v=>v!=null),growth=gr.length>=5?gr.reduce((a,b)=>a+b,0)/gr.length:null;
  if(growth!=null)params.push(["pg","Engraissement de l'impression",`${growth>=0?"+":""}${Math.round(growth*100)} % de module par barre`,null]);
  const qz=ok.map(l=>l.qz).filter(v=>v!=null);
  if(qz.length)params.push(["qz",`Zones de silence (≥ ${qzNeed} modules)`,qz.every(Boolean)?"dégagées":"encombrées ou trop étroites",qz.every(Boolean)?4:0]);
  const score=ok.reduce((a,l)=>a+Math.min(...Object.values(l.g)),0)/ok.length;
  const qzBad=qz.length&&!qz.every(Boolean);
  return{kind:"1D",norm:"ISO/IEC 15416",score:qzBad?0:score,params,X,growth,
    detail:mods?`${ok.length} lignes de lecture · ${mods} modules · ≈ ${X.toFixed(1)} px par module`:`${ok.length} lignes de lecture · ≈ ${X.toFixed(1)} px par module`};
}
function analyseProfile(R,step,X,m,qzNeed){
  const val=R.filter(v=>!isNaN(v));if(val.length<R.length*.8)return null;
  const rmax=quantile(val,.995),rmin=quantile(val,.005),sc=rmax-rmin;if(sc<.05)return null;
  const gt=(rmax+rmin)/2;
  const el=elementsOf(R,gt);
  const fb=el.findIndex(e=>e.d===1),lb=el.length-1-[...el].reverse().findIndex(e=>e.d===1);if(fb<1||lb>=el.length-1)return null;
  const sym=el.slice(fb,lb+1);
  // Contraste de bord : entre chaque espace (max) et barre (min) voisins, zones de silence comprises
  let ecmin=1;for(let i=fb-1;i<=lb;i++){const a=el[i],b=el[i+1];if(!a||!b)continue;const sp=a.d?b:a,ba=a.d?a:b;ecmin=Math.min(ecmin,sp.max-ba.min);}
  // Défauts : creux et bosses à l'intérieur d'un élément (bords exclus)
  let ern=0;for(let i=fb-1;i<=lb+1;i++){const e=el[i];if(!e)continue;const n=e.end-e.start+1,cut=Math.max(1,Math.round(Math.min(n*.2,X/step*.5)));if(n-2*cut<2)continue;
    let lo=1,hi=0;for(let k=e.start+cut;k<=e.end-cut;k++){if(isNaN(R[k]))continue;lo=Math.min(lo,R[k]);hi=Math.max(hi,R[k]);}if(hi>lo)ern=Math.max(ern,hi-lo);}
  // Décodabilité : écart des largeurs mesurées aux largeurs théoriques
  const ws=sym.map(e=>e.w),tot=ws.reduce((a,b)=>a+b,0);let dec=0,edgeOk=true,growth=null;
  if(m.runs){edgeOk=sym.length===m.runs.length;
    if(edgeOk){const Xm=tot/m.mods;let worst=0,gs=0,gn=0;ws.forEach((w,i)=>{const dv=w/Xm-m.runs[i][1];worst=Math.max(worst,Math.abs(dv));if(sym[i].d){gs+=dv;gn++;}});dec=Math.max(0,1-worst/.5);growth=gn?gs/gn:null;}}
  else if(m.twoW){let rt=(Math.min(...ws)+Math.max(...ws))/2,N=0,W=0;
    for(let k=0;k<6;k++){const n=ws.filter(w=>w<rt),wd=ws.filter(w=>w>=rt);if(!n.length||!wd.length)break;N=n.reduce((a,b)=>a+b,0)/n.length;W=wd.reduce((a,b)=>a+b,0)/wd.length;rt=(N+W)/2;}
    const n=ws.filter(w=>w<rt),wd=ws.filter(w=>w>=rt);
    if(n.length&&wd.length&&W>N)dec=Math.max(0,Math.min(1,(rt-Math.max(...n))/(rt-N),(Math.min(...wd)-rt)/(W-rt)));}
  else if(m.mods){const Xm=tot/m.mods;let worst=0;ws.forEach(w=>{worst=Math.max(worst,Math.abs(w/Xm-Math.max(1,Math.round(w/Xm))));});dec=Math.max(0,1-worst/.5);}
  // Zones de silence : assez de clair avant la première barre et après la dernière (non vérifiable au bord de l'image)
  let qz=null;const before=el[fb-1],after=el[lb+1];
  if(!isNaN(R[0])&&!isNaN(R[R.length-1]))qz=before.w*step>=qzNeed*X*.9&&after.w*step>=qzNeed*X*.9;
  const mod=ecmin/sc,def=ern/sc;
  const g={sc:gHi(sc,[.7,.55,.4,.2]),rmin:rmin<=.5*rmax?4:0,ec:ecmin>=.15?4:0,mod:gHi(mod,[.7,.6,.5,.4]),def:gLo(def,[.15,.2,.25,.3]),dec:edgeOk?gHi(dec,[.62,.5,.37,.25]):0};
  if(m.runs)g.edge=edgeOk?4:0;
  return{sc,rmin,rmax,ecmin,mod,def,dec,edgeOk,qz,g,growth};
}

/* ---------- Codes 2D (ISO/IEC 15415) : chaque module comparé au symbole décodé ---------- */
function verifyMatrix(r){
  const s=r.symbol,p=r.pts;
  if(!s||!s.width||!s.height||r.mirrored||!/QRCode|DataMatrix/.test(r.format))return null;
  const cols=s.width,rows=s.height;
  const X=(vDist(p[0],p[1])+vDist(p[3],p[2]))/2/cols,Y=(vDist(p[0],p[3])+vDist(p[1],p[2]))/2/rows,m=Math.min(X,Y);
  if(m<2.5)return{tooSmall:true,X:m};
  const rad=Math.max(.5,m*.2);
  const sampleAt=(u,v)=>{const c=quadAt(p,u,v);let a=0,n=0;for(const[dx,dy]of[[0,0],[rad,0],[-rad,0],[0,rad],[0,-rad]]){const l=lumAt(c.x+dx,c.y+dy);if(!isNaN(l)){a+=l;n++;}}
    if(!n)return NaN;const v1=a/n;return r.inverted?1-v1:v1;};
  const R=new Float32Array(cols*rows),E=new Uint8Array(cols*rows);
  for(let i=0;i<rows;i++)for(let j=0;j<cols;j++){R[i*cols+j]=sampleAt((j+.5)/cols,(i+.5)/rows);E[i*cols+j]=s.data[i*cols+j]<128?1:0;}
  if(R.some(isNaN))return null;
  // Zone de silence : une couronne à 1,5 module autour du symbole
  const qzv=[];const du=1.5/cols,dv=1.5/rows;
  for(let k=0;k<=40;k++){const t=k/40;for(const[u,v]of[[t,-dv],[t,1+dv],[-du,t],[1+du,t]]){const l=sampleAt(u,v);if(!isNaN(l))qzv.push(l);}}
  const dark=[],light=[...qzv];for(let k=0;k<R.length;k++)(E[k]?dark:light).push(R[k]);
  const rmax=quantile(light,.98),rmin=quantile(dark,.02),sc=rmax-rmin;if(sc<.05)return null;
  const gt=(rmax+rmin)/2;
  // Marge de chaque module : du bon côté du seuil, et de combien (1 = aussi net que le meilleur module)
  const gm=[0,0,0,0,0];let wrong=0,weak=0;
  for(let k=0;k<R.length;k++){const mg=(E[k]?gt-R[k]:R[k]-gt)/(sc/2);if(mg<0)wrong++;else if(mg<.5)weak++;gm[mg<0?0:gHi(mg,[.5,.4,.3,.2])]++;}
  const n=R.length;let modG=0;for(let L=4;L>=1;L--){let below=0;for(let g=0;g<L;g++)below+=gm[g];if(below/n<=.02){modG=L;break;}}
  // Motifs fixes : repères et motifs de synchronisation
  const fixed=(i,j)=>/QR/.test(r.format)?((i<8&&j<8)||(i<8&&j>=cols-8)||(i>=rows-8&&j<8)||i===6||j===6):(i===0||j===0||i===rows-1||j===cols-1);
  let fw=0,fn=0;for(let i=0;i<rows;i++)for(let j=0;j<cols;j++)if(fixed(i,j)){fn++;const k=i*cols+j;if((E[k]?gt-R[k]:R[k]-gt)<0)fw++;}
  const fr=fw/fn,fpG=fw===0?4:fr<=.02?3:fr<=.05?2:fr<=.1?1:0;
  // Engraissement : part du foncé le long d'un motif alterné (synchronisation QR, bord d'horloge Data Matrix)
  let growth=null;{const isQR=/QR/.test(r.format),ri=isQR?6:0,c0=isQR?8:1,c1=isQR?cols-8:cols-1,Ns=c1-c0,fine=[];
    let nDark=0,runsD=0;for(let j=c0;j<c1;j++){if(E[ri*cols+j]){nDark++;if(j===c0||!E[ri*cols+j-1])runsD++;}}
    const steps=Math.round(Ns*m*3);
    for(let k=0;k<steps;k++){const l=sampleAt((c0+(k+.5)/steps*Ns)/cols,(ri+.5)/rows);if(!isNaN(l))fine.push(l);}
    if(fine.length>20&&runsD>=3&&modG>=2){const lt=(quantile(fine,.95)+quantile(fine,.05))/2,d=fine.filter(l=>l<lt).length/fine.length;growth=(d*Ns-nDark)/runsD;}}
  const an=Math.abs(X-Y)/((X+Y)/2);
  const params=[
    ["sc","Contraste de symbole (SC)",pctOf(sc),gHi(sc,[.7,.55,.4,.2])],
    ["mod","Modulation",wrong?(weak?`${wrong} module(s) lu(s) à l'envers · ${weak} peu marqué(s) sur ${n}`:`${wrong} module(s) lu(s) à l'envers sur ${n}`):(weak?`aucun module lu à l'envers · ${weak} peu marqué(s) sur ${n}`:`aucun module lu à l'envers sur ${n}`),modG],
    ["fpd","Motifs fixes (repères)",fw?`${fw} module(s) abîmé(s) sur ${fn}`:"intacts",fpG],
    ["an","Non-uniformité axiale",an.toFixed(3),gLo(an,[.06,.08,.1,.12])],
  ];
  if(r.uec!=null)params.push(["uec","Correction d'erreur inutilisée (UEC)",pctOf(r.uec),gHi(r.uec,[.62,.5,.37,.25])]);
  if(growth!=null)params.push(["pg","Engraissement de l'impression",`${growth>=0?"+":""}${Math.round(growth*100)} % de module`,null]);
  const graded=params.filter(x=>x[3]!=null);
  const score=Math.min(...graded.map(x=>x[3]));
  return{kind:"2D",norm:"ISO/IEC 15415",score,params,X:m,growth,detail:`${cols} × ${rows} modules · ≈ ${m.toFixed(1)} px par module`};
}
/* Conseils : ce que le paramètre le plus faible veut dire sur le terrain */
const VADVICE={
  sc:"Contraste faible : encre ou ruban trop clair, fond coloré ou brillant, noirceur trop basse. Vérifie le ruban et la noirceur de l'imprimante.",
  rmin:"Barres pas assez foncées : ruban usé, noirceur trop basse, ou encre inadaptée au support.",
  ec:"Bords peu marqués entre barres et espaces : impression floue, encre qui bave, ou photo floue.",
  mod:"Barres ou modules d'intensité inégale : impression grise ou engraissée. Ajuste noirceur et vitesse, vérifie la tête.",
  def:"Taches ou manques dans les barres ou les espaces : tête d'impression encrassée ou abîmée (points morts), support poussiéreux.",
  dec:"Largeurs des barres irrégulières : engraissement ou maigreur, vitesse trop élevée, résolution de l'imprimante inadaptée à la taille du code.",
  edge:"Certaines barres sont mal séparées sur une partie du code : rayure, tache, pli ou barre coupée.",
  qz:"Marge claire insuffisante autour du code : texte, cadre ou bord d'étiquette trop proche.",
  fpd:"Motifs de repérage abîmés (coins, bords) : marge rognée, tache ou pli sur un repère.",
  an:"Modules étirés dans un sens : pas d'impression mal réglé, support étiré, ou photo prise de biais.",
  uec:"Correction d'erreur très sollicitée : modules abîmés, logo trop grand, rayures ou taches."
};
function verifyCode(r){
  try{const v=MATRIX.test(r.format)?verifyMatrix(r):verifyLinear(r);if(!v||v.tooSmall)return v;
    const grade=v.score>=3.5?4:v.score>=2.5?3:v.score>=1.5?2:v.score>=.5?1:0;v.grade=VGRADE[grade];v.gradeN=grade;
    const weak=v.params.filter(x=>x[3]!=null&&x[3]<3.5).sort((a,b)=>a[3]-b[3]);
    v.limit=weak.length?weak[0][1]:"";
    v.advice=weak.slice(0,2).map(x=>VADVICE[x[0]]).filter(Boolean);
    if(v.growth!=null&&Math.abs(v.growth)>=.2){v.advice=v.advice.filter(a=>a!==VADVICE.dec);
      v.advice.push(v.growth>0?"Impression engraissée (barres ou modules foncés trop larges) : baisse la noirceur ou ralentis l'impression.":"Impression maigre (barres ou modules foncés trop fins) : augmente la noirceur ou vérifie le ruban.");}
    return v;}catch(e){console.warn(e);return null;}
}
function verifyHtml(v){
  if(!v)return"";
  if(v.tooSmall)return`<h4>Pré-vérification</h4><p class="hint">Trop peu de pixels par module (≈ ${v.X.toFixed(1)}) pour mesurer la qualité : approche-toi ou fais une image plus nette.</p>`;
  const cls=g=>g>=3?"good":g>=1.5?"warn":"bad";
  const letter=g=>VGRADE[Math.max(0,Math.min(4,Math.round(g)))];
  return`<h4>Pré-vérification <span class="pill ${cls(v.gradeN)}">Note estimée ${v.grade}</span></h4>
    <p class="hint">Paramètres de la norme ${v.norm}, mesurés sur l'image (${esc(v.detail)}). Estimation pour trouver la cause d'un défaut : elle ne remplace pas un vérificateur étalonné.</p>
    <dl class="kv">${v.params.map(([,name,val,g])=>`<dt>${esc(name)}</dt><dd>${esc(val)}${g!=null?` <span class="pill ${cls(g)}">${letter(g)}</span>`:""}</dd>`).join("")}</dl>
`;
}
