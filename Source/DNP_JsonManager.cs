using System;
using System.Collections.Generic;
using System.IO;
using SimpleJSON;
using Verse;
using RimWorld;

namespace DungeonsAndPawns
{
    // ─────────────────────────────────────────────────────────────
    // JSON MANAGER
    // Export and import sessions, characters, and custom content
    // using SimpleJSON (same library as EchoColony).
    //
    // Files live in:
    //   <RimWorld SaveData>/DungeonsAndPawns/sessions/
    //   <RimWorld SaveData>/DungeonsAndPawns/characters/
    //   <RimWorld SaveData>/DungeonsAndPawns/custom/
    // ─────────────────────────────────────────────────────────────
    public static class DNP_JsonManager
    {
        // ── Folder paths ───────────────────────────────────────

        private static string RootFolder =>
            Path.Combine(GenFilePaths.SaveDataFolderPath, "DungeonsAndPawns");

        private static string SessionsFolder  => Path.Combine(RootFolder, "sessions");
        private static string CharactersFolder => Path.Combine(RootFolder, "characters");
        private static string CustomFolder     => Path.Combine(RootFolder, "custom");
        private static string WorldFolder      => Path.Combine(RootFolder, "worlds");

        private static void EnsureFolders()
        {
            if (!Directory.Exists(RootFolder))       Directory.CreateDirectory(RootFolder);
            if (!Directory.Exists(SessionsFolder))   Directory.CreateDirectory(SessionsFolder);
            if (!Directory.Exists(CharactersFolder)) Directory.CreateDirectory(CharactersFolder);
            if (!Directory.Exists(CustomFolder))     Directory.CreateDirectory(CustomFolder);
            if (!Directory.Exists(WorldFolder))      Directory.CreateDirectory(WorldFolder);
        }

        // ── SESSION EXPORT ─────────────────────────────────────

        public static void ExportSession(DNP_Session session)
        {
            EnsureFolders();

            var root = new JSONObject();
            root["sessionId"]       = session.sessionId;
            root["rulesetId"]       = session.rulesetId;
            root["scenarioId"]      = session.scenarioId ?? "";
            root["isActive"]        = session.isActive;
            root["playerIsDM"]      = session.playerIsDM;
            root["dmPawn"]          = session.dmPawn?.Name?.ToStringShort ?? "";
            root["turnIndex"]       = session.turnIndex;
            root["exportedAt"]      = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

            // Characters
            var chars = new JSONArray();
            foreach (var pc in session.characters)
                chars.Add(SerializeCharacter(pc));
            root["characters"] = chars;

            // Session log
            var log = new JSONArray();
            foreach (var entry in session.sessionLog)
            {
                var e = new JSONObject();
                e["type"]    = entry.entryType.ToString();
                e["speaker"] = entry.speakerName;
                e["text"]    = entry.text;
                e["tick"]    = entry.tick;
                log.Add(e);
            }
            root["sessionLog"] = log;

            string filename = "session_" + SanitizeFilename(session.sessionId.Substring(0, 8))
                              + "_" + DateTime.Now.ToString("yyyyMMdd_HHmm") + ".json";
            string path = Path.Combine(SessionsFolder, filename);

            WriteJson(path, root);
            Messages.Message(
                "DNP.Export.SessionExported".Translate(path),
                MessageTypeDefOf.TaskCompletion, false);
        }

        // ── SESSION IMPORT ─────────────────────────────────────

