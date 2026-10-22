
/* ================= Langue de l'interface (programme Windows) ================= */
// Le programme envoie sa table français → anglais ; chaque texte affiché (et ceux ajoutés ensuite) est remplacé
// s'il figure dans la table, tel quel ou selon un modèle à parties variables {0}, {1}…
(function(){
  let exact=null,patterns=[],prefixes=[];
  const slot=/\{(\d+)\}/g;
  const esc=s=>s.replace(/[.*+?^${}()|[\]\\]/g,"\\$&");
  function tr(t,depth){
    if(!exact||!t||depth>3)return t;
    if(exact.has(t))return exact.get(t);
    const s=t.trim();if(s!==t&&s){const r=tr(s,depth);return r===s?t:t.replace(s,r);}
    for(const p of patterns){const m=p.re.exec(t);if(m)return p.en.replace(slot,(x,n)=>tr(m.groups["s"+n],depth+1));}
    for(const p of prefixes)if(t.startsWith(p.fr))return p.en+tr(t.slice(p.fr.length),depth+1);
    // Ponctuation ou « (AI) » en tête : on traduit la suite (« : GS1 DataMatrix (…) », « (17) Date d'expiration »)
    const lead=/^([\s:·,;\-–]+|\(\d{2,4}\)\s)([\s\S]+)$/.exec(t);
    if(lead){const r=tr(lead[2],depth+1);if(r!==lead[2])return lead[1]+r;}
    return t;
  }
  window.atelierT=t=>tr(t,0);
  const ATTRS=["title","placeholder","aria-label"];
  function walk(node){
    if(node.nodeType===3){const v=node.nodeValue;if(v&&/[A-Za-zÀ-ÿ]/.test(v)){const n=tr(v,0);if(n!==v)node.nodeValue=n;}return;}
    if(node.nodeType!==1)return;
    const tag=node.tagName;if(tag==="SCRIPT"||tag==="STYLE"||tag==="TEXTAREA"||tag==="PRE"||tag==="CODE"||node.isContentEditable)return;
    if(node.classList&&node.classList.contains("content"))return; // contenu lu dans un code : jamais traduit
    for(const a of ATTRS){const v=node.getAttribute&&node.getAttribute(a);if(v){const n=tr(v,0);if(n!==v)node.setAttribute(a,n);}}
    if(tag==="INPUT"&&(node.type==="button"||node.type==="submit")&&node.value){const n=tr(node.value,0);if(n!==node.value)node.value=n;}
    for(const c of node.childNodes)walk(c);
  }
  let observer=null;
  window.atelierSetLanguage=function(entries){
    exact=new Map();patterns=[];prefixes=[];
    for(const [fr,en] of entries){
      if(/\{\d+\}/.test(fr)){
        const re=new RegExp("^"+esc(fr).replace(/\\\{(\d+)\\\}/g,(x,n)=>`(?<s${n}>[\\s\\S]+?)`)+"$");
        patterns.push({re,en,fixed:fr.replace(slot,"").length});
      }else{exact.set(fr,en);if(/[:·]$/.test(fr))prefixes.push({fr:fr+" ",en:en+" "});}
    }
    patterns.sort((a,b)=>b.fixed-a.fixed);prefixes.sort((a,b)=>b.fr.length-a.fr.length);
    document.documentElement.lang="en";
    // Dates et nombres : format anglais là où le code demande le format français.
    const swap=l=>(l==="fr-FR"||l==="fr")?"en-GB":l;
    const dl=Date.prototype.toLocaleDateString,ds=Date.prototype.toLocaleString,dt=Date.prototype.toLocaleTimeString,nl=Number.prototype.toLocaleString;
    Date.prototype.toLocaleDateString=function(l,o){return dl.call(this,swap(l),o);};
    Date.prototype.toLocaleString=function(l,o){return ds.call(this,swap(l),o);};
    Date.prototype.toLocaleTimeString=function(l,o){return dt.call(this,swap(l),o);};
    Number.prototype.toLocaleString=function(l,o){return nl.call(this,swap(l),o);};
    walk(document.body);
    if(observer)observer.disconnect();
    observer=new MutationObserver(list=>{for(const m of list){
      if(m.type==="characterData")walk(m.target);
      else if(m.type==="attributes")walk(m.target);
      else m.addedNodes.forEach(walk);}});
    observer.observe(document.body,{subtree:true,childList:true,characterData:true,attributes:true,attributeFilter:ATTRS});
  };
})();
