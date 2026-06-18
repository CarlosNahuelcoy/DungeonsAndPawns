using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SimpleJSON;
using Verse;
using RimWorld;

namespace DungeonsAndPawns
{
    // ─────────────────────────────────────────────────────────────
    // CLASS DATA — runtime representation of a playable class.
    // Loaded from JSON, not from RimWorld's DefDatabase.
    // ─────────────────────────────────────────────────────────────
    public class DNP_ClassData
    {
        public string id;           // unique key: "warrior", "mage", etc.
        public string className;
        public string description;
        public string flavorText;
        // How this archetype behaves narratively — used by DM-AI and colonist-AI
        // in both combat AND roleplay. Describes personality, speech style, fighting approach.
        public string aiNarrativeStyle = "";

        public int   baseHp        = 10;
        public int   hpPerLevel    = 4;
        public int   baseStrength  = 5;
        public int   baseDexterity = 5;
        public int   baseMind      = 5;

        public string primaryStat         = "Strength"; // "Strength", "Dexterity", "Mind"
        public string linkedRimWorldSkill = "";

        public List<DNP_AbilityData> abilities = new List<DNP_AbilityData>();

        // Which stat value is primary for this class
        public int GetPrimaryStat(DNP_PlayerCharacter pc)
        {
            switch (primaryStat)
            {
                case "Dexterity": return pc.statDexterity;
                case "Mind":      return pc.statMind;
                default:          return pc.statStrength;
            }
        }
    }

    public class DNP_AbilityData
    {
        public string abilityName;
        public string description;
        public int    unlockAtLevel = 1;
        public string statUsed      = "Strength";
        public int    baseDamage    = 0;
        public string targetType    = "Enemy"; // "Enemy", "Ally", "Self"
    }

    // ─────────────────────────────────────────────────────────────
    // CLASS REGISTRY — in-memory store, loaded once at startup.
    // Access via DNP_ClassRegistry.Get("warrior") or .All
    // ─────────────────────────────────────────────────────────────
    public static class DNP_ClassRegistry
    {
        private static List<DNP_ClassData> _classes = new List<DNP_ClassData>();
        public  static bool IsLoaded { get; private set; } = false;

        public static IReadOnlyList<DNP_ClassData> All => _classes;

        public static DNP_ClassData Get(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            return _classes.FirstOrDefault(c =>
                string.Equals(c.id, id, StringComparison.OrdinalIgnoreCase));
        }

        public static DNP_ClassData GetByName(string className)
        {
            if (string.IsNullOrEmpty(className)) return null;
            return _classes.FirstOrDefault(c =>
                string.Equals(c.className, className, StringComparison.OrdinalIgnoreCase));
        }

        // Called by DNP_ClassLoader — replaces the entire list
        public static void SetClasses(List<DNP_ClassData> classes)
        {
            _classes  = classes ?? new List<DNP_ClassData>();
            IsLoaded  = true;
            Log.Message("[DungeonsAndPawns] Class registry loaded: "
                + _classes.Count + " classes.");
        }

        // Pick the best class for a pawn based on their skills
        public static DNP_ClassData SuggestForPawn(Pawn pawn)
        {
            if (!_classes.Any()) return null;
            if (pawn?.skills == null) return _classes.First();

            int melee    = pawn.skills.GetSkill(SkillDefOf.Melee)?.Level        ?? 0;
            int shooting = pawn.skills.GetSkill(SkillDefOf.Shooting)?.Level     ?? 0;
            int intel    = pawn.skills.GetSkill(SkillDefOf.Intellectual)?.Level  ?? 0;
            int medicine = pawn.skills.GetSkill(SkillDefOf.Medicine)?.Level      ?? 0;
            int social   = pawn.skills.GetSkill(SkillDefOf.Social)?.Level        ?? 0;

            // Score each class by its linked skill
            DNP_ClassData best  = null;
            int           bestScore = -1;

            foreach (var cls in _classes)
            {
                int score = 0;
                switch (cls.linkedRimWorldSkill)
                {
                    case "Melee":        score = melee;    break;
                    case "Shooting":     score = shooting; break;
                    case "Intellectual": score = intel;    break;
                    case "Medicine":     score = medicine; break;
                    case "Social":       score = social;   break;
                }
                if (score > bestScore) { bestScore = score; best = cls; }
            }

            return best ?? _classes.First();
        }
    }

    // ─────────────────────────────────────────────────────────────
    // CLASS LOADER — reads JSON files from disk at startup.
    // Falls back to embedded defaults if no files found.
    // ─────────────────────────────────────────────────────────────
    public static class DNP_ClassLoader
    {
        private static string ClassesFolder =>
            Path.Combine(GenFilePaths.SaveDataFolderPath, "DungeonsAndPawns", "classes");

