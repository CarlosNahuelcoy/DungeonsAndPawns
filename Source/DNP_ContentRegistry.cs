using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SimpleJSON;
using Verse;

namespace DungeonsAndPawns
{
    // ─────────────────────────────────────────────────────────────
    // RUNTIME DATA MODELS
    // These replace the old XML Def classes entirely.
    // ─────────────────────────────────────────────────────────────

    public class DNP_RulesetData
    {
        public string id;
        public string label;
        public string description;

        public int   checkDiceSides          = 20;
        public int   statBonusInterval       = 2;
        public int   baseXpPerLevel          = 100;
        public float xpMultiplierPerLevel    = 1.5f;
        public int   maxPlayers              = 4;
        public bool  applyMoodBuffs          = true;
        public bool  applyMoodDebuffs        = true;
        public string successThoughtDef      = "DNP_GoodSession";
        public string failureThoughtDef      = "DNP_BadSession";
        public bool  useColonistSkillBonuses = true;
    }

    public class DNP_EnemyData
    {
        public string id;
        public string enemyName;
        public string description;
        public string flavorText;

        public int   hp              = 8;
        public int   armor           = 0;
        public int   attackDamage    = 3;
        public int   attackBonus     = 1;
        public int   xpReward        = 25;
        public float challengeRating = 1f;
        public string behaviorTag    = "aggressive";

        public List<DNP_LootEntry> lootTable = new List<DNP_LootEntry>();
    }

    public class DNP_LootEntry
    {
        public string itemId;
        public float  dropChance = 0.3f;
    }

    public class DNP_ItemData
    {
        public string id;
        public string itemName;
        public string description;

        public string itemType        = "Weapon"; // Weapon, Armor, Consumable, Quest
        public int    damageBonus     = 0;
        public string statBonusType   = "";
        public int    statBonusAmount = 0;
        public int    armorBonus      = 0;

        public string consumableEffect = "";
        public int    consumableValue  = 0;

        public bool isStackable = false;
        public int  maxStack    = 1;
    }

    public class DNP_ScenarioData
    {
        public string id;
        public string scenarioTitle;
        public string scenarioDescription;
        public string openingNarration;
        public string recommendedRulesetId  = "standard";

        public List<DNP_EncounterTemplate>  encounters           = new List<DNP_EncounterTemplate>();
        public List<string>                 dmNarrationHints     = new List<string>();
        public List<string>                 completionRewardItemIds = new List<string>();
        public string                       completionThoughtDef = "DNP_GrandVictory";
    }

    public class DNP_EncounterTemplate
    {
        public string encounterName;
        public string narrationBefore;
        public List<DNP_EnemySpawnEntry> enemies = new List<DNP_EnemySpawnEntry>();
        public string narrationAfter;
    }

    public class DNP_EnemySpawnEntry
    {
        public string enemyId;
        public int    count = 1;
    }

    // ─────────────────────────────────────────────────────────────
    // CONTENT REGISTRY — in-memory store for all JSON content
    // ─────────────────────────────────────────────────────────────
    public static class DNP_ContentRegistry
    {
        // Rulesets
        private static List<DNP_RulesetData>  _rulesets  = new List<DNP_RulesetData>();
        private static List<DNP_EnemyData>    _enemies   = new List<DNP_EnemyData>();
        private static List<DNP_ItemData>     _items     = new List<DNP_ItemData>();
        private static List<DNP_ScenarioData> _scenarios = new List<DNP_ScenarioData>();

        public static IReadOnlyList<DNP_RulesetData>  AllRulesets  => _rulesets;
        public static IReadOnlyList<DNP_EnemyData>    AllEnemies   => _enemies;
        public static IReadOnlyList<DNP_ItemData>     AllItems     => _items;
        public static IReadOnlyList<DNP_ScenarioData> AllScenarios => _scenarios;

        public static DNP_RulesetData  GetRuleset (string id) => _rulesets .FirstOrDefault(x => x.id == id);
        public static DNP_EnemyData    GetEnemy   (string id) => _enemies  .FirstOrDefault(x => x.id == id);
        public static DNP_ItemData     GetItem    (string id) => _items    .FirstOrDefault(x => x.id == id);
        public static DNP_ScenarioData GetScenario(string id) => _scenarios.FirstOrDefault(x => x.id == id);

        public static DNP_RulesetData FirstRuleset =>
            _rulesets.FirstOrDefault(r => r.id == "standard") ?? _rulesets.FirstOrDefault();

        public static void SetRulesets (List<DNP_RulesetData>  r) { _rulesets  = r ?? new List<DNP_RulesetData>();  }
        public static void SetEnemies  (List<DNP_EnemyData>    e) { _enemies   = e ?? new List<DNP_EnemyData>();    }
        public static void SetItems    (List<DNP_ItemData>      i) { _items     = i ?? new List<DNP_ItemData>();     }
        public static void SetScenarios(List<DNP_ScenarioData>  s) { _scenarios = s ?? new List<DNP_ScenarioData>(); }
    }