        public static DNP_Session ImportSession(string path)
        {
            string raw = ReadJson(path);
            if (raw == null) return null;

            var root = JSON.Parse(raw);
            if (root == null)
            {
                Log.Error("[DungeonsAndPawns] Failed to parse session JSON: " + path);
                return null;
            }

            var session = new DNP_Session
            {
                sessionId       = root["sessionId"],
                rulesetId       = root["rulesetId"],
                scenarioId      = root["scenarioId"].Value == "" ? null : root["scenarioId"],
                isActive        = false, // imported sessions start paused
                playerIsDM      = root["playerIsDM"].AsBool,
                turnIndex       = root["turnIndex"].AsInt
            };

            // Characters — restore as much as possible (pawn ref will be null if not in current map)
            foreach (JSONNode node in root["characters"].AsArray)
                session.characters.Add(DeserializeCharacter(node));

            // Log
            foreach (JSONNode node in root["sessionLog"].AsArray)
            {
                DNP_LogEntry.EntryType entryType;
                if (!Enum.TryParse(node["type"].Value, out entryType))
                    entryType = DNP_LogEntry.EntryType.System;

                session.sessionLog.Add(new DNP_LogEntry
                {
                    entryType   = entryType,
                    speakerName = node["speaker"],
                    text        = node["text"],
                    tick        = node["tick"].AsInt
                });
            }

            Log.Message("[DungeonsAndPawns] Session imported from: " + path);
            return session;
        }

        // ── CHARACTER EXPORT ───────────────────────────────────

        public static void ExportCharacter(DNP_PlayerCharacter pc)
        {
            EnsureFolders();
            var node = SerializeCharacter(pc);

            string filename = "char_" + SanitizeFilename(pc.characterName)
                              + "_" + DateTime.Now.ToString("yyyyMMdd_HHmm") + ".json";
            string path = Path.Combine(CharactersFolder, filename);

            WriteJson(path, node);
            Messages.Message(
                "DNP.Export.CharExported".Translate(path),
                MessageTypeDefOf.TaskCompletion, false);
        }

        public static DNP_PlayerCharacter ImportCharacter(string path)
        {
            string raw = ReadJson(path);
            if (raw == null) return null;

            var node = JSON.Parse(raw);
            if (node == null)
            {
                Log.Error("[DungeonsAndPawns] Failed to parse character JSON: " + path);
                return null;
            }

            return DeserializeCharacter(node);
        }

        // ── CUSTOM CONTENT EXPORT (enemies, classes, scenarios) ─

        /// <summary>
        /// Exports a custom enemy definition as JSON for sharing/editing.
        /// User can edit the file and reimport it as a new Def.
        /// </summary>
        public static void ExportEnemyTemplate(DNP_EnemyData def)
        {
            EnsureFolders();
            var node = new JSONObject();
            node["id"]              = def.id;
            node["enemyName"]       = def.enemyName;
            node["description"]     = def.description ?? "";
            node["flavorText"]      = def.flavorText ?? "";
            node["hp"]              = def.hp;
            node["armor"]           = def.armor;
            node["attackDamage"]    = def.attackDamage;
            node["attackBonus"]     = def.attackBonus;
            node["xpReward"]        = def.xpReward;
            node["challengeRating"] = def.challengeRating;
            node["behaviorTag"]     = def.behaviorTag;

            var loot = new JSONArray();
            foreach (var entry in def.lootTable)
            {
                var l = new JSONObject();
                l["itemId"]      = entry.itemId;
                l["dropChance"]  = entry.dropChance;
                loot.Add(l);
            }
            node["lootTable"] = loot;

            string filename = "enemy_" + SanitizeFilename(def.id) + ".json";
            WriteJson(Path.Combine(CustomFolder, filename), node);
            Messages.Message("DNP.Export.EnemyExported".Translate(), MessageTypeDefOf.TaskCompletion, false);
        }

        public static void ExportClassTemplate(DNP_ClassData def)
        {
            EnsureFolders();
            var node = new JSONObject();
            node["id"]                  = def.id;
            node["className"]           = def.className;
            node["description"]         = def.description ?? "";
            node["flavorText"]          = def.flavorText ?? "";
            node["baseHp"]              = def.baseHp;
            node["hpPerLevel"]          = def.hpPerLevel;
            node["baseStrength"]        = def.baseStrength;
            node["baseDexterity"]       = def.baseDexterity;
            node["baseMind"]            = def.baseMind;
            node["primaryStat"]         = def.primaryStat;
            node["linkedRimWorldSkill"] = def.linkedRimWorldSkill;

            var abilities = new JSONArray();
            foreach (var a in def.abilities)
            {
                var ab = new JSONObject();
                ab["abilityName"]   = a.abilityName;
                ab["description"]   = a.description ?? "";
                ab["unlockAtLevel"] = a.unlockAtLevel;
                ab["statUsed"]      = a.statUsed;
                ab["baseDamage"]    = a.baseDamage;
                ab["targetType"]    = a.targetType;
                abilities.Add(ab);
            }
            node["abilities"] = abilities;

            string filename = "class_" + SanitizeFilename(def.id) + ".json";
            WriteJson(Path.Combine(CustomFolder, filename), node);
            Messages.Message("DNP.Export.ClassExported".Translate(), MessageTypeDefOf.TaskCompletion, false);
        }

