"""Site vitrine de Scanalyse (GitHub Pages) : site/index.html (français) et site/en/index.html (anglais).

Un seul gabarit, deux langues. Les captures d'écran sont reprises si elles existent dans site/captures/
(voir CAPTURES ci-dessous) ; sans elles, la section n'apparaît pas.

    python3 tools/build_site.py
"""

from __future__ import annotations

import html
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
SITE = ROOT / "site"
REPO = "https://github.com/Kanard-Compagny/scanalyse"
DOWNLOAD = f"{REPO}/releases/latest/download/Scanalyse.exe"
URL = "https://kanard-compagny.github.io/scanalyse/"

VERSION = re.search(r"<VersionPrefix>([^<]+)</VersionPrefix>", (ROOT / "Directory.Build.props").read_text("utf-8")).group(1)

# Captures : fichier dans site/captures/, légende française, légende anglaise.
CAPTURES = [
    ("analyser.webp", "Analyser une image : chaque code lu et expliqué, cohérence de l'étiquette", "Analyze an image: every code read and explained, label consistency"),
    ("banc.webp", "Banc d'essai : la lecture d'un terminal Android comparée au vrai contenu", "Scanner test bench: an Android terminal's read compared with the real content"),
    ("etiquette.webp", "Étiquette imprimante : modèle prêt à l'emploi, aperçu et programme ZPL", "Printer label: ready-made template, preview and ZPL program"),
    ("conf.webp", "Conf imprimante : fichier créé depuis un formulaire, Wi-Fi compris", "Printer config: file created from a form, Wi-Fi included"),
    ("creer.webp", "Créer un code : GS1-128 relu et vérifié", "Create a code: GS1-128 read back and verified"),
    ("appareils.webp", "Appareils de lecture : un terminal relié en Wi-Fi par QR code", "Reading devices: a terminal linked over Wi-Fi with a QR code"),
    ("relais.webp", "Le téléphone ou le PDA sert de douchette", "The phone or PDA works as a scanner"),
]