    // ─────────────────────────────────────────────────────────────
    // CONTENT LOADER — reads all JSON folders at startup
    // ─────────────────────────────────────────────────────────────
    public static class DNP_ContentLoader
    {
        private static string Root       => Path.Combine(GenFilePaths.SaveDataFolderPath, "DungeonsAndPawns");
        private static string Rulesets   => Path.Combine(Root, "rulesets");
        private static string Enemies    => Path.Combine(Root, "enemies");
        private static string Items      => Path.Combine(Root, "items");
        private static string Scenarios  => Path.Combine(Root, "scenarios");
        private static string Worlds     => Path.Combine(Root, "worlds");

        public static void Load()
        {
            EnsureFolders();
            CopyDefaults();

            var rulesets  = LoadRulesets();
            var enemies   = LoadEnemies();
            var items     = LoadItems();
            var scenarios = LoadScenarios();

            // If any category is empty, embedded defaults weren't copied — force them now
            if (rulesets.Count == 0 || enemies.Count == 0 || items.Count == 0)
            {
                Log.Warning("[DungeonsAndPawns] Some content categories empty — forcing embedded defaults.");
                WriteEmbeddedDefaults();
                if (rulesets.Count  == 0) rulesets  = LoadRulesets();
                if (enemies.Count   == 0) enemies   = LoadEnemies();
                if (items.Count     == 0) items     = LoadItems();
                if (scenarios.Count == 0) scenarios = LoadScenarios();
            }

            // Ensure default world exists even if other content was already present
            if (!Directory.GetFiles(Worlds, "*.json").Any())
            {
                SaveWorldFile(EmbeddedDefaultWorldEN(), "world_dnd_default");
                SaveWorldFile(EmbeddedDefaultWorldES(), "world_dnd_default_es");
            }

            DNP_ContentRegistry.SetRulesets (rulesets);
            DNP_ContentRegistry.SetEnemies  (enemies);
            DNP_ContentRegistry.SetItems    (items);
            DNP_ContentRegistry.SetScenarios(scenarios);

            Log.Message("[DungeonsAndPawns] Content loaded — "
                + DNP_ContentRegistry.AllRulesets.Count  + " rulesets, "
                + DNP_ContentRegistry.AllEnemies.Count   + " enemies, "
                + DNP_ContentRegistry.AllItems.Count     + " items, "
                + DNP_ContentRegistry.AllScenarios.Count + " scenarios.");
        }

        // ── Reload (called after in-game edits) ────────────────
        public static void Reload() => Load();

        // ── Loaders ────────────────────────────────────────────

        private static List<DNP_RulesetData> LoadRulesets()
        {
            var list = new List<DNP_RulesetData>();
            foreach (var file in Files(Rulesets))
            {
                try
                {
                    var n = Parse(file);
                    if (n == null) continue;
                    list.Add(new DNP_RulesetData
                    {
                        id                      = n["id"],
                        label                   = n["label"],
                        description             = n["description"],
                        checkDiceSides          = n["checkDiceSides"].AsInt,
                        statBonusInterval       = n["statBonusInterval"].AsInt,
                        baseXpPerLevel          = n["baseXpPerLevel"].AsInt,
                        xpMultiplierPerLevel    = n["xpMultiplierPerLevel"].AsFloat,
                        maxPlayers              = n["maxPlayers"].AsInt,
                        applyMoodBuffs          = n["applyMoodBuffs"].AsBool,
                        applyMoodDebuffs        = n["applyMoodDebuffs"].AsBool,
                        successThoughtDef       = n["successThoughtDef"],
                        failureThoughtDef       = n["failureThoughtDef"],
                        useColonistSkillBonuses = n["useColonistSkillBonuses"].AsBool
                    });
                }
                catch (Exception ex) { Warn(file, ex); }
            }
            // Ensure sensible defaults for zero values
            foreach (var r in list)
            {
                if (r.checkDiceSides   == 0) r.checkDiceSides   = 20;
                if (r.maxPlayers       == 0) r.maxPlayers        = 4;
                if (r.baseXpPerLevel   == 0) r.baseXpPerLevel    = 100;
                if (r.xpMultiplierPerLevel == 0) r.xpMultiplierPerLevel = 1.5f;
            }
            return list;
        }

        private static List<DNP_EnemyData> LoadEnemies()
        {
            var list = new List<DNP_EnemyData>();
            foreach (var file in Files(Enemies))
            {
                try
                {
                    var n = Parse(file);
                    if (n == null) continue;
                    var e = new DNP_EnemyData
                    {
                        id              = n["id"],
                        enemyName       = n["enemyName"],
                        description     = n["description"],
                        flavorText      = n["flavorText"],
                        hp              = n["hp"].AsInt,
                        armor           = n["armor"].AsInt,
                        attackDamage    = n["attackDamage"].AsInt,
                        attackBonus     = n["attackBonus"].AsInt,
                        xpReward        = n["xpReward"].AsInt,
                        challengeRating = n["challengeRating"].AsFloat,
                        behaviorTag     = n["behaviorTag"]
                    };
                    if (e.hp == 0) e.hp = 8;
                    foreach (JSONNode l in n["lootTable"].AsArray)
                        e.lootTable.Add(new DNP_LootEntry
                        {
                            itemId     = l["itemId"],
                            dropChance = l["dropChance"].AsFloat
                        });
                    list.Add(e);
                }
                catch (Exception ex) { Warn(file, ex); }
            }
            return list;
        }

