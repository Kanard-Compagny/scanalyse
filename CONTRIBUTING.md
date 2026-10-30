# Contribuer à Scanalyse · Contributing

🇫🇷 Merci de vouloir aider ! 🇬🇧 Thanks for wanting to help! (English below.)

## Français

**Sans écrire de code**, vous aidez déjà beaucoup :
- **Signaler un problème** ou **proposer une idée** : [ouvrir un ticket](https://github.com/Kanard-Compagny/scanalyse/issues/new/choose). Joignez le diagnostic (*Paramètres → À propos → Copier le diagnostic*) et, si possible, l'image, le fichier ou la lecture en cause.
- **Demander la prise en charge d'un matériel** (douchette, terminal, imprimante, toutes marques) : le modèle de ticket « Matériel » liste ce qui est utile (modèle, firmware, fichier exemple).
- **Faire connaître Scanalyse** à vos collègues : c'est gratuit et le restera.

**Pour le code** : tout est expliqué dans [docs/DEVELOPPEMENT.md](docs/DEVELOPPEMENT.md) (organisation du projet, compilation, tests, traductions).
- Une proposition = une modification ciblée, avec ses tests quand c'est le cœur d'analyse (`tests/Scanalyse.Core.Tests`).
- Les textes de l'interface sont écrits en français ; leur traduction anglaise va dans `src/Scanalyse.Desktop/Lang/en.tsv` (`python3 tools/i18n_check.py` liste les oublis).
- Aucune donnée ne doit quitter le PC de l'utilisateur, aucune dépendance à un service en ligne.
- En contribuant, vous acceptez que votre code soit publié sous la licence du projet ([GPL v3](LICENSE)).

## English

**Without writing code**, you already help a lot:
- **Report a problem** or **suggest an idea**: [open an issue](https://github.com/Kanard-Compagny/scanalyse/issues/new/choose) — English is fine. Attach the diagnostics (*Settings → About → Copy diagnostics*) and, if possible, the image, file or read involved.
- **Ask for hardware support** (scanner, terminal, printer, any brand): the "Hardware" issue template lists what helps (model, firmware, sample file).
- **Tell your colleagues about Scanalyse**: it is free and will stay free.

**For code**: everything is explained in [docs/DEVELOPPEMENT.md](docs/DEVELOPPEMENT.md) (in French: project layout, build, tests, translations).
- One pull request = one focused change, with tests when it touches the analysis core (`tests/Scanalyse.Core.Tests`).
- Interface strings are written in French; their English translation goes in `src/Scanalyse.Desktop/Lang/en.tsv`.
- No data may leave the user's PC, and no dependency on an online service.
- By contributing, you agree that your code is published under the project's license ([GPL v3](LICENSE)).
