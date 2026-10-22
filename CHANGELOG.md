# Scanalyse — Quoi de neuf

## 0.10.0 — octobre 2026

### Code imprimante (nouvel onglet)
- Génère le programme d'une étiquette en **ZPL II** (Zebra et émulations ZPL), **TSPL** (TSC…) ou **EPL2** (anciennes Zebra) : Code 128, GS1-128, EAN-13/8, UPC-A, Code 39, ITF, QR Code, Data Matrix, GS1 DataMatrix (FNC1), PDF417, Aztec.
- Taille de l'étiquette, résolution de la tête (203 / 300 / 600 dpi), position, module, hauteur, rotation, texte lisible, texte libre, exemplaires ; aperçu de l'étiquette.
- Caractères de contrôle (<GS>, <RS>, <EOT>…) et caractères réservés encodés correctement (^FH) ; alertes sur les clés de contrôle, les tailles de module GS1 et les combinaisons non prises en charge.
- Programme modifiable, copie, enregistrement (.zpl, .prn), envoi direct à l'imprimante en réseau (port 9100) ou par une imprimante installée sur le PC (mode RAW).
- **Lire un programme ZPL** : colle (ou ouvre, ou glisse) le fichier d'un client pour voir l'étiquette dessinée, chaque code-barres avec ce qu'une douchette lira, les données GS1 et leurs clés de contrôle, les réglages (module, hauteur, rotation, position) et les points à corriger (^XZ manquant, accents sans ^CI28 et ce qu'ils impriment vraiment, taille non fixée, Data Matrix ECC ancien…).
- Depuis la lecture : « Réimprimer ce code » reprend un code dans le générateur, « Analyse complète de l'étiquette » l'envoie dans Analyser une image (fiches, cohérence GS1, historique, rapport).
- Imprimante choisie dans une barre en haut de la page (réseau ou imprimante du PC en liste déroulante), avec « Envoyer à l'imprimante » ; la carte Programme ne garde que Copier, Enregistrer, Régénérer.
- Fenêtre non agrandie : les en-têtes des cartes passent à la ligne au lieu d'écraser les titres ; les choix de l'en-tête de page se placent sous le titre quand la place manque.
- Défilement enchaîné (Code imprimante, Banc d'essai) : arrivée en bas ou en haut d'une liste ou du programme, la molette fait défiler la page.
- Fichiers enregistrés en « Unicode » (UTF-16, Bloc-notes) ou UTF-8 avec BOM lus correctement (plus de « ÿþ » ni de lignes vides en trop, aperçu affiché).
- « Ouvrir un fichier… » dans la carte Programme (et glisser-déposer) : un fichier .txt, .zpl, .prn, .epl… du PC s'ouvre en lecture. Langage reconnu automatiquement (ZPL, TSPL, EPL2, DPL, SBPL, Fingerprint) ; pour le TSPL et l'EPL2, liste des codes-barres, de leur contenu et des données GS1, avec « Réimprimer en ZPL ».
- Fenêtre du programme détaché aux couleurs de Scanalyse (barre de titre personnalisée) ; première et dernière lignes du programme toujours entières.
- Textes d'aide des champs (adresse IP, recherche d'imprimante, référence du banc) placés exactement là où le curseur apparaît.
- Couleurs du programme calées sur la position réelle du texte (sélection et curseur alignés).
- « Afficher : Aperçu / Programme / Les deux » pour choisir ce qui occupe la colonne de droite, et « Détacher » pour ouvrir le programme dans sa propre fenêtre (deuxième écran) : l'étiquette et le code en grand en même temps.
- Programme coloré par catégorie (début/fin, position, texte, code-barres, graphique, paramètres, données) avec légende.
- Lecture : aperçu pivotable (↺ ↻, sens de lecture choisi automatiquement), zoom (Ctrl + molette, « Ajuster » / taille réelle) ; clic sur un élément de l'étiquette → sa ligne dans le programme et sa fiche ; liste des textes, cadres et traits cliquable ; le curseur dans le programme surligne l'élément dans l'aperçu.
- Réglages du générateur présentés en étapes numérotées.
- Éditeur de programme avec numéros de ligne, « Coller un programme client », « Voir la ligne » depuis chaque code lu.
- Les pages Code imprimante et Banc d'essai défilent quand la fenêtre est trop basse : plus rien n'est coupé.
- Programme et aperçu agrandissables ; choix de l'imprimante Windows dans une liste avec recherche.
- **Modèles d'étiquettes** (Code imprimante → Modèles) : palette logistique GS1 (SSCC, GTIN contenu, quantité, lot, DDM, poids — trois GS1-128), colis transporteur, produit (EAN-13 + GS1 DataMatrix GTIN/lot/date), emplacement de stock, inventaire (QR + Code 128). Clés de contrôle calculées, données GS1 vérifiées, module le plus large qui tient dans l'étiquette, aperçu et envoi comme un programme client.
- Aperçu ZPL : blocs de texte ^FB sur plusieurs lignes et coupés entre les mots.
- **DPL (Honeywell / Datamax)** : génération (Code 128, EAN-13/8, UPC-A, Code 39, ITF, QR Code, Data Matrix ; unités 0,1 mm, origine en bas à gauche) et lecture des programmes DPL (codes-barres, contenu, données GS1). À valider sur l'imprimante.
- Fenêtre réduite : la barre d'outils garde ses icônes (bulles d'aide) sans les libellés et le menu se replie tout seul sous 1 180 points de large ; boutons du dossier (Historique) qui passent à la ligne ; colonnes de l'Historique et des Symbologies qui s'adaptent ; fiches des symbologies redimensionnées selon la place.
- Page « Douchettes » renommée « Appareils de lecture » : elle regroupe douchettes, terminaux Android et caméras.
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