        // Called once from DNP_Startup
        public static void Load()
        {
            EnsureFolder();
            CopyDefaultsIfEmpty();

            var classes = new List<DNP_ClassData>();

            foreach (var file in Directory.GetFiles(ClassesFolder, "*.json"))
            {
                try
                {
                    string raw     = File.ReadAllText(file);
                    var    node    = JSON.Parse(raw);
                    if (node == null) { Log.Warning("[DungeonsAndPawns] Skipping invalid JSON: " + file); continue; }

                    var cls = ParseClass(node);
                    if (cls == null || string.IsNullOrEmpty(cls.id)) continue;

                    classes.Add(cls);
                }
                catch (Exception ex)
                {
                    Log.Error("[DungeonsAndPawns] Error loading class file " + file + ": " + ex.Message);
                }
            }

            // If nothing loaded, force-write embedded defaults and try again
            if (classes.Count == 0)
            {
                Log.Warning("[DungeonsAndPawns] No classes loaded from disk — forcing embedded defaults.");
                WriteEmbeddedDefaults();
                // Try loading again
                foreach (var file in Directory.GetFiles(ClassesFolder, "*.json"))
                {
                    try
                    {
                        var node = JSON.Parse(File.ReadAllText(file));
                        if (node == null) continue;
                        var cls = ParseClass(node);
                        if (cls != null && !string.IsNullOrEmpty(cls.id))
                            classes.Add(cls);
                    }
                    catch { }
                }
            }

            DNP_ClassRegistry.SetClasses(classes);
            Log.Message("[DungeonsAndPawns] Final class count: " + classes.Count);
        }

        // Save a single class back to disk (for the in-game editor)
        public static void Save(DNP_ClassData cls)
        {
            EnsureFolder();
            string path = Path.Combine(ClassesFolder, cls.id + ".json");
            File.WriteAllText(path, Serialize(cls).ToString(2));
            Log.Message("[DungeonsAndPawns] Class saved: " + path);
        }

        // Delete a class file from disk
        public static void Delete(DNP_ClassData cls)
        {
            string path = Path.Combine(ClassesFolder, cls.id + ".json");
            if (File.Exists(path)) File.Delete(path);
        }

        public static string GetClassesFolder() => ClassesFolder;

        // ── Private helpers ────────────────────────────────────

        private static void EnsureFolder()
        {
            if (!Directory.Exists(ClassesFolder))
                Directory.CreateDirectory(ClassesFolder);
        }

        // Copy default JSONs from mod folder to save folder — only if folder is empty
        private static void CopyDefaultsIfEmpty()
        {
            if (Directory.GetFiles(ClassesFolder, "*.json").Length > 0) return;

            Log.Message("[DungeonsAndPawns] Classes folder is empty — looking for defaults.");
            Log.Message("[DungeonsAndPawns] Classes folder path: " + ClassesFolder);

            // Try multiple ways to find the mod folder
            string modFolder = null;

            // Method 1: exact PackageId match (case insensitive)
            var mod = LoadedModManager.RunningMods.FirstOrDefault(m =>
                string.Equals(m.PackageId, "CarlosNahuelcoy.DungeonsAndPawns",
                    StringComparison.OrdinalIgnoreCase));

            if (mod != null)
            {
                modFolder = mod.RootDir;
                Log.Message("[DungeonsAndPawns] Found mod folder via PackageId: " + modFolder);
            }

            // Method 2: search by folder name if PackageId failed
            if (modFolder == null)
            {
                mod = LoadedModManager.RunningMods.FirstOrDefault(m =>
                    m.RootDir != null && m.RootDir.IndexOf("DungeonsAndPawns",
                        StringComparison.OrdinalIgnoreCase) >= 0);
                if (mod != null)
                {
                    modFolder = mod.RootDir;
                    Log.Message("[DungeonsAndPawns] Found mod folder via folder name: " + modFolder);
                }
            }

            if (modFolder == null)
            {
                Log.Warning("[DungeonsAndPawns] Could not find mod folder. Loaded mods: "
                    + string.Join(", ", LoadedModManager.RunningMods.Select(m => m.PackageId)));
                WriteEmbeddedDefaults();
                return;
            }

            string defaultsPath = Path.Combine(modFolder, "DefaultContent", "classes");
            Log.Message("[DungeonsAndPawns] Looking for defaults at: " + defaultsPath);

            if (!Directory.Exists(defaultsPath))
            {
                Log.Warning("[DungeonsAndPawns] DefaultContent/classes not found at: " + defaultsPath
                    + " — writing embedded defaults.");
                WriteEmbeddedDefaults();
                return;
            }

            int copied = 0;
            foreach (var file in Directory.GetFiles(defaultsPath, "*.json"))
            {
                string dest = Path.Combine(ClassesFolder, Path.GetFileName(file));
                if (!File.Exists(dest)) { File.Copy(file, dest); copied++; }
            }

            Log.Message("[DungeonsAndPawns] Copied " + copied + " default class files to: " + ClassesFolder);

            // If nothing was copied (e.g. DefaultContent folder exists but is empty), use embedded
            if (copied == 0 && Directory.GetFiles(ClassesFolder, "*.json").Length == 0)
            {
                Log.Warning("[DungeonsAndPawns] No files copied — writing embedded defaults.");
                WriteEmbeddedDefaults();
            }
        }