T = {
    "fr": {
        "lang": "fr",
        "title": "Scanalyse — la boîte à outils gratuite du technicien codes-barres",
        "desc": "Programme Windows gratuit : lire et expliquer les codes-barres (GS1, QR, Data Matrix), tester une douchette, lire et envoyer les programmes et fichiers de configuration des imprimantes d'étiquettes, toutes marques.",
        "other": ("English", "en/"),
        "nav_dl": "Télécharger",
        "h1": "Trouver où ça coince, entre le code, la douchette et l'imprimante.",
        "lead": "Scanalyse lit les codes-barres de vos clients, compare ce que leur douchette transmet vraiment et explique les programmes de leurs imprimantes d'étiquettes. Gratuit, sans installation, toutes marques.",
        "dl": "Télécharger Scanalyse {v}",
        "dl_note": "Windows 10 et 11, un seul fichier Scanalyse.exe, rien à installer.",
        "source": "Code source sur GitHub",
        "readout_title": "Lu par la douchette du client",
        "readout": [
            ("01", "03453120000011", "GTIN, clé de contrôle juste"),
            ("10", "LOT42", "Numéro de lot"),
            ("17", "261231", "À consommer avant le 31/12/2026"),
        ],
        "readout_warn": "Séparateur GS perdu après le lot : le logiciel lit « LOT4217261231 » et ne trouve plus la date. À corriger dans le réglage clavier de la douchette.",
        "calls_h": "Trois appels qu'on connaît par cœur",
        "calls": [
            ("« La douchette ne lit pas. »", "Le code est lu sur une photo ou une capture : contenu, clé de contrôle, contraste, zone blanche. On sait tout de suite si le code est en cause."),
            ("« Le logiciel reçoit n'importe quoi. »", "On scanne avec la douchette du client : Scanalyse compare ce qu'elle envoie au vrai contenu, octet par octet, et donne le code de configuration qui corrige."),
            ("« L'étiquette sort mal. »", "Le programme ou le fichier de configuration de l'imprimante est lu ligne par ligne, avec l'aperçu de l'étiquette et chaque erreur expliquée."),
        ],
        "tools_h": "Ce qu'il y a dedans",
        "tools": [
            ("Analyser une image", "Photo, capture d'écran ou PDF de plusieurs pages : chaque code lu, ses données GS1 décodées, ses défauts signalés."),
            ("Créer un code", "Plus de 25 types : QR personnalisé, Data Matrix, GS1, EAN, Code 128, PDF417, MaxiCode. Export en image."),
            ("Étiquette imprimante", "ZPL, TSPL, EPL2, DPL, SBPL, Fingerprint : aperçu, contenu des codes, modèles prêts à l'emploi, envoi à l'imprimante."),
            ("Conf imprimante", "Fichiers de configuration analysés, convertis d'une marque à l'autre ou créés depuis un formulaire, Wi-Fi compris. L'imprimante est reconnue sur le réseau."),
            ("Banc d'essai douchette", "Ce que la douchette transmet, comparé au code : clavier, préfixe, suffixe, séparateur GS1. Codes de configuration Honeywell, Datalogic, Zebra."),
            ("Appareils de lecture", "Douchettes USB, série et HID POS, terminaux Android en USB ou en Wi-Fi. Un téléphone sans lecteur lit avec sa caméra."),
            ("Historique et rapports", "Analyses rangées par dossier client, rapport PDF à votre en-tête, dossier partageable avec l'équipe."),
        ],
        "shots_h": "En images",
        "brands_h": "Toutes marques",
        "brands_p": "Scanalyse ne privilégie aucun fabricant. Il reconnaît notamment :",
        "start_h": "Démarrer en deux minutes",
        "start": [
            "Téléchargez Scanalyse.exe et placez-le où vous voulez, même sur une clé USB.",
            "Lancez-le. Si Windows affiche « Windows a protégé votre ordinateur », choisissez « Informations complémentaires » puis « Exécuter quand même ».",
            "Choisissez la langue et vos dossiers, puis suivez la visite guidée.",
        ],
        "trust_h": "Ce qui ne change pas",
        "trust": [
            ("Rien ne quitte le PC", "Aucune image, aucun code lu, aucun rapport n'est envoyé sur Internet. Pas de compte, pas de publicité, pas de mesure d'audience."),
            ("Gratuit et ouvert", "Licence GPL v3. Le code est public, chaque version publie son empreinte SHA-256, vérifiée par la mise à jour automatique."),
            ("Fait par un technicien", "Né sur le terrain, pour combler un manque d'outils. Un problème ou un matériel à ajouter ? Ouvrez un ticket."),
        ],
        "issue": "Signaler un problème ou proposer une idée",
        "changelog": "Nouveautés de chaque version",
        "donate": "Soutenir le projet (don libre)",
        "foot": "Scanalyse est un projet indépendant, sans lien avec les fabricants cités.",
    },
    "en": {
        "lang": "en",
        "title": "Scanalyse — the free toolbox for barcode technicians",
        "desc": "Free Windows program: read and explain barcodes (GS1, QR, Data Matrix), test a scanner, read and send label printer programs and configuration files, all brands.",
        "other": ("Français", "../"),
        "nav_dl": "Download",
        "h1": "Find where it goes wrong, between the code, the scanner and the printer.",
        "lead": "Scanalyse reads your customers' barcodes, compares what their scanner really sends and explains the programs of their label printers. Free, nothing to install, all brands.",
        "dl": "Download Scanalyse {v}",
        "dl_note": "Windows 10 and 11, a single Scanalyse.exe file, nothing to install.",
        "source": "Source code on GitHub",
        "readout_title": "Read by the customer's scanner",
        "readout": [
            ("01", "03453120000011", "GTIN, check digit correct"),
            ("10", "LOT42", "Batch number"),
            ("17", "261231", "Use by 31/12/2026"),
        ],
        "readout_warn": "GS separator lost after the batch: the software reads “LOT4217261231” and no longer finds the date. Fix it in the scanner's keyboard settings.",
        "calls_h": "Three calls you know by heart",
        "calls": [
            ("“The scanner won't read.”", "The code is read from a photo or a screenshot: content, check digit, contrast, quiet zone. You know at once whether the code is to blame."),
            ("“The software receives garbage.”", "Scan with the customer's scanner: Scanalyse compares what it sends with the real content, byte by byte, and gives the configuration code that fixes it."),
            ("“The label prints wrong.”", "The printer's program or configuration file is read line by line, with a preview of the label and every error explained."),
        ],
        "tools_h": "What's inside",
        "tools": [
            ("Analyze an image", "Photo, screenshot or multi-page PDF: every code read, its GS1 data decoded, its defects reported."),
            ("Create a code", "More than 25 types: custom QR, Data Matrix, GS1, EAN, Code 128, PDF417, MaxiCode. Export as an image."),
            ("Printer label", "ZPL, TSPL, EPL2, DPL, SBPL, Fingerprint: preview, content of the codes, ready-made templates, send to the printer."),
            ("Printer config", "Configuration files analyzed, converted from one brand to another or created from a form, Wi-Fi included. The printer is recognized on the network."),
            ("Scanner test bench", "What the scanner sends, compared with the code: keyboard, prefix, suffix, GS1 separator. Honeywell, Datalogic and Zebra configuration codes."),
            ("Reading devices", "USB, serial and HID POS scanners, Android terminals over USB or Wi-Fi. A phone without a scan engine reads with its camera."),
            ("History and reports", "Analyses filed by customer case, PDF report with your letterhead, folder shared with the team."),
        ],
        "shots_h": "Screenshots",
        "brands_h": "All brands",
        "brands_p": "Scanalyse favors no manufacturer. Among others, it recognizes:",
        "start_h": "Up and running in two minutes",
        "start": [
            "Download Scanalyse.exe and put it wherever you like, even on a USB stick.",
            "Run it. If Windows shows “Windows protected your PC”, choose “More info” then “Run anyway”.",
            "Pick the language and your folders, then follow the guided tour.",
        ],
        "trust_h": "What doesn't change",
        "trust": [
            ("Nothing leaves the PC", "No image, no code read and no report is sent over the Internet. No account, no ads, no analytics."),
            ("Free and open", "GPL v3 license. The code is public, every release publishes its SHA-256 hash, checked by the automatic update."),
            ("Made by a technician", "Born in the field to fill a gap in the tools. A problem, or hardware to support? Open an issue."),
        ],
        "issue": "Report a problem or suggest an idea",
        "changelog": "What's new in each release",
        "donate": "Support the project (optional donation)",
        "foot": "Scanalyse is an independent project, not affiliated with the manufacturers mentioned.",
    },
}