        // ── CONTENT EDITOR HELPERS ────────────────────────────────

        private static string EnemiesFolder   => Path.Combine(RootFolder, "enemies");
        private static string ItemsFolder     => Path.Combine(RootFolder, "items");
        private static string ScenariosFolder => Path.Combine(RootFolder, "scenarios");

        private static void EnsureContentFolders()
        {
            EnsureFolders();
            if (!Directory.Exists(EnemiesFolder))   Directory.CreateDirectory(EnemiesFolder);
            if (!Directory.Exists(ItemsFolder))     Directory.CreateDirectory(ItemsFolder);
            if (!Directory.Exists(ScenariosFolder)) Directory.CreateDirectory(ScenariosFolder);
        }

        public static void ExportEnemy(DNP_EnemyData def)
        {
            EnsureContentFolders();
            var node = new JSONObject();
            node["id"]           = def.id;
            node["enemyName"]    = def.enemyName;
            node["hp"]           = def.hp;
            node["armor"]        = def.armor;
            node["attackDamage"] = def.attackDamage;
            node["attackBonus"]  = def.attackBonus;
            node["behaviorTag"]  = def.behaviorTag;
            node["xpReward"]     = def.xpReward;
            var loot = new JSONArray();
            foreach (var l in def.lootTable ?? new List<DNP_LootEntry>())
            { var ln = new JSONObject(); ln["itemId"] = l.itemId; ln["dropChance"] = l.dropChance; loot.Add(ln); }
            node["lootTable"] = loot;
            WriteJson(Path.Combine(EnemiesFolder, SanitizeFilename(def.id) + ".json"), node);
        }

        public static void DeleteEnemy(string id)
        {
            string path = Path.Combine(EnemiesFolder, SanitizeFilename(id) + ".json");
            if (File.Exists(path)) File.Delete(path);
        }

        public static void ExportItem(DNP_ItemData item)
        {
            EnsureContentFolders();
            var node = new JSONObject();
            node["id"]               = item.id;
            node["itemName"]         = item.itemName;
            node["itemType"]         = item.itemType;
            node["consumableEffect"] = item.consumableEffect ?? "";
            node["consumableValue"]  = item.consumableValue;
            node["damageBonus"]      = item.damageBonus;
            node["armorBonus"]       = item.armorBonus;
            WriteJson(Path.Combine(ItemsFolder, SanitizeFilename(item.id) + ".json"), node);
        }

        public static void DeleteItem(string id)
        {
            string path = Path.Combine(ItemsFolder, SanitizeFilename(id) + ".json");
            if (File.Exists(path)) File.Delete(path);
        }

        public static void ExportScenario(DNP_ScenarioData s)
        {
            EnsureContentFolders();
            var node = new JSONObject();
            node["id"]               = s.id;
            node["scenarioTitle"]    = s.scenarioTitle;
            node["openingNarration"] = s.openingNarration ?? "";
            var encounters = new JSONArray();
            foreach (var enc in s.encounters ?? new List<DNP_EncounterTemplate>())
            {
                var en = new JSONObject();
                en["encounterName"] = enc.encounterName ?? "";
                var enemies = new JSONArray();
                foreach (var sp in enc.enemies ?? new List<DNP_EnemySpawnEntry>())
                {
                    var se = new JSONObject();
                    se["enemyId"] = sp.enemyId;
                    se["count"]   = sp.count;
                    enemies.Add(se);
                }
                en["enemies"] = enemies;
                encounters.Add(en);
            }
            node["encounters"] = encounters;
            WriteJson(Path.Combine(ScenariosFolder, SanitizeFilename(s.id) + ".json"), node);
        }

