# Scanalyse

Analyse et diagnostic de lecture des codes-barres : **ce que contient un code, à quoi il sert, et pourquoi il passe ou non** entre le code imprimé, le scanner et l'application.

> Logiciel privé — tous droits réservés.

## Contenu de la solution

| Projet | Rôle | Cible |
|---|---|---|
| `src/Scanalyse.Core` | Cerveau de l'analyse : lecture GS1, interprétation du contenu (liens, Wi-Fi, vCard, SEPA, StageNow…), diagnostic « contenu du code ↔ données reçues ». Aucune dépendance externe. | .NET 10 (multi-plateforme) |
| `src/Scanalyse.Desktop` | Programme Windows (WPF) : menu latéral avec **Analyser une image**, **Créer un code** (Atelier embarqué), **Code imprimante** (programme ZPL / TSPL / EPL2 / DPL, envoi réseau ou file Windows), **Banc d'essai douchette**, **Appareils de lecture** (douchettes en port COM, HID POS, caméras, relais pour terminaux Android par ADB ou Wi-Fi, moniteur des octets reçus), **Historique** (dossiers SAV, rapport PDF), **Symbologies** et **Paramètres** ; barre d'outils (coller une image, capturer une zone de l'écran, thème) ; journal des lectures. | .NET 10 Windows (10.0.19041+) |
| `web/` | Sources de l'Atelier (HTML/CSS/JS) et bibliothèques figées dans `web/vendor`. `web/build.py` génère la page embarquée `src/Scanalyse.Desktop/Web/index.html`. | Navigateur / WebView2 |
| `tests/Scanalyse.Core.Tests` | Tests automatiques du Core (xUnit). | .NET 10 |

À venir : douchettes HID POS / port COM / mode clavier avancé, application Android **Relais**, historique et rapports.

## Parcours type : « le client m'envoie une photo »

1. **Coller une image** (Ctrl + Maj + V) ou **Capturer une zone** depuis la barre d'outils, ou importer un fichier dans *Analyser une image*.
2. Sur la fiche du code : **Comparer avec une lecture douchette**.
3. Le banc d'essai s'ouvre avec le contenu exact du code : scanner le même code avec la douchette, puis **Comparer**.
   Avec une douchette reliée en port COM ou en HID POS (page **Appareils de lecture**), la comparaison se lance seule à chaque lecture, sur les octets exacts.
4. Pour réimprimer le code chez le client : **Code imprimante** sur la fiche du code (ou au banc d'essai), choisir ZPL / TSPL / EPL2 et la taille de l'étiquette, puis **Envoyer à l'imprimante** (adresse IP, port 9100) ou enregistrer le fichier.

## Direction artistique

Direction C « Atelier » : menu latéral vert foncé, contenu sur fond neutre, **vert** pour l'identité et la sélection, **orange bec de canard** réservé à l'action principale de chaque écran (une seule fois), couleurs d'état dédiées (le jaune pour « à vérifier », jamais l'orange de marque). Polices natives Windows 11 : Segoe UI Variable et Cascadia Mono. Thèmes clair, sombre et « suivre Windows ».

**Source unique : `design/tokens.json`.** Après modification, lancer `python tools/build_theme.py` (thèmes WPF + variables CSS de l'Atelier), puis `python web/build.py`.

## Modifier l'Atelier

Modifier les fichiers de `web/src/`, puis lancer `python web/build.py` (Python 3) pour régénérer la page embarquée. Ne jamais modifier `src/Scanalyse.Desktop/Web/index.html` à la main.

## Prérequis

- Visual Studio 2026 avec la charge de travail **Développement .NET Desktop**.
- Windows 10 ou 11 avec le composant Microsoft Edge **WebView2** (présent d'office sur Windows 11 et sur les Windows 10 à jour).
- Python 3, uniquement pour modifier l'Atelier.
- (Plus tard, pour Relais) la charge de travail **Développement mobile .NET**.

## Démarrer dans Visual Studio

1. Ouvrir `Scanalyse.slnx`.
2. Clic droit sur **Scanalyse.Desktop** → *Définir comme projet de démarrage*.
3. **F5** pour lancer.
4. Tests : menu *Test* → *Explorateur de tests* → *Exécuter tout*.

## Publier l'exécutable portable

Clic droit sur **Scanalyse.Desktop** → *Publier* → profil **Portable** → *Publier*.
On obtient un unique `Scanalyse.exe` autonome dans `src/Scanalyse.Desktop/bin/Portable/` : il se lance sans installation, depuis une clé USB, sur n'importe quel PC Windows 10 (2004 ou plus récent) ou 11, 64 bits.

## Diffuser Scanalyse à l'équipe

- **Compilation automatique (GitHub Actions)** : à chaque envoi sur `main`, les tests tournent et `Scanalyse.exe` est disponible dans l'onglet *Actions* du dépôt (artefact de la compilation).
- **Version officielle** : sur GitHub, *Actions → Compilation → Run workflow*, saisir la version (ex. `0.10.0`) puis *Run workflow*. L'étiquette `vX.Y.Z` et la *Release* (exécutable + contenu de `CHANGELOG.md`) sont créées automatiquement. Créer soi-même un tag `vX.Y.Z` fonctionne aussi.
- **Sur le PC d'un collègue** : copier `Scanalyse.exe` où il veut. Prérequis : le composant Microsoft Edge WebView2 (présent sur Windows 11 et Windows 10 à jour). L'historique se range dans `Historique` à côté de l'exe ; pour partager les dossiers SAV, chacun choisit le même dossier réseau dans *Paramètres → Historique et rapports*.
- **Support** : *Paramètres → À propos → Copier le diagnostic* donne versions et emplacements à joindre à une demande d'aide.

## Façon de travailler (Git)

- `main` : version stable, qui compile et dont les tests passent.
- Une branche par évolution (`feature/douchette-hid`, `fix/…`), fusionnée dans `main` quand elle est validée.
- Dans Visual Studio : fenêtre **Modifications Git** (*Affichage → Modifications Git*) pour valider, **Récupérer / Tirer** pour récupérer le travail, sélecteur de branche en bas à droite.

## Conventions

- Code et commentaires en français pour la documentation, identifiants en anglais.
- Avertissements traités comme des erreurs dans le Core.
- Toute règle d'analyse ou de diagnostic s'accompagne d'un test.
