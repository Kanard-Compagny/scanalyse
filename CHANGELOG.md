# Scanalyse — Quoi de neuf

## 0.10.0 — octobre 2026

### Code imprimante (nouvel onglet)
- Génère le programme d'une étiquette en **ZPL II** (Zebra et émulations ZPL), **TSPL** (TSC…) ou **EPL2** (anciennes Zebra) : Code 128, GS1-128, EAN-13/8, UPC-A, Code 39, ITF, QR Code, Data Matrix, GS1 DataMatrix (FNC1), PDF417, Aztec.
- Taille de l'étiquette, résolution de la tête (203 / 300 / 600 dpi), position, module, hauteur, rotation, texte lisible, texte libre, exemplaires ; aperçu de l'étiquette.
- Caractères de contrôle (<GS>, <RS>, <EOT>…) et caractères réservés encodés correctement (^FH) ; alertes sur les clés de contrôle, les tailles de module GS1 et les combinaisons non prises en charge.
- Programme modifiable, copie, enregistrement (.zpl, .prn), envoi direct à l'imprimante en réseau (port 9100) ou par une imprimante installée sur le PC (mode RAW).
- **Lire un programme ZPL** : colle (ou ouvre, ou glisse) le fichier d'un client pour voir l'étiquette dessinée, chaque code-barres avec ce qu'une douchette lira, les données GS1 et leurs clés de contrôle, les réglages (module, hauteur, rotation, position) et les points à corriger (^XZ manquant, accents sans ^CI28 et ce qu'ils impriment vraiment, taille non fixée, Data Matrix ECC ancien…).
- Depuis la lecture : « Réimprimer ce code » reprend un code dans le générateur, « Analyse complète de l'étiquette » l'envoie dans Analyser une image (fiches, cohérence GS1, historique, rapport).
- Programme coloré par catégorie (début/fin, position, texte, code-barres, graphique, paramètres, données) avec légende.
- Lecture : aperçu pivotable (↺ ↻, sens de lecture choisi automatiquement), zoom (Ctrl + molette, « Ajuster » / taille réelle) ; clic sur un élément de l'étiquette → sa ligne dans le programme et sa fiche ; liste des textes, cadres et traits cliquable ; le curseur dans le programme surligne l'élément dans l'aperçu.
- Réglages du générateur présentés en étapes numérotées.
- Éditeur de programme avec numéros de ligne, « Coller un programme client », « Voir la ligne » depuis chaque code lu.
- Les pages Code imprimante et Banc d'essai défilent quand la fenêtre est trop basse : plus rien n'est coupé.
- Programme et aperçu agrandissables ; choix de l'imprimante Windows dans une liste avec recherche.
- Bouton « Code imprimante » sur chaque code analysé, au banc d'essai et dans « Créer un code » (QR compris) : on recrée le code d'un client en un clic.

## 0.9.0 — octobre 2026

Première version complète, prête à être diffusée à l'équipe.

### Analyser une image
- Lecture de tous les types de codes (QR, Data Matrix, PDF417, Aztec, MaxiCode, EAN/UPC, Code 128, GS1, DataBar…) depuis une image, un collage, une capture d'écran ou une caméra.
- Fiche complète de chaque code : contenu interprété (GS1, liens, Wi-Fi, profils Zebra StageNow…), caractéristiques techniques, contraste, zone de silence, octets.
- Vue structurée des messages ISO/IEC 15434 (codes transporteurs, identifiants de données) : sections et champs.
- Cohérence d'une étiquette complète : mêmes données d'un code à l'autre, règles GS1, dates, poids.
- Caméra en direct avec indicateur de netteté ; accentuation des images légèrement floues.

### Créer un code
- QR personnalisé et 20 symbologies, assistant GS1, vérification par relecture, taille d'impression, export PNG/SVG.
- Caractères invisibles saisis en notation <GS>, <RS>, <EOT>…

### Banc d'essai douchette
- Comparaison entre le contenu réel du code et ce que la douchette transmet, avec explication de chaque écart (clavier, GS perdu, préfixes, suffixes, troncature…).
- Référence prise depuis une image ou depuis une lecture, aperçu du code testé, agrandissement pour scanner l'écran.

### Douchettes et terminaux
- Douchettes en port série (COM) ou HID POS, caméras.
- Relais terminal Android (USB via ADB, ou Wi-Fi) : la douchette intégrée envoie ses lectures au banc d'essai, sans application à installer.

### Historique et rapports
- Dossiers SAV par client et par ticket, analyses enregistrées automatiquement, noms et commentaires.
- Rapport PDF (dossier ou client complet), photo annotée, contenu détaillé ou brut.
- Historique à côté du programme ou dans un dossier partagé de l'équipe.
