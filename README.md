# Scanalyse

Analyse et diagnostic de lecture des codes-barres : **ce que contient un code, à quoi il sert, et pourquoi il passe ou non** entre le code imprimé, le scanner et l'application.

> Logiciel privé — tous droits réservés.

## Contenu de la solution

| Projet | Rôle | Cible |
|---|---|---|
| `src/Scanalyse.Core` | Cerveau de l'analyse : lecture GS1, interprétation du contenu (liens, Wi-Fi, vCard, SEPA, StageNow…), diagnostic « contenu du code ↔ données reçues ». Aucune dépendance externe. | .NET 10 (multi-plateforme) |
| `src/Scanalyse.Desktop` | Programme Windows (WPF) : onglet **Atelier** (création de QR personnalisés, analyse d'images, captures d'écran, zoom) et onglet **Banc d'essai douchette** (comparaison et diagnostic). | .NET 10 Windows |
| `web/` | Sources de l'Atelier (HTML/CSS/JS) et bibliothèques figées dans `web/vendor`. `web/build.py` génère la page embarquée `src/Scanalyse.Desktop/Web/index.html`. | Navigateur / WebView2 |
| `tests/Scanalyse.Core.Tests` | Tests automatiques du Core (xUnit). | .NET 10 |

À venir : douchettes HID POS / port COM / mode clavier avancé, application Android **Relais**, historique et rapports.

## Parcours type : « le client m'envoie une photo »

1. Onglet **Atelier** → *Analyser* : importer, coller (Ctrl+V) ou capturer l'image, sélectionner le code si besoin.
2. Sur la fiche du code : **Comparer avec une lecture douchette**.
3. Le banc d'essai s'ouvre avec le contenu exact du code : scanner le même code avec la douchette, puis **Comparer**.

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
On obtient un unique `Scanalyse.exe` autonome dans `src/Scanalyse.Desktop/bin/Portable/` : il se lance sans installation, depuis une clé USB, sur n'importe quel PC Windows 10/11 64 bits.

## Façon de travailler (Git)

- `main` : version stable, qui compile et dont les tests passent.
- Une branche par évolution (`feature/douchette-hid`, `fix/…`), fusionnée dans `main` quand elle est validée.
- Dans Visual Studio : fenêtre **Modifications Git** (*Affichage → Modifications Git*) pour valider, **Récupérer / Tirer** pour récupérer le travail, sélecteur de branche en bas à droite.

## Conventions

- Code et commentaires en français pour la documentation, identifiants en anglais.
- Avertissements traités comme des erreurs dans le Core.
- Toute règle d'analyse ou de diagnostic s'accompagne d'un test.