BRANDS = ["Zebra", "Honeywell", "Datalogic", "TSC", "SATO", "Toshiba TEC", "Intermec", "Datamax-O'Neil", "Newland", "Urovo", "CipherLab", "Point Mobile"]

CSS = """
@font-face{font-family:Archivo;src:url(%(root)sfonts/archivo.woff2) format("woff2");font-weight:100 900;font-stretch:62% 125%;font-display:swap}
@font-face{font-family:"JetBrains Mono";src:url(%(root)sfonts/jetbrains-mono.woff2) format("woff2");font-weight:400;font-display:swap}
:root{--forest:#0F3D2E;--deep:#0B2A20;--brand:#145C43;--mint:#E1EEE8;--paper:#F6F7F5;--ink:#15191C;--muted:#55605B;--line:#D9DED9;--orange:#F07F13;--orange-ink:#2B1600;--warn:#FFE7CF}
*{box-sizing:border-box}
html{-webkit-text-size-adjust:100%%}
body{margin:0;background:var(--paper);color:var(--ink);font:400 17px/1.55 Archivo,"Segoe UI",system-ui,sans-serif;font-stretch:100%%}
a{color:var(--brand)}
a:focus-visible,button:focus-visible{outline:3px solid var(--orange);outline-offset:3px;border-radius:6px}
.wrap{max-width:1120px;margin:0 auto;padding:0 24px}
header.top{background:var(--forest);color:#fff}
header.top .wrap{display:flex;align-items:center;gap:18px;min-height:64px}
.brand{display:flex;align-items:center;gap:10px;color:#fff;text-decoration:none;font-weight:700;font-size:19px;font-stretch:112%%}
.brand img{width:32px;height:32px}
header.top nav{margin-left:auto;display:flex;align-items:center;gap:20px;font-size:15px}
header.top nav a{color:#CFE3D9;text-decoration:none}
header.top nav a:hover{color:#fff;text-decoration:underline}
header.top nav a.dl{color:var(--orange-ink);background:var(--orange);padding:8px 14px;border-radius:8px;font-weight:600}
header.top nav a.dl:hover{text-decoration:none;filter:brightness(1.06)}
.hero{background:var(--forest);color:#fff;padding:48px 0 72px}
.hero .wrap{display:grid;grid-template-columns:1.05fr .95fr;gap:56px;align-items:center}
h1{font-size:clamp(34px,5vw,56px);line-height:1.04;font-weight:760;font-stretch:118%%;letter-spacing:-.01em;margin:0 0 22px;max-width:15ch}
.lead{font-size:19px;color:#D6E6DE;max-width:36em;margin:0 0 30px}
.cta{display:flex;flex-wrap:wrap;align-items:center;gap:14px 22px}
.btn{display:inline-flex;align-items:center;gap:10px;background:var(--orange);color:var(--orange-ink);font-weight:700;font-size:18px;padding:15px 22px;border-radius:10px;text-decoration:none}
.btn:hover{filter:brightness(1.06)}
.btn svg{width:20px;height:20px}
.cta .src{color:#CFE3D9}
.note{font-size:14px;color:#A9C6B8;margin:14px 0 0}
/* Lecture dans le viseur : l'élément signature, repris des coins orange du logo */
.finder{position:relative;padding:26px}
.finder::before,.finder::after,.finder i::before,.finder i::after{content:"";position:absolute;width:38px;height:38px;border:5px solid var(--orange)}
.finder::before{top:0;left:0;border-right:0;border-bottom:0;border-top-left-radius:10px}
.finder::after{top:0;right:0;border-left:0;border-bottom:0;border-top-right-radius:10px}
.finder i::before{bottom:0;left:0;border-right:0;border-top:0;border-bottom-left-radius:10px}
.finder i::after{bottom:0;right:0;border-left:0;border-top:0;border-bottom-right-radius:10px}
.readout{background:var(--deep);border:1px solid #23503F;border-radius:12px;padding:20px 22px;font-size:15px}
.readout h2{font-size:14px;font-weight:600;color:#A9C6B8;margin:0 0 12px}
.raw{font-family:"JetBrains Mono",Consolas,monospace;font-size:14px;color:#fff;word-break:break-all;margin:0 0 16px;line-height:1.6}
.raw .gs{border-radius:3px;padding:0 4px;margin:0 2px;font-size:12px}
.raw .lost{color:var(--orange-ink);background:var(--orange)}
.ai{display:grid;grid-template-columns:auto 1fr;gap:4px 14px;margin:0}
.ai dt{font-family:"JetBrains Mono",Consolas,monospace;color:#7FD1A8}
.ai dd{margin:0;color:#E6F0EB}
.ai dd code{font-family:"JetBrains Mono",Consolas,monospace;color:#fff;margin-right:8px}
.warn{margin:16px 0 0;padding:10px 12px;border-radius:8px;background:var(--warn);color:#5A2E00;font-size:14px;font-weight:600}
section{padding:72px 0}
h2.sec{font-size:clamp(26px,3.2vw,36px);font-weight:720;font-stretch:112%%;line-height:1.15;margin:0 0 28px;max-width:22ch}
.calls{display:grid;grid-template-columns:repeat(3,1fr);gap:36px}
.calls blockquote{margin:0 0 10px;font-size:22px;font-weight:650;font-stretch:108%%;line-height:1.2;color:var(--forest)}
.calls p{margin:0;color:var(--muted)}
.tools{background:#fff;border-top:1px solid var(--line);border-bottom:1px solid var(--line)}
.tools dl{display:grid;grid-template-columns:repeat(2,1fr);gap:0 56px;margin:0}
.tools .row{padding:18px 0;border-top:1px solid var(--line)}
.tools dt{font-weight:700;font-size:19px;margin:0 0 4px}
.tools dd{margin:0;color:var(--muted);max-width:42em}
.shots{display:grid;grid-template-columns:repeat(2,1fr);gap:28px}
.shots figure{margin:0}
.shots img{width:100%%;height:auto;border-radius:10px;border:1px solid var(--line);display:block;background:#fff}
.shots figcaption{font-size:15px;color:var(--muted);margin-top:8px}
.brands p{color:var(--muted);margin:0 0 16px}
.brands ul{list-style:none;margin:0;padding:0;display:flex;flex-wrap:wrap;gap:10px}
.brands li{background:var(--mint);color:var(--forest);font-weight:600;padding:8px 14px;border-radius:999px;font-size:15px}
.split{display:grid;grid-template-columns:1fr 1fr;gap:56px;align-items:start}
ol.steps{margin:0;padding:0;list-style:none;counter-reset:s}
ol.steps li{counter-increment:s;position:relative;padding:0 0 18px 48px}
ol.steps li::before{content:counter(s);position:absolute;left:0;top:-2px;width:32px;height:32px;border-radius:50%%;background:var(--forest);color:#fff;font-weight:700;display:grid;place-items:center;font-size:15px}
.trust div{padding:0 0 18px}
.trust h3{margin:0 0 4px;font-size:18px}
.trust p{margin:0;color:var(--muted)}
footer{background:var(--forest);color:#CFE3D9;padding:36px 0;font-size:15px}
footer .wrap{display:flex;flex-wrap:wrap;gap:12px 28px;align-items:center}
footer a{color:#fff}
footer small{flex-basis:100%%;color:#A9C6B8}
@media (max-width:860px){
 .hero .wrap,.split{grid-template-columns:1fr;gap:36px}
 .calls,.tools dl,.shots{grid-template-columns:1fr}
 .calls{gap:28px}
 header.top nav a.src{display:none}
 section{padding:52px 0}
}
@media (max-width:480px){.wrap{padding:0 16px}.finder{padding:18px}.btn{width:100%%;justify-content:center}}
"""

