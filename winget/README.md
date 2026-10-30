# Scanalyse dans winget

Fichiers de description (manifestes) pour le catalogue `winget` de Microsoft. Une fois acceptés :

```powershell
winget install KanardCompagny.Scanalyse
```

## Proposer une version (sur un PC Windows)

1. Installer l'outil de Microsoft : `winget install Microsoft.WingetCreate`
2. Dans ce dossier : `wingetcreate submit .\0.12.3` — l'outil demande de se connecter à GitHub, puis ouvre la proposition (pull request) sur `microsoft/winget-pkgs`.
3. Les robots de Microsoft vérifient le fichier (téléchargement, empreinte, analyse antivirus), puis un modérateur valide, en général en quelques jours.

Pour les versions suivantes : `wingetcreate update KanardCompagny.Scanalyse --version X.Y.Z --urls https://github.com/Kanard-Compagny/scanalyse/releases/download/vX.Y.Z/Scanalyse.exe --submit`.
