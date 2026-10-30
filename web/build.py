"""
Assemble l'Atelier (création de QR + analyse d'images) à partir des sources de web/src.

Sorties :
  - src/Scanalyse.Desktop/Web/index.html : version embarquée dans le programme Windows (hors ligne, tout inclus)
  - web/dist/standalone/                  : version web autonome installable (PWA)
  - web/dist/artifact.html                : version qui charge les bibliothèques depuis un CDN (artefact Claude)

Usage : python web/build.py   (depuis la racine du dépôt ou n'importe où)
"""
import base64
import os
import re
import shutil

ROOT = os.path.dirname(os.path.abspath(__file__))
SRC = os.path.join(ROOT, "src")
VENDOR = os.path.join(ROOT, "vendor")
DIST = os.path.join(ROOT, "dist")
DESKTOP_OUT = os.path.join(ROOT, "..", "src", "Scanalyse.Desktop", "Web", "index.html")

QR_VERSION = "1.9.2"
ZXING_VERSION = "3.1.4"


def read(*parts, mode="r"):
    with open(os.path.join(*parts), mode, **({} if "b" in mode else {"encoding": "utf-8"})) as f:
        return f.read()


def write(path, text):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        f.write(text)


def rep(s, old, new):
    assert old in s, "Motif introuvable : " + old[:80]
    return s.replace(old, new, 1)


def inline_script(code):
    return "<script>" + code.replace("</script", "<\\/script") + "</script>"


def assemble():
    s = read(SRC, "atelier.html")
    css = read(SRC, "scan.css") + "\n" + read(SRC, "camera.css") + "\n" + read(SRC, "label.css")
    scan_html = read(SRC, "scan.html")
    scan_js = read(SRC, "scan.js")
    # La caméra partage la portée de scan.js (moteur de lecture, source, rendu) : insérée avant la fermeture.
    body, end = scan_js.rstrip().rsplit("})();", 1)
    scan_js = body + read(SRC, "camera.js") + read(SRC, "label.js") + "\n})();" + read(SRC, "i18n.js") + end
    create_css = read(SRC, "create.css")
    create_html = read(SRC, "create.html")
    create_std_html = read(SRC, "create-std.html")
    create_js = read(SRC, "create.js")

    s = s.replace("qr-code-styling@1.6.0-rc.1", "qr-code-styling@" + QR_VERSION)
    # Couleurs et polices : générées depuis design/tokens.json (tools/build_theme.py)
    s = rep(s, "/*THEME*/", read(SRC, "theme.css"))
    s = rep(s, "</style>", css + create_css + "</style>")
    s = rep(s, """  <header>
    <h1>Atelier <span>QR</span></h1>
    <p>Contenu, couleurs, formes des modules et des repères, logo central. L'aperçu se met à jour en direct et vérifie que le code reste lisible.</p>
  </header>""", """  <header>
    <div class="head-l">
      <h1>Atelier <span>QR</span></h1>
      <div class="modes" role="tablist" aria-label="Mode">
        <button type="button" role="tab" id="tabCreate" aria-selected="true" aria-controls="view-create">Créer</button>
        <button type="button" role="tab" id="tabScan" aria-selected="false" aria-controls="view-scan">Analyser</button>
      </div>
    </div>
    <p id="headDesc">Contenu, couleurs, formes des modules et des repères, logo central. L'aperçu se met à jour en direct et vérifie que le code reste lisible.</p>
  </header>""")
    # Onglet Créer : choix du type en haut, module QR personnalisé, puis générateur standard (tous les autres types)
    s = rep(s, '  <div class="grid">', '  <div id="view-create" role="tabpanel">\n' + create_html + '  <div id="qrMode">\n  <div class="grid">')
    s = rep(s, "    </aside>\n  </div>\n", "    </aside>\n  </div>\n  </div>\n" + create_std_html + "  </div>\n" + scan_html)
    s = rep(s, "\nupdate();fillRanges();\n})();", """
window.atelierLoad=function(t){
  const isUrl=/^https?:\\/\\/\\S+$/i.test(t.trim());
  const k=isUrl?"url":"text";
  document.querySelector(`input[name="kind"][value="${k}"]`).checked=true;
  document.querySelectorAll("[data-pane]").forEach(p=>p.hidden=p.dataset.pane!==k);
  if(isUrl)$("url").value=t.trim();else $("text").value=t;
  update();toast("Contenu repris depuis l'analyse.");
};
if(window.chrome&&window.chrome.webview){
  const b=document.createElement("button");b.className="btn";b.type="button";b.id="qrPrintCode";b.textContent="Code imprimante";
  b.title="Programme ZPL / TSPL / EPL2 de ce QR (sans couleurs ni logo : une imprimante thermique imprime en noir)";
  $("dl").insertAdjacentElement("afterend",b);
  b.addEventListener("click",()=>{const p=payload();if(!p){toast("Le contenu est vide.");return;}
    window.chrome.webview.postMessage(JSON.stringify({type:"print",text:p,format:"qrplain",gs1:false,source:"Créer un code · QR Code"}));});
}
update();fillRanges();
})();""")
    wasm = base64.b64encode(read(VENDOR, f"zxing_full-{ZXING_VERSION}.wasm", mode="rb")).decode()
    tail = (f'<script type="application/octet-stream" id="zxwasm">{wasm}</script>\n<script>\n{scan_js}\n</script>\n'
            f'<script>\n{create_js}\n</script>\n')
    return s, tail


