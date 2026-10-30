# Scanalyse — Quoi de neuf

## 0.12.1 — octobre 2026

- **Fenêtre de mise à jour** : les boutons restent visibles quelle que soit la taille de la fenêtre, les nouveautés défilent entre le titre et les boutons. Seules les nouveautés de la version proposée sont affichées, en liste à puces, sans l'empreinte ni la mise en forme brute.

## 0.12.0 — octobre 2026

- **Nouveau logo** : un code-barres dans un viseur, aux couleurs du programme ; version simplifiée pour les petites tailles (barre des tâches, barres de titre, page de relais). **Écran de démarrage** avec le logo, qui reste affiché jusqu’à ce que la fenêtre et l’Atelier soient prêts (au moins 1,5 s, 12 s au plus).
- **Visite guidée complète** (français et anglais) : un mot d'accueil, puis chaque page ouverte et présentée tour à tour — menu, barre du haut, Analyser une image, Créer un code, Étiquette imprimante, Conf imprimante et son imprimante de référence, Banc d'essai, Appareils de lecture et relais, Historique, dossier SAV, Symbologies, journal et Paramètres. Retour à « Analyser une image » à la fin.
- **Conf imprimante, création : module Wi-Fi** — nom du réseau, sécurité (WPA / WPA2 personnel ou réseau ouvert), mot de passe, pays, adresse automatique ou fixe, et redémarrage en fin de fichier. Pratique pour préparer en USB une imprimante qui passera en Wi-Fi. Zebra : commandes SGD, le mot de passe devient la clé WPA calculée (jamais en clair dans le fichier) ; TSC : commandes WLAN ; autres marques : l'outil du fabricant à utiliser est indiqué. Les commandes WLAN de TSC sont aussi analysées et relues pour la conversion.
- **Texte net dans les fenêtres superposées** (accueil, visite guidée, confirmations, propositions de l'éditeur) : l'ombre portée rendait le texte légèrement flou.
- **Page de relais en français ou en anglais**, selon la langue choisie dans Scanalyse (textes, messages, aide au certificat, heure).
- **Nouvel onglet « Conf imprimante »** : colle, ouvre ou glisse le fichier de configuration envoyé à une imprimante ; chaque ligne est expliquée et contrôlée avant l'envoi. Langages reconnus : Zebra ZPL (^MD, ~SD, ^PR, ^MN, ^MT, ^MM, ^PW, ^LL, ^JUS, ^NS…) et SGD (`! U1 setvar / getvar / do`, et format JSON `{}{…}`), TSC TSPL (SIZE, GAP, SPEED, DENSITY, SET…), Honeywell / Datamax DPL et Fingerprint (SETUP), SATO SBPL, EPL2.
  - Erreurs signalées : nom de réglage SGD mal écrit (avec la bonne orthographe proposée), valeur hors limites ou inconnue, virgule au lieu du point, guillemets typographiques copiés depuis Word ou un e-mail, « !U1 » sans espace, majuscules, JSON mal formé, adresse IP invalide, dernière commande SGD sans retour à la ligne (non exécutée), réglages ZPL sans ^JUS (perdus à l'extinction) ou ^JUS placé trop tôt, redémarrage ou retour usine avant d'autres commandes, mot de passe Wi-Fi en clair, option (décolleur, massicot) requise.
  - **Propositions à la saisie**, comme dans un éditeur de code : bibliothèque intégrée des commandes de chaque langage, avec leur rôle et leurs valeurs ; noms SGD proposés entre les guillemets, puis leurs valeurs (ex. `media.printmode` → tear off, peel off… ; `wlan.country_code` → europe, usa/canada…). Flèches pour choisir, Entrée ou Tab pour insérer, Ctrl + Espace pour toute la bibliothèque.
  - **Toutes les commandes ZPL** sont connues (réseau filaire et Wi-Fi ^NC, ^NP, ^WI, ^WS, ^WX, ^WE, ^NN, ^KC…, RFID, polices, mémoire…) : une commande inconnue ou mal écrite est signalée en erreur, avec la bonne écriture si elle existe (ex. ^MX → ^MD, ^MM, ^MN, ^MT…) ; réglages SGD Wi-Fi ajoutés (pays, bandes, mode international…), un nom SGD inconnu est signalé.
  - **Code coloré** comme dans un éditeur : commandes, actions, noms de réglages, valeurs et paramètres en couleurs, commande inconnue en rouge souligné ; numéros de ligne, et lignes en erreur ou à vérifier surlignées (rouge, jaune) avec leurs messages au survol.
  - **Clés Wi-Fi** : une clé calculée (PSK de 64 caractères hexadécimaux, comme l'enregistre Zebra Setup Utilities) est reconnue et expliquée — ce n'est pas le mot de passe lisible, mais elle suffit pour se connecter — avec les bonnes pratiques (diffusion, stockage, suppression, WPA Entreprise).
  - **Toshiba TEC (TPCL)** au même titre que les autres marques : lecture et contrôle des commandes de réglage ({D…|} taille, {AX;…|} réglages fins, {AY;…|} noirceur et méthode, {T…|} avance, {XS;I,…|} impression : capteur, mode, vitesse, ruban), commande mal fermée en erreur, couleurs et propositions à la saisie ; conversion vers et depuis TPCL, création dans l'assistant ; imprimante Toshiba reconnue (SNMP, pilote) avec sa compatibilité (TPCL, émulation ZPL).
  - **Trois parcours clairs** en haut de l'onglet : « Analyser un fichier », « Convertir », « Créer un fichier », chacun en étapes numérotées. Conversion et création s'affichent à côté du fichier de départ, sans l'écraser, colorées comme l'éditeur, avec « Enregistrer sous », « Copier », « Ouvrir dans l'analyse » et l'envoi à l'imprimante.
  - **Convertir** : réécrit le fichier dans le langage d'une autre marque (ZPL, SGD Zebra, TSPL, EPL2, DPL, Fingerprint, SBPL ; par défaut celui de l'imprimante reconnue). Les réglages communs sont repris (noirceur, vitesse, support, méthode, sortie, largeur et longueur, décalages, réseau filaire), avec un compte-rendu : repris, approché (échelles différentes, ex. noirceur 0–30 en ZPL, 0–15 en TSPL, 1–5 en SBPL : à ajuster après un essai) et non converti (réglages propres à une marque : Wi-Fi Zebra, Weblink, mots de passe, <ESC>PG, <STX>Kc…, avec où les régler). Points ↔ millimètres selon la résolution de la tête (lue sur l'imprimante ou choisie). « Revenir au fichier d'avant » en un clic.
  - **Créer un fichier** : un formulaire, à partir de rien, du fichier ouvert ou des réglages lus sur l'imprimante, et le fichier écrit en direct à côté ; un formulaire (noirceur en %, vitesse, méthode, sortie, support, taille, décalages, réseau, enregistrement) pré-rempli avec les réglages du fichier ouvert ; Scanalyse écrit le fichier dans le langage choisi, avec l'équivalent exact affiché (ex. « Sera écrit : DENSITY 9 (0 à 15) »).
  - Les notations <ESC>, <STX>, <SOH>, <ETX> sont envoyées comme vrais caractères de contrôle.
  - **Enregistrer sous** (bouton ou Ctrl + S) : le fichier modifié est enregistré dans son encodage d'origine.
  - **Imprimante reconnue automatiquement, toutes marques** : dès qu'une adresse IP est saisie ou qu'une imprimante installée est choisie, Scanalyse l'identifie sans rien imprimer (SNMP : marque, modèle, firmware, langages ; nom du pilote Windows pour une imprimante USB ; modèle TSC par ~!T) et l'affiche sous le choix de l'imprimante. Chaque frappe, import ou modification est alors vérifié pour cette imprimante : langage compris (natif, par émulation ou non), langage actif, commandes SGD envoyées à une autre marque qu'une Zebra, etc.
  - **Zebra : ses réglages font référence** : tous ses réglages sont lus (« allcv ») ; un nom qu'elle connaît n'est plus signalé, un nom qu'elle ignore est une erreur, ses valeurs acceptées remplacent les limites intégrées, et les propositions de saisie sont ses propres réglages et valeurs. Relecture automatique après l'envoi.
  - Liste intégrée des réglages SGD Zebra très complétée (sécurité, HTTPS, LPD, SMTP, Weblink, ZBI, USB, panneau, Energy Star…) : plus de fausse alerte sur ces réglages, et une faute de frappe n'est proposée que si le nom ressemble vraiment à un réglage de la même famille. Mots de passe (page web, panneau, SNMP, Bluetooth) signalés en clair ; « ip.port » à 0 expliqué.
  - **Lire l'imprimante** (Zebra, par le réseau) : lit tous ses réglages, montre ce qui va changer et vérifie chaque valeur avec les choix annoncés par ce modèle et ce firmware.
  - **Envoyer à l'imprimante** (réseau ou imprimante installée) : confirmation demandée si le fichier contient des erreurs, redémarre l'imprimante, la remet en usine ou change son réseau ; retour à la ligne final ajouté si besoin.
  - Un fichier de configuration ouvert avec Scanalyse arrive directement dans cet onglet.
- **Programmes SATO (SBPL) et Honeywell / Intermec (Fingerprint, Direct Protocol)** : lecture complète dans « Code imprimante », comme pour le ZPL, le TSPL, l'EPL2 et le DPL. Aperçu de l'étiquette (textes, cadres, traits, codes), liste de tous les codes-barres avec ce qu'une douchette lira, données GS1 détaillées, « Voir la ligne » et « Reprendre dans Créer une étiquette ».
  - SBPL : commandes ESC réelles ou notation `<ESC>`, programmes encadrés par STX / ETX, rotation, agrandissement, polices, codes 1D (Code 128 avec FNC1, SSCC « BI » complété de sa clé), QR Code et Data Matrix, cadres et zones inversées ; programme coloré dans l'éditeur. Un SSCC au mauvais nombre de chiffres est signalé.
  - Fingerprint : mots-clés complets ou abrégés (PP, PT, PB…), numéros de ligne, plusieurs étiquettes par fichier, ancrages (ALIGN), CHR$(128) = FNC1 ; position « y » comptée depuis le bas de l'étiquette.
- Un programme SATO commençant par STX n'est plus pris pour du DPL.
- **Code imprimante** : en lecture d'un programme et avec les modèles, la zone du programme n'est plus écrasée par l'aperçu (en plein écran comme en fenêtre réduite) ; aperçu et programme se partagent la hauteur, l'éditeur garde toujours une place lisible.
- **Filet de sécurité en cas d'erreur imprévue** : au lieu de se fermer sans un mot, Scanalyse affiche ce qui s'est passé, propose de continuer (ou se ferme proprement), enregistre un rapport (dossier « Plantages » des réglages, 20 derniers gardés) et permet de le copier ou de le signaler sur GitHub dans un ticket déjà rempli. Le rapport ne contient ni image ni donnée lue ; le dossier personnel y est masqué.
- **Ouvrir avec Scanalyse** : un fichier glissé sur Scanalyse.exe (ou passé par « Ouvrir avec ») s'ouvre au bon endroit : programme d'étiquette dans « Code imprimante » (langage reconnu), image ou PDF dans « Analyser une image », profil d'équipe à l'import. Si Scanalyse est déjà ouvert, le fichier arrive dans la fenêtre existante. Option dans Paramètres → Général pour proposer Scanalyse dans le menu « Ouvrir avec » de l'Explorateur (session de l'utilisateur, sans droits administrateur, retirée à la désinstallation).
- **Empreinte SHA-256** publiée avec chaque version (dans les notes et en fichier Scanalyse.exe.sha256) ; la mise à jour automatique la vérifie et refuse un fichier abîmé ou modifié.
- **Page de relais repensée pour le téléphone et le terminal** : choix « Lecteur » / « Caméra » toujours visible en haut (lecteur par défaut sur un terminal à gâchette, caméra sur un téléphone, choix mémorisé) ; grande zone « Prêt à lire » qui indique si la gâchette va bien taper dans la page ; caméra plein cadre avec lampe (si le téléphone en a une) ; dernière lecture en grand avec « ✓ reçu par le PC » (ou l'erreur), éclair vert et vibration ; historique et aide sur le certificat repliés ; « Arrêter la liaison » discret en bas.
- **Lecture d'un terminal relié** : Scanalyse passe au banc d'essai à la première lecture, même depuis l'onglet de connexion des terminaux.
- **Relais des terminaux en HTTPS, en USB comme en Wi-Fi** : la page ne s'ouvre plus en http://, bloqué par beaucoup de terminaux (navigateur géré par MDM, Chrome « HTTPS uniquement »). Scanalyse crée un certificat propre au PC ; installé une fois sur le terminal (bouton « Installer le certificat » par câble, lien sur la page de relais, ou export .crt pour un déploiement MDM), il supprime tout avertissement de sécurité. Le certificat du relais se refait seul quand l'adresse du PC change (avant, la page HTTPS en Wi-Fi était refusée après un changement d'adresse). L'adresse http:// reste disponible pour les terminaux anciens.
- L'onglet « Code imprimante » devient **« Étiquette imprimante »**, plus parlant (il crée, lit et envoie des étiquettes), à côté de « Conf imprimante ».
- **Étiquette imprimante** : décocher « Texte lisible sous le code » retire bien le texte de l'aperçu (il n'était retiré que du programme).
- **Créer un code** : le type de code se choisit avec des cartes (catégorie, puis type avec son usage), comme les parcours de « Conf imprimante ».
- **Barre « Imprimante » en fenêtre étroite** (Conf imprimante, Étiquette imprimante) : l'adresse passe sur sa propre ligne, en pleine largeur, au lieu d'être écrasée ; le texte d'aide d'un champ d'une ligne n'est plus coupé sur plusieurs lignes.
- **Champs de saisie** (tout le programme) : le texte d'aide est dessiné par le champ lui-même, à l'endroit exact du texte saisi ; le curseur se place juste avant l'aide, sans la chevaucher, et les champs d'une ligne sont centrés verticalement.
- **Barre des tâches** : le logo de Scanalyse est repris directement de Scanalyse.exe, il ne disparaît plus.
- **Fenêtres** : ombre portée et liseré gris comme les fenêtres de Windows, coins arrondis sous Windows 11 ; Scanalyse ne se confond plus avec les programmes ouverts derrière.
- **Banc d'essai et Appareils de lecture** : bouton « Régler la douchette » plus visible (icône, teinte verte), et accès direct aux codes d'interface USB depuis « Passer en liaison directe » ; après une comparaison qui révèle un réglage à corriger (clavier, préfixe, suffixe, séparateur GS1), un encadré propose directement les codes de configuration.
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
