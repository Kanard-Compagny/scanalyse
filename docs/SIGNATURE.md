# Signature du code avec SignPath

> **État (octobre 2026)** : demande au programme gratuit SignPath Foundation refusée pour le moment, faute de visibilité publique suffisante (étoiles, contributeurs, articles…). Elle pourra être redéposée plus tard. L'étape de signature reste en sommeil dans la compilation : rien ne change tant que les variables SignPath n'existent pas.

Objectif : que Windows reconnaisse `Scanalyse.exe` comme un programme signé, sans changer la façon de publier.
Le code, le dépôt, le copyright et la licence restent ceux de Mr.Kanard. SignPath ne reçoit que l'exécutable compilé par GitHub, le signe et le renvoie.
L'éditeur affiché par Windows sur le fichier est « SignPath Foundation » (c'est la contrepartie de la signature gratuite).

## Ce qui est déjà prêt dans le dépôt

- La compilation (`.github/workflows/build.yml`) contient l'étape « Signature (SignPath) ». **Elle reste inactive** tant que la variable `SIGNPATH_ORGANIZATION_ID` n'existe pas : les versions sont alors publiées sans signature, comme avant.
- Une fois active, seule une **version publiée** est envoyée à la signature (pas les compilations `-dev`). La Release n'est créée qu'après validation et contrôle de la signature : si la signature échoue, est refusée ou invalide, **rien n'est publié** et la version précédente reste en place.
- La page d'accueil contient les sections « Confidentialité » et « Code signing policy » demandées par SignPath.
- La mise à jour automatique de Scanalyse ne change pas : elle télécharge l'exécutable de la Release, signé ou non.

## Étapes (à faire par le propriétaire du dépôt)

1. **Double authentification** sur le compte GitHub (*Settings → Password and authentication*), obligatoire pour SignPath.
2. **Demande** sur [signpath.org](https://signpath.org) (programme gratuit pour les projets libres). Éléments à donner :
   - Projet : Scanalyse — https://github.com/Kanard-Compagny/scanalyse — licence GPL-3.0-only.
   - Description : boîte à outils Windows pour les techniciens codes-barres (analyse et explication des codes, banc d'essai de douchettes, lecture et envoi de programmes d'imprimantes d'étiquettes).
   - Compilation : GitHub Actions, workflow `.github/workflows/build.yml`, exécutable portable unique `Scanalyse.exe` (.NET 10, WPF).
   - Composants tiers (voir `THIRD-PARTY-NOTICES.md`) : tous sous licence libre (MIT, Apache 2.0), sauf **Microsoft Edge WebView2** (composant d'affichage de Microsoft, redistribution autorisée). Le signaler franchement et demander s'il est accepté comme composant système.
3. Après acceptation, dans SignPath :
   - relier le projet à **GitHub.com** comme système de compilation de confiance (*Trusted Build System*) ;
   - **configuration d'artefact** : l'artefact GitHub est une archive zip contenant `Scanalyse.exe` à la racine :
     ```xml
     <?xml version="1.0" encoding="utf-8"?>
     <artifact-configuration xmlns="http://signpath.io/artifact-configuration/v1">
       <zip-file>
         <pe-file path="Scanalyse.exe">
           <authenticode-sign />
         </pe-file>
       </zip-file>
     </artifact-configuration>
     ```
   - stratégie de signature **release-signing** avec validation manuelle (*approval*) ;
   - créer un **jeton d'API** pour un utilisateur CI (*CI user*) ayant le droit de soumettre des demandes.
4. Dans GitHub, *Settings → Secrets and variables → Actions* :
   - **Secret** `SIGNPATH_API_TOKEN` : le jeton d'API ;
   - **Variables** `SIGNPATH_ORGANIZATION_ID` (identifiant de l'organisation SignPath), `SIGNPATH_PROJECT_SLUG` (ex. `scanalyse`), `SIGNPATH_SIGNING_POLICY_SLUG` (ex. `release-signing`).
5. Sur la page d'accueil (README), retirer la mention « en cours de mise en place » de la section *Code signing policy*.

## Publier une version signée

Comme aujourd'hui : changer `VersionPrefix`, compléter `CHANGELOG.md`, envoyer sur `main`.
La compilation envoie l'exécutable à SignPath et attend la validation (jusqu'à 4 heures) : valider la demande sur SignPath.
L'exécutable signé est vérifié, puis la Release est créée avec la mention de la signature.
Sans validation dans les 4 heures, rien n'est publié : relancer la compilation (*Actions → Compilation → Re-run*).

## Revenir en arrière

Supprimer la variable `SIGNPATH_ORGANIZATION_ID` : les versions sont de nouveau publiées sans signature. Rien d'autre à changer.
