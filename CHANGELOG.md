# Scanalyse — Quoi de neuf

## 0.12.0 — en préparation

- **Programmes SATO (SBPL) et Honeywell / Intermec (Fingerprint, Direct Protocol)** : lecture complète dans « Code imprimante », comme pour le ZPL, le TSPL, l'EPL2 et le DPL. Aperçu de l'étiquette (textes, cadres, traits, codes), liste de tous les codes-barres avec ce qu'une douchette lira, données GS1 détaillées, « Voir la ligne » et « Reprendre dans Créer une étiquette ».
  - SBPL : commandes ESC réelles ou notation `<ESC>`, programmes encadrés par STX / ETX, rotation, agrandissement, polices, codes 1D (Code 128 avec FNC1, SSCC « BI » complété de sa clé), QR Code et Data Matrix, cadres et zones inversées ; programme coloré dans l'éditeur. Un SSCC au mauvais nombre de chiffres est signalé.
  - Fingerprint : mots-clés complets ou abrégés (PP, PT, PB…), numéros de ligne, plusieurs étiquettes par fichier, ancrages (ALIGN), CHR$(128) = FNC1 ; position « y » comptée depuis le bas de l'étiquette.
- Un programme SATO commençant par STX n'est plus pris pour du DPL.
- **Code imprimante** : en lecture d'un programme et avec les modèles, la zone du programme n'est plus écrasée par l'aperçu (en plein écran comme en fenêtre réduite) ; aperçu et programme se partagent la hauteur, l'éditeur garde toujours une place lisible.
- **Fenêtres** : ombre portée et liseré gris comme les fenêtres de Windows, coins arrondis sous Windows 11 ; Scanalyse ne se confond plus avec les programmes ouverts derrière.
- **Banc d'essai** : bouton « Régler la douchette » plus visible (icône, teinte verte) ; après une comparaison qui révèle un réglage à corriger (clavier, préfixe, suffixe, séparateur GS1), un encadré propose directement les codes de configuration.
- **Historique** : la liste des analyses n'est plus écrasée par la fiche du dossier quand la fenêtre est petite (la page défile).
- **Banc d'essai** : les champs « Référence » et « Données reçues » gardent leur hauteur (octets reçus dépliés, petite fenêtre) ; dans une colonne étroite, les choix de « Code testé » passent sous la vignette.
- **Analyser une image** : les outils de l'image (Agrandir, Sélectionner une zone) n'apparaissent qu'une fois l'image chargée. **Créer un code** : espace rétabli sous « Type de code ».
- **Clavier** : focus visible sur les dépliants (octets reçus, vue structurée) et les boutons de la barre de titre ; boutons ↺ ↻ − + nommés pour les lecteurs d'écran.
- **Paramètres par catégories**, comme les réglages de GitHub ou Discord : menu à gauche (Général, Mises à jour, Prise en main, Rapports PDF, Historique, Impression et banc d'essai, Profil d'équipe, À propos, Désinstaller) et une catégorie à la fois. Chaque dossier est rangé avec ce qu'il concerne (rapports, historique, programmes d'étiquettes) ; le diagnostic a sa propre carte dans À propos.

## 0.11.2 — octobre 2026

- Le projet a déménagé : **github.com/Kanard-Compagny/scanalyse**. Les mises à jour sont cherchées à cette adresse.
- Nouvelle page de présentation du projet sur GitHub.

## 0.11.1 — octobre 2026

- **Logiciel libre** : Scanalyse est publié sous licence **GNU GPL v3** (Paramètres → À propos → « Licence GPL v3 »).

## 0.11.0 — octobre 2026