        private static List<DNP_ItemData> LoadItems()
        {
            var list = new List<DNP_ItemData>();
            foreach (var file in Files(Items))
            {
                try
                {
                    var root = JSON.Parse(File.ReadAllText(file));
                    if (root == null) continue;

                    // Support both single object and array in one file
                    if (root.IsArray)
                    {
                        foreach (JSONNode n in root.AsArray)
                            list.Add(ParseItem(n));
                    }
                    else
                    {
                        list.Add(ParseItem(root));
                    }
                }
                catch (Exception ex) { Warn(file, ex); }
            }
            return list;
        }

        private static DNP_ItemData ParseItem(JSONNode n)
        {
            return new DNP_ItemData
            {
                id              = n["id"],
                itemName        = n["itemName"],
                description     = n["description"],
                itemType        = n["itemType"].Value.Length > 0 ? n["itemType"].Value : "Weapon",
                damageBonus     = n["damageBonus"].AsInt,
                statBonusType   = n["statBonusType"],
                statBonusAmount = n["statBonusAmount"].AsInt,
                armorBonus      = n["armorBonus"].AsInt,
                consumableEffect = n["consumableEffect"],
                consumableValue = n["consumableValue"].AsInt,
                isStackable     = n["isStackable"].AsBool,
                maxStack        = n["maxStack"].AsInt > 0 ? n["maxStack"].AsInt : 1
            };
        }

        private static List<DNP_ScenarioData> LoadScenarios()
        {
            var list = new List<DNP_ScenarioData>();
            foreach (var file in Files(Scenarios))
            {
                try
                {
                    var n = Parse(file);
                    if (n == null) continue;
                    var s = new DNP_ScenarioData
                    {
                        id                   = n["id"],
                        scenarioTitle        = n["scenarioTitle"],
                        scenarioDescription  = n["scenarioDescription"],
                        openingNarration     = n["openingNarration"],
                        recommendedRulesetId = n["recommendedRulesetId"].Value.Length > 0
                                               ? n["recommendedRulesetId"].Value : "standard",
                        completionThoughtDef = n["completionThoughtDef"].Value.Length > 0
                                               ? n["completionThoughtDef"].Value : "DNP_GrandVictory"
                    };
                    foreach (JSONNode enc in n["encounters"].AsArray)
                    {
                        var t = new DNP_EncounterTemplate
                        {
                            encounterName  = enc["encounterName"],
                            narrationBefore = enc["narrationBefore"],
                            narrationAfter = enc["narrationAfter"]
                        };
                        foreach (JSONNode e in enc["enemies"].AsArray)
                            t.enemies.Add(new DNP_EnemySpawnEntry
                            {
                                enemyId = e["enemyId"],
                                count   = e["count"].AsInt > 0 ? e["count"].AsInt : 1
                            });
                        s.encounters.Add(t);
                    }
                    foreach (JSONNode h in n["dmNarrationHints"].AsArray)
                        s.dmNarrationHints.Add(h.Value);
                    foreach (JSONNode r in n["completionRewardItemIds"].AsArray)
                        s.completionRewardItemIds.Add(r.Value);

                    list.Add(s);
                }
                catch (Exception ex) { Warn(file, ex); }
            }
            return list;
        }

        // ── Serializers (for in-game editor save) ─────────────

        public static void SaveRuleset(DNP_RulesetData r)
        {
            EnsureFolders();
            var n = new JSONObject();
            n["id"]                      = r.id;
            n["label"]                   = r.label;
            n["description"]             = r.description ?? "";
            n["checkDiceSides"]          = r.checkDiceSides;
            n["statBonusInterval"]       = r.statBonusInterval;
            n["baseXpPerLevel"]          = r.baseXpPerLevel;
            n["xpMultiplierPerLevel"]    = r.xpMultiplierPerLevel;
            n["maxPlayers"]              = r.maxPlayers;
            n["applyMoodBuffs"]          = r.applyMoodBuffs;
            n["applyMoodDebuffs"]        = r.applyMoodDebuffs;
            n["successThoughtDef"]       = r.successThoughtDef;
            n["failureThoughtDef"]       = r.failureThoughtDef;
            n["useColonistSkillBonuses"] = r.useColonistSkillBonuses;
            Write(Path.Combine(Rulesets, r.id + ".json"), n);
        }