        // Hard-coded fallback in case the mod folder can't be found
        private static void WriteEmbeddedDefaults()
        {
            var defaults = GetEmbeddedDefaults();
            foreach (var cls in defaults)
            {
                string path = Path.Combine(ClassesFolder, cls.id + ".json");
                if (!File.Exists(path))
                    File.WriteAllText(path, Serialize(cls).ToString(2));
            }
        }

        private static DNP_ClassData ParseClass(JSONNode node)
        {
            var cls = new DNP_ClassData
            {
                id                = node["id"],
                className         = node["className"],
                description       = node["description"],
                flavorText        = node["flavorText"],
                baseHp            = node["baseHp"].AsInt,
                hpPerLevel        = node["hpPerLevel"].AsInt,
                baseStrength      = node["baseStrength"].AsInt,
                baseDexterity     = node["baseDexterity"].AsInt,
                baseMind          = node["baseMind"].AsInt,
                primaryStat       = node["primaryStat"],
                linkedRimWorldSkill = node["linkedRimWorldSkill"],
                aiNarrativeStyle    = node["aiNarrativeStyle"] ?? ""
            };

            if (cls.baseHp     == 0) cls.baseHp     = 10;
            if (cls.hpPerLevel == 0) cls.hpPerLevel = 4;

            foreach (JSONNode a in node["abilities"].AsArray)
            {
                cls.abilities.Add(new DNP_AbilityData
                {
                    abilityName   = a["abilityName"],
                    description   = a["description"],
                    unlockAtLevel = a["unlockAtLevel"].AsInt,
                    statUsed      = a["statUsed"],
                    baseDamage    = a["baseDamage"].AsInt,
                    targetType    = a["targetType"]
                });
            }

            return cls;
        }

        public static JSONNode Serialize(DNP_ClassData cls)
        {
            var node = new JSONObject();
            node["id"]                  = cls.id;
            node["className"]           = cls.className;
            node["description"]         = cls.description ?? "";
            node["flavorText"]          = cls.flavorText  ?? "";
            node["baseHp"]              = cls.baseHp;
            node["hpPerLevel"]          = cls.hpPerLevel;
            node["baseStrength"]        = cls.baseStrength;
            node["baseDexterity"]       = cls.baseDexterity;
            node["baseMind"]            = cls.baseMind;
            node["primaryStat"]         = cls.primaryStat;
            node["linkedRimWorldSkill"] = cls.linkedRimWorldSkill ?? "";
            node["aiNarrativeStyle"]    = cls.aiNarrativeStyle    ?? "";

            var abilities = new JSONArray();
            foreach (var a in cls.abilities)
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
            return node;
        }

        // Embedded defaults — used only if mod folder and DefaultContent are missing
        private static List<DNP_ClassData> GetEmbeddedDefaults()
        {
            return new List<DNP_ClassData>
            {
                new DNP_ClassData { id="warrior", className="Warrior",
                    description="A frontline fighter. High HP, strong melee attacks.",
                    flavorText="Where others hesitate, the Warrior charges.",
                    baseHp=14, hpPerLevel=5, baseStrength=8, baseDexterity=4, baseMind=3,
                    primaryStat="Strength", linkedRimWorldSkill="Melee" },
                new DNP_ClassData { id="ranger", className="Ranger",
                    description="A swift attacker. Favors speed and precision over brute force.",
                    flavorText="The arrow finds its mark before the enemy hears the bowstring.",
                    baseHp=10, hpPerLevel=4, baseStrength=4, baseDexterity=8, baseMind=4,
                    primaryStat="Dexterity", linkedRimWorldSkill="Shooting" },
                new DNP_ClassData { id="mage", className="Mage",
                    description="A wielder of arcane power. Low HP but devastating spells.",
                    flavorText="Reality is just a suggestion, and the Mage disagrees with it.",
                    baseHp=6, hpPerLevel=3, baseStrength=2, baseDexterity=4, baseMind=10,
                    primaryStat="Mind", linkedRimWorldSkill="Intellectual" },
                new DNP_ClassData { id="cleric", className="Cleric",
                    description="A devoted healer and support caster. Keeps the party alive.",
                    flavorText="Faith is armor. Compassion is a weapon.",
                    baseHp=10, hpPerLevel=4, baseStrength=4, baseDexterity=3, baseMind=8,
                    primaryStat="Mind", linkedRimWorldSkill="Medicine" },
                new DNP_ClassData { id="rogue", className="Rogue",
                    description="A cunning fighter who strikes from the shadows.",
                    flavorText="The best fight is the one the enemy never saw coming.",
                    baseHp=8, hpPerLevel=3, baseStrength=4, baseDexterity=9, baseMind=5,
                    primaryStat="Dexterity", linkedRimWorldSkill="Social" },
                new DNP_ClassData { id="paladin", className="Paladin",
                    description="A heavily armored warrior with healing abilities.",
                    flavorText="Steel and faith. Neither bends.",
                    baseHp=13, hpPerLevel=5, baseStrength=7, baseDexterity=3, baseMind=6,
                    primaryStat="Strength", linkedRimWorldSkill="Medicine" },
            };
        }
    }
}