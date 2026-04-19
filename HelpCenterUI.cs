using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Libraries.Covalence;
using Oxide.Core.Plugins;
using Oxide.Game.Rust.Cui;
using UnityEngine;
using UnityEngine.UI;

namespace Oxide.Plugins
{
    [Info("Help Center UI", "gamezoneone", "1.5.2")]
    [Description("In-game help system with categories, sub-pages, editor UI and optional chat output.")]
    public class HelpCenterUI : RustPlugin
    {
        private const string UiNameMain = "HelpCenterUI.Main";
        private const string UiNameBg = "HelpCenterUI.Bg";
        private const string UiNameEdit = "HelpCenterUI.Edit";
        private const string PermAdmin = "helpcenterui.admin";
        private const string PermEdit = "helpcenterui.edit";
        private const int MaxMainCategories = 5;
        private const int ContentLinesPerPart = 17;
        private const int SubCategoriesPerPage = 12;
        /// <summary>Text-Panel vertikal: unter den beiden Überschrift-Zeilen, über der Fußleiste.</summary>
        private const float MainContentMinY = 0.088f;
        private const float MainContentMaxY = 0.688f;
        private const float MainContentMaxYHardCap = 0.700f;
        private const bool AutoCommandsIncludeConsole = false;
        private const string FullscreenRawImageSprite = "assets/content/textures/generic/fulltransparent.tga";
        private const string ImageLibraryBackgroundName = "HelpCenterUI.Background";
        private const float PendingDeleteConfirmSeconds = 12f;

        [PluginReference]
        private Plugin ImageLibrary;

        private PluginConfig _config;
        private readonly Dictionary<ulong, PlayerUiState> _uiStates = new Dictionary<ulong, PlayerUiState>();
        private readonly HashSet<ulong> _helpBgActive = new HashSet<ulong>();

        private sealed class EditDraft
        {
            public string CategoryKey;
            public string PageKey;
            public string Title = string.Empty;
            public string Content = string.Empty;
        }

        private readonly Dictionary<ulong, EditDraft> _editDrafts = new Dictionary<ulong, EditDraft>();

        private sealed class PendingDeleteConfirm
        {
            public string Kind;
            public string CategoryKey;
            public string PageKey;
            public float Time;
        }

        private readonly Dictionary<ulong, PendingDeleteConfirm> _pendingDeletes = new Dictionary<ulong, PendingDeleteConfirm>();
        private string _backgroundImageRegisteredForUrl;
        private string _cachedPublicCommandsText = "Befehlsindex wird erstellt ...";
        private string _cachedInternalCommandsText = "Interner Befehlsindex wird erstellt ...";
        private string _cachedTeamRosterText = "Teamliste wird erstellt ...";

        private class PlayerUiState
        {
            public string CategoryKey;
            public string PageKey;
            public int ContentPart;
            public int SubCategoryPage;
        }

        private class PluginConfig
        {
            [JsonProperty("Commands")]
            public Dictionary<string, string> Commands = new Dictionary<string, string>
            {
                ["help"] = "",
                ["info"] = "",
                ["helppage"] = "",
                ["wiki"] = "commands:bindings",
                ["events"] = "events:overview",
                ["regeln"] = "server:regeln",
                ["befehle"] = "commands:teleport",
                ["markt"] = "commands:economy",
                ["shops"] = "commands:economy"
            };

            [JsonProperty("Main Category Order (max 5)")]
            public List<string> MainCategoryOrder = new List<string> { "start", "server", "commands", "events", "community" };

            [JsonProperty("Fullscreen Layout")]
            public bool FullscreenLayout = true;

            [JsonProperty("Background Image Url")]
            public string BackgroundImageUrl = "https://pic.gamezoneone.de/api/media/4g7wkwbb.png";

            [JsonProperty("Background Global Darken")]
            public float BackgroundGlobalDarken = 0.45f;

            [JsonProperty("Background Image Alpha")]
            public float BackgroundImageAlpha = 1f;

            /// <summary>Beim Join automatisch /help öffnen (null = Standard ja).</summary>
            [JsonProperty("Open Help On Connect")]
            public bool? OpenHelpOnConnect;

            /// <summary>Verzögerung nach Spawn, damit Client/UI bereit ist (Sekunden).</summary>
            [JsonProperty("Open Help Delay Seconds")]
            public float? OpenHelpDelaySeconds;