        public static void SaveEnemy(DNP_EnemyData e)
        {
            EnsureFolders();
            var n = new JSONObject();
            n["id"]              = e.id;
            n["enemyName"]       = e.enemyName;
            n["description"]     = e.description     ?? "";
            n["flavorText"]      = e.flavorText       ?? "";
            n["hp"]              = e.hp;
            n["armor"]           = e.armor;
            n["attackDamage"]    = e.attackDamage;
            n["attackBonus"]     = e.attackBonus;
            n["xpReward"]        = e.xpReward;
            n["challengeRating"] = e.challengeRating;
            n["behaviorTag"]     = e.behaviorTag      ?? "aggressive";
            var loot = new JSONArray();
            foreach (var l in e.lootTable)
            {
                var le = new JSONObject();
                le["itemId"]     = l.itemId;
                le["dropChance"] = l.dropChance;
                loot.Add(le);
            }
            n["lootTable"] = loot;
            Write(Path.Combine(Enemies, e.id + ".json"), n);
        }

        public static void SaveItem(DNP_ItemData item)
        {
            EnsureFolders();
            var n = new JSONObject();
            n["id"]               = item.id;
            n["itemName"]         = item.itemName;
            n["description"]      = item.description      ?? "";
            n["itemType"]         = item.itemType;
            n["damageBonus"]      = item.damageBonus;
            n["statBonusType"]    = item.statBonusType     ?? "";
            n["statBonusAmount"]  = item.statBonusAmount;
            n["armorBonus"]       = item.armorBonus;
            n["consumableEffect"] = item.consumableEffect  ?? "";
            n["consumableValue"]  = item.consumableValue;
            n["isStackable"]      = item.isStackable;
            n["maxStack"]         = item.maxStack;
            Write(Path.Combine(Items, item.id + ".json"), n);
        }

        public static void SaveScenario(DNP_ScenarioData s)
        {
            EnsureFolders();
            var n = new JSONObject();
            n["id"]                  = s.id;
            n["scenarioTitle"]       = s.scenarioTitle;
            n["scenarioDescription"] = s.scenarioDescription  ?? "";
            n["openingNarration"]    = s.openingNarration      ?? "";
            n["recommendedRulesetId"] = s.recommendedRulesetId;
            n["completionThoughtDef"] = s.completionThoughtDef;

            var encs = new JSONArray();
            foreach (var e in s.encounters)
            {
                var en = new JSONObject();
                en["encounterName"]   = e.encounterName;
                en["narrationBefore"] = e.narrationBefore ?? "";
                en["narrationAfter"]  = e.narrationAfter  ?? "";
                var enemies = new JSONArray();
                foreach (var sp in e.enemies)
                {
                    var se = new JSONObject();
                    se["enemyId"] = sp.enemyId;
                    se["count"]   = sp.count;
                    enemies.Add(se);
                }
                en["enemies"] = enemies;
                encs.Add(en);
            }
            n["encounters"] = encs;

            var hints = new JSONArray();
            foreach (var h in s.dmNarrationHints) hints.Add(h);
            n["dmNarrationHints"] = hints;

            var rewards = new JSONArray();
            foreach (var r in s.completionRewardItemIds) rewards.Add(r);
            n["completionRewardItemIds"] = rewards;

            Write(Path.Combine(Scenarios, s.id + ".json"), n);
        }

        public static void DeleteRuleset (DNP_RulesetData  r) => TryDelete(Path.Combine(Rulesets,  r.id + ".json"));
        public static void DeleteEnemy   (DNP_EnemyData    e) => TryDelete(Path.Combine(Enemies,   e.id + ".json"));
        public static void DeleteItem    (DNP_ItemData      i) => TryDelete(Path.Combine(Items,     i.id + ".json"));
        public static void DeleteScenario(DNP_ScenarioData  s) => TryDelete(Path.Combine(Scenarios, s.id + ".json"));

        // ── Folder helpers ─────────────────────────────────────

        private static void EnsureFolders()
        {
            foreach (var p in new[] { Root, Rulesets, Enemies, Items, Scenarios })
                if (!Directory.Exists(p)) Directory.CreateDirectory(p);
        }

        private static void CopyDefaults()
        {
            // Try case-insensitive PackageId match first, then folder name
            string modFolder = null;
            var mod = LoadedModManager.RunningMods.FirstOrDefault(m =>
                string.Equals(m.PackageId, "CarlosNahuelcoy.DungeonsAndPawns",
                    StringComparison.OrdinalIgnoreCase));

            if (mod == null)
                mod = LoadedModManager.RunningMods.FirstOrDefault(m =>
                    m.RootDir != null && m.RootDir.IndexOf("DungeonsAndPawns",
                        StringComparison.OrdinalIgnoreCase) >= 0);

            modFolder = mod?.RootDir;

            if (modFolder == null)
            {
                Log.Warning("[DungeonsAndPawns] ContentLoader: mod folder not found — using embedded defaults.");
                WriteEmbeddedDefaults();
                return;
            }

            Log.Message("[DungeonsAndPawns] ContentLoader: found mod folder at: " + modFolder);
            CopyFolder(Path.Combine(modFolder, "DefaultContent", "rulesets"),  Rulesets);
            CopyFolder(Path.Combine(modFolder, "DefaultContent", "enemies"),   Enemies);
            CopyFolder(Path.Combine(modFolder, "DefaultContent", "items"),     Items);
            CopyFolder(Path.Combine(modFolder, "DefaultContent", "scenarios"), Scenarios);
            CopyFolder(Path.Combine(modFolder, "DefaultContent", "worlds"),    Worlds);
        }

