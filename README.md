# Scanalyse

Analyse et diagnostic de lecture des codes-barres : **ce que contient un code, à quoi il sert, et pourquoi il passe ou non** entre le code imprimé, le scanner et l'application.

> Logiciel privé — tous droits réservés.

## Contenu de la solution

| Projet | Rôle | Cible |
|---|---|---|
| `src/Scanalyse.Core` | Cerveau de l'analyse : lecture GS1, interprétation du contenu (liens, Wi-Fi, vCard, SEPA, StageNow…), diagnostic « contenu du code ↔ données reçues ». Aucune dépendance externe. | .NET 10 (multi-plateforme) |
| `src/Scanalyse.Desktop` | Programme Windows (WPF). Pour l'instant : banc d'essai de comparaison et d'analyse. | .NET 10 Windows |
| `tests/Scanalyse.Core.Tests` | Tests automatiques du Core (xUnit). | .NET 10 |

À venir : moteur de décodage d'images (zxing-cpp compilé dans la solution), douchettes HID POS / port COM / mode clavier, application Android **Relais**, historique et rapports.

## Prérequis

- Visual Studio 2026 avec la charge de travail **Développement .NET Desktop**.
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