### Nouveautés
- **Codes de configuration de douchettes** (bouton dans « Banc d'essai » et « Appareils de lecture ») : codes Honeywell à scanner (réglages par défaut, clavier AZERTY / QWERTY, Entrée après chaque lecture, suppression des préfixes et suffixes, séparateur GS1, activation GS1-128 et Code 128) et Datalogic (réglages par défaut, entrée et sortie du mode programmation). Codes tirés des manuels des fabricants, relus et vérifiés ; impression d'une feuille de codes. Zebra : 17 codes (réglages par défaut, Entrée ou Tab après la lecture, verrouillage des majuscules, interfaces USB, série et clavier) relus dans le guide du DS3678 et régénérés à l'identique (Data Matrix de programmation).
- **Maintenance de l'imprimante** (Code imprimante) : lire l'état (papier, ruban, tête ouverte, pause…), calibrer, imprimer la page de configuration, redémarrer, en ZPL, TSPL, EPL2 et DPL. L'état se lit par le réseau (adresse IP, ou file Windows reliée à un port TCP/IP).
- **Mises à jour** : Scanalyse cherche une version plus récente dans le dossier de diffusion de l'équipe (et sur GitHub si le dépôt devient public) ; un bouton dans la barre de titre propose de l'installer et redémarre, sans script.
- **Profil d'équipe** : exporter / importer les réglages communs (en-tête et logo des rapports, dossiers partagés, mises à jour, impression) ; import proposé dès l'accueil.
- **Caméra des terminaux sans lecteur** : sur la page de relais, « Utiliser la caméra » lit les codes avec la caméra du terminal (décodeur servi par le PC, séparateurs GS1 conservés) et les envoie au banc d'essai. En USB, rien à régler ; en Wi-Fi, la page passe en HTTPS (certificat créé par Scanalyse, avertissement à accepter une fois ; pare-feu : ports 8765 à 8767).
- **Analyse en lot** : plusieurs images ou un PDF (une image par page) importés ou déposés d'un coup ; chaque image est enregistrée dans le dossier SAV puis le rapport du dossier est proposé.

### Corrections et améliorations
- **Premier lancement** : choix de la langue (français / English) avant l'accueil et la visite guidée ; le programme démarre directement dans la langue choisie. Langue proposée d'office : celle de Windows.
- **Anglais** : traduction complète de l'interface (certains écrans restaient en français), des titres des fenêtres d'ouverture et d'enregistrement, et de « Créer un code » (styles du QR, alertes, export).
- **Visite guidée** : l'encadré et la bulle se placent sur l'élément présenté (ils restaient au centre de l'écran).
- **Fermeture** : Scanalyse s'arrête complètement (douchettes, relais, moteur web libérés) ; Scanalyse.exe peut être supprimé ou remplacé juste après.
- **Modèles d'étiquettes en TSPL, EPL2 et DPL** : choix du langage dans « Modèles » ; le programme est converti et l'aperçu dessiné. Les données GS1 (FNC1) restent à valider à l'impression hors ZPL.
- **Lire un programme TSPL, EPL2 ou DPL** : aperçu de l'étiquette (textes, cadres, codes-barres), clic sur un élément pour retrouver sa ligne.
- **Créer un code** : MicroPDF417, MaxiCode, GS1 DataBar Limited, Code 32 (pharmacie Italie) et PZN (pharmacie Allemagne) ; « Créer ce type de code » disponible pour toutes les symbologies.
- **Grands écrans** : « Analyser une image » utilise toute la largeur (source fixe, fiches des codes en colonnes) ; les cartes des Paramètres se rangent en colonnes.
- **Paramètres** : « Quoi de neuf » et les licences s'ouvrent dans une fenêtre aux couleurs de Scanalyse ; **désinstallation** : Scanalyse efface lui-même ses réglages, son cache et, en option, l'historique, les rapports et les étiquettes (jamais un dossier choisi à la main), puis ouvre l'Explorateur sur Scanalyse.exe et son dossier temporaire à supprimer. Aucun script caché : rien qui alerte un antivirus.
- **Dossiers** : plus aucun dossier créé au démarrage ; Rapports, Étiquettes et Historique apparaissent au premier enregistrement.
- **Crédits** : badge et pseudo de l'auteur, Mr.Kanard, dans Paramètres → À propos.
- Fenêtre de choix de langue lisible en thème sombre ; plus de petit cadre carré autour des pastilles au clic.

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
- **Langue de l'interface (anglais / français)** : choix dans Paramètres, appliqué au redémarrage (bouton « Redémarrer maintenant »). L'interface Windows (menus, pages, boutons, messages, journal) et l'Atelier (analyse d'image, création de codes, fiches, dates et nombres au format anglais) sont traduits , ainsi que les textes d'analyse (diagnostics du banc d'essai, lecture des programmes ZPL, modèles, symbologies) et les rapports PDF. Table de traduction : src/Scanalyse.Desktop/Lang/en.tsv (outil de contrôle : tools/i18n_check.py).
- **Paramètres** : en-tête des rapports (société, technicien, coordonnées, logo) ; page d'ouverture, taille de l'interface (100 / 115 / 130 %), journal déplié ou non ; enregistrement automatique des analyses et suppression des anciennes (30 jours à 1 an) ; comparaison automatique au banc d'essai ; langage et résolution d'imprimante par défaut.
- **Prise en main** : accueil au premier lancement (présentation, choix des dossiers Historique / Rapports / Programmes d'étiquettes), visite guidée en 5 bulles (barre du haut, banc d'essai, code imprimante, appareils, dossier SAV), relançables dans Paramètres → Prise en main.
- « Analyser une image » s'ouvre vide avec quatre boutons (Coller, Capturer une zone, Importer, Caméra) au lieu d'un QR de démonstration ; « Lire un programme » vide propose Coller, Ouvrir un fichier, Partir d'un modèle.
- Paramètres : dossiers proposés pour les rapports PDF et les programmes d'étiquettes.
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