            [JsonProperty("Pages")]
            public Dictionary<string, CategoryConfig> Pages = new Dictionary<string, CategoryConfig>
            {
                ["start"] = new CategoryConfig
                {
                    Title = "Start",
                    Entries = new Dictionary<string, PageConfig>
                    {
                        ["welcome"] = new PageConfig
                        {
                            Title = "Willkommen",
                            Content = "Willkommen auf GameZoneOne!\n\n" +
                                      "Wir sind ein deutscher Rust-Server mit klarem Fokus: entspanntes PvE, faire Regeln und spannende PvP-Hotspots – wenn du sie suchst. Vor jedem Map-Wipe gibt es eine Purge-Phase: dann wird es überall härter – plane rechtzeitig mit.\n\n" +
                                      "Was dich erwartet (Kurzüberblick):\n" +
                                      "• Sammeln: erhöhte Raten (Gather)\n" +
                                      "• Rhythmus: Map-Wipe etwa alle 3 Wochen (Details im Discord)\n" +
                                      "• Community: Discord ist der beste Ort für Termine, Events und Support\n\n" +
                                      "Links:\n" +
                                      "• Discord: discord.gg/szBd7ZD5Bq\n" +
                                      "• Website: www.gamezoneone.de\n\n" +
                                      "Dieses Menü öffnest du mit /help (Kurzform /info). Unten rechts kannst du die aktuelle Seite mit „In Chat anzeigen“ in den Chat kopieren – praktisch, wenn du etwas nachlesen willst.",
                            FontSize = 14,
                            Center = false,
                            IncreaseHeightValue = 0
                        },
                        ["erste_schritte"] = new PageConfig
                        {
                            Title = "Erste Schritte",
                            Content = "Deine ersten Minuten – ohne Stress:\n\n" +
                                      "1) Sammeln & Überleben\n" +
                                      "• Holz von Bäumen, Steine am Boden, ggf. Tier für Tuch\n" +
                                      "• Crafte eine Steinaxt, dann Spitzhacke und eine Waffe (Bogen reicht am Anfang)\n\n" +
                                      "2) Sichere Basis\n" +
                                      "• Kleine 1×1 oder 2×2 reicht zum Start – Hauptsache: Tool Cupboard (TC) mit Ressourcen füllen\n" +
                                      "• Bett oder Schlafsack setzen = eigener Respawn\n" +
                                      "• Türen mit Schloss oder Code-Schloss sichern\n\n" +
                                      "3) Recycler & Loot\n" +
                                      "• Monumente mit Recycler nutzen (Komponenten → Scrap)\n" +
                                      "• Vorsicht in PvP-Zonen: andere Spieler dürfen dort angreifen\n\n" +
                                      "4) Server kennenlernen\n" +
                                      "• /help durchstöbern (du bist hier schon richtig)\n" +
                                      "• /link für Discord – dann oft /daily und Community-Features\n\n" +
                                      "Tipp: Spiel mit Kopf, nicht mit Ego. Fragen? Discord oder /report bei echten Problemen.",
                            FontSize = 14,
                            Center = false,
                            IncreaseHeightValue = 0
                        }
                    }
                },
                ["server"] = new CategoryConfig
                {
                    Title = "Server",
                    Entries = new Dictionary<string, PageConfig>
                    {
                        ["regeln"] = new PageConfig
                        {
                            Title = "Regeln & Fairplay",
                            Content = "Ziel: für alle ein faires, respektvolles Spiel. Regeln können sich weiterentwickeln – maßgeblich sind Ankündigungen im Discord.\n\n" +
                                      "Allgemein\n" +
                                      "• Kein Cheating, Makros mit Vorteil, Exploits oder absichtliches Glitchen\n" +
                                      "• Keine Beleidigungen, Hate, Rassismus, Sexismus, Homophobie – null Toleranz\n" +
                                      "• Kein Doxxing, keine echten Drohungen\n" +
                                      "• Kein Spam, kein Werben für andere Server/Communities\n" +
                                      "• Anweisungen des Teams sind einzuhalten\n\n" +
                                      "PvE & Basen\n" +
                                      "• Fremde Basen nicht ausrauben oder zerstören (PvE-Phase)\n" +
                                      "• Keine Türme oder Belästigungen direkt an fremden Bases (kein „Camping“ am TC)\n" +
                                      "• Monumente nicht dauerhaft blockieren (z. B. mit Wänden)\n" +
                                      "• Verlassene Basen: nach Serverregel oft erst nach Ablauf der Inaktivitätsfrist – Infos im Discord\n\n" +
                                      "PvP-Zonen & Purge\n" +
                                      "• In markierten PvP-Zonen gelten andere Risiken – mit Vorsicht reingehen\n" +
                                      "• Purge (kurz vor Map-Wipe): intensiveres PvP und Raiding nach Server-Ankündigung – vorbereiten!\n\n" +
                                      "Melden\n" +
                                      "• Verstöße: /report oder Discord-Ticket mit Beweisen (Clip, Screenshots)\n\n" +
                                      "Bei Unklarheiten: vorher im Discord nachfragen – lieber einmal zu viel als ein Ban zu viel.",
                            FontSize = 13,
                            Center = false,
                            IncreaseHeightValue = 0
                        },
                        ["pvp"] = new PageConfig
                        {
                            Title = "PvP-Zonen & Risiko",
                            Content = "Nicht überall ist Rust „entspannt“ – auf GameZoneOne gibt es klar markierte PvP-Zonen (z. B. bestimmte Monumente oder Gebiete).\n\n" +
                                      "Was du wissen solltest\n" +
                                      "• In diesen Zonen darfst du angegriffen werden – auch ohne vorheriges „RPS“\n" +
                                      "• Ohne Plan und ohne Ausrüstung: oft schnell tot. Mit Team: deutlich besser\n" +
                                      "• Nutze /pvpzones (wenn aktiv), um Bereiche auf der Karte zu sehen\n\n" +
                                      "Außerhalb der PvP-Zonen gilt im Normalbetrieb PvE-Charakter – bis zur Purge-Phase vor dem Wipe.\n\n" +
                                      "Purge kurz erklärt\n" +
                                      "• In den letzten Tagen vor dem Map-Wipe wird angekündigt: stärkeres PvP und Raiding serverweit nach Regelwerk\n" +
                                      "• Base, Loot und Meds rechtzeitig sichern oder einplanen\n\n" +
                                      "Tipp: Nichts mitnehmen, was du nicht verlieren kannst – besonders in Hotspots.",
                            FontSize = 14,
                            Center = false,
                            IncreaseHeightValue = 0
                        },
                        ["wipe"] = new PageConfig
                        {
                            Title = "Wipe & Termine",
                            Content = "Map-Wipe (ca. alle 3 Wochen)\n" +
                                      "• Neue Karte, frische Monumente, oft auch Blueprint-Reset – je nach Ankündigung\n" +
                                      "• Exakte Daten und Uhrzeiten: immer im Discord (z. B. #ankündigungen oder #wipe)\n" +
                                      "• Vor dem Wipe: meist Purge-Phase – siehe Seite „PvP-Zonen & Risiko“\n\n" +
                                      "Warum Wipe?\n" +
                                      "• Hält den Server performant und fair – alle starten wieder mit ähnlichen Chancen\n\n" +
                                      "Was du tun kannst\n" +
                                      "• Vor dem Wipe: Ressourcen verbrauchen oder mit Freunden raiden (während Purge erlaubt)\n" +
                                      "• Nach dem Wipe: neu planen, neue Routen, neue Nachbarn\n\n" +
                                      "Diese Hilfe enthält keine festen Kalenderdaten – die ändern sich. Discord ist die Quelle der Wahrheit.",
                            FontSize = 14,
                            Center = false,
                            IncreaseHeightValue = 0
                        },
                        ["report"] = new PageConfig
                        {
                            Title = "Report & Hilfe",
                            Content = "Etwas stimmt nicht – Spieler, Bug oder Verdacht?\n\n" +
                                      "Im Spiel\n" +
                                      "• /report öffnet das Melde-Interface (Grund, optional Spieler, Text, optional Medien-Links)\n" +
                                      "• Beschreibe sachlich: Was? Wo? Wann? Wer (wenn bekannt)?\n" +
                                      "• Bei Bugs: kurz reproduzierbar machen („mache X, dann Y, dann passiert Z“)\n" +
                                      "• Nach dem Absenden: Trace-ID für das Team; Snapshots unter oxide/data (falls aktiv)\n\n" +
                                      "Team (Mods/Admins)\n" +
                                      "• /reportadmin — letzte Meldungen (optional /reportadmin 20)\n" +
                                      "• Recht: gamezonereportui.admin oder Admin-Flag\n\n" +
                                      "Discord\n" +
                                      "• Für längere Fälle, Beweise oder Rückfragen: Ticket im Discord\n" +
                                      "• Screenshots, Clips, Log-Auschnitte helfen dem Team enorm\n\n" +
                                      "Was wir nicht brauchen\n" +
                                      "• Flaming im Ticket – kostet nur Zeit\n" +
                                      "• Meldungen ohne Inhalt („der ist doof“)\n\n" +
                                      "Antwortzeiten können je nach Auslastung variieren – bitte Geduld. Ernstfälle (Cheating, echte Belästigung) haben Priorität.",
                            FontSize = 14,
                            Center = false,
                            IncreaseHeightValue = 0
                        },
                        ["performance"] = new PageConfig
                        {
                            Title = "FPS & Stabilität",
                            Content = "Rust ist hungrig – ein paar Einstellungen helfen oft mehr als neuer Rechner.\n\n" +
                                      "Grafik (Ingame)\n" +
                                      "• Schatten, Wasser, Reflexionen, Gras: runter oder aus\n" +
                                      "• Partikel und Effekte reduzieren\n" +
                                      "• Auflösung: eine Stufe niedriger kann Wunder wirken\n\n" +
                                      "System\n" +
                                      "• Vollbild oder randloser Fenster-Modus testen\n" +
                                      "• Browser, Launcher, unnötige Apps schließen\n" +
                                      "• Treiber (GPU) aktuell halten\n\n" +
                                      "Auf dem Server\n" +
                                      "• Große PvP-Schlachten und viele Spieler = mehr Last – kurze Ruckler können normal sein\n" +
                                      "• Wenn du dauerhaft hohen Ping hast: Verbindung/WLAN prüfen\n\n" +
                                      "Tipp: In den Optionen die FPS-Anzeige aktivieren – so siehst du, ob es Grafik oder Netz ist.",
                            FontSize = 14,
                            Center = false,
                            IncreaseHeightValue = 0
                        }
                    }
                },
                ["commands"] = new CategoryConfig
                {
                    Title = "Befehle",
                    Entries = new Dictionary<string, PageConfig>
                    {
                        ["teleport"] = new PageConfig
                        {
                            Title = "Teleport & Homes",
                            Content = "Teleport (Spieler zu Spieler)\n" +
                                      "• /tpr <Name> – Anfrage senden\n" +
                                      "• /tpa – Anfrage annehmen\n" +
                                      "• /tpc – ablehnen oder abbrechen\n" +
                                      "• /tpb – zurück zur letzten Position (wenn verfügbar)\n" +
                                      "• /tpinfo – Limits und Cooldowns\n" +
                                      "• /tphelp – ausführliche Hilfe zum Plugin\n\n" +
                                      "Homes (gespeicherte Punkte)\n" +
                                      "• /home set <Name> – Home an aktueller Position setzen\n" +
                                      "• /home <Name> – zum Home teleportieren\n" +
                                      "• /home list – alle Homes anzeigen\n" +
                                      "• /home remove <Name> – Home löschen\n\n" +
                                      "Typische Limits (können variieren – /tpinfo prüfen)\n" +
                                      "• Standard: z. B. 3 Homes, begrenzte Teleports pro Tag, Cooldown zwischen Sprüngen\n" +
                                      "• VIP: oft mehr Homes, kürzerer Cooldown, mehr Teleports – Details im Discord\n\n" +
                                      "Fairplay: keine Teleport-Tricks in Kämpfen ausnutzen, wo es Regeln gibt – im Zweifel: Team fragen.",
                            FontSize = 13,
                            Center = false,
                            IncreaseHeightValue = 0
                        },
                        ["remove"] = new PageConfig
                        {
                            Title = "Remover & Bauen",
                            Content = "Remover\n" +
                                      "• /remove – Menü oder Modus zum Entfernen eigener Bauteile (je nach Plugin)\n" +
                                      "• Oft zeitlich begrenzt oder mit Kosten – Hinweise im Chat beachten\n" +
                                      "• Nur eigene Bauteile – kein Fremd-Raid im PvE außerhalb der erlaubten Phasen\n\n" +
                                      "Bauen (Allgemein)\n" +
                                      "• Tool Cupboard (TC) immer zuerst – ohne TC kein Upkeep-Schutz\n" +
                                      "• Fundament vor Wänden, Decken für Schutz gegen Wetter\n" +
                                      "• Airlocks (Doppel-Türen) reduzieren Offline-Doorcamp-Risiko\n\n" +
                                      "Wenn du dich vertan hast: Remover nutzen, statt alles zu sprengen – spart Ressourcen und Nerven.",
                            FontSize = 14,
                            Center = false,
                            IncreaseHeightValue = 0
                        },
                        ["clan"] = new PageConfig
                        {
                            Title = "Clan & Allianz",
                            Content = "Clan\n" +
                                      "• /clan – Menü oder Hilfe\n" +
                                      "• /clan create <Tag> <Name> – Clan gründen\n" +
                                      "• /clan invite <Spielername> – einladen\n" +
                                      "• /clan join <Tag> – Beitritt\n" +
                                      "• /clan leave – austreten\n" +
                                      "• /clan kick <Name> – Mitglied entfernen\n" +
                                      "• /clan promote <Name> | /clan demote <Name> – Ränge\n" +
                                      "• /clan disband – Clan auflösen (Admin-Rechte im Clan)\n" +
                                      "• /cinfo – Informationen\n\n" +
                                      "Chat\n" +
                                      "• /c <Text> – Clan-Chat\n" +
                                      "• /a <Text> – Allianz-Chat (wenn aktiv)\n" +
                                      "• /ally – Allianzen verwalten\n\n" +
                                      "Tipp: Tag kurz halten, Namen lesbar – das hilft bei Einladungen und Reports.",
                            FontSize = 13,
                            Center = false,
                            IncreaseHeightValue = 0
                        },
                        ["kits"] = new PageConfig
                        {
                            Title = "Kits & Inventar",
                            Content = "Kits\n" +
                                      "• /kit – Kit-Menü öffnen\n" +
                                      "• /kit starter – Starter-Kit (wenn verfügbar)\n" +
                                      "• /kit vip – VIP-Kit (oft 1× pro Tag, nur mit VIP)\n" +
                                      "• /kit autokit – automatisches Kit an/aus (falls konfiguriert)\n\n" +
                                      "Inventar & Komfort\n" +
                                      "• /backpack – Zusatz-Inventar (Slots je nach Server/VIP)\n" +
                                      "• /sort oder /qs – Inventar sortieren\n" +
                                      "• /fs – Ofensplitter (wenn aktiv)\n\n" +
                                      "Tipp: Kits nicht horten, wenn du Platz brauchst – nutze Recycler und Lagerboxen früh.",
                            FontSize = 14,
                            Center = false,
                            IncreaseHeightValue = 0
                        },
                        ["economy"] = new PageConfig
                        {
                            Title = "Shop & Wirtschaft",
                            Content = "Überblick\n" +
                                      "• /markt oder /shops — Kurzerklärung im Chat (RP-Shop)\n" +
                                      "• /s — Server Rewards: Items, Kits, Commands (z. B. Outpost-TP), RP-Transfer; Verkauf & Economics-Tausch bei uns aus\n" +
                                      "• /kit — Kits direkt; im /s-Shop dieselben Kit-Namen gegen RP, wo angeboten\n" +
                                      "• /daily — tägliche Belohnung (oft nach /link)\n" +
                                      "• /refer <Name> — Referral-Bonus (wenn aktiv)\n\n" +
                                      "RP nutzen\n" +
                                      "• RP sammeln (Spielzeit, Events, Discord) und im /s-Shop ausgeben\n" +
                                      "• RP an andere senden: Transfer im /s-Menü (wenn aktiv)\n\n" +
                                      "Spartipps\n" +
                                      "• Vor großen Käufen: Preise im Shop prüfen\n" +
                                      "• Team: vorher absprechen, wer was kauft",
                            FontSize = 14,
                            Center = false,
                            IncreaseHeightValue = 0
                        },
                        ["bindings"] = new PageConfig
                        {
                            Title = "Tasten & F1-Konsole",
                            Content = "Eigene Tasten (Bindings)\n" +
                                      "• F1-Konsole öffnen, dann z. B.:\n" +
                                      "  bind k kill\n" +
                                      "  bind b chat.say /backpack\n" +
                                      "• Leerzeichen in Chat-Befehlen: in Anführungszeichen setzen\n" +
                                      "  Beispiel: bind b \"chat.say /home main\"\n\n" +
                                      "Wichtig\n" +
                                      "• Zu viele Bindings können unübersichtlich werden – lieber wenige sinnvolle\n" +
                                      "• Nach Updates: testen, ob Befehle noch gleich heißen\n\n" +
                                      "Tipp: Ein Binding für „schnell Home“ und einer für „Rucksack“ reicht vielen Spielern.",
                            FontSize = 14,
                            Center = false,
                            IncreaseHeightValue = 0
                        },
                        ["misc"] = new PageConfig
                        {
                            Title = "Chat, Discord & Sonstiges",
                            Content = "Discord\n" +
                                      "• /link – Code anzeigen, im Discord mit Bot verknüpfen\n" +
                                      "• /unlink – Verknüpfung trennen (Vorsicht: Features weg)\n\n" +
                                      "Melden & Infos\n" +
                                      "• /report – Meldeformular\n" +
                                      "• /playtime – eigene Spielzeit (wenn aktiv)\n\n" +
                                      "Welt & Komfort\n" +
                                      "• /voteday – Abstimmung Tag/Nacht (wenn aktiv)\n" +
                                      "• /pvpzones – PvP-Zonen auf der Karte (wenn aktiv)\n" +
                                      "• /killfeed – Killfeed ein/aus\n" +
                                      "• /nv oder /nightvision – Nachtsicht (wenn aktiv)\n\n" +
                                      "Wenn ein Befehl „nicht gefunden“ meldet: Plugin kann fehlen oder Befehl umbenannt – kurz im Discord nachfragen.",
                            FontSize = 13,
                            Center = false,
                            IncreaseHeightValue = 0
                        },
                        ["farming"] = new PageConfig
                        {
                            Title = "Routen & Tipps",
                            Content = "Sanfter Start\n" +
                                      "• Straße entlang: Straßenschilder, kleine Monumente, Recycler\n" +
                                      "• Supermarkt, Tankstelle, Oxide-Kisten: Komponenten und Scrap\n" +
                                      "• Recycler immer mit nutzen – aus Schrott werden Bauteile\n\n" +
                                      "Risiko einteilen\n" +
                                      "• Grüne und blaue Schlüssel-Karten: mit Meds, Waffe und Zeitplan\n" +
                                      "• PvP-Zonen: nur mit Team oder nach Übung\n\n" +
                                      "Mid- bis Endgame\n" +
                                      "• Größere Monumente, Heli, Cargo, Oil – mit Base in der Nähe oder Boot\n" +
                                      "• Vor Purge: Loot einlagern oder verbrauchen, Pläne mit dem Team klären\n\n" +
                                      "Tipp: Stirb nicht mit dem ganzen Scrap in der Tasche – Bank vor dem Run.",
                            FontSize = 14,
                            Center = false,
                            IncreaseHeightValue = 0
                        }
                    }
                },
                ["community"] = new CategoryConfig
                {
                    Title = "Community",
                    Entries = new Dictionary<string, PageConfig>
                    {
                        ["discord"] = new PageConfig
                        {
                            Title = "Discord",
                            Content = "Unser Discord ist der Hub für Termine, Events, Regeln-Updates und Support.\n\n" +
                                      "Link\n" +
                                      "• discord.gg/szBd7ZD5Bq\n\n" +
                                      "Im Spiel verbinden\n" +
                                      "• /link ausführen – Code merken\n" +
                                      "• Im Discord den Anweisungen des Bots folgen (Verknüpfung)\n" +
                                      "• Danach: /daily und andere Features oft nutzbar\n\n" +
                                      "Was du im Discord findest\n" +
                                      "• Wipe- & Purge-Termine\n" +
                                      "• Event-Ankündigungen\n" +
                                      "• Tickets & Support\n" +
                                      "• Stimmung der Community – respektvoll bleiben\n\n" +
                                      "Tipp: Rolle lesen, Kanäle nicht spammen – dann hilft dir das Team schneller.",
                            FontSize = 14,
                            Center = false,
                            IncreaseHeightValue = 0
                        },
                        ["vip"] = new PageConfig
                        {
                            Title = "VIP",
                            Content = "VIP ist ein Danke an Spieler, die den Server unterstützen – und bringt praktische Vorteile.\n\n" +
                                      "Typische Vorteile (kann je nach Paket variieren – Discord ist maßgeblich)\n" +
                                      "• Mehr Homes und mehr Teleports / kürzerer Cooldown\n" +
                                      "• Größerer Backpack oder zusätzliche Slots\n" +
                                      "• Sichtbare Vorteile im Chat (z. B. Farbe oder Tag)\n" +
                                      "• Ggf. zusätzliche Kits\n\n" +
                                      "Wie bekomme ich VIP?\n" +
                                      "• Über das Discord und die dort beschriebenen Schritte (Spende, Paket, Rolle)\n" +
                                      "• Kein Pay-to-win im Sinne von unfairen Kampfvorteilen – Server-Philosophie im Discord nachlesen\n\n" +
                                      "Fragen?\n" +
                                      "• Vor dem Kauf: Infos im Discord\n" +
                                      "• Probleme mit Rolle: Support-Ticket, nicht im All-Chat eskalieren",
                            FontSize = 13,
                            Center = false,
                            IncreaseHeightValue = 0
                        },
                        ["staff"] = new PageConfig
                        {
                            Title = "Team & Support",
                            Content = "Das Team kümmert sich um Regeln, Technik und Streitschlichtung – aber keiner sitzt 24/7 nur im Spiel.\n\n" +
                                      "Erreichbarkeit\n" +
                                      "• Am zuverlässigsten: Discord (Tickets, Kanäle)\n" +
                                      "• Eiliges im Spiel: /report (mit Beweisen)\n\n" +
                                      "Was wir gut können\n" +
                                      "• Sachliche Meldungen mit Details\n" +
                                      "• Bugs mit Reproduktion\n" +
                                      "• Verstöße mit Beweisen\n\n" +
                                      "Was uns Zeit kostet\n" +
                                      "• „Admin kommt mal her“ ohne Grund\n" +
                                      "• Diskussionen im Chat statt Ticket\n\n" +
                                      "Gegen das Team selbst?\n" +
                                      "• Ruhig, sachlich, mit Beweisen – es gibt Eskalationswege im Discord\n\n" +
                                      "Danke, dass du fair bleibst – das macht den Server für alle besser.",
                            FontSize = 14,
                            Center = false,
                            IncreaseHeightValue = 0
                        }
                    }
                },
                ["events"] = new CategoryConfig
                {
                    Title = "Events",
                    Entries = new Dictionary<string, PageConfig>
                    {
                        ["overview"] = new PageConfig
                        {
                            Title = "Übersicht",
                            Content = "Auf GameZoneOne laufen verschiedene Events – automatisch (Server) oder manuell (Team).\n\n" +
                                      "Typische Beispiele\n" +
                                      "• Heli / Bradley / Kisten-Events\n" +
                                      "• Cargo-Schiff und Ölfelder\n" +
                                      "• Airdrops (stark frequentiert, PvP-Risiko)\n" +
                                      "• Purge-Phase vor dem Map-Wipe\n\n" +
                                      "Wo Infos?\n" +
                                      "• Chat-Ankündigungen\n" +
                                      "• Discord (#events, #ankündigungen)\n\n" +
                                      "Tipp: Trag dich nicht mit vollem Inventar in jedes Event – besonders bei Airdrop und Heli.",
                            FontSize = 14,
                            Center = false,
                            IncreaseHeightValue = 0
                        },
                        ["purge"] = new PageConfig
                        {
                            Title = "Purge",
                            Content = "Die Purge ist die heiße Phase vor dem Map-Wipe.\n\n" +
                                      "Was sich ändert\n" +
                                      "• Serverweit gelten die angekündigten PvP- und Raid-Regeln – intensiver als im normalen PvE-Alltag\n" +
                                      "• Basen, Allianzen und Loot sind stärker bedroht\n\n" +
                                      "Was du tun solltest\n" +
                                      "• Vorräte einplanen: Meds, Munition, Sprengstoff nach Regelwerk\n" +
                                      "• Mit Team sprechen: wer verteidigt, wer farmt, wer raided\n" +
                                      "• Nicht alles auf eine Karte setzen – Backup-Depot kann retten\n\n" +
                                      "Wann genau?\n" +
                                      "• Immer im Discord ankündigt – keine festen Zeiten in dieser Hilfe\n\n" +
                                      "Kein Überraschungs-Purge: achte auf Discord und Chat.",
                            FontSize = 14,
                            Center = false,
                            IncreaseHeightValue = 0
                        },
                        ["airdrop"] = new PageConfig
                        {
                            Title = "Airdrop",
                            Content = "Airdrops ziehen Spieler an wie ein Licht – erwarte Kampf.\n\n" +
                                      "Vorbereitung\n" +
                                      "• Waffe, Meds, ggf. Sprengstoff – je nach Situation\n" +
                                      "• Position: Deckung, Höhe, Fluchtweg\n" +
                                      "• Team: einer schaut Umgebung, einer lootet\n\n" +
                                      "Verhalten\n" +
                                      "• Nicht im offenen Feld stehen bleiben\n" +
                                      "• Auf Schüsse und Schritte hören\n" +
                                      "• Lieber mitziehen als unnötig sterben\n\n" +
                                      "Nach dem Loot: schnell weg oder sichern – Gefecht geht oft weiter als die Kiste.",
                            FontSize = 14,
                            Center = false,
                            IncreaseHeightValue = 0
                        },
                        ["cargoship"] = new PageConfig
                        {
                            Title = "Cargo & Oil",
                            Content = "Cargo-Schiff\n" +
                                      "• Hoher Loot, viele Spieler, Chaos möglich\n" +
                                      "• Boot, Meds, Wiederbelebung / Bett in der Nähe planen\n" +
                                      "• Nicht alleine gehen, wenn du unsicher bist\n\n" +
                                      "Oil Rig / große Wasser-Events\n" +
                                      "• Fernkampf, Sniper, Boote – oft Endgame-Niveau\n" +
                                      "• Wissenschaftler und Spieler gleichermaßen gefährlich\n\n" +
                                      "Tipp: Wenn du nur farmen willst, meide die Hotspots zur Rush-Hour – oder geh mit klarer Absprache im Team.",
                            FontSize = 14,
                            Center = false,
                            IncreaseHeightValue = 0
                        }
                    }
                },
            };
        }