        public static void DeleteScenario(string id)
        {
            string path = Path.Combine(ScenariosFolder, SanitizeFilename(id) + ".json");
            if (File.Exists(path)) File.Delete(path);
        }

        // ── List available files ───────────────────────────────

        // ── WORLD EXPORT / IMPORT ─────────────────────────────

        public static void ExportWorld(DNP_WorldData world)
        {
            EnsureFolders();

            // FIX: previously the filename included a timestamp
            // (yyyyMMdd_HHmm), so calling ExportWorld twice for the SAME
            // world — e.g. opening the editor, making no changes, and
            // closing it — created a brand new file each time instead of
            // overwriting the existing one. That's exactly what produced
            // duplicate "Las Tierras Olvidadas" entries in the world list.
            //
            // Worlds now get a stable id the first time they're saved
            // (based on the sanitized name, with NO timestamp), and that
            // id is reused on every subsequent save so the same world
            // always writes to the same file, regardless of how many times
            // the editor is opened and closed.
            if (string.IsNullOrEmpty(world.id))
                world.id = SanitizeFilename(world.worldName);

            var node = new JSONObject();
            node["id"]                = world.id;
            node["worldName"]         = world.worldName;
            node["genre"]             = world.genre;
            node["tone"]              = world.tone;
            node["summary"]           = world.summary;
            node["history"]           = world.history;
            node["factions"]          = world.factions;
            node["locations"]         = world.locations;
            node["rules"]             = world.rules;
            node["aiInstructions"]    = world.aiInstructions;
            node["campaignName"]      = world.campaignName;
            node["campaignObjective"] = world.campaignObjective;
            // campaignNotes intentionally excluded — private DM notes stay local

            string filename = "world_" + world.id + ".json";
            string path = Path.Combine(WorldFolder, filename);

            WriteJson(path, node);
            Messages.Message("DNP.Export.WorldExported".Translate(path),
                MessageTypeDefOf.TaskCompletion, false);
        }

        public static DNP_WorldData ImportWorld(string path)
        {
            string raw = ReadJson(path);
            if (raw == null) return null;

            var node = JSON.Parse(raw);
            if (node == null)
            {
                Log.Error("[DungeonsAndPawns] Failed to parse world JSON: " + path);
                return null;
            }

            // FIX: fall back to deriving the id from the filename for any
            // world files saved before this fix (no "id" field present yet
            // in older JSON files), so existing worlds keep loading
            // correctly and get a proper stable id from here on once
            // re-saved through the editor.
            string id = node["id"];
            if (string.IsNullOrEmpty(id))
            {
                string fname = Path.GetFileNameWithoutExtension(path);
                id = fname.StartsWith("world_") ? fname.Substring(6) : fname;
            }

            return new DNP_WorldData
            {
                id                = id,
                worldName         = node["worldName"],
                genre             = node["genre"],
                tone              = node["tone"],
                summary           = node["summary"],
                history           = node["history"],
                factions          = node["factions"],
                locations         = node["locations"],
                rules             = node["rules"],
                aiInstructions    = node["aiInstructions"],
                campaignName      = node["campaignName"],
                campaignObjective = node["campaignObjective"]
                // campaignNotes not imported — it's private
            };
        }

        public static System.Collections.Generic.List<string> GetWorldFiles()
        {
            EnsureFolders();
            var files = new System.Collections.Generic.List<string>(
                Directory.GetFiles(WorldFolder, "*.json"));
            files.Sort();
            return files;
        }

        public static List<string> GetSessionFiles()
        {
            EnsureFolders();
            var files = new List<string>(Directory.GetFiles(SessionsFolder, "*.json"));
            files.Sort();
            files.Reverse(); // newest first
            return files;
        }