PAGE = """<!doctype html>
<html lang="{lang}">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<title>{title}</title>
<meta name="description" content="{desc}">
<link rel="canonical" href="{canonical}">
<link rel="alternate" hreflang="fr" href="{url}">
<link rel="alternate" hreflang="en" href="{url}en/">
<link rel="icon" type="image/svg+xml" href="{root}logo-petit.svg">
<meta property="og:type" content="website">
<meta property="og:title" content="{title}">
<meta property="og:description" content="{desc}">
<meta property="og:image" content="{url}og.png">
<meta property="og:url" content="{canonical}">
<meta name="twitter:card" content="summary_large_image">
<meta name="theme-color" content="#0F3D2E">
<style>{css}</style>
</head>
<body>
<header class="top"><div class="wrap">
  <a class="brand" href="{root_self}"><img src="{root}logo.svg" alt="" width="32" height="32">Scanalyse</a>
  <nav><a href="{other_href}" hreflang="{other_lang}" lang="{other_lang}">{other_label}</a><a class="src" href="{repo}">GitHub</a><a class="dl" href="{download}">{nav_dl}</a></nav>
</div></header>
<main>
<div class="hero"><div class="wrap">
  <div>
    <h1>{h1}</h1>
    <p class="lead">{lead}</p>
    <div class="cta">
      <a class="btn" href="{download}"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M12 4v11M7 10l5 5 5-5M5 20h14"/></svg>{dl}</a>
      <a class="src" href="{repo}">{source}</a>
    </div>
    <p class="note">{dl_note}</p>
  </div>
  <div class="finder"><i></i>
    <div class="readout">
      <h2>{readout_title}</h2>
      <p class="raw">]d201{a1}10{a2}<span class="gs lost">GS ?</span>17{a3}</p>
      <dl class="ai">{readout_rows}</dl>
      <p class="warn">{readout_warn}</p>
    </div>
  </div>
</div></div>

<section><div class="wrap">
  <h2 class="sec">{calls_h}</h2>
  <div class="calls">{calls}</div>
</div></section>

<section class="tools"><div class="wrap">
  <h2 class="sec">{tools_h}</h2>
  <dl>{tools}</dl>
</div></section>
{shots}
<section class="brands"><div class="wrap">
  <h2 class="sec">{brands_h}</h2>
  <p>{brands_p}</p>
  <ul>{brands}</ul>
</div></section>

<section style="padding-top:0"><div class="wrap split">
  <div><h2 class="sec">{start_h}</h2><ol class="steps">{start}</ol>
    <a class="btn" href="{download}"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M12 4v11M7 10l5 5 5-5M5 20h14"/></svg>{dl}</a></div>
  <div class="trust"><h2 class="sec">{trust_h}</h2>{trust}</div>
</div></section>
</main>
<footer><div class="wrap">
  <a href="{repo}/issues/new/choose">{issue}</a>
  <a href="{repo}/blob/main/CHANGELOG.md">{changelog}</a>
  <a href="{repo}">GitHub</a>
  <a href="{repo}/blob/main/LICENSE">GPL v3</a>
  <a href="https://www.paypal.me/MrKanard">{donate}</a>
  <small>{foot}</small>
</div></footer>
</body>
</html>
"""


