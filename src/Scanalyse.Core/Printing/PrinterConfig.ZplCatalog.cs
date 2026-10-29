using Scanalyse.Core.Content;

namespace Scanalyse.Core.Printing;

public static partial class PrinterConfig
{
    /// <summary>
    /// Commandes ZPL II (guide de programmation Zebra) avec leur rôle : une commande absente de cette liste est inconnue de
    /// l'imprimante, qui l'ignore sans message. Les polices (^A…) et codes-barres (^B…) sont reconnus à part.
    /// </summary>
    private static readonly Dictionary<string, (string Meaning, string Category)> ZplCatalog = new(StringComparer.Ordinal)
    {
        ["^A@"] = ("Police téléchargée (nom de fichier)", "Étiquette"),
        ["^BY"] = ("Largeur des barres par défaut des codes-barres", "Étiquette"),
        ["^CC"] = ("Change le caractère de commande (^)", "Système"),
        ["~CC"] = ("Change le caractère de commande (^)", "Système"),
        ["^CD"] = ("Change le séparateur de paramètres (,)", "Système"),
        ["~CD"] = ("Change le séparateur de paramètres (,)", "Système"),
        ["^CF"] = ("Police par défaut", "Étiquette"),
        ["^CI"] = ("Jeu de caractères (28 = UTF-8)", "Impression"),
        ["^CM"] = ("Lettres des mémoires (R:, E:, B:, A:)", "Système"),
        ["^CN"] = ("Massicot et applicateur (coupe ou non)", "Impression"),
        ["^CO"] = ("Mémoire cache des polices", "Système"),
        ["^CP"] = ("Retrait de l'étiquette (mode kiosque)", "Impression"),
        ["^CT"] = ("Change le caractère de contrôle (~)", "Système"),
        ["~CT"] = ("Change le caractère de contrôle (~)", "Système"),
        ["^CV"] = ("Validation des codes-barres (refuse les données invalides)", "Étiquette"),
        ["^CW"] = ("Identifiant de police", "Étiquette"),
        ["~DB"] = ("Téléchargement d'une police bitmap", "Système"),
        ["~DE"] = ("Téléchargement d'une table d'encodage", "Système"),
        ["^DF"] = ("Enregistrement d'un format", "Étiquette"),
        ["~DG"] = ("Téléchargement d'une image", "Étiquette"),
        ["~DN"] = ("Annulation d'un téléchargement", "Système"),
        ["~DS"] = ("Téléchargement d'une police vectorielle", "Système"),
        ["~DT"] = ("Téléchargement d'une police TrueType", "Système"),
        ["~DU"] = ("Téléchargement d'une police TrueType (grande taille)", "Système"),
        ["~DY"] = ("Téléchargement d'un fichier (police, image, firmware…)", "Système"),
        ["~EG"] = ("Effacement des images téléchargées", "Système"),
        ["^FB"] = ("Bloc de texte (largeur, lignes, alignement)", "Étiquette"),
        ["^FC"] = ("Indicateur d'horloge (date et heure dans le texte)", "Étiquette"),
        ["^FD"] = ("Données du champ", "Étiquette"),
        ["^FH"] = ("Données en hexadécimal (_xx)", "Étiquette"),
        ["^FL"] = ("Liaison de polices", "Étiquette"),
        ["^FM"] = ("Origine de champs multiples", "Étiquette"),
        ["^FN"] = ("Numéro de champ variable", "Étiquette"),
        ["^FO"] = ("Position du champ (x, y)", "Étiquette"),
        ["^FP"] = ("Sens d'écriture du champ", "Étiquette"),
        ["^FR"] = ("Champ en inversion (blanc sur noir)", "Étiquette"),
        ["^FS"] = ("Fin de champ", "Étiquette"),
        ["^FT"] = ("Position du champ (ligne de base)", "Étiquette"),
        ["^FV"] = ("Données de champ variable", "Étiquette"),
        ["^FW"] = ("Orientation par défaut des champs", "Étiquette"),
        ["^FX"] = ("Commentaire", "Étiquette"),
        ["^GB"] = ("Cadre ou trait", "Étiquette"),
        ["^GC"] = ("Cercle", "Étiquette"),
        ["^GD"] = ("Trait en diagonale", "Étiquette"),
        ["^GE"] = ("Ellipse", "Étiquette"),
        ["^GF"] = ("Image incluse dans le format", "Étiquette"),
        ["^GS"] = ("Symbole (®, ©, ™…)", "Étiquette"),
        ["~HB"] = ("Demande d'état de la batterie", "Lecture"),
        ["~HD"] = ("Demande de diagnostic de la tête", "Lecture"),
        ["^HF"] = ("Renvoi d'un format enregistré", "Lecture"),
        ["^HG"] = ("Renvoi d'une image", "Lecture"),
        ["^HH"] = ("Renvoi de l'étiquette de configuration", "Lecture"),
        ["~HI"] = ("Demande d'identification de l'imprimante", "Lecture"),
        ["~HM"] = ("Demande de la mémoire disponible", "Lecture"),
        ["~HQ"] = ("Demande d'informations (numéro de série, alertes…)", "Lecture"),
        ["^HR"] = ("Calibrage RFID", "Système"),
        ["~HS"] = ("Demande d'état de l'imprimante", "Lecture"),
        ["^HT"] = ("Renvoi des liaisons de polices", "Lecture"),
        ["~HU"] = ("Renvoi des alertes configurées", "Lecture"),
        ["^HV"] = ("Renvoi de données de champ", "Lecture"),
        ["^HW"] = ("Renvoi du contenu d'une mémoire", "Lecture"),
        ["^HY"] = ("Renvoi d'une image", "Lecture"),
        ["^HZ"] = ("Renvoi d'informations (XML)", "Lecture"),
        ["^ID"] = ("Suppression d'un fichier", "Système"),
        ["^IL"] = ("Chargement d'une image en fond d'étiquette", "Étiquette"),
        ["^IM"] = ("Placement d'une image enregistrée", "Étiquette"),
        ["^IS"] = ("Enregistrement de l'étiquette comme image", "Étiquette"),
        ["~JA"] = ("Annulation de tous les travaux en attente", "Système"),
        ["^JB"] = ("Formatage d'une mémoire Flash", "Système"),
        ["~JB"] = ("Réinitialisation de la batterie optionnelle", "Système"),
        ["~JC"] = ("Calibrage du capteur d'étiquettes", "Support"),
        ["~JD"] = ("Diagnostic de communication activé", "Communication"),
        ["~JE"] = ("Diagnostic de communication désactivé", "Communication"),
        ["~JF"] = ("Gestion de la batterie faible", "Système"),
        ["~JG"] = ("Courbe du capteur (impression)", "Support"),
        ["^JH"] = ("Alertes de maintenance (tête, ruban)", "Système"),
        ["^JI"] = ("Démarrage d'un programme ZBI", "Système"),
        ["~JI"] = ("Démarrage d'un programme ZBI", "Système"),
        ["^JJ"] = ("Port auxiliaire (applicateur)", "Communication"),
        ["~JL"] = ("Mesure de la longueur d'étiquette", "Support"),
        ["^JM"] = ("Résolution (points par millimètre)", "Impression"),
        ["~JN"] = ("Test de la tête : erreur bloquante", "Système"),
        ["~JO"] = ("Test de la tête : erreur non bloquante", "Système"),
        ["~JP"] = ("Pause et annulation du format", "Système"),
        ["~JQ"] = ("Arrêt du programme ZBI", "Système"),
        ["~JR"] = ("Redémarrage de l'imprimante", "Système"),
        ["^JS"] = ("Choix du capteur", "Support"),
        ["~JS"] = ("Recul de l'étiquette (avant ou après impression)", "Support"),
        ["^JT"] = ("Fréquence du test de la tête", "Système"),
        ["^JU"] = ("Configuration (enregistrer, recharger, usine)", "Système"),
        ["^JW"] = ("Tension du ruban", "Support"),
        ["~JX"] = ("Annulation du format en cours de réception", "Système"),
        ["^JZ"] = ("Réimpression après une erreur", "Impression"),
        ["~KB"] = ("Arrêt de la batterie", "Système"),
        ["^KC"] = ("Identifiant client DHCP (option 61)", "Réseau"),
        ["^KD"] = ("Format de la date et de l'heure à l'écran", "Système"),
        ["^KL"] = ("Langue de l'écran", "Système"),
        ["^KN"] = ("Nom de l'imprimante", "Système"),
        ["^KP"] = ("Mot de passe du panneau de commande", "Système"),
        ["^KV"] = ("Réglages du mode kiosque", "Impression"),
        ["^LF"] = ("Liste des liaisons de polices", "Lecture"),
        ["^LH"] = ("Origine de l'étiquette (x, y en points)", "Support"),
        ["^LL"] = ("Longueur d'étiquette (points)", "Support"),
        ["^LR"] = ("Étiquette entière en inversion", "Étiquette"),
        ["^LS"] = ("Décalage horizontal de l'étiquette (points)", "Support"),
        ["^LT"] = ("Décalage vertical de l'étiquette (points)", "Support"),
        ["^MA"] = ("Alertes de maintenance", "Système"),
        ["^MC"] = ("Conservation de l'image entre deux étiquettes", "Étiquette"),
        ["^MD"] = ("Ajustement de noirceur", "Impression"),
        ["^MF"] = ("Action à la mise sous tension et à la fermeture de la tête", "Support"),
        ["^MI"] = ("Message de maintenance", "Système"),
        ["^ML"] = ("Longueur maximale d'étiquette", "Support"),
        ["^MM"] = ("Mode de sortie", "Impression"),
        ["^MN"] = ("Suivi du support", "Support"),
        ["^MP"] = ("Verrouillage des réglages du panneau", "Système"),
        ["^MT"] = ("Méthode d'impression", "Support"),
        ["^MU"] = ("Unité de mesure (points, pouces, millimètres)", "Étiquette"),
        ["^MW"] = ("Avertissement de tête froide", "Système"),
        ["^NB"] = ("Recherche du serveur d'impression filaire au démarrage", "Réseau"),
        ["^NC"] = ("Réseau principal (1 filaire, 2 Wi-Fi, 3 serveur externe)", "Réseau"),
        ["~NC"] = ("Connexion réseau (imprimantes en réseau RS-485)", "Réseau"),
        ["^ND"] = ("Réglages réseau d'une interface (Wi-Fi ou externe)", "Réseau"),
        ["^NI"] = ("Numéro d'identification réseau", "Réseau"),
        ["^NN"] = ("Réglages SNMP (nom, contact, emplacement, communautés)", "Réseau"),
        ["^NP"] = ("Appareil principal (P imprimante, M serveur d'impression)", "Réseau"),
        ["~NR"] = ("Toutes les imprimantes du réseau en mode transparent", "Réseau"),
        ["^NS"] = ("Réglages du réseau filaire", "Réseau"),
        ["^NT"] = ("Réglages SMTP (serveur de messagerie)", "Réseau"),
        ["~NT"] = ("Imprimante connectée en mode transparent", "Réseau"),
        ["^NW"] = ("Délai d'authentification de la page web", "Réseau"),
        ["^PA"] = ("Réglages avancés du texte", "Étiquette"),
        ["^PF"] = ("Avance d'un nombre de lignes de points", "Impression"),
        ["^PH"] = ("Avance jusqu'au début de l'étiquette suivante", "Impression"),
        ["~PH"] = ("Avance jusqu'au début de l'étiquette suivante", "Impression"),
        ["~PL"] = ("Longueur de présentation (kiosque)", "Impression"),
        ["^PM"] = ("Impression en miroir", "Impression"),
        ["^PN"] = ("Présentation immédiate (kiosque)", "Impression"),
        ["^PO"] = ("Orientation de l'impression", "Impression"),
        ["^PP"] = ("Pause programmée", "Impression"),
        ["~PP"] = ("Pause programmée", "Impression"),
        ["^PQ"] = ("Quantité à imprimer", "Étiquette"),
        ["^PR"] = ("Vitesse d'impression", "Impression"),
        ["~PR"] = ("Réimpression (applicateur)", "Impression"),
        ["~PS"] = ("Reprise de l'impression après une pause", "Impression"),
        ["^PW"] = ("Largeur d'impression (points)", "Support"),
        ["^RA"] = ("Lecture du numéro AFI (RFID)", "Système"),
        ["^RB"] = ("Structure des données RFID", "Système"),
        ["^RE"] = ("Surveillance électronique d'article (RFID)", "Système"),
        ["^RF"] = ("Lecture ou écriture d'une étiquette RFID", "Système"),
        ["^RI"] = ("Lecture de l'identifiant RFID", "Système"),
        ["^RL"] = ("Verrouillage d'une étiquette RFID", "Système"),
        ["^RM"] = ("Gestion des erreurs RFID", "Système"),
        ["^RN"] = ("Détection d'étiquettes RFID multiples", "Système"),
        ["~RO"] = ("Remise à zéro des compteurs", "Système"),
        ["^RQ"] = ("Lecture rapide RFID", "Système"),
        ["^RR"] = ("Nombre de tentatives RFID", "Système"),
        ["^RS"] = ("Réglages RFID", "Système"),
        ["^RT"] = ("Lecture d'une étiquette RFID", "Système"),
        ["~RV"] = ("Rapport d'encodage RFID", "Système"),
        ["^RW"] = ("Puissance de lecture et d'écriture RFID", "Système"),
        ["^RZ"] = ("Mot de passe RFID", "Système"),
        ["^SC"] = ("Port série", "Communication"),
        ["~SD"] = ("Noirceur (contraste)", "Impression"),
        ["^SE"] = ("Table d'encodage", "Impression"),
        ["^SF"] = ("Incrément de champ (numérotation)", "Étiquette"),
        ["^SI"] = ("Sensibilité du capteur", "Support"),
        ["^SL"] = ("Mode et langue de l'horloge", "Système"),
        ["^SN"] = ("Numérotation automatique", "Étiquette"),
        ["^SO"] = ("Décalage de l'horloge", "Système"),
        ["^SP"] = ("Début d'impression avant la fin du format", "Impression"),
        ["^SQ"] = ("Arrêt des alertes ZebraNet", "Réseau"),
        ["^SR"] = ("Résistance de la tête", "Système"),
        ["^SS"] = ("Réglage manuel des capteurs", "Support"),
        ["^ST"] = ("Réglage de la date et de l'heure", "Système"),
        ["^SX"] = ("Alertes ZebraNet", "Réseau"),
        ["^SZ"] = ("Mode ZPL (1 ou 2)", "Système"),
        ["~TA"] = ("Position de détachage", "Support"),
        ["^TB"] = ("Bloc de texte", "Étiquette"),
        ["^TO"] = ("Copie d'un fichier", "Système"),
        ["^WA"] = ("Antennes Wi-Fi (D diversité, L gauche, R droite)", "Réseau"),
        ["~WC"] = ("Impression de l'étiquette de configuration", "Lecture"),
        ["^WD"] = ("Impression de la liste des fichiers", "Lecture"),
        ["^WE"] = ("Chiffrement WEP (Wi-Fi)", "Réseau"),
        ["^WF"] = ("Encodage AFI (RFID)", "Système"),
        ["^WI"] = ("Réglages IP du Wi-Fi", "Réseau"),
        ["^WK"] = ("Réglages Kerberos (Wi-Fi)", "Réseau"),
        ["^WL"] = ("Réglages LEAP (Wi-Fi)", "Réseau"),
        ["~WL"] = ("Impression de l'étiquette de configuration réseau", "Lecture"),
        ["^WP"] = ("Mot de passe des réglages Wi-Fi", "Réseau"),
        ["^WR"] = ("Débit et puissance d'émission Wi-Fi", "Réseau"),
        ["~WR"] = ("Redémarrage de la carte Wi-Fi et du serveur d'impression", "Réseau"),
        ["^WS"] = ("Réseau Wi-Fi (SSID, mode, préambule)", "Réseau"),
        ["^WT"] = ("Écriture d'une étiquette RFID", "Système"),
        ["^WV"] = ("Vérification de l'encodage RFID", "Système"),
        ["^WX"] = ("Sécurité Wi-Fi (WPA, EAP, WEP…)", "Réseau"),
        ["^XA"] = ("Début de format", "Étiquette"),
        ["^XB"] = ("Pas de recul après cette étiquette", "Impression"),
        ["^XF"] = ("Rappel d'un format enregistré", "Étiquette"),
        ["^XG"] = ("Rappel d'une image enregistrée", "Étiquette"),
        ["^XZ"] = ("Fin de format (les commandes sont appliquées)", "Étiquette"),
        ["^ZZ"] = ("Mise en veille de l'imprimante", "Système"),
    };

