# Sécurité · Security

## Signaler une faille · Reporting a vulnerability

🇫🇷 Si vous trouvez une faille de sécurité (par exemple dans le relais des terminaux, la mise à jour automatique ou la lecture des fichiers), **ne l'exposez pas dans un ticket public** : utilisez le bouton **« Report a vulnerability »** de l'onglet [Security](https://github.com/Kanard-Compagny/scanalyse/security) du dépôt. Décrivez la version concernée, les étapes pour reproduire et l'impact. Une réponse est donnée dès que possible ; le correctif est publié dans une nouvelle version, avec une mention dans les nouveautés.

🇬🇧 If you find a security issue (for example in the terminal relay, the automatic update or file parsing), **do not disclose it in a public issue**: use the **"Report a vulnerability"** button in the repository's [Security](https://github.com/Kanard-Compagny/scanalyse/security) tab. Include the affected version, steps to reproduce and the impact. You will get an answer as soon as possible; the fix ships in a new release and is mentioned in the changelog.

## Versions prises en charge · Supported versions

Seule la dernière version publiée reçoit des correctifs. · Only the latest release receives fixes.

## Vérifier un téléchargement · Verifying a download

Chaque version publie l'empreinte SHA-256 de `Scanalyse.exe` (fichier `Scanalyse.exe.sha256` et notes de version). · Every release publishes the SHA-256 hash of `Scanalyse.exe`.

```powershell
Get-FileHash .\Scanalyse.exe -Algorithm SHA256
```