def e(s: str) -> str:
    return html.escape(s, quote=True)


def build(code: str) -> str:
    t = T[code]
    root = "" if code == "fr" else "../"
    rows = "".join(f"<dt>({e(ai)})</dt><dd><code>{e(v)}</code>{e(m)}</dd>" for ai, v, m in t["readout"])
    calls = "".join(f"<div><blockquote>{e(q)}</blockquote><p>{e(a)}</p></div>" for q, a in t["calls"])
    tools = "".join(f'<div class="row"><dt>{e(n)}</dt><dd>{e(d)}</dd></div>' for n, d in t["tools"])
    figs = []
    for name, fr, en in CAPTURES:
        if (SITE / "captures" / name).exists():
            cap = fr if code == "fr" else en
            figs.append(f'<figure><a href="{root}captures/{name}"><img src="{root}captures/{name}" alt="{e(cap)}" width="1600" height="875" loading="lazy"></a><figcaption>{e(cap)}</figcaption></figure>')
    shots = (f'\n<section><div class="wrap">\n  <h2 class="sec">{e(t["shots_h"])}</h2>\n  <div class="shots">{"".join(figs)}</div>\n</div></section>\n'
             if figs else "")
    other_label, other_href = t["other"]
    canonical = URL if code == "fr" else URL + "en/"
    return PAGE.format(
        lang=code, title=e(t["title"]), desc=e(t["desc"]), canonical=canonical, url=URL, root=root, root_self="./",
        css=CSS.replace("%(root)s", root).replace("%%", "%"), other_href=other_href, other_lang="en" if code == "fr" else "fr", other_label=other_label,
        repo=REPO, download=DOWNLOAD, nav_dl=e(t["nav_dl"]), h1=e(t["h1"]), lead=e(t["lead"]),
        dl=e(t["dl"].format(v=VERSION)), source=e(t["source"]), dl_note=e(t["dl_note"]),
        readout_title=e(t["readout_title"]), a1=t["readout"][0][1], a2=t["readout"][1][1], a3=t["readout"][2][1],
        readout_rows=rows, readout_warn=e(t["readout_warn"]), calls_h=e(t["calls_h"]), calls=calls,
        tools_h=e(t["tools_h"]), tools=tools, shots=shots, brands_h=e(t["brands_h"]), brands_p=e(t["brands_p"]),
        brands="".join(f"<li>{e(b)}</li>" for b in BRANDS), start_h=e(t["start_h"]),
        start="".join(f"<li>{e(s)}</li>" for s in t["start"]), trust_h=e(t["trust_h"]),
        trust="".join(f"<div><h3>{e(h)}</h3><p>{e(p)}</p></div>" for h, p in t["trust"]),
        issue=e(t["issue"]), donate=e(t["donate"]), changelog=e(t["changelog"]), foot=e(t["foot"]),
    )


def main() -> None:
    (SITE / "en").mkdir(parents=True, exist_ok=True)
    (SITE / "index.html").write_text(build("fr"), "utf-8")
    (SITE / "en" / "index.html").write_text(build("en"), "utf-8")
    print(f"Site écrit : version {VERSION}")


if __name__ == "__main__":
    main()