        private class CategoryConfig
        {
            [JsonProperty("Title")]
            public string Title = "Kategorie";

            [JsonProperty("Entries")]
            public Dictionary<string, PageConfig> Entries = new Dictionary<string, PageConfig>();
        }

        private class PageConfig
        {
            [JsonProperty("Title")]
            public string Title = "Seite";

            [JsonProperty("Content")]
            public string Content = string.Empty;

            [JsonProperty("Font Size")]
            public int FontSize = 15;

            [JsonProperty("Center")]
            public bool Center;

            [JsonProperty("Increase Height Value")]
            public int IncreaseHeightValue;

            [JsonProperty("Admin Only")]
            public bool AdminOnly;
        }

        protected override void LoadDefaultConfig()
        {
            _config = new PluginConfig();
            SaveConfig();
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            _config = Config.ReadObject<PluginConfig>();
            if (_config == null)
            {
                PrintWarning("Config war leer/ungueltig, Standard wird neu erstellt.");
                LoadDefaultConfig();
            }

            EnsureConfigDefaults();
        }

        protected override void SaveConfig() => Config.WriteObject(_config, true);

        private string T(string key, string userId = null, params object[] args)
        {
            var msg = lang.GetMessage(key, this, userId);
            return args.Length > 0 ? string.Format(msg, args) : msg;
        }

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["NoCategories"]      = "No categories configured — please check HelpCenterUI.json.",
                ["NoPageVisible"]     = "<color=#ff8888>No help page visible — check permissions or admin pages.</color>",
                ["PageNotFound"]      = "<color=#ff8888>Help editor: page not found or not visible.</color>",
                ["AdminOnly"]         = "<color=#ff8888>Only server admins may edit this page.</color>",
                ["PageInvalid"]       = "Page is no longer valid.",
                ["HelpSaved"]         = "<color=#8fdf9f>Help saved: {0} / {1}</color>",
                ["ChatHeader"]        = "<color=#8EC6FF>----- Help: {0} -----</color>",
                ["ConfirmDelPage"]    = "<color=#ffcc88>To delete this page press \"DELETE PAGE\" again (within {0} s).</color>",
                ["ConfirmDelCat"]     = "<color=#ffcc88>To delete this category press \"DELETE CATEGORY\" again (within {0} s).</color>",
                ["OpenHelpFirst"]     = "Open /help first or use /helpedit newpage <category> <slug>.",
            }, this);
        }

        private void Init()
        {
            permission.RegisterPermission(PermAdmin, this);
            permission.RegisterPermission(PermEdit, this);
            AddCovalenceCommand("helpreload", nameof(CmdHelpReload));
            AddCovalenceCommand("helpedit", nameof(CmdHelpEdit));
            RegisterConfiguredCommands();
        }

        private void OnServerInitialized()
        {
            RebuildDynamicCaches();
            EnsureHelpBackgroundImageRegistered();
            if (!string.IsNullOrWhiteSpace(_config?.BackgroundImageUrl) && ImageLibrary == null)
                PrintWarning(
                    "Background Image Url ist gesetzt, aber ImageLibrary fehlt. Installiere ImageLibrary für zuverlässige Hintergrundbilder (Png-Cache). Direkte URLs zeigt der Client oft nicht an.");
        }

        private void OnPluginLoaded(Plugin plugin)
        {
            if (plugin?.Name != "ImageLibrary")
                return;
            _backgroundImageRegisteredForUrl = null;
            EnsureHelpBackgroundImageRegistered();
        }

        private void OnPlayerDisconnected(BasePlayer player, string reason)
        {
            if (player == null)
                return;
            _pendingDeletes.Remove(player.userID);
        }

        private void OnPlayerConnected(BasePlayer player)
        {
            if (player == null || player.IsNpc)
                return;
            if (!(_config?.OpenHelpOnConnect ?? true))
                return;

            var delay = Mathf.Clamp(_config.OpenHelpDelaySeconds ?? 2.5f, 0.5f, 30f);
            var userId = player.userID;
            timer.Once(delay, () =>
            {
                var p = BasePlayer.FindByID(userId);
                if (p == null || !p.IsConnected || p.IsNpc)
                    return;
                OpenUi(p, null, null);
            });
        }

        private void EnsureConfigDefaults()
        {
            var defaults = new PluginConfig();
            bool changed = false;

            if (_config.Commands == null)
            {
                _config.Commands = new Dictionary<string, string>();
                changed = true;
            }
            foreach (var kv in defaults.Commands)
            {
                if (!_config.Commands.ContainsKey(kv.Key))
                {
                    _config.Commands[kv.Key] = kv.Value;
                    changed = true;
                }
            }

            if (_config.BackgroundImageUrl == null)
            {
                _config.BackgroundImageUrl = defaults.BackgroundImageUrl;
                changed = true;
            }

            if (_config.OpenHelpOnConnect == null)
            {
                _config.OpenHelpOnConnect = true;
                changed = true;
            }

            if (_config.OpenHelpDelaySeconds == null)
            {
                _config.OpenHelpDelaySeconds = 2.5f;
                changed = true;
            }

            if (_config.MainCategoryOrder == null || _config.MainCategoryOrder.Count == 0)
            {
                _config.MainCategoryOrder = new List<string>(defaults.MainCategoryOrder);
                changed = true;
            }
            else
            {
                var normalized = new List<string>();
                foreach (var raw in _config.MainCategoryOrder)
                {
                    string key = (raw ?? string.Empty).Trim().ToLowerInvariant();
                    if (string.IsNullOrEmpty(key) || normalized.Contains(key))
                        continue;
                    normalized.Add(key);
                }
                foreach (var key in defaults.MainCategoryOrder)
                {
                    if (!normalized.Contains(key))
                        normalized.Add(key);
                }
                if (normalized.Count > MaxMainCategories)
                    normalized = normalized.Take(MaxMainCategories).ToList();

                if (_config.MainCategoryOrder.Count != normalized.Count ||
                    _config.MainCategoryOrder.Where((t, i) => i < normalized.Count && t != normalized[i]).Any())
                {
                    _config.MainCategoryOrder = normalized;
                    changed = true;
                }
            }

            if (_config.MainCategoryOrder != null &&
                _config.MainCategoryOrder.Any(x =>
                {
                    var k = (x ?? string.Empty).Trim().ToLowerInvariant();
                    return k == "ranks" || k == "wiki";
                }))
            {
                _config.MainCategoryOrder = new List<string>(defaults.MainCategoryOrder);
                changed = true;
            }

            if (_config.Pages == null)
            {
                _config.Pages = new Dictionary<string, CategoryConfig>();
                changed = true;
            }

            foreach (var cat in defaults.Pages)
            {
                if (!_config.Pages.TryGetValue(cat.Key, out var existingCat) || existingCat == null)
                {
                    _config.Pages[cat.Key] = cat.Value;
                    changed = true;
                    continue;
                }

                if (string.IsNullOrWhiteSpace(existingCat.Title))
                {
                    existingCat.Title = cat.Value.Title;
                    changed = true;
                }

                if (existingCat.Entries == null)
                {
                    existingCat.Entries = new Dictionary<string, PageConfig>();
                    changed = true;
                }

                foreach (var page in cat.Value.Entries)
                {
                    if (!existingCat.Entries.ContainsKey(page.Key))
                    {
                        existingCat.Entries[page.Key] = page.Value;
                        changed = true;
                    }
                }

                // Migration für bestehende Configs.
                foreach (var existingPage in existingCat.Entries)
                {
                    if (existingPage.Value == null)
                        continue;

                    if (cat.Key == "commands" && existingPage.Key == "allebefehle" &&
                        string.Equals(existingPage.Value.Content?.Trim(), "{{AUTO_COMMANDS}}", StringComparison.OrdinalIgnoreCase))
                    {
                        existingPage.Value.Content = "{{AUTO_COMMANDS_PUBLIC}}";
                        if (string.Equals(existingPage.Value.Title, "Alle Befehle (Auto)", StringComparison.OrdinalIgnoreCase))
                            existingPage.Value.Title = "Spielerbefehle (Auto)";
                        changed = true;
                    }

                    if (cat.Key == "commands" && existingPage.Key == "adminbefehle" && !existingPage.Value.AdminOnly)
                    {
                        existingPage.Value.AdminOnly = true;
                        changed = true;
                    }
                }
            }

            StripLegacyAutoCommandPages(ref changed);

            if (changed)
                SaveConfig();
        }

        private void StripLegacyAutoCommandPages(ref bool changed)
        {
            if (_config?.Pages == null)
                return;

            foreach (var catKvp in _config.Pages.ToList())
            {
                var entries = catKvp.Value?.Entries;
                if (entries == null)
                    continue;

                if (catKvp.Key == "commands")
                {
                    if (entries.Remove("allebefehle"))
                        changed = true;
                    if (entries.Remove("adminbefehle"))
                        changed = true;
                }

                if (catKvp.Key == "ranks" && entries.Remove("teamliste"))
                    changed = true;
            }
        }

        private void Unload()
        {
            foreach (var player in BasePlayer.activePlayerList)
            {
                CuiHelper.DestroyUi(player, UiNameMain);
                CuiHelper.DestroyUi(player, UiNameBg);
                CuiHelper.DestroyUi(player, UiNameEdit);
            }

            _helpBgActive.Clear();
            _uiStates.Clear();
            _editDrafts.Clear();
            _pendingDeletes.Clear();
            _backgroundImageRegisteredForUrl = null;
        }

        private void RegisterConfiguredCommands()
        {
            if (_config.Commands == null || _config.Commands.Count == 0)
                return;

            foreach (var cmd in _config.Commands.Keys.ToList())
            {
                if (string.IsNullOrWhiteSpace(cmd))
                    continue;
                AddCovalenceCommand(cmd.Trim().ToLowerInvariant(), nameof(CmdConfiguredHelp));
            }
        }

        private void CmdConfiguredHelp(IPlayer iPlayer, string command, string[] args)
        {
            var player = iPlayer.Object as BasePlayer;
            if (player == null)
                return;

            var cmd = (command ?? string.Empty).Trim().ToLowerInvariant();
            string target = string.Empty;
            if (_config.Commands != null && _config.Commands.TryGetValue(cmd, out var configured))
                target = configured ?? string.Empty;

            // Sonderfall /helppage <kategorie> <seite>
            if (cmd == "helppage" && args != null && args.Length > 0)
            {
                string cat = args[0].ToLowerInvariant();
                string page = args.Length > 1 ? args[1].ToLowerInvariant() : string.Empty;
                OpenUi(player, cat, page);
                return;
            }

            if (string.IsNullOrWhiteSpace(target))
            {
                OpenUi(player, null, null);
                return;
            }

            SplitTarget(target, out var category, out var pageKey);
            OpenUi(player, category, pageKey);
        }

        private void CmdHelpReload(IPlayer iPlayer, string command, string[] args)
        {
            if (iPlayer == null)
                return;

            bool allowed = iPlayer.HasPermission(PermAdmin) || iPlayer.IsAdmin;
            var basePlayer = iPlayer.Object as BasePlayer;
            if (!allowed && basePlayer != null)
                allowed = basePlayer.IsAdmin || permission.UserHasPermission(basePlayer.UserIDString, PermAdmin);

            if (!allowed)
            {
                iPlayer.Reply("Keine Berechtigung.");
                return;
            }

            try
            {
                LoadConfig();
                _backgroundImageRegisteredForUrl = null;
                RebuildDynamicCaches();
                EnsureHelpBackgroundImageRegistered();
                iPlayer.Reply("HelpCenterUI wurde neu geladen und aktualisiert.");
            }
            catch (Exception ex)
            {
                iPlayer.Reply("Fehler beim Reload: " + ex.Message);
            }
        }

        [ConsoleCommand("hcui.close")]
        private void CmdUiClose(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null) return;
            CloseUi(player);
        }

        [ConsoleCommand("hcui.main")]
        private void CmdUiMain(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null || arg.Args == null || arg.Args.Length < 1) return;

            string category = arg.Args[0].ToLowerInvariant();
            OpenUi(player, category, null);
        }

        [ConsoleCommand("hcui.page")]
        private void CmdUiPage(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null || arg.Args == null || arg.Args.Length < 2) return;

            string category = arg.Args[0].ToLowerInvariant();
            string page = arg.Args[1].ToLowerInvariant();
            OpenUi(player, category, page);
        }

        [ConsoleCommand("hcui.part")]
        private void CmdUiPart(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null || arg.Args == null || arg.Args.Length < 1) return;
            if (!_uiStates.TryGetValue(player.userID, out var state)) return;

            if (!int.TryParse(arg.Args[0], out int delta)) return;
            state.ContentPart += delta;
            if (state.ContentPart < 0) state.ContentPart = 0;

            OpenUi(player, state.CategoryKey, state.PageKey, false);
        }

        [ConsoleCommand("hcui.subpart")]
        private void CmdUiSubPart(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null || arg.Args == null || arg.Args.Length < 1) return;
            if (!_uiStates.TryGetValue(player.userID, out var state)) return;

            if (!int.TryParse(arg.Args[0], out int delta)) return;
            state.SubCategoryPage += delta;
            if (state.SubCategoryPage < 0) state.SubCategoryPage = 0;

            OpenUi(player, state.CategoryKey, state.PageKey, false);
        }

        [ConsoleCommand("hcui.print")]
        private void CmdUiPrint(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null) return;
            if (!_uiStates.TryGetValue(player.userID, out var state)) return;

            if (!TryGetPage(state.CategoryKey, state.PageKey, out var page))
                return;

            var rendered = RenderPageContent(page.Content);
            var lines = SplitLines(rendered);
            player.ChatMessage(T("ChatHeader", player.UserIDString, page.Title));
            foreach (var line in lines)
            {
                if (!string.IsNullOrWhiteSpace(line))
                    player.ChatMessage(line);
            }
        }

        private void EnsureHelpBackgroundImageRegistered()
        {
            var url = (_config?.BackgroundImageUrl ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(url) || ImageLibrary == null)
                return;
            if (string.Equals(_backgroundImageRegisteredForUrl, url, StringComparison.Ordinal))
                return;
            ImageLibrary.Call("AddImage", url, ImageLibraryBackgroundName, 0UL);
            _backgroundImageRegisteredForUrl = url;
        }

        private void AddFullscreenBackgroundLayers(CuiElementContainer ui, string bgParent)
        {
            var url = (_config?.BackgroundImageUrl ?? string.Empty).Trim();
            if (!string.IsNullOrEmpty(url))
            {
                var imgAlpha = Mathf.Clamp01(_config.BackgroundImageAlpha <= 0f ? 1f : _config.BackgroundImageAlpha);
                EnsureHelpBackgroundImageRegistered();

                var raw = new CuiRawImageComponent
                {
                    Sprite = FullscreenRawImageSprite,
                    Color = $"1 1 1 {imgAlpha:F2}"
                };

                var usePng = false;
                if (ImageLibrary != null && ImageLibrary.Call<bool>("HasImage", ImageLibraryBackgroundName, 0UL))
                {
                    var pngId = ImageLibrary.Call<string>("GetImage", ImageLibraryBackgroundName, 0UL);
                    if (!string.IsNullOrEmpty(pngId))
                    {
                        raw.Png = pngId;
                        usePng = true;
                    }
                }

                if (!usePng)
                    raw.Url = url;

                ui.Add(new CuiElement
                {
                    Name = CuiHelper.GetGuid(),
                    Parent = bgParent,
                    Components =
                    {
                        raw,
                        new CuiRectTransformComponent { AnchorMin = "0 0", AnchorMax = "1 1" }
                    }
                });
            }

            var darken = _config.BackgroundGlobalDarken;
            if (darken <= 0.001f)
                darken = 0.45f;
            darken = Mathf.Clamp(darken, 0f, 0.88f);
            ui.Add(new CuiPanel
            {
                Image = { Color = $"0 0 0 {darken:F2}" },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" }
            }, bgParent);
        }

        private void EnsureHelpBackground(BasePlayer player)
        {
            if (_config == null || !_config.FullscreenLayout || player == null)
                return;
            if (_helpBgActive.Contains(player.userID))
                return;

            var ui = new CuiElementContainer();
            ui.Add(new CuiPanel
            {
                Image = { Color = "0 0 0 0" },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" },
                CursorEnabled = false
            }, "Overlay", UiNameBg);

            AddFullscreenBackgroundLayers(ui, UiNameBg);
            CuiHelper.AddUi(player, ui);
            _helpBgActive.Add(player.userID);
        }

        private void DestroyHelpBg(BasePlayer player)
        {
            if (player == null)
                return;
            if (!_helpBgActive.Contains(player.userID))
                return;
            CuiHelper.DestroyUi(player, UiNameBg);
            _helpBgActive.Remove(player.userID);
        }

        private bool HasEditPermission(BasePlayer player)
        {
            if (player == null)
                return false;
            if (player.IsAdmin)
                return true;
            return permission.UserHasPermission(player.UserIDString, PermEdit);
        }

        private void OpenUi(BasePlayer player, string categoryKey, string pageKey, bool resetPart = true)
        {
            var mainKeys = GetMainCategoryKeys();
            if (mainKeys.Count == 0)
            {
                player.ChatMessage(T("NoCategories", player.UserIDString));
                return;
            }

            categoryKey = ResolveCategoryKey(categoryKey, mainKeys);
            pageKey = ResolvePageKey(categoryKey, pageKey, player);

            if (!_uiStates.TryGetValue(player.userID, out var state))
                _uiStates[player.userID] = state = new PlayerUiState();

            bool changedPage = state.PageKey != pageKey || state.CategoryKey != categoryKey;
            bool changedCategory = state.CategoryKey != categoryKey;
            state.CategoryKey = categoryKey;
            state.PageKey = pageKey;
            if (resetPart || changedPage)
                state.ContentPart = 0;
            if (changedCategory)
                state.SubCategoryPage = 0;

            if (!TryGetPage(categoryKey, pageKey, out var page) || !IsPageVisibleToPlayer(page, player))
                return;

            var renderedContent = RenderPageContent(page.Content);
            var parts = Paginate(renderedContent, ContentLinesPerPart);
            if (state.ContentPart >= parts.Count)
                state.ContentPart = parts.Count - 1;
            if (state.ContentPart < 0)
                state.ContentPart = 0;

            CuiHelper.DestroyUi(player, UiNameMain);
            CuiHelper.DestroyUi(player, UiNameEdit);

            var fullscreen = _config != null && _config.FullscreenLayout;
            if (fullscreen)
                EnsureHelpBackground(player);
            else
                DestroyHelpBg(player);

            var ui = new CuiElementContainer();
            if (fullscreen)
            {
                ui.Add(new CuiPanel
                {
                    Image = { Color = "0 0 0 0" },
                    RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" },
                    CursorEnabled = true
                }, "Overlay", UiNameMain);
            }
            else
            {
                ui.Add(new CuiPanel
                {
                    Image = { Color = "0.02 0.02 0.02 0.86" },
                    RectTransform = { AnchorMin = "0.03 0.05", AnchorMax = "0.97 0.95" },
                    CursorEnabled = true
                }, "Overlay", UiNameMain);
            }

            ui.Add(new CuiPanel
            {
                Image = { Color = "0.02 0.02 0.04 0.96" },
                RectTransform = { AnchorMin = "0.01 0.87", AnchorMax = "0.99 0.99" }
            }, UiNameMain);

            ui.Add(new CuiLabel
            {
                Text = { Text = "GAMEZONEONE · HILFE", FontSize = 28, Align = TextAnchor.MiddleLeft, Color = "1 0.82 0.45 1" },
                RectTransform = { AnchorMin = "0.025 0.925", AnchorMax = "0.72 0.985" }
            }, UiNameMain);

            if (HasEditPermission(player))
            {
                ui.Add(new CuiButton
                {
                    Button = { Color = "0.22 0.35 0.55 1", Command = "hcui.edit.open" },
                    Text = { Text = "BEARBEITEN", FontSize = 12, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                    RectTransform = { AnchorMin = "0.78 0.93", AnchorMax = "0.958 0.982" }
                }, UiNameMain);
            }

            ui.Add(new CuiButton
            {
                Button = { Color = "0.75 0.23 0.18 1", Command = "hcui.close" },
                Text = { Text = "✕", FontSize = 22, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                RectTransform = { AnchorMin = "0.965 0.93", AnchorMax = "0.992 0.982" }
            }, UiNameMain);

            ui.Add(new CuiPanel
            {
                Image = { Color = "0.06 0.06 0.08 0.90" },
                RectTransform = { AnchorMin = "0.018 0.12", AnchorMax = "0.305 0.86" }
            }, UiNameMain);

            ui.Add(new CuiPanel
            {
                Image = { Color = "0.06 0.06 0.08 0.90" },
                RectTransform = { AnchorMin = "0.315 0.12", AnchorMax = "0.982 0.86" }
            }, UiNameMain);

            BuildMainTabs(ui, mainKeys, categoryKey);
            BuildSubPages(ui, categoryKey, pageKey, state, player);
            BuildContent(ui, categoryKey, page, parts[state.ContentPart], state.ContentPart, parts.Count);

            CuiHelper.AddUi(player, ui);
        }

        private void BuildMainTabs(CuiElementContainer ui, List<string> mainKeys, string activeCategory)
        {
            float startX = 0.03f;
            float gap = 0.006f;
            float totalWidth = 0.94f - startX;
            float width = (totalWidth - ((mainKeys.Count - 1) * gap)) / Math.Max(1, mainKeys.Count);

            for (int i = 0; i < mainKeys.Count; i++)
            {
                string key = mainKeys[i];
                string title = GetCategoryTitle(key);
                bool active = key == activeCategory;

                float min = startX + i * (width + gap);
                float max = min + width;
                ui.Add(new CuiButton
                {
                    Button = { Color = active ? "0.55 0.32 0.08 1" : "0.10 0.10 0.10 0.98", Command = "hcui.main " + key },
                    Text = { Text = title.ToUpperInvariant(), FontSize = 16, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                    RectTransform = { AnchorMin = min.ToString("0.000") + " 0.80", AnchorMax = max.ToString("0.000") + " 0.885" }
                }, UiNameMain);
            }
        }

        private void BuildSubPages(CuiElementContainer ui, string categoryKey, string activePage, PlayerUiState state, BasePlayer player)
        {
            ui.Add(new CuiLabel
            {
                Text = { Text = "THEMEN", FontSize = 15, Align = TextAnchor.MiddleLeft, Color = "0.95 0.95 0.95 1" },
                RectTransform = { AnchorMin = "0.035 0.74", AnchorMax = "0.29 0.79" }
            }, UiNameMain);

            var pages = GetPageKeys(categoryKey, player);
            if (pages.Count == 0) return;

            int maxPage = (int)Math.Ceiling(pages.Count / (float)SubCategoriesPerPage) - 1;
            if (maxPage < 0) maxPage = 0;
            if (state.SubCategoryPage > maxPage) state.SubCategoryPage = maxPage;

            int start = state.SubCategoryPage * SubCategoriesPerPage;
            int end = Math.Min(start + SubCategoriesPerPage, pages.Count);

            float yTop = 0.72f;
            float rowHeight = 0.046f;

            for (int i = start; i < end; i++)
            {
                string pageKey = pages[i];
                var page = _config.Pages[categoryKey].Entries[pageKey];
                bool active = pageKey == activePage;
                int row = i - start;

                float max = yTop - row * rowHeight;
                float min = max - (rowHeight - 0.004f);
                if (min < 0.14f) break;

                ui.Add(new CuiButton
                {
                    Button = { Color = active ? "0.55 0.32 0.08 1" : "0.07 0.07 0.07 0.95", Command = $"hcui.page {categoryKey} {pageKey}" },
                    Text = { Text = page.Title.ToUpperInvariant(), FontSize = 14, Align = TextAnchor.MiddleLeft, Color = "1 1 1 1" },
                    RectTransform = { AnchorMin = $"0.035 {min:0.000}", AnchorMax = $"0.295 {max:0.000}" }
                }, UiNameMain);
            }

            if (maxPage > 0)
            {
                ui.Add(new CuiLabel
                {
                    Text = { Text = $"Seite {state.SubCategoryPage + 1}/{maxPage + 1}", FontSize = 12, Align = TextAnchor.MiddleCenter, Color = "0.9 0.9 0.9 1" },
                    RectTransform = { AnchorMin = "0.11 0.12", AnchorMax = "0.23 0.15" }
                }, UiNameMain);

                ui.Add(new CuiButton
                {
                    Button = { Color = "0.16 0.16 0.16 0.95", Command = "hcui.subpart -1" },
                    Text = { Text = "<", FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                    RectTransform = { AnchorMin = "0.04 0.12", AnchorMax = "0.09 0.15" }
                }, UiNameMain);

                ui.Add(new CuiButton
                {
                    Button = { Color = "0.16 0.16 0.16 0.95", Command = "hcui.subpart 1" },
                    Text = { Text = ">", FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                    RectTransform = { AnchorMin = "0.245 0.12", AnchorMax = "0.295 0.15" }
                }, UiNameMain);
            }
        }

        private void BuildContent(CuiElementContainer ui, string categoryKey, PageConfig page, string partText, int partIndex, int totalParts)
        {
            ui.Add(new CuiLabel
            {
                Text = { Text = GetCategoryTitle(categoryKey).ToUpperInvariant(), FontSize = 14, Align = TextAnchor.MiddleLeft, Color = "0.82 0.84 0.88 1" },
                RectTransform = { AnchorMin = "0.33 0.752", AnchorMax = "0.96 0.796" }
            }, UiNameMain);

            ui.Add(new CuiLabel
            {
                Text = { Text = page.Title.ToUpperInvariant(), FontSize = 16, Align = TextAnchor.MiddleLeft, Color = "1 0.88 0.45 1" },
                RectTransform = { AnchorMin = "0.33 0.704", AnchorMax = "0.96 0.746" }
            }, UiNameMain);

            float minY = MainContentMinY;
            float maxY = MainContentMaxY;
            if (page.IncreaseHeightValue != 0)
            {
                float expand = Mathf.Clamp(page.IncreaseHeightValue / 1000f, -0.08f, 0.12f);
                maxY = Mathf.Clamp(maxY + expand, 0.58f, MainContentMaxYHardCap);
            }

            ui.Add(new CuiPanel
            {
                Image = { Color = "0.05 0.05 0.07 0.93" },
                RectTransform = { AnchorMin = $"0.327 {minY:0.000}", AnchorMax = $"0.965 {maxY:0.000}" }
            }, UiNameMain, UiNameMain + ".Content");

            ui.Add(new CuiLabel
            {
                Text =
                {
                    Text = partText,
                    Font = "robotocondensed-regular.ttf",
                    FontSize = Mathf.Clamp(page.FontSize, 12, 24),
                    Align = page.Center ? TextAnchor.UpperCenter : TextAnchor.UpperLeft,
                    Color = "0.95 0.95 0.95 1",
                    VerticalOverflow = VerticalWrapMode.Overflow
                },
                RectTransform = { AnchorMin = "0.01 0.03", AnchorMax = "0.99 0.97" }
            }, UiNameMain + ".Content");

            if (totalParts > 1)
            {
                ui.Add(new CuiLabel
                {
                    Text = { Text = $"Seite {partIndex + 1}/{totalParts}", FontSize = 13, Align = TextAnchor.MiddleCenter, Color = "0.9 0.9 0.9 1" },
                    RectTransform = { AnchorMin = "0.57 0.055", AnchorMax = "0.70 0.085" }
                }, UiNameMain);

                ui.Add(new CuiButton
                {
                    Button = { Color = "0.20 0.20 0.20 0.95", Command = "hcui.part -1" },
                    Text = { Text = "<", FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                    RectTransform = { AnchorMin = "0.52 0.055", AnchorMax = "0.56 0.085" }
                }, UiNameMain);

                ui.Add(new CuiButton
                {
                    Button = { Color = "0.20 0.20 0.20 0.95", Command = "hcui.part 1" },
                    Text = { Text = ">", FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                    RectTransform = { AnchorMin = "0.71 0.055", AnchorMax = "0.75 0.085" }
                }, UiNameMain);
            }

            ui.Add(new CuiButton
            {
                Button = { Color = "0.18 0.40 0.62 1", Command = "hcui.print" },
                Text = { Text = "IN CHAT ANZEIGEN", FontSize = 13, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                RectTransform = { AnchorMin = "0.79 0.055", AnchorMax = "0.96 0.085" }
            }, UiNameMain);
        }

        private void CloseUi(BasePlayer player)
        {
            CuiHelper.DestroyUi(player, UiNameMain);
            CuiHelper.DestroyUi(player, UiNameEdit);
            DestroyHelpBg(player);
            _uiStates.Remove(player.userID);
            _editDrafts.Remove(player.userID);
        }

        private void CmdHelpEdit(IPlayer iPlayer, string command, string[] args)
        {
            var player = iPlayer?.Object as BasePlayer;
            if (player == null)
                return;
            if (!HasEditPermission(player))
            {
                iPlayer.Reply("Keine Berechtigung. Benötigt: helpcenterui.edit (oder Admin).");
                return;
            }

            if (args != null && args.Length >= 1)
            {
                var sub = (args[0] ?? string.Empty).Trim().ToLowerInvariant();
                if (sub == "newpage" && args.Length >= 3)
                {
                    var title = args.Length > 3 ? string.Join(" ", args.Skip(3)) : null;
                    TryCreateNewPage(player, args[1].Trim().ToLowerInvariant(), args[2].Trim().ToLowerInvariant(), title, iPlayer);
                    return;
                }

                if (sub == "newcategory" && args.Length >= 2)
                {
                    var title = args.Length > 2 ? string.Join(" ", args.Skip(2)) : null;
                    TryCreateNewCategory(player, args[1].Trim().ToLowerInvariant(), title, iPlayer);
                    return;
                }

                if (sub == "delpage" && args.Length >= 3)
                {
                    TryDeletePage(player, args[1].Trim().ToLowerInvariant(), args[2].Trim().ToLowerInvariant(), iPlayer);
                    return;
                }

                if (sub == "delcategory" && args.Length >= 2)
                {
                    TryDeleteCategory(player, args[1].Trim().ToLowerInvariant(), iPlayer);
                    return;
                }
            }

            string cat = null;
            string page = null;
            if (args != null && args.Length >= 2)
            {
                cat = args[0].Trim().ToLowerInvariant();
                page = args[1].Trim().ToLowerInvariant();
            }
            else if (_uiStates.TryGetValue(player.userID, out var st))
            {
                cat = st.CategoryKey;
                page = st.PageKey;
            }

            if (string.IsNullOrEmpty(cat) || string.IsNullOrEmpty(page))
            {
                var mk = GetMainCategoryKeys();
                if (mk.Count == 0)
                {
                    iPlayer.Reply("Keine Kategorien.");
                    return;
                }

                cat = mk[0];
                var pk = GetPageKeys(cat, player);
                if (pk.Count == 0)
                {
                    iPlayer.Reply("Keine Seiten in dieser Kategorie.");
                    return;
                }

                page = pk[0];
            }

            OpenEditUi(player, cat, page);
        }

        [ConsoleCommand("hcui.edit.open")]
        private void CmdEditOpen(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null || !HasEditPermission(player))
                return;

            string cat = null;
            string page = null;
            if (_uiStates.TryGetValue(player.userID, out var st))
            {
                cat = st.CategoryKey;
                page = st.PageKey;
            }

            if (string.IsNullOrEmpty(cat) || string.IsNullOrEmpty(page))
            {
                var mk = GetMainCategoryKeys();
                if (mk.Count == 0)
                    return;
                cat = mk[0];
                var pk = GetPageKeys(cat, player);
                if (pk.Count == 0)
                    return;
                page = pk[0];
            }

            OpenEditUi(player, cat, page);
        }

        private void OpenEditUi(BasePlayer player, string categoryKey, string pageKey)
        {
            if (!HasEditPermission(player))
                return;
            if (!TryGetPage(categoryKey, pageKey, out var pageCfg) || !IsPageVisibleToPlayer(pageCfg, player))
            {
                player.ChatMessage(T("PageNotFound", player.UserIDString));
                return;
            }

            if (pageCfg.AdminOnly && !player.IsAdmin)
            {
                player.ChatMessage(T("AdminOnly", player.UserIDString));
                return;
            }

            CuiHelper.DestroyUi(player, UiNameMain);
            CuiHelper.DestroyUi(player, UiNameEdit);

            var draft = new EditDraft
            {
                CategoryKey = categoryKey,
                PageKey = pageKey,
                Title = pageCfg.Title ?? string.Empty,
                Content = pageCfg.Content ?? string.Empty
            };
            _editDrafts[player.userID] = draft;

            var c = new CuiElementContainer();
            c.Add(new CuiPanel
            {
                Image = { Color = "0.04 0.04 0.06 0.97" },
                RectTransform = { AnchorMin = "0 0", AnchorMax = "1 1" },
                CursorEnabled = true
            }, "Overlay", UiNameEdit);

            c.Add(new CuiLabel
            {
                Text =
                {
                    Text = "HILFE-EDITOR · " + categoryKey + " / " + pageKey,
                    FontSize = 22,
                    Align = TextAnchor.MiddleLeft,
                    Color = "1 0.85 0.45 1"
                },
                RectTransform = { AnchorMin = "0.04 0.88", AnchorMax = "0.62 0.96" }
            }, UiNameEdit);

            c.Add(new CuiButton
            {
                Button = { Color = "0.18 0.42 0.32 1", Command = "hcui.edit.newpage" },
                Text = { Text = "Neue Seite", FontSize = 12, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                RectTransform = { AnchorMin = "0.63 0.89", AnchorMax = "0.738 0.955" }
            }, UiNameEdit);

            c.Add(new CuiButton
            {
                Button = { Color = "0.28 0.32 0.48 1", Command = "hcui.edit.newcategory" },
                Text = { Text = "Neue Kategorie", FontSize = 11, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                RectTransform = { AnchorMin = "0.744 0.89", AnchorMax = "0.872 0.955" }
            }, UiNameEdit);

            c.Add(new CuiButton
            {
                Button = { Color = "0.75 0.23 0.18 1", Command = "hcui.edit.cancel" },
                Text = { Text = "✕", FontSize = 20, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                RectTransform = { AnchorMin = "0.878 0.89", AnchorMax = "0.985 0.965" }
            }, UiNameEdit);

            c.Add(new CuiLabel
            {
                Text = { Text = "Titel (Tab-Name in der Liste)", FontSize = 14, Align = TextAnchor.MiddleLeft, Color = "0.85 0.88 0.95 1" },
                RectTransform = { AnchorMin = "0.04 0.80", AnchorMax = "0.55 0.845" }
            }, UiNameEdit);

            c.Add(new CuiPanel
            {
                Image = { Color = "0.1 0.1 0.12 1" },
                RectTransform = { AnchorMin = "0.04 0.735", AnchorMax = "0.96 0.795" }
            }, UiNameEdit, UiNameEdit + ".TitleBg");
            c.Add(new CuiElement
            {
                Name = "EditTitleField",
                Parent = UiNameEdit + ".TitleBg",
                Components =
                {
                    new CuiInputFieldComponent
                    {
                        Align = TextAnchor.MiddleLeft,
                        CharsLimit = 120,
                        Command = "hcui.edit.settitle ",
                        FontSize = 16,
                        IsPassword = false,
                        Text = draft.Title,
                        NeedsKeyboard = true,
                        Color = "0.95 0.95 0.95 1"
                    },
                    new CuiRectTransformComponent { AnchorMin = "0.02 0.1", AnchorMax = "0.98 0.9" }
                }
            });

            c.Add(new CuiLabel
            {
                Text = { Text = "Inhalt (mehrzeilig; Zeilenumbrüche bleiben erhalten)", FontSize = 14, Align = TextAnchor.MiddleLeft, Color = "0.85 0.88 0.95 1" },
                RectTransform = { AnchorMin = "0.04 0.665", AnchorMax = "0.92 0.715" }
            }, UiNameEdit);

            c.Add(new CuiPanel
            {
                Image = { Color = "0.08 0.08 0.1 1" },
                RectTransform = { AnchorMin = "0.04 0.175", AnchorMax = "0.96 0.655" }
            }, UiNameEdit, UiNameEdit + ".BodyBg");
            c.Add(new CuiElement
            {
                Name = "EditContentField",
                Parent = UiNameEdit + ".BodyBg",
                Components =
                {
                    new CuiInputFieldComponent
                    {
                        Align = TextAnchor.UpperLeft,
                        CharsLimit = 8000,
                        Command = "hcui.edit.setcontent ",
                        Font = "robotocondensed-regular.ttf",
                        FontSize = 14,
                        LineType = InputField.LineType.MultiLineNewline,
                        IsPassword = false,
                        Text = draft.Content,
                        NeedsKeyboard = true,
                        Color = "0.92 0.92 0.92 1"
                    },
                    new CuiRectTransformComponent { AnchorMin = "0.015 0.03", AnchorMax = "0.985 0.97" }
                }
            });

            c.Add(new CuiLabel
            {
                Text =
                {
                    Text = "Löschen: Button zweimal innerhalb von " + (int)PendingDeleteConfirmSeconds + " s drücken · Chat: /helpedit delpage <kat> <seite> · /helpedit delcategory <kat>",
                    FontSize = 10,
                    Align = TextAnchor.MiddleLeft,
                    Color = "0.7 0.55 0.55 1"
                },
                RectTransform = { AnchorMin = "0.04 0.098", AnchorMax = "0.96 0.128" }
            }, UiNameEdit);

            c.Add(new CuiButton
            {
                Button = { Color = "0.55 0.28 0.18 1", Command = "hcui.edit.delpage" },
                Text = { Text = "SEITE LÖSCHEN", FontSize = 12, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                RectTransform = { AnchorMin = "0.04 0.03", AnchorMax = "0.30 0.088" }
            }, UiNameEdit);

            c.Add(new CuiButton
            {
                Button = { Color = "0.42 0.14 0.14 1", Command = "hcui.edit.delcategory" },
                Text = { Text = "KATEGORIE LÖSCHEN", FontSize = 11, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                RectTransform = { AnchorMin = "0.31 0.03", AnchorMax = "0.58 0.088" }
            }, UiNameEdit);

            c.Add(new CuiButton
            {
                Button = { Color = "0.2 0.55 0.32 1", Command = "hcui.edit.save" },
                Text = { Text = "SPEICHERN → HelpCenterUI.json", FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                RectTransform = { AnchorMin = "0.59 0.03", AnchorMax = "0.82 0.088" }
            }, UiNameEdit);

            c.Add(new CuiButton
            {
                Button = { Color = "0.25 0.25 0.28 1", Command = "hcui.edit.cancel" },
                Text = { Text = "ABBRECHEN", FontSize = 14, Align = TextAnchor.MiddleCenter, Color = "1 1 1 1" },
                RectTransform = { AnchorMin = "0.83 0.03", AnchorMax = "0.97 0.088" }
            }, UiNameEdit);

            c.Add(new CuiLabel
            {
                Text =
                {
                    Text = "Änderungen gelten sofort; helpreload nur nötig, wenn du die JSON extern geändert hast.",
                    FontSize = 10,
                    Align = TextAnchor.MiddleLeft,
                    Color = "0.65 0.7 0.8 1"
                },
                RectTransform = { AnchorMin = "0.04 0.128", AnchorMax = "0.96 0.168" }
            }, UiNameEdit);

            CuiHelper.AddUi(player, c);
        }

        [ConsoleCommand("hcui.edit.settitle")]
        private void CmdEditSetTitle(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null || !HasEditPermission(player))
                return;
            if (!_editDrafts.TryGetValue(player.userID, out var d))
                return;
            d.Title = JoinConsoleArgs(arg);
        }

        [ConsoleCommand("hcui.edit.setcontent")]
        private void CmdEditSetContent(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null || !HasEditPermission(player))
                return;
            if (!_editDrafts.TryGetValue(player.userID, out var d))
                return;
            d.Content = JoinConsoleArgsMultiline(arg);
        }

        /// <summary>
        /// Einzeilig (Titel): mehrere Args = Leerzeichen dazwischen.
        /// </summary>
        private static string JoinConsoleArgs(ConsoleSystem.Arg arg)
        {
            if (arg.Args == null || arg.Args.Length == 0)
                return string.Empty;
            if (arg.Args.Length == 1)
                return arg.Args[0];
            return string.Join(" ", arg.Args);
        }

        /// <summary>
        /// Mehrzeilig: ein Arg oft kompletter Text mit \n; sonst eine Zeile pro Arg vom Client.
        /// </summary>
        private static string JoinConsoleArgsMultiline(ConsoleSystem.Arg arg)
        {
            if (arg.Args == null || arg.Args.Length == 0)
                return string.Empty;
            if (arg.Args.Length == 1)
                return arg.Args[0];
            return string.Join("\n", arg.Args);
        }

        private static bool IsValidHelpSlug(string raw)
        {
            if (string.IsNullOrEmpty(raw))
                return false;
            var s = raw.Trim().ToLowerInvariant();
            if (s.Length < 2 || s.Length > 48)
                return false;
            foreach (char c in s)
            {
                if (c >= 'a' && c <= 'z')
                    continue;
                if (c >= '0' && c <= '9')
                    continue;
                if (c == '-' || c == '_')
                    continue;
                return false;
            }
            return true;
        }

        private string GenerateUniquePageKey(string categoryKey)
        {
            if (_config.Pages == null || !_config.Pages.TryGetValue(categoryKey, out var cat) || cat.Entries == null)
                return "seite_" + UnityEngine.Random.Range(10000, 99999);

            for (int i = 0; i < 64; i++)
            {
                var candidate = "seite_" + UnityEngine.Random.Range(10000, 99999);
                if (!cat.Entries.Keys.Any(k => string.Equals(k, candidate, StringComparison.OrdinalIgnoreCase)))
                    return candidate;
            }

            return "seite_" + DateTime.UtcNow.Ticks;
        }

        private void NotifyEditor(BasePlayer player, IPlayer iPlayer, string message)
        {
            if (iPlayer != null)
                iPlayer.Reply(message);
            else if (player != null)
                player.ChatMessage(message);
        }

        private void TryCreateNewPage(BasePlayer player, string categoryKey, string pageKey, string titleOpt, IPlayer iPlayer)
        {
            if (player == null || _config.Pages == null)
                return;
            categoryKey = (categoryKey ?? string.Empty).Trim().ToLowerInvariant();
            pageKey = (pageKey ?? string.Empty).Trim().ToLowerInvariant();

            if (!IsValidHelpSlug(categoryKey) || !IsValidHelpSlug(pageKey))
            {
                NotifyEditor(player, iPlayer,
                    "Ungültiger Name: nur Kleinbuchstaben, Ziffern, - und _, Länge 2–48. Beispiel: /helpedit newpage server regeln");
                return;
            }

            if (!_config.Pages.TryGetValue(categoryKey, out var category) || category?.Entries == null)
            {
                NotifyEditor(player, iPlayer, "Kategorie nicht gefunden: " + categoryKey + " – zuerst /helpedit newcategory " + categoryKey);
                return;
            }

            if (category.Entries.Keys.Any(k => string.Equals(k, pageKey, StringComparison.OrdinalIgnoreCase)))
            {
                NotifyEditor(player, iPlayer, "Seite existiert bereits: " + categoryKey + " / " + pageKey);
                return;
            }

            var title = string.IsNullOrWhiteSpace(titleOpt) ? pageKey : titleOpt.Trim();
            category.Entries[pageKey] = new PageConfig
            {
                Title = title,
                Content = "Neuer Inhalt – hier Text einfügen.\n\nZeilenumbrüche mit Enter im Editor.\n\nHinweis: Rust-CUI zeigt meist nur Klartext; farbige Tags siehst du u. U. nur in der Chat-Ausgabe (Button unten).",
                FontSize = 15,
                Center = false,
                IncreaseHeightValue = 0,
                AdminOnly = false
            };

            SaveConfig();
            NotifyEditor(player, iPlayer, "Neue Seite angelegt: " + categoryKey + " / " + pageKey);
            OpenEditUi(player, categoryKey, pageKey);
        }

        private void TryCreateNewCategory(BasePlayer player, string categoryKey, string titleOpt, IPlayer iPlayer)
        {
            if (player == null || _config.Pages == null)
                return;
            categoryKey = (categoryKey ?? string.Empty).Trim().ToLowerInvariant();

            if (!IsValidHelpSlug(categoryKey))
            {
                NotifyEditor(player, iPlayer, "Ungültiger Kategoriename (a–z, 0–9, -, _). Beispiel: /helpedit newcategory extras");
                return;
            }

            if (_config.Pages.ContainsKey(categoryKey))
            {
                NotifyEditor(player, iPlayer, "Kategorie existiert bereits: " + categoryKey);
                return;
            }

            var title = string.IsNullOrWhiteSpace(titleOpt) ? char.ToUpper(categoryKey[0]) + categoryKey.Substring(1) : titleOpt.Trim();

            _config.Pages[categoryKey] = new CategoryConfig
            {
                Title = title,
                Entries = new Dictionary<string, PageConfig>
                {
                    ["welcome"] = new PageConfig
                    {
                        Title = "Willkommen",
                        Content = "Neue Kategorie.\n\nText hier oder im Editor anpassen.",
                        FontSize = 15,
                        Center = false,
                        IncreaseHeightValue = 0,
                        AdminOnly = false
                    }
                }
            };

            if (_config.MainCategoryOrder == null)
                _config.MainCategoryOrder = new List<string>();

            if (_config.MainCategoryOrder.Count < MaxMainCategories &&
                !_config.MainCategoryOrder.Any(x => string.Equals(x, categoryKey, StringComparison.OrdinalIgnoreCase)))
                _config.MainCategoryOrder.Add(categoryKey);
            else if (!_config.MainCategoryOrder.Any(x => string.Equals(x, categoryKey, StringComparison.OrdinalIgnoreCase)))
                NotifyEditor(player, iPlayer,
                    "Hinweis: \"Main Category Order\" hat bereits 5 Einträge – neue Kategorie ist in der JSON sichtbar; ggf. Reihenfolge anpassen.");

            SaveConfig();
            NotifyEditor(player, iPlayer, "Neue Kategorie angelegt: " + categoryKey);
            OpenEditUi(player, categoryKey, "welcome");
        }

        private bool MatchesPendingDelete(ulong userId, string kind, string cat, string page)
        {
            if (!_pendingDeletes.TryGetValue(userId, out var p))
                return false;
            if (p.Kind != kind)
                return false;
            if (!string.Equals(p.CategoryKey, cat, StringComparison.OrdinalIgnoreCase))
                return false;
            if (kind == "page" &&
                !string.Equals(p.PageKey ?? string.Empty, page ?? string.Empty, StringComparison.OrdinalIgnoreCase))
                return false;
            return UnityEngine.Time.realtimeSinceStartup - p.Time <= PendingDeleteConfirmSeconds;
        }

        private void SetPendingDelete(ulong userId, string kind, string cat, string page)
        {
            _pendingDeletes[userId] = new PendingDeleteConfirm
            {
                Kind = kind,
                CategoryKey = cat,
                PageKey = page,
                Time = UnityEngine.Time.realtimeSinceStartup
            };
        }

        private void TryDeletePage(BasePlayer player, string categoryKey, string pageKey, IPlayer iPlayer)
        {
            if (player == null || _config.Pages == null)
                return;
            categoryKey = (categoryKey ?? string.Empty).Trim().ToLowerInvariant();
            pageKey = (pageKey ?? string.Empty).Trim().ToLowerInvariant();

            var catKeyExact = _config.Pages.Keys.FirstOrDefault(k => string.Equals(k, categoryKey, StringComparison.OrdinalIgnoreCase));
            if (catKeyExact == null || !_config.Pages.TryGetValue(catKeyExact, out var cat) || cat?.Entries == null)
            {
                NotifyEditor(player, iPlayer, "Seite nicht gefunden.");
                return;
            }

            var pageKeyExact = cat.Entries.Keys.FirstOrDefault(k => string.Equals(k, pageKey, StringComparison.OrdinalIgnoreCase));
            if (pageKeyExact == null || !cat.Entries.TryGetValue(pageKeyExact, out var pageCfg))
            {
                NotifyEditor(player, iPlayer, "Seite nicht gefunden.");
                return;
            }

            if (!IsPageVisibleToPlayer(pageCfg, player))
            {
                NotifyEditor(player, iPlayer, "Keine Berechtigung für diese Seite.");
                return;
            }

            if (pageCfg.AdminOnly && !player.IsAdmin)
            {
                NotifyEditor(player, iPlayer, "Nur Server-Admins dürfen diese Seite löschen.");
                return;
            }

            if (cat.Entries.Count <= 1)
            {
                NotifyEditor(player, iPlayer,
                    "Letzte Seite in dieser Kategorie – zuerst neue Seite anlegen oder Kategorie löschen (/helpedit delcategory " + catKeyExact + ").");
                return;
            }

            cat.Entries.Remove(pageKeyExact);
            SaveConfig();
            _editDrafts.Remove(player.userID);
            _pendingDeletes.Remove(player.userID);

            NotifyEditor(player, iPlayer, "Seite gelöscht: " + catKeyExact + " / " + pageKeyExact);

            CuiHelper.DestroyUi(player, UiNameEdit);
            var next = GetPageKeys(catKeyExact, player).FirstOrDefault();
            if (!string.IsNullOrEmpty(next))
                OpenUi(player, catKeyExact, next);
            else
                OpenUi(player, null, null);
        }

        private void TryDeleteCategory(BasePlayer player, string categoryKey, IPlayer iPlayer)
        {
            if (player == null || _config.Pages == null)
                return;
            categoryKey = (categoryKey ?? string.Empty).Trim().ToLowerInvariant();

            var catKeyExact = _config.Pages.Keys.FirstOrDefault(k => string.Equals(k, categoryKey, StringComparison.OrdinalIgnoreCase));
            if (catKeyExact == null || !_config.Pages.TryGetValue(catKeyExact, out var cat) || cat?.Entries == null)
            {
                NotifyEditor(player, iPlayer, "Kategorie nicht gefunden.");
                return;
            }

            foreach (var kv in cat.Entries)
            {
                if (kv.Value != null && kv.Value.AdminOnly && !player.IsAdmin)
                {
                    NotifyEditor(player, iPlayer, "Kategorie enthält Admin-Seiten – nur Server-Admin kann die Kategorie löschen.");
                    return;
                }
            }

            if (_config.Pages.Count <= 1)
            {
                NotifyEditor(player, iPlayer, "Die letzte verbleibende Kategorie kann nicht gelöscht werden.");
                return;
            }

            _config.Pages.Remove(catKeyExact);
            if (_config.MainCategoryOrder != null)
            {
                for (int i = _config.MainCategoryOrder.Count - 1; i >= 0; i--)
                {
                    if (string.Equals(_config.MainCategoryOrder[i], catKeyExact, StringComparison.OrdinalIgnoreCase))
                        _config.MainCategoryOrder.RemoveAt(i);
                }
            }

            SaveConfig();
            _editDrafts.Remove(player.userID);
            _pendingDeletes.Remove(player.userID);

            NotifyEditor(player, iPlayer, "Kategorie gelöscht: " + catKeyExact);

            CuiHelper.DestroyUi(player, UiNameEdit);
            var mk = GetMainCategoryKeys();
            if (mk.Count == 0)
            {
                player.ChatMessage(T("NoCategories", player.UserIDString));
                return;
            }

            foreach (var c in mk)
            {
                var pk = GetPageKeys(c, player);
                if (pk.Count > 0)
                {
                    OpenUi(player, c, pk[0]);
                    return;
                }
            }

            player.ChatMessage(T("NoPageVisible", player.UserIDString));
        }

        [ConsoleCommand("hcui.edit.delpage")]
        private void CmdEditDelPage(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null || !HasEditPermission(player))
                return;
            if (!_editDrafts.TryGetValue(player.userID, out var d))
                return;

            if (!MatchesPendingDelete(player.userID, "page", d.CategoryKey, d.PageKey))
            {
                SetPendingDelete(player.userID, "page", d.CategoryKey, d.PageKey);
                player.ChatMessage(T(“ConfirmDelPage”, player.UserIDString, (int)PendingDeleteConfirmSeconds));
                return;
            }

            _pendingDeletes.Remove(player.userID);
            TryDeletePage(player, d.CategoryKey, d.PageKey, null);
        }

        [ConsoleCommand("hcui.edit.delcategory")]
        private void CmdEditDelCategory(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null || !HasEditPermission(player))
                return;
            if (!_editDrafts.TryGetValue(player.userID, out var d))
                return;

            if (!MatchesPendingDelete(player.userID, "cat", d.CategoryKey, null))
            {
                SetPendingDelete(player.userID, "cat", d.CategoryKey, null);
                player.ChatMessage(T(“ConfirmDelCat”, player.UserIDString, (int)PendingDeleteConfirmSeconds));
                return;
            }

            _pendingDeletes.Remove(player.userID);
            TryDeleteCategory(player, d.CategoryKey, null);
        }

        [ConsoleCommand("hcui.edit.newpage")]
        private void CmdEditNewPageConsole(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null || !HasEditPermission(player))
                return;

            string cat;
            string pageKey;
            if (arg.Args != null && arg.Args.Length >= 2)
            {
                cat = arg.Args[0].Trim().ToLowerInvariant();
                pageKey = arg.Args[1].Trim().ToLowerInvariant();
            }
            else if (_editDrafts.TryGetValue(player.userID, out var draft))
            {
                cat = draft.CategoryKey;
                pageKey = GenerateUniquePageKey(cat);
            }
            else if (_uiStates.TryGetValue(player.userID, out var st))
            {
                cat = st.CategoryKey;
                pageKey = GenerateUniquePageKey(cat);
            }
            else
            {
                player.ChatMessage(T("OpenHelpFirst", player.UserIDString));
                return;
            }

            TryCreateNewPage(player, cat, pageKey, null, null);
        }

        [ConsoleCommand("hcui.edit.newcategory")]
        private void CmdEditNewCategoryConsole(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null || !HasEditPermission(player))
                return;

            if (arg.Args != null && arg.Args.Length >= 1)
            {
                var catKey = arg.Args[0].Trim().ToLowerInvariant();
                var title = arg.Args.Length > 1 ? string.Join(" ", arg.Args.Skip(1)) : null;
                TryCreateNewCategory(player, catKey, title, null);
                return;
            }

            var generated = "kat_" + UnityEngine.Random.Range(10000, 99999);
            TryCreateNewCategory(player, generated, null, null);
        }

        [ConsoleCommand("hcui.edit.save")]
        private void CmdEditSave(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null || !HasEditPermission(player))
                return;
            if (!_editDrafts.TryGetValue(player.userID, out var d))
                return;
            if (!TryGetPage(d.CategoryKey, d.PageKey, out var pageCfg) || !IsPageVisibleToPlayer(pageCfg, player))
            {
                player.ChatMessage(T("PageInvalid", player.UserIDString));
                return;
            }

            if (pageCfg.AdminOnly && !player.IsAdmin)
                return;

            pageCfg.Title = (d.Title ?? string.Empty).Trim();
            pageCfg.Content = d.Content ?? string.Empty;
            SaveConfig();
            player.ChatMessage(T("HelpSaved", player.UserIDString, d.CategoryKey, d.PageKey));
            CuiHelper.DestroyUi(player, UiNameEdit);
            OpenUi(player, d.CategoryKey, d.PageKey);
        }

        [ConsoleCommand("hcui.edit.cancel")]
        private void CmdEditCancel(ConsoleSystem.Arg arg)
        {
            var player = arg.Player();
            if (player == null)
                return;
            CuiHelper.DestroyUi(player, UiNameEdit);
            _editDrafts.Remove(player.userID);
            if (_uiStates.TryGetValue(player.userID, out var st))
                OpenUi(player, st.CategoryKey, st.PageKey);
            else
                OpenUi(player, null, null);
        }

        private string RenderPageContent(string raw)
        {
            string content = raw ?? string.Empty;
            if (content.Contains("{{AUTO_COMMANDS}}"))
                content = content.Replace("{{AUTO_COMMANDS}}", _cachedPublicCommandsText ?? string.Empty);
            if (content.Contains("{{AUTO_COMMANDS_PUBLIC}}"))
                content = content.Replace("{{AUTO_COMMANDS_PUBLIC}}", _cachedPublicCommandsText ?? string.Empty);
            if (content.Contains("{{AUTO_COMMANDS_INTERNAL}}"))
                content = content.Replace("{{AUTO_COMMANDS_INTERNAL}}", _cachedInternalCommandsText ?? string.Empty);
            if (content.Contains("{{TEAM_ROSTER}}"))
                content = content.Replace("{{TEAM_ROSTER}}", _cachedTeamRosterText ?? string.Empty);
            return content;
        }

        private void RebuildDynamicCaches()
        {
            _cachedPublicCommandsText = BuildCommandsIndexFromPlugins(includeInternal: false);
            _cachedInternalCommandsText = BuildCommandsIndexFromPlugins(includeInternal: true);
            _cachedTeamRosterText = BuildTeamRosterText();
        }

        private string BuildCommandsIndexFromPlugins(bool includeInternal)
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine(includeInternal
                    ? "AUTO-LISTE INTERNE/ADMIN BEFEHLE (PLUGIN-SCAN)"
                    : "AUTO-LISTE SPIELERBEFEHLE (PLUGIN-SCAN)");
                sb.AppendLine("Stand: " + DateTime.Now.ToString("dd.MM.yyyy HH:mm"));
                sb.AppendLine();

                string pluginsDir = ResolvePluginsDirectory();
                if (string.IsNullOrEmpty(pluginsDir) || !Directory.Exists(pluginsDir))
                    return "Plugin-Ordner nicht gefunden (Pfadauflösung fehlgeschlagen).";

                var files = Directory.GetFiles(pluginsDir, "*.cs", SearchOption.TopDirectoryOnly);
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var entries = new List<(string plugin, string type, string cmd)>();

                var infoRegex = new Regex("\\[Info\\(\"([^\"]+)\"", RegexOptions.Compiled);
                var chatRegex = new Regex("\\[ChatCommand\\(\"([^\"]+)\"\\)\\]", RegexOptions.Compiled);
                var consoleRegex = new Regex("\\[ConsoleCommand\\(\"([^\"]+)\"\\)\\]", RegexOptions.Compiled);
                var covRegex = new Regex("AddCovalenceCommand\\(\"([^\"]+)\"", RegexOptions.Compiled);

                foreach (var file in files)
                {
                    string text;
                    try
                    {
                        text = File.ReadAllText(file);
                    }
                    catch
                    {
                        continue;
                    }

                    string pluginName = Path.GetFileNameWithoutExtension(file);
                    var infoMatch = infoRegex.Match(text);
                    if (infoMatch.Success && !string.IsNullOrWhiteSpace(infoMatch.Groups[1].Value))
                        pluginName = infoMatch.Groups[1].Value.Trim();

                    foreach (Match m in chatRegex.Matches(text))
                    {
                        string cmd = m.Groups[1].Value.Trim();
                        if (string.IsNullOrWhiteSpace(cmd)) continue;
                        if (!includeInternal && !IsPublicPlayerCommand(cmd, "Chat")) continue;
                        if (includeInternal && IsPublicPlayerCommand(cmd, "Chat")) continue;
                        string key = pluginName + "|Chat|/" + cmd;
                        if (seen.Add(key)) entries.Add((pluginName, "Chat", "/" + cmd));
                    }
                    foreach (Match m in consoleRegex.Matches(text))
                    {
                        if (!includeInternal && !AutoCommandsIncludeConsole)
                            continue;
                        string cmd = m.Groups[1].Value.Trim();
                        if (string.IsNullOrWhiteSpace(cmd)) continue;
                        if (!includeInternal && !IsPublicPlayerCommand(cmd, "Konsole")) continue;
                        if (includeInternal && IsPublicPlayerCommand(cmd, "Konsole")) continue;
                        string key = pluginName + "|Konsole|" + cmd;
                        if (seen.Add(key)) entries.Add((pluginName, "Konsole", cmd));
                    }
                    foreach (Match m in covRegex.Matches(text))
                    {
                        string cmd = m.Groups[1].Value.Trim().ToLowerInvariant();
                        if (string.IsNullOrWhiteSpace(cmd)) continue;
                        if (!includeInternal && !IsPublicPlayerCommand(cmd, "Covalence")) continue;
                        if (includeInternal && IsPublicPlayerCommand(cmd, "Covalence")) continue;
                        string key = pluginName + "|Covalence|/" + cmd;
                        if (seen.Add(key)) entries.Add((pluginName, "Covalence", "/" + cmd));
                    }
                }

                foreach (var group in entries.OrderBy(e => e.plugin).ThenBy(e => e.type).ThenBy(e => e.cmd).GroupBy(e => e.plugin))
                {
                    sb.AppendLine("[" + group.Key + "]");
                    foreach (var entry in group)
                        sb.AppendLine("- " + entry.type + ": " + entry.cmd);
                    sb.AppendLine();
                }

                if (entries.Count == 0)
                    return includeInternal ? "Keine internen Befehle gefunden." : "Keine Spielerbefehle gefunden.";

                return sb.ToString().TrimEnd();
            }
            catch (Exception ex)
            {
                return "Fehler beim Befehls-Scan: " + ex.Message;
            }
        }

        private bool IsPublicPlayerCommand(string command, string type)
        {
            if (string.IsNullOrWhiteSpace(command))
                return false;

            string cmd = command.Trim().ToLowerInvariant();

            // Interne/technische Prefixes rausfiltern.
            if (cmd.StartsWith("global.") || cmd.StartsWith("server.") || cmd.StartsWith("debug.") ||
                cmd.StartsWith("oxide.") || cmd.StartsWith("rcon.") || cmd.StartsWith("app."))
                return false;

            // Für normale Spieler relevante Kommandos behalten, Admin-/Maintenance-Kommandos ausblenden.
            string[] blockedTokens =
            {
                "admin", "debug", "reload", "unload", "load", "grant", "revoke", "perm",
                "permission", "kick", "ban", "wipe", "restart", "shutdown", "killall",
                "healore", "tpconsole", "convar", "helpreload"
            };
            foreach (var token in blockedTokens)
            {
                if (cmd.Contains(token))
                    return false;
            }

            // Console-Kommandos sind standardmäßig komplett deaktiviert (siehe Konstante oben).
            if (type == "Konsole" && !AutoCommandsIncludeConsole)
                return false;

            return true;
        }

        private string ResolvePluginsDirectory()
        {
            try
            {
                var candidates = new List<string>();

                if (!string.IsNullOrWhiteSpace(Interface.Oxide.RootDirectory))
                    candidates.Add(Path.Combine(Interface.Oxide.RootDirectory, "plugins"));

                if (!string.IsNullOrWhiteSpace(Interface.Oxide.ConfigDirectory))
                {
                    candidates.Add(Path.Combine(Interface.Oxide.ConfigDirectory, "..", "plugins"));
                    var configParent = Path.GetDirectoryName(Interface.Oxide.ConfigDirectory);
                    if (!string.IsNullOrWhiteSpace(configParent))
                        candidates.Add(Path.Combine(configParent, "plugins"));
                }

                foreach (var candidate in candidates)
                {
                    if (string.IsNullOrWhiteSpace(candidate))
                        continue;

                    string normalized;
                    try { normalized = Path.GetFullPath(candidate); }
                    catch { continue; }

                    if (Directory.Exists(normalized))
                        return normalized;
                }
            }
            catch
            {
                // Ignorieren und leer zurückgeben.
            }

            return string.Empty;
        }

        private string BuildTeamRosterText()
        {
            var sb = new StringBuilder();
            sb.AppendLine("TEAM / MODERATION");
            sb.AppendLine("Stand: " + DateTime.Now.ToString("dd.MM.yyyy HH:mm"));
            sb.AppendLine();

            AppendGroupRoster(sb, "owner", "Owner");
            AppendGroupRoster(sb, "admin", "Admins");
            AppendGroupRoster(sb, "developer", "Developer");
            AppendGroupRoster(sb, "dev", "Devs");
            AppendGroupRoster(sb, "moderator", "Moderatoren");
            AppendGroupRoster(sb, "mod", "Mods");

            return sb.ToString().TrimEnd();
        }

        private void AppendGroupRoster(StringBuilder sb, string group, string title)
        {
            if (!permission.GroupExists(group))
                return;

            sb.AppendLine(title + ":");
            var users = permission.GetUsersInGroup(group);
            if (users == null || users.Length == 0)
            {
                sb.AppendLine("- (keine Einträge)");
                sb.AppendLine();
                return;
            }

            foreach (var raw in users.OrderBy(x => x))
            {
                string line = (raw ?? string.Empty).Trim();
                if (string.IsNullOrEmpty(line)) continue;

                string[] split = line.Split(' ');
                string id = split.Length > 0 ? split[0] : line;
                string fallbackName = split.Length > 1 ? string.Join(" ", split.Skip(1)) : id;

                string display = fallbackName;
                if (ulong.TryParse(id, out ulong uid))
                {
                    var online = BasePlayer.FindByID(uid) ?? BasePlayer.FindSleeping(uid);
                    if (online != null && !string.IsNullOrWhiteSpace(online.displayName))
                        display = online.displayName;
                }

                sb.AppendLine("- " + display + " (" + id + ")");
            }
            sb.AppendLine();
        }

        private List<string> GetMainCategoryKeys()
        {
            var valid = new List<string>();
            if (_config.Pages == null) return valid;

            foreach (var key in _config.MainCategoryOrder)
            {
                if (valid.Count >= MaxMainCategories) break;
                string k = (key ?? string.Empty).Trim().ToLowerInvariant();
                if (string.IsNullOrEmpty(k) || valid.Contains(k)) continue;
                if (_config.Pages.ContainsKey(k)) valid.Add(k);
            }

            foreach (var key in _config.Pages.Keys)
            {
                if (valid.Count >= MaxMainCategories) break;
                string k = key.ToLowerInvariant();
                if (!valid.Contains(k))
                    valid.Add(k);
            }

            return valid;
        }

        private string ResolveCategoryKey(string requested, List<string> mainKeys)
        {
            string key = (requested ?? string.Empty).Trim().ToLowerInvariant();
            if (!string.IsNullOrEmpty(key) && mainKeys.Contains(key))
                return key;
            return mainKeys[0];
        }

        private string ResolvePageKey(string categoryKey, string requestedPage, BasePlayer player)
        {
            var pageKeys = GetPageKeys(categoryKey, player);
            if (pageKeys.Count == 0) return string.Empty;

            string page = (requestedPage ?? string.Empty).Trim().ToLowerInvariant();
            if (!string.IsNullOrEmpty(page) && pageKeys.Contains(page))
                return page;

            return pageKeys[0];
        }

        private List<string> GetPageKeys(string categoryKey, BasePlayer player = null)
        {
            if (string.IsNullOrEmpty(categoryKey) || _config.Pages == null || !_config.Pages.TryGetValue(categoryKey, out var category) || category.Entries == null)
                return new List<string>();
            var keys = new List<string>();
            foreach (var kv in category.Entries)
            {
                if (!IsPageVisibleToPlayer(kv.Value, player))
                    continue;
                keys.Add(kv.Key.ToLowerInvariant());
            }
            return keys;
        }

        private string GetCategoryTitle(string categoryKey)
        {
            if (_config.Pages != null && _config.Pages.TryGetValue(categoryKey, out var category) && !string.IsNullOrEmpty(category.Title))
                return category.Title;
            return categoryKey;
        }

        private bool TryGetPage(string categoryKey, string pageKey, out PageConfig page)
        {
            page = null;
            if (string.IsNullOrEmpty(categoryKey) || string.IsNullOrEmpty(pageKey))
                return false;
            if (_config.Pages == null || !_config.Pages.TryGetValue(categoryKey, out var category))
                return false;
            if (category.Entries == null || !category.Entries.TryGetValue(pageKey, out page))
                return false;
            return true;
        }

        private bool IsPageVisibleToPlayer(PageConfig page, BasePlayer player)
        {
            if (page == null)
                return false;
            if (!page.AdminOnly)
                return true;
            return IsHelpAdmin(player);
        }

        private bool IsHelpAdmin(BasePlayer player)
        {
            return player != null && (player.IsAdmin || permission.UserHasPermission(player.UserIDString, PermAdmin));
        }

        private static List<string> Paginate(string text, int linesPerPart)
        {
            var lines = SplitLines(text);
            var parts = new List<string>();
            if (lines.Count == 0)
            {
                parts.Add(string.Empty);
                return parts;
            }

            for (int i = 0; i < lines.Count; i += linesPerPart)
            {
                int count = Math.Min(linesPerPart, lines.Count - i);
                parts.Add(string.Join("\n", lines.GetRange(i, count)));
            }

            return parts;
        }

        private static List<string> SplitLines(string text)
        {
            return (text ?? string.Empty).Replace("\r", "").Split('\n').ToList();
        }

        private static void SplitTarget(string target, out string category, out string pageKey)
        {
            category = null;
            pageKey = null;

            var parts = (target ?? string.Empty).Split(':');
            if (parts.Length >= 1 && !string.IsNullOrWhiteSpace(parts[0]))
                category = parts[0].Trim().ToLowerInvariant();
            if (parts.Length >= 2 && !string.IsNullOrWhiteSpace(parts[1]))
                pageKey = parts[1].Trim().ToLowerInvariant();
        }
    }
}