    /// <summary>Codes-barres ZPL : ^B suivi d'un de ces caractères.</summary>
    private const string ZplBarcodes = "0123456789ABCDEFIJKLMOPQRSTUXZ";

    /// <summary>Commande ZPL connue ? Polices (^A + police), codes-barres (^B…) et catalogue.</summary>
    private static bool ZplKnown(char prefix, string code, out string meaning, out string category)
    {
        if (ZplCatalog.TryGetValue(prefix + code, out var entry))
        {
            (meaning, category) = entry;
            return true;
        }

        if (prefix == '^' && code[0] == 'A')
        {
            (meaning, category) = ("Police du champ", "Étiquette");
            return true;
        }

        if (prefix == '^' && code[0] == 'B' && code.Length == 2 && ZplBarcodes.Contains(code[1], StringComparison.Ordinal))
        {
            (meaning, category) = ("Code-barres", "Étiquette");
            return true;
        }

        (meaning, category) = (string.Empty, string.Empty);
        return false;
    }

    /// <summary>Commande ZPL inconnue : erreur, avec la bonne écriture si elle existe (autre préfixe ou faute de frappe).</summary>
    private static void ZplUnknown(Ctx ctx, int n, char prefix, string code, string raw)
    {
        ctx.Item(n, "ZPL", raw, "Commande ZPL inconnue", "Système");
        var other = prefix == '^' ? '~' : '^';
        if (ZplCatalog.ContainsKey(other + code))
        {
            ctx.Find(n, Severity.Error, $"{prefix}{code} n'existe pas : cette commande s'écrit {other}{code}. L'imprimante l'ignore sans message d'erreur.");
            return;
        }

        var near = ZplCatalog.Keys.Where(k => k[0] == prefix && Distance(k[1..], code) == 1).OrderBy(k => k, StringComparer.Ordinal).ToList();
        ctx.Find(n, Severity.Error, near.Count > 0
            ? $"{prefix}{code} : commande ZPL inconnue, l'imprimante l'ignore sans message d'erreur. Vouliez-vous écrire {string.Join(" ou ", near.Take(3))} ?"
            : $"{prefix}{code} : commande ZPL inconnue, l'imprimante l'ignore sans message d'erreur.");
    }
}