        public static List<string> GetCharacterFiles()
        {
            EnsureFolders();
            var files = new List<string>(Directory.GetFiles(CharactersFolder, "*.json"));
            files.Sort();
            return files;
        }

        public static List<string> GetCustomFiles()
        {
            EnsureFolders();
            var files = new List<string>(Directory.GetFiles(CustomFolder, "*.json"));
            files.Sort();
            return files;
        }

        // ── Internal serialization helpers ─────────────────────

        private static JSONNode SerializeCharacter(DNP_PlayerCharacter pc)
        {
            var node = new JSONObject();
            node["characterName"]  = pc.characterName;
            node["classId"]        = pc.classId;
            node["pawnName"]       = pc.pawn?.Name?.ToStringShort ?? "";
            node["pawnId"]         = pc.pawn?.ThingID ?? "";         // ThingID for re-linking
            node["hp"]             = pc.hp;
            node["maxHp"]          = pc.maxHp;
            node["level"]          = pc.level;
            node["xp"]             = pc.xp;
            node["statStrength"]   = pc.statStrength;
            node["statDexterity"]  = pc.statDexterity;
            node["statMind"]       = pc.statMind;
            node["playstyleHint"]  = pc.GetPlaystyleHint();

            // Pawn context snapshot (for Player2 prompts later)
            node["pawnContext"] = DNP_SessionManager.BuildPawnContext(pc.pawn);

            var items = new JSONArray();
            foreach (var item in pc.inventory ?? new System.Collections.Generic.List<string>()) items.Add(item);
            node["items"] = items;

            var status = new JSONArray();
            foreach (var s in pc.activeStatusEffects) status.Add(s);
            node["statusEffects"] = status;

            return node;
        }

        private static DNP_PlayerCharacter DeserializeCharacter(JSONNode node)
        {
            var pc = new DNP_PlayerCharacter
            {
                characterName = node["characterName"],
                classId       = node["classId"],
                hp            = node["hp"].AsInt,
                maxHp         = node["maxHp"].AsInt,
                level         = node["level"].AsInt,
                xp            = node["xp"].AsInt,
                statStrength  = node["statStrength"].AsInt,
                statDexterity = node["statDexterity"].AsInt,
                statMind      = node["statMind"].AsInt
            };

            // Try to re-link pawn by ThingID
            string thingId = node["pawnId"];
            if (!string.IsNullOrEmpty(thingId))
            {
                var map = Find.CurrentMap;
                if (map != null)
                {
                    foreach (var colonist in map.mapPawns.FreeColonists)
                    {
                        if (colonist.ThingID == thingId)
                        {
                            pc.pawn = colonist;
                            break;
                        }
                    }
                }
            }

            foreach (JSONNode item in node["items"].AsArray)
            {
                if (pc.inventory == null)
                    pc.inventory = new System.Collections.Generic.List<string>();
                pc.inventory.Add(item.Value);
            }

            foreach (JSONNode s in node["statusEffects"].AsArray)
                pc.activeStatusEffects.Add(s.Value);

            return pc;
        }

        // ── File I/O ───────────────────────────────────────────

        private static void WriteJson(string path, JSONNode node)
        {
            try
            {
                File.WriteAllText(path, node.ToString(2)); // indent=2 for readability
                Log.Message("[DungeonsAndPawns] JSON written to: " + path);
            }
            catch (Exception ex)
            {
                Log.Error("[DungeonsAndPawns] Failed to write JSON: " + ex.Message);
            }
        }

        private static string ReadJson(string path)
        {
            try
            {
                if (!File.Exists(path))
                {
                    Log.Error("[DungeonsAndPawns] File not found: " + path);
                    return null;
                }
                return File.ReadAllText(path);
            }
            catch (Exception ex)
            {
                Log.Error("[DungeonsAndPawns] Failed to read JSON: " + ex.Message);
                return null;
            }
        }

        private static string SanitizeFilename(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name.Length > 40 ? name.Substring(0, 40) : name;
        }
    }
}