        private static void CopyFolder(string src, string dest)
        {
            if (!Directory.Exists(src))
            {
                Log.Warning("[DungeonsAndPawns] DefaultContent folder not found: " + src
                    + " — will use embedded defaults for this category.");
                return;
            }
            if (Directory.GetFiles(dest, "*.json").Length > 0) return; // already has content

            int count = 0;
            foreach (var file in Directory.GetFiles(src, "*.json"))
            {
                File.Copy(file, Path.Combine(dest, Path.GetFileName(file)), false);
                count++;
            }
            Log.Message("[DungeonsAndPawns] Copied " + count + " files from " + src);
        }

        private static void WriteEmbeddedDefaults()
        {
            // Only write if folders are empty — never overwrite user changes
            if (!Directory.GetFiles(Rulesets,  "*.json").Any()) SaveRuleset(EmbeddedStandard());
            if (!Directory.GetFiles(Rulesets,  "*.json").Skip(1).Any()) SaveRuleset(EmbeddedGrim());
            if (!Directory.GetFiles(Enemies,   "*.json").Any()) { SaveEnemy(EmbeddedGoblin()); SaveEnemy(EmbeddedOrc()); SaveEnemy(EmbeddedDarkMage()); }
            if (!Directory.GetFiles(Items,     "*.json").Any()) foreach (var i in EmbeddedItems()) SaveItem(i);
            if (!Directory.GetFiles(Scenarios, "*.json").Any()) SaveScenario(EmbeddedGoblinWarren());

            // Save both language versions of the default world
            if (!Directory.GetFiles(Worlds, "*.json").Any())
            {
                SaveWorldFile(EmbeddedDefaultWorldEN(), "world_dnd_default");
                SaveWorldFile(EmbeddedDefaultWorldES(), "world_dnd_default_es");
            }
        }

        private static void SaveWorldFile(JSONNode node, string filename)
        {
            string path = Path.Combine(Worlds, filename + ".json");
            Write(path, node);
            Log.Message("[DungeonsAndPawns] Created default world: " + path);
        }

        // ── Default world loader ──────────────────────────────
        // If no world is active on the GameComponent, load the default D&D world.

        public static void LoadDefaultWorld()
        {
            var comp = DNP_GameComponent.Instance;
            if (comp == null) return;

            // Already has a world with content — don't overwrite
            if (comp.World != null &&
                !string.IsNullOrEmpty(comp.World.worldName) &&
                comp.World.worldName != "Unnamed World") return;

            // Pick language-appropriate default world
            string lang      = (Prefs.LangFolderName ?? "English").ToLowerInvariant();
            bool   isSpanish = lang.Contains("spanish");

            // Try language-specific file first, then generic, then any
            string[] candidates = isSpanish
                ? new[] { "world_dnd_default_es.json", "world_dnd_default.json" }
                : new[] { "world_dnd_default.json", "world_dnd_default_es.json" };

            string worldPath = null;
            foreach (var candidate in candidates)
            {
                string p = Path.Combine(Worlds, candidate);
                if (File.Exists(p)) { worldPath = p; break; }
            }

            // Fallback to any world file
            if (worldPath == null)
            {
                worldPath = Directory.GetFiles(Worlds, "*.json").FirstOrDefault();
                if (worldPath == null) return;
            }

            try
            {
                var node = JSON.Parse(File.ReadAllText(worldPath));
                if (node == null) return;

                comp.World = new DNP_WorldData
                {
                    worldName         = node["worldName"]?.Value         ?? "The Forgotten Reaches",
                    genre             = node["genre"]?.Value             ?? "",
                    tone              = node["tone"]?.Value              ?? "",
                    summary           = node["summary"]?.Value           ?? "",
                    history           = node["history"]?.Value           ?? "",
                    factions          = node["factions"]?.Value          ?? "",
                    locations         = node["locations"]?.Value         ?? "",
                    rules             = node["rules"]?.Value             ?? "",
                    aiInstructions    = node["aiInstructions"]?.Value    ?? "",
                    campaignName      = node["campaignName"]?.Value      ?? "",
                    campaignObjective = node["campaignObjective"]?.Value ?? "",
                    campaignNotes     = node["campaignNotes"]?.Value     ?? ""
                };

                Log.Message("[DungeonsAndPawns] Default world loaded: "
                    + comp.World.worldName);
            }
            catch (Exception ex)
            {
                Log.Warning("[DungeonsAndPawns] Failed to load default world: " + ex.Message);
            }
        }

        private static JSONNode EmbeddedDefaultWorldEN()
        {
            var n = new JSONObject();
            n["id"]               = "world_dnd_default";
            n["worldName"]        = "The Forgotten Reaches";
            n["genre"]            = "High Fantasy";
            n["tone"]             = "Heroic adventure with dark undertones. Danger is real, but courage and wit can prevail. The world is ancient, scarred by old wars, and full of forgotten secrets waiting to be uncovered. Not every problem can be solved with a sword, but a sword never hurts.";
            n["summary"]          = "The Forgotten Reaches is a vast frontier region of the world of Aeloria, where the great empires of old have crumbled and left behind ruins, monster-haunted wilderlands, and scattered settlements clinging to survival. Ancient magic lingers in the land. The players are adventurers — colonists, survivors, and wanderers — making names for themselves on this lawless frontier.";
            n["history"]          = "Three centuries ago, the Aeloric Empire stretched across the known world. Its fall during the Sundering War left a power vacuum filled by warlords, cults, and monsters. Valdren was the last imperial capital — now a half-ruined city-state ruled by a council of merchant guilds. The old empire's roads still exist, but few dare travel them alone. Ruins hide both treasure and terrible things.";
            n["factions"]         = "The Merchant Council of Valdren: pragmatic rulers who value profit over principle, but maintain order in the city.\nThe Order of the Silver Flame: paladins and clerics who protect travelers and fight the undead. Strict, honorable, sometimes inflexible.\nThe Thornwood Clans: tribal humans who have lived in the wilderness for generations. They respect strength and despise weakness.\nThe Serpent Cult: a secretive organization rumored to worship a dead god. Uses poison, blackmail, and manipulation.\nThe Goblin Warrens: numerous and fractious raiding tribes. Individually weak, dangerous in numbers.";
            n["locations"]        = "Valdren: the last great city, half in ruins. Seat of the Merchant Council. Markets, temples, and a thieves' quarter.\nThe Thornwood: an ancient forest stretching for hundreds of miles. Strange lights and sounds at night. The Thornwood Clans live here.\nThe Sunken Keep: imperial ruins subsiding into a swamp. Rumored to contain the treasury of a Sundering War general.\nThe Ashfields: a magically blasted plain. Nothing grows. Undead wander at night. Something terrible happened here.\nThe Road of Bones: the old imperial highway. Still passable. Bandits operate freely. Caravans use it because there is no other way.";
            n["rules"]            = "Standard D&D 5e rules, 2024 Player's Handbook. Ability checks use d20 + modifier vs Difficulty Class set by the DM. Combat uses initiative order. Death saving throws at 0 HP. Short rest: 1 hour, spend Hit Dice to recover HP. Long rest: 8 hours, recover all HP and half max Hit Dice. Advantage and disadvantage cancel each other out.";
            n["aiInstructions"]   = "Narrate in the style of classic D&D adventure fiction — vivid, grounded, with a sense of real consequence. Make the world feel lived-in and dangerous. NPCs have their own agendas and can be reasoned with, bribed, or threatened. Reward creative problem-solving. Use faction and location names from this world consistently. Never make combat trivial — every fight should feel like it could go wrong. Describe the environment actively during exploration.";
            n["campaignName"]     = "Shadows Over the Reaches";
            n["campaignObjective"]= "The party has been hired by Ser Morvaine of the Merchant Council to investigate a series of disappearances along the Road of Bones. Three caravans have vanished in the past month without a single survivor found. The reward is substantial — 500 gold pieces plus lodging in Valdren. Find out what is happening and stop it.";
            n["campaignNotes"]    = "The Serpent Cult is orchestrating the disappearances. They are capturing travelers for a ritual in the Sunken Keep. The goblins near the Road of Bones have seen cultists moving at night but are too frightened to speak openly. Brother Aldric of the Silver Flame suspects the cult but lacks proof. The trail leads to the Sunken Keep where the ritual nears completion. If it succeeds, something buried there will awaken.";
            return n;
        }