def english_entries(page):
    """Traductions du programme utiles à la page : textes présents tels quels, ou modèles {0} dont un morceau fixe y figure."""
    import json
    entries = []
    for line in read(ROOT, "..", "src", "Scanalyse.Desktop", "Lang", "en.tsv").split("\n"):
        if "\t" not in line or line.startswith("#"):
            continue
        fr, en = line.split("\t", 1)
        if re.search(r"\{\d+\}", fr):
            parts = [p for p in re.split(r"\{\d+\}", fr) if len(p.strip()) >= 4]
            keep = bool(parts) and all(p in page for p in parts)
        else:
            keep = fr in page or (len(fr) <= 40 and fr.lower() in page.lower())
        if keep:
            entries.append([fr, en])
    return json.dumps(entries, ensure_ascii=False).replace("</", "<\\/")


def app_tail(page):
    """Version téléphone : traductions de l'Atelier, puis les écrans mobiles (web/mobile)."""
    return inline_script("window.SCANALYSE_EN=" + english_entries(page) + ";") + "\n" + inline_script(read(ROOT, "mobile", "mobile.js"))


def mobile_markup():
    """Écrans du téléphone, avec le logo simplifié en ligne."""
    logo = read(ROOT, "..", "docs", "logo-petit.svg")
    logo = re.sub(r"<\?xml[^>]*\?>|<!DOCTYPE[^>]*>", "", logo).strip()
    logo = logo.replace("<svg ", '<svg class="m-logo" aria-hidden="true" ', 1)
    html = read(ROOT, "mobile", "mobile.html")
    return rep(html, '<svg class="m-logo" aria-hidden="true" viewBox="0 0 1024 1024"><use href="#m-logo-art"/></svg>', logo)


def main():
    s, tail = assemble()
    qr_cdn = f'<script src="https://cdn.jsdelivr.net/npm/qr-code-styling@{QR_VERSION}/lib/qr-code-styling.js"></script>'
    qr_inline = inline_script(read(VENDOR, f"qr-code-styling-{QR_VERSION}.js"))
    zx_inline = inline_script(read(VENDOR, f"zxing-wasm-full-{ZXING_VERSION}.js"))

    # 1. Artefact Claude (bibliothèques sur CDN)
    artifact = (s + f'\n<script src="https://cdn.jsdelivr.net/npm/zxing-wasm@{ZXING_VERSION}/dist/iife/full/index.js"></script>\n'
                + '<script src="https://cdn.jsdelivr.net/npm/jsqr@1.4.0/dist/jsQR.js"></script>\n' + tail)
    write(os.path.join(DIST, "artifact.html"), artifact)

    # Base hors ligne (tout inclus)
    offline = s.replace(qr_cdn, qr_inline)
    title = re.search(r"<title>.*?</title>", offline).group(0)
    offline = offline.replace(title, "", 1).replace('<meta name="claude:viewport-fit-sides" content="cover">\n', "", 1)
    offline += "\n" + zx_inline + "\n" + tail

    # 2. Version web autonome (PWA)
    head = read(ROOT, "standalone", "head.html").replace("/*MOBILECSS*/", read(ROOT, "mobile", "mobile.css"))
    out = os.path.join(DIST, "standalone")
    write(os.path.join(out, "index.html"), head + mobile_markup() + offline + "\n" + app_tail(offline) + "\n</body></html>\n")
    for f in ("manifest.webmanifest", "sw.js", "icon-192.png", "icon-512.png", "icon-maskable-512.png", "apple-touch-icon.png"):
        shutil.copyfile(os.path.join(ROOT, "standalone", f), os.path.join(out, f))

    # 3. Version embarquée dans Scanalyse.Desktop
    desktop_head = """<!doctype html>
<html lang="fr"><head>
<!-- Fichier généré par web/build.py : ne pas modifier à la main. -->
<meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<title>Scanalyse · Atelier</title>
<style>body{margin:0}img{max-width:100%}</style>
</head><body>
"""
    write(DESKTOP_OUT, desktop_head + offline + "\n</body></html>\n")
    print("OK :", os.path.normpath(DESKTOP_OUT), "et", os.path.normpath(DIST))


if __name__ == "__main__":
    main()
