"""
Génère les thèmes à partir de design/tokens.json (source unique) :
  - src/Scanalyse.Desktop/Themes/Light.xaml et Dark.xaml (pinceaux WPF)
  - web/src/theme.css (variables CSS de l'Atelier, clair + sombre)

Usage : python tools/build_theme.py
"""
import json
import os

ROOT = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
tokens = json.load(open(os.path.join(ROOT, "design", "tokens.json"), encoding="utf-8"))

HEADER = "<!-- Fichier généré par tools/build_theme.py depuis design/tokens.json : ne pas modifier à la main. -->\n"


def write(path, text):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "w", encoding="utf-8", newline="\r\n") as f:
        f.write(text)


def xaml(theme):
    lines = [HEADER.rstrip(), '<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"',
             '                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">']
    for key, value in tokens[theme].items():
        lines.append(f'  <Color x:Key="{key}Color">{value}</Color>')
        lines.append(f'  <SolidColorBrush x:Key="{key}" Color="{value}" />')
    lines.append("</ResourceDictionary>")
    return "\n".join(lines) + "\n"


# Correspondance avec les noms de variables historiques de l'Atelier.
CSS_MAP = {
    "--bg": "Bg", "--panel": "Surface", "--surface2": "Surface2", "--ink": "Ink", "--muted": "Muted", "--line": "Line",
    "--accent": "Accent", "--accent-ink": "OnAccent", "--soft": "BrandSoft", "--brand": "Brand", "--brand-text": "BrandText",
    "--action": "Action", "--action-hover": "ActionHover", "--on-action": "OnAction", "--focus": "Focus",
    "--ok": "OkFg", "--ok-bg": "OkBg", "--info": "InfoFg", "--info-bg": "InfoBg",
    "--warn": "WarnFg", "--warn-bg": "WarnBg", "--bad": "BadFg", "--bad-bg": "BadBg", "--check": "Check",
}


def css_vars(theme):
    return " ".join(f"{var}:{tokens[theme][key]};" for var, key in CSS_MAP.items())


fonts = tokens["fonts"]
css = f"""/* Fichier généré par tools/build_theme.py depuis design/tokens.json : ne pas modifier à la main. */
:root{{ {css_vars("light")}
  --f-display:{fonts["display"]}; --f-body:{fonts["ui"]}; --f-mono:{fonts["mono"]}; }}
@media (prefers-color-scheme:dark){{:root:not([data-theme="light"]){{ {css_vars("dark")} color-scheme:dark }}}}
:root[data-theme="dark"]{{ {css_vars("dark")} color-scheme:dark }}
"""

write(os.path.join(ROOT, "src", "Scanalyse.Desktop", "Themes", "Light.xaml"), xaml("light"))
write(os.path.join(ROOT, "src", "Scanalyse.Desktop", "Themes", "Dark.xaml"), xaml("dark"))
write(os.path.join(ROOT, "web", "src", "theme.css"), css)
print("Thèmes générés : Light.xaml, Dark.xaml, theme.css")
