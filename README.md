# AVIX-DashSystem

AVIX LMU Dash Switcher  v1.0.0
==============================
Plugin SimHub : dans Le Mans Ultimate, le dashboard de tes écrans change tout seul
selon la voiture que tu pilotes (Ferrari 499P -> dash 499P, BMW M4 GT3 -> dash M4, etc.).


INSTALLATION (1 minute)
-----------------------
1. Double-clique sur  AvixInstaller.exe
   - il ferme SimHub s'il est ouvert,
   - copie le plugin dans le dossier de SimHub,
   - installe toutes les polices (SimHub\DashFonts + Windows pour ton compte),
   - installe tes 31 dashboards dans SimHub (ceux que tu as déjà sont conservés),
   - relance SimHub.
2. Au démarrage, SimHub demande s'il faut activer le nouveau plugin : réponds OUI.
3. Dans le menu de gauche de SimHub : « AVIX LMU Dash ». C'est tout.

Installation manuelle (si tu préfères) : copie Plugin\AvixLmuDashSwitcher.dll ET le dossier
Plugin\AvixLmuDash dans le dossier de SimHub (C:\Program Files (x86)\SimHub), puis lance SimHub :
le plugin installe lui-même polices et dashboards au premier démarrage.


COMMENT ÇA MARCHE
-----------------
Dans LMU, l'« ID voiture » de SimHub, c'est le nom de la livrée/équipe (ex. « GT3_Akkodis ASP Team
2026_78 »), pas le modèle. C'est pour ça que la « Playlist par voiture » de SimHub ne marche pas bien
toute seule : il faudrait assigner chaque livrée à la main.

Le plugin lit le vrai modèle de la voiture dans la télémétrie native de LMU (mVehicleModel) + la
classe (Hypercar / LMP2 / LMP3 / GTE / GT3), choisit le dashboard avec ses règles, puis remplit
la « Playlist par voiture » de chaque écran (onglet Appareils). SimHub fait le changement.
Si le modèle n'est pas disponible, il se rabat sur des mots-clés d'équipe (AF Corse -> Ferrari,
Akkodis -> Lexus, Manthey -> Porsche...).

Écrans pilotés : tous les écrans SimHub qui affichent un dashboard (tes VoCore, etc.).
Tu peux en décocher dans la page du plugin.


SI UNE VOITURE A LE MAUVAIS DASH
--------------------------------
Page « AVIX LMU Dash » > bloc « Voiture actuelle » : tu vois le modèle détecté et la règle utilisée.
Choisis le bon dashboard dans la liste puis « Utiliser ce dash pour cette voiture » : une règle est
ajoutée en haut de la liste. Tu peux aussi modifier les règles directement (mots-clés, classe, dash).


VOITURES / DASHBOARDS PAR DÉFAUT
--------------------------------
Hypercar : 499P, GR010, 963, BMW M Hybrid V8 (version « new version »), Cadillac V-Series.R,
           9X8, A424, SC63, Isotta Tipo 6, Glickenhaus 007, Vanwall 680, Valkyrie, Genesis GMR-001
LMGTE    : 488 GTE, Corvette C8.R, Aston Vantage GTE, 911 RSR
LMGT3    : BMW M4, Ferrari 296, Porsche 911 GT3 R (version « standard size DDU »), Lexus RC F,
           McLaren 720S, Mercedes-AMG, Corvette Z06, Huracan Evo2, Aston Vantage
LMP2     : toutes -> LMP2 LMU
LMP3     : Ginetta G61, Ligier JS P325 (Duqueine / Adess -> Ligier)
Pas de dash fourni pour : Ford Mustang LMGT3 -> le dash ne change pas (ou mets un « dash de secours »).


POLICES
-------
82 polices incluses (celles de tous tes dashboards + les zips 911 Porscha / Ferro Rosso /
Monkirta / Peugeot / Peugeot New + Roboto et Cascadia Code téléchargées).
Bouton « Vérifier les polices » dans la page du plugin pour voir s'il en manque.
2 polices commerciales introuvables gratuitement : « Designer » (Glickenhaus) et « Eurostar »
(6 textes du classement du Cadillac). Si tu les as, mets le .ttf dans SimHub\DashFonts.


DÉSINSTALLER
------------
Page du plugin > « Supprimer les assignations créées par le plugin », puis supprime
AvixLmuDashSwitcher.dll et le dossier AvixLmuDash dans le dossier de SimHub.


Propriétés SimHub exposées : AvixLmuDashPlugin.VehicleModel / .CarClass / .Dashboard / .Status / .CarId
Action mappable sur un bouton : AvixLmuDashPlugin.ReapplyDashboard
Code source dans le dossier Source (C#, .NET 4.8).