        private static JSONNode EmbeddedDefaultWorldES()
        {
            var n = new JSONObject();
            n["id"]               = "world_dnd_default_es";
            n["worldName"]        = "Las Tierras Olvidadas";
            n["genre"]            = "Alta Fantasía";
            n["tone"]             = "Aventura heroica con matices oscuros. El peligro es real, pero el coraje y el ingenio pueden triunfar. El mundo es antiguo, marcado por viejas guerras y lleno de secretos olvidados esperando ser descubiertos. No todo problema se resuelve con una espada, pero una espada nunca está de más.";
            n["summary"]          = "Las Tierras Olvidadas son una vasta región fronteriza del mundo de Aeloria, donde los grandes imperios de antaño se han derrumbado y dejado atrás ruinas, tierras salvajes plagadas de monstruos, y asentamientos dispersos que luchan por sobrevivir. La magia antigua impregna la tierra. Los jugadores son aventureros — colonos, supervivientes y vagabundos — labrándose un nombre en esta frontera sin ley.";
            n["history"]          = "Hace tres siglos, el Imperio Aelórico se extendía por el mundo conocido. Su caída durante la Guerra de la Escisión dejó un vacío de poder llenado por señores de la guerra, cultos y monstruos. Valdren fue la última capital imperial — ahora una ciudad-estado semiarruinada gobernada por un consejo de gremios mercantiles. Los caminos del viejo imperio aún existen, pero pocos se atreven a recorrerlos solos. Las ruinas esconden tanto tesoros como cosas terribles.";
            n["factions"]         = "El Consejo Mercantil de Valdren: gobernantes pragmáticos que valoran el beneficio sobre los principios, pero mantienen el orden en la ciudad.\nLa Orden de la Llama Plateada: paladines y clérigos que protegen a los viajeros y combaten a los muertos vivientes. Estrictos, honorables, a veces inflexibles.\nLos Clanes del Bosque Espinoso: humanos tribales que han vivido en la naturaleza por generaciones. Respetan la fortaleza y desprecian la debilidad.\nEl Culto de la Serpiente: organización secreta que se dice adora a un dios muerto. Usa veneno, chantaje y manipulación.\nLas Guaridas de los Goblins: numerosas y discordes tribus de saqueo. Individualmente débiles, peligrosas en masa.";
            n["locations"]        = "Valdren: la última gran ciudad, medio en ruinas. Sede del Consejo Mercantil. Mercados, templos y un barrio de ladrones.\nEl Bosque Espinoso: un bosque antiguo que se extiende por cientos de kilómetros. Luces y sonidos extraños de noche. Los Clanes del Bosque viven aquí.\nEl Fuerte Hundido: ruinas imperiales que se hunden en un pantano. Se dice que contiene el tesoro de un general de la Guerra de la Escisión.\nLos Campos de Ceniza: una llanura arrasada mágicamente. Nada crece. Los muertos vivientes rondan de noche. Algo terrible ocurrió aquí.\nEl Camino de los Huesos: la antigua calzada imperial. Aún transitable. Los bandidos operan libremente. Las caravanas lo usan porque no hay otra ruta.";
            n["rules"]            = "Reglas estándar de D&D 5e, Manual del Jugador 2024. Las pruebas de habilidad usan d20 + modificador vs Clase de Dificultad fijada por el DM. El combate usa orden de iniciativa. Tiradas de muerte a 0 PG. Descanso corto: 1 hora, gastar Dados de Golpe para recuperar PG. Descanso largo: 8 horas, recuperar todos los PG y la mitad de los Dados de Golpe máximos. La ventaja y la desventaja se anulan mutuamente.";
            n["aiInstructions"]   = "Narra al estilo de la ficción de aventuras clásica de D&D — vívida, concreta, con sentido de consecuencia real. Haz que el mundo se sienta habitado y peligroso. Los PNJ tienen sus propias agendas y se puede razonar con ellos, sobornarlos o amenazarlos. Recompensa la resolución creativa de problemas. Usa de manera consistente los nombres de facciones y ubicaciones de este mundo. No hagas que el combate sea trivial — cada pelea debería sentir que puede salir mal. Describe el entorno activamente durante la exploración.";
            n["campaignName"]     = "Sombras Sobre las Tierras";
            n["campaignObjective"]= "El grupo ha sido contratado por Ser Morvaine del Consejo Mercantil para investigar una serie de desapariciones a lo largo del Camino de los Huesos. Tres caravanas han desaparecido en el último mes sin que se haya encontrado un solo superviviente. La recompensa es sustancial — 500 piezas de oro más alojamiento en Valdren. Descubrid qué está ocurriendo y detenedle.";
            n["campaignNotes"]    = "El Culto de la Serpiente orquesta las desapariciones. Están capturando viajeros para un ritual en el Fuerte Hundido. Los goblins cerca del Camino han visto cultistas moviéndose de noche pero tienen demasiado miedo para hablar abiertamente. El Hermano Aldric de la Llama Plateada sospecha del culto pero carece de pruebas. La pista lleva al Fuerte Hundido donde el ritual está próximo a completarse. Si tiene éxito, algo enterrado allí despertará.";
            return n;
        }

        private static IEnumerable<string> Files(string folder) =>
            Directory.Exists(folder) ? Directory.GetFiles(folder, "*.json") : Enumerable.Empty<string>();

        private static JSONNode Parse(string file)
        {
            var n = JSON.Parse(File.ReadAllText(file));
            if (n == null) Log.Warning("[DungeonsAndPawns] Could not parse: " + file);
            return n;
        }

        private static void Write(string path, JSONNode node)
        {
            try   { File.WriteAllText(path, node.ToString(2)); }
            catch (Exception ex) { Log.Error("[DungeonsAndPawns] Write failed: " + path + " — " + ex.Message); }
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch (Exception ex) { Log.Error("[DungeonsAndPawns] Delete failed: " + path + " — " + ex.Message); }
        }

