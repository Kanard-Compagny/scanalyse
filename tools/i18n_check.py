"""Liste les textes français de l'interface Windows et du cœur qui n'ont pas encore de traduction anglaise.

Usage : python tools/i18n_check.py   (depuis la racine du dépôt)
La table est src/Scanalyse.Desktop/Lang/en.tsv : une ligne « texte français<TAB>texte anglais », {0}, {1}… pour les
parties variables. Les textes de l'Atelier (web/src) se vérifient en ouvrant le programme en anglais.
"""
import glob, html, re

table = set()
for line in open("src/Scanalyse.Desktop/Lang/en.tsv", encoding="utf-8"):
    if "\t" in line and not line.startswith("#"):
        table.add(line.split("\t", 1)[0].replace("\\n", "\n").replace("\\\\", "\\"))

found = {}
def add(s, where):
    s = s.strip()
    if len(s) < 2 or not re.search(r"[a-zà-ÿ]{3,}", s) or re.search(r"(string\.Empty|\{\(|\.ToString\(|https?:|\\u)", s):
        return
    if re.fullmatch(r"[\w.]+", s) and not re.fullmatch(r"[A-ZÀ-Ý][a-zà-ÿ]{3,}", s):
        return
    found.setdefault(s, where)

for f in glob.glob("src/Scanalyse.Desktop/**/*.xaml", recursive=True):
    for m in re.finditer(r'\b(Text|Content|ToolTip|Title|Header)="([^"{][^"]*)"', open(f, encoding="utf-8").read()):
        add(html.unescape(m.group(2)), f)
for f in glob.glob("src/Scanalyse.Desktop/**/*.cs", recursive=True) + glob.glob("src/Scanalyse.Core/**/*.cs", recursive=True):
    t = re.sub(r"^\s*//[^\n]*", "", open(f, encoding="utf-8").read(), flags=re.M)
    for m in re.finditer(r'(\$?)"((?:[^"\\\n]|\\.)*)"', t):
        s = m.group(2)
        if m.group(1) == "$":
            n = iter(range(100))
            s = re.sub(r"\{[^{}]*\}", lambda _: "{%d}" % next(n), s)
        add(s.replace('\\"', '"'), f)

missing = sorted((w, s) for s, w in found.items() if s not in table)
for w, s in missing:
    print(f"{w}\t{s}")
print(f"\n{len(missing)} texte(s) sans traduction (certains sont techniques : chemins, formats, noms propres).")
