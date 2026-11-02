# Conf imprimante — règles de l'onglet

Ces règles sont figées : toute évolution de l'onglet **Conf imprimante** les respecte. Elles évitent qu'une imprimante
lue influence un travail qui n'a rien à voir avec elle, et qu'on ne sache plus ce qu'un bouton va modifier.

## Une règle par partie

| Partie | Point de départ | Rôle de l'imprimante lue | Ce que les boutons modifient |
|---|---|---|---|
| **1. Analyser un fichier** | un fichier (ouvert, collé, glissé, ou préparé ailleurs dans l'onglet) | référence **seulement après « Lire l'imprimante »** ; « Ne plus utiliser l'imprimante » l'oublie | rien : on lit, on explique, on signale |
| **2. Convertir / Créer un fichier** | un fichier du PC, ou zéro | **aucun**, sauf le bouton « Partir des réglages de l'imprimante » (Créer) | le fichier produit à droite, jamais l'imprimante |
| **3. Réglages de l'imprimante** (dans la page) | l'imprimante lue | on la **regarde** | le fichier de l'éditeur (« Ajouter au fichier », « Retirer du fichier ») |
| **4. Plan de travail** (fenêtre détachée) | l'imprimante de la fenêtre, affichée en permanence | on **agit sur elle** | l'imprimante, uniquement par « Envoyer n modification(s) à [nom] ([adresse]) », après confirmation, puis relecture |

## Ce qui en découle

- **Rien n'est lu sans clic.** Choisir une imprimante en haut de la page dit seulement où envoyer. Sa marque et ses
  réglages ne servent de référence qu'après « Lire l'imprimante » ; « Ne plus utiliser l'imprimante » les oublie
  (l'imprimante reste la destination de l'envoi). Changer d'imprimante oublie aussi ce qui avait été lu.
- Lire, relire ou oublier une imprimante ne modifie **jamais** un choix déjà fait : langage d'arrivée, résolution,
  formulaire, fichier ouvert, fichier converti.
- À l'entrée de Convertir ou de Créer, le langage n'est coché d'office que si une imprimante a été **lue** ; la raison
  est écrite (« Choisi d'après l'imprimante lue… »). Sinon rien n'est coché et rien n'est produit tant que le langage
  n'est pas choisi. Une fois choisi, il ne change plus tout seul.
- Dans la page, tout ce qui modifie quelque chose modifie **le fichier**, et le dit (« fichier » dans le libellé du bouton).
  Dans le plan de travail, tout ce qui modifie quelque chose modifie **l'imprimante de la fenêtre**, et la nomme.
- Ne sont jamais repris dans un fichier ni copiés d'une imprimante à l'autre : les réglages propres à une imprimante
  (adresses, nom, numéro de série, secrets), ceux qui déclenchent une action (remise en usine, calibrage) et les
  informations en lecture seule.

## Selon la marque et la liaison

| | Zebra en réseau | Zebra en USB direct | Autres marques |
|---|---|---|---|
| Reconnaissance (marque, modèle, langages) | oui | par le pilote Windows | réseau (SNMP) ou pilote Windows |
| Analyser, Convertir, Créer, Envoyer | oui | oui (« Installée sur ce PC ») | oui |
| Lecture des réglages (parties 3 et 4, « Partir des réglages ») | oui | **non, pour l'instant** | non (ces marques ne donnent pas leurs réglages) |

Une imprimante « Installée sur ce PC » dont le port Windows est une adresse IP (port TCP/IP standard) est lue par le réseau.
La lecture d'une Zebra en USB direct est une évolution possible, à mener à part.