        private static void Warn(string file, Exception ex) =>
            Log.Error("[DungeonsAndPawns] Error loading " + file + ": " + ex.Message);

        // ── Embedded fallback defaults ─────────────────────────

        private static DNP_RulesetData EmbeddedStandard() => new DNP_RulesetData
        {
            id="standard", label="Standard Rules",
            description="Balanced rules for a classic tabletop experience.",
            checkDiceSides=20, statBonusInterval=2, baseXpPerLevel=100,
            xpMultiplierPerLevel=1.5f, maxPlayers=4,
            applyMoodBuffs=true, applyMoodDebuffs=true,
            successThoughtDef="DNP_GoodSession", failureThoughtDef="DNP_BadSession",
            useColonistSkillBonuses=true
        };

        private static DNP_RulesetData EmbeddedGrim() => new DNP_RulesetData
        {
            id="grim", label="Grim Rules",
            description="Harder combat. No mood debuffs on failure.",
            checkDiceSides=20, statBonusInterval=3, baseXpPerLevel=150,
            xpMultiplierPerLevel=2.0f, maxPlayers=4,
            applyMoodBuffs=true, applyMoodDebuffs=false,
            successThoughtDef="DNP_GoodSession", failureThoughtDef="DNP_BadSession",
            useColonistSkillBonuses=false
        };

        private static DNP_EnemyData EmbeddedGoblin() => new DNP_EnemyData
        {
            id="goblin", enemyName="Goblin",
            description="A small, cowardly creature that fights in packs.",
            hp=5, armor=0, attackDamage=2, attackBonus=1,
            xpReward=15, challengeRating=0.5f, behaviorTag="cowardly",
            lootTable=new List<DNP_LootEntry>{ new DNP_LootEntry{itemId="small_knife",dropChance=0.3f} }
        };

        private static DNP_EnemyData EmbeddedOrc() => new DNP_EnemyData
        {
            id="orc_warrior", enemyName="Orc Warrior",
            description="A brutish fighter with thick hide and a love of combat.",
            hp=16, armor=3, attackDamage=5, attackBonus=3,
            xpReward=40, challengeRating=2f, behaviorTag="aggressive",
            lootTable=new List<DNP_LootEntry>{ new DNP_LootEntry{itemId="iron_sword",dropChance=0.4f} }
        };

        private static DNP_EnemyData EmbeddedDarkMage() => new DNP_EnemyData
        {
            id="dark_mage", enemyName="Dark Mage",
            description="A corrupted sorcerer. Fragile but hits hard.",
            hp=8, armor=0, attackDamage=7, attackBonus=4,
            xpReward=55, challengeRating=3f, behaviorTag="cunning",
            lootTable=new List<DNP_LootEntry>{ new DNP_LootEntry{itemId="arcane_scroll",dropChance=0.6f} }
        };

        private static List<DNP_ItemData> EmbeddedItems() => new List<DNP_ItemData>
        {
            new DNP_ItemData{id="small_knife",   itemName="Small Knife",    description="A crude blade.",        itemType="Weapon",     damageBonus=1},
            new DNP_ItemData{id="iron_sword",     itemName="Iron Sword",     description="Reliable violence.",    itemType="Weapon",     damageBonus=3, statBonusType="Strength", statBonusAmount=1},
            new DNP_ItemData{id="arcane_scroll",  itemName="Arcane Scroll",  description="Single-use spell.",    itemType="Consumable", consumableEffect="AddBuff",  consumableValue=1},
            new DNP_ItemData{id="healing_potion", itemName="Healing Potion", description="Tastes terrible.",     itemType="Consumable", consumableEffect="HealHP",   consumableValue=8, isStackable=true, maxStack=5},
        };

        private static DNP_ScenarioData EmbeddedGoblinWarren() => new DNP_ScenarioData
        {
            id="goblin_warren", scenarioTitle="The Goblin Warren",
            scenarioDescription="A nearby cave network has become infested with goblins.",
            openingNarration="The cave mouth yawns before you, reeking of smoke and worse things.",
            recommendedRulesetId="standard",
            encounters=new List<DNP_EncounterTemplate>
            {
                new DNP_EncounterTemplate
                {
                    encounterName="Cave Entrance",
                    narrationBefore="Three goblins scramble to block the entrance.",
                    narrationAfter="The goblins fall.",
                    enemies=new List<DNP_EnemySpawnEntry>{new DNP_EnemySpawnEntry{enemyId="goblin",count=3}}
                },
                new DNP_EncounterTemplate
                {
                    encounterName="The Warren Boss",
                    narrationBefore="A massive orc lounges on a throne of stolen goods.",
                    narrationAfter="The orc crashes to the ground.",
                    enemies=new List<DNP_EnemySpawnEntry>
                    {
                        new DNP_EnemySpawnEntry{enemyId="orc_warrior",count=1},
                        new DNP_EnemySpawnEntry{enemyId="dark_mage",count=1}
                    }
                }
            },
            completionRewardItemIds=new List<string>{"healing_potion","iron_sword"}
        };
    }
}