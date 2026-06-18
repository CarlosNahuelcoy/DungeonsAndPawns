using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using RimWorld;

namespace DungeonsAndPawns
{
    // ─────────────────────────────────────────────────────────────
    // GAME COMPONENT — one per save. Holds all session state.
    // ─────────────────────────────────────────────────────────────
    public class DNP_GameComponent : GameComponent
    {
        // Correct accessor — Current.Game, not Find.Game (matches EchoColony pattern)
        public static DNP_GameComponent Instance =>
            Current.Game?.GetComponent<DNP_GameComponent>();

        public DNP_Session              ActiveSession;
        public List<DNP_SessionSummary> CompletedSessions = new List<DNP_SessionSummary>();
        public DNP_WorldData            World             = new DNP_WorldData();

        // AI Controller — not persisted, rebuilt when session loads
        [System.NonSerialized]
        public DNP_AIController AIController;

        public void InitAIController()
        {
            if (ActiveSession != null)
                AIController = new DNP_AIController(ActiveSession);
        }

        public DNP_GameComponent(Game game) { }

        public override void FinalizeInit()
        {
            base.FinalizeInit();
            // Load default world now that Current.Game is available
            DNP_ContentLoader.LoadDefaultWorld();
        }

        public override void ExposeData()
        {
            Scribe_Deep.Look(ref ActiveSession,             "activeSession");
            Scribe_Collections.Look(ref CompletedSessions, "completedSessions", LookMode.Deep);
            Scribe_Deep.Look(ref World,                     "world");
            if (World == null) World = new DNP_WorldData(); // safety for old saves
        }
    }

    // ─────────────────────────────────────────────────────────────
    // SESSION MANAGER — static helpers used throughout the mod
    // ─────────────────────────────────────────────────────────────
    public static class DNP_SessionManager
    {
        public static DNP_Session ActiveSession => DNP_GameComponent.Instance?.ActiveSession;

        // ── Start ──────────────────────────────────────────────

        public static DNP_Session StartSession(List<Pawn> players, bool playerIsDM, Pawn dmPawn,
                                                string rulesetId, string scenarioId = null,
                                                Dictionary<string, string> classAssignments = null,
                                                Pawn playerCharPawn = null)
        {
            var comp = DNP_GameComponent.Instance;
            if (comp == null) return null;

            var ruleset = DNP_ContentRegistry.GetRuleset(rulesetId) ?? DNP_ContentRegistry.FirstRuleset;
            if (ruleset == null)
            {
                Log.Error("[DungeonsAndPawns] No ruleset found — cannot start session.");
                return null;
            }

            var session = new DNP_Session
            {
                sessionId       = Guid.NewGuid().ToString(),
                rulesetId       = ruleset.id,
                scenarioId      = scenarioId,
                isActive        = true,
                playerIsDM      = playerIsDM,
                dmPawn          = playerIsDM ? null : dmPawn,
                turnIndex       = 0
            };

            foreach (var pawn in players)
            {
                string forceClass = null;
                if (classAssignments != null)
                    classAssignments.TryGetValue(pawn.ThingID, out forceClass);
                var pc = BuildCharacter(pawn, ruleset, forceClass);
                // Mark the human player's chosen character
                if (playerCharPawn != null && pawn == playerCharPawn)
                    pc.isPlayerControlled = true;
                else if (playerIsDM && players.Count == 1)
                    pc.isPlayerControlled = false; // DM mode, no player char
                session.characters.Add(pc);
            }

            string opener = "DNP.Log.OpeningNarration".Translate();
            if (!string.IsNullOrEmpty(scenarioId))
            {
                var sc = DNP_ContentRegistry.GetScenario(scenarioId);
                if (sc != null && !string.IsNullOrEmpty(sc.openingNarration))
                    opener = sc.openingNarration;
            }

            AddLog(session, DNP_LogEntry.EntryType.DM, "DNP.Log.SpeakerDM".Translate(), opener);

            comp.ActiveSession = session;
            comp.InitAIController();
            comp.AIController?.StartSession();
            return session;
        }

        // ── End ────────────────────────────────────────────────

        public static void EndSession(bool success)
        {
            var comp = DNP_GameComponent.Instance;
            if (comp?.ActiveSession == null) return;

            var session = comp.ActiveSession;
            session.isActive = false;

            // Apply mood buffs/debuffs to all participants
            var ruleset = DNP_ContentRegistry.GetRuleset(session.rulesetId);
            if (ruleset != null && (success ? ruleset.applyMoodBuffs : ruleset.applyMoodDebuffs))
            {
                string    thoughtName = success ? ruleset.successThoughtDef : ruleset.failureThoughtDef;
                ThoughtDef thoughtDef = DefDatabase<ThoughtDef>.GetNamedSilentFail(thoughtName);
                if (thoughtDef != null)
                {
                    foreach (var pc in session.characters)
                    {
                        if (pc.pawn != null && !pc.pawn.Dead)
                            pc.pawn.needs?.mood?.thoughts?.memories?.TryGainMemory(thoughtDef);
                    }
                }
            }

            // Archive summary
            comp.CompletedSessions.Add(new DNP_SessionSummary
            {
                sessionId    = session.sessionId,
                rulesetDef   = session.rulesetId,
                success      = success,
                tick         = Find.TickManager?.TicksGame ?? 0,
                participants = session.characters
                    .Select(c => c.pawn?.Name?.ToStringShort ?? "Unknown").ToList()
            });

            comp.ActiveSession = null;
        }

        // ── Log ────────────────────────────────────────────────

        // Named AddEntry to avoid collision with Verse.Log static class
        // ── Loot distribution ─────────────────────────────────────

        public static void DistributeLoot(DNP_Session session, DNP_Encounter encounter)
        {
            if (session == null || encounter == null) return;
            var lootGained = new System.Collections.Generic.List<string>();

            foreach (var enemy in encounter.enemies)
            {
                var def = DNP_ContentRegistry.GetEnemy(enemy.enemyId);
                if (def?.lootTable == null) continue;
                foreach (var loot in def.lootTable)
                {
                    if (Rand.Value <= loot.dropChance)
                    {
                        // Give to a random living character
                        var living = session.characters.Where(c => c.hp > 0).ToList();
                        if (living.Any())
                        {
                            var recipient = living[Rand.RangeInclusive(0, living.Count - 1)];
                            if (recipient.inventory == null)
                                recipient.inventory = new System.Collections.Generic.List<string>();
                            recipient.inventory.Add(loot.itemId);
                            var itemDef = DNP_ContentRegistry.GetItem(loot.itemId);
                            lootGained.Add(recipient.characterName + " found "
                                + (itemDef?.itemName ?? loot.itemId));
                        }
                    }
                }
            }

            if (lootGained.Any())
                AddEntry(DNP_LogEntry.EntryType.System, "System",
                    "DNP.Log.LootFound".Translate() + " " + string.Join(", ", lootGained) + ".");
        }

        public static void AddEntry(DNP_LogEntry.EntryType type, string speaker, string text)
        {
            var session = DNP_GameComponent.Instance?.ActiveSession;
            if (session != null) AddLog(session, type, speaker, text);
        }

        // Adds a temporary "thinking..." entry and returns it so it can be removed later
        public static DNP_LogEntry AddEntryTemp(DNP_LogEntry.EntryType type, string speaker, string text)
        {
            var session = DNP_GameComponent.Instance?.ActiveSession;
            if (session == null) return null;
            var entry = new DNP_LogEntry
            {
                entryType   = type,
                speakerName = speaker,
                text        = text,
                tick        = Find.TickManager?.TicksGame ?? 0
            };
            session.sessionLog.Add(entry);
            return entry;
        }

        public static void RemoveTempEntry(DNP_LogEntry entry)
        {
            if (entry == null) return;
            var session = DNP_GameComponent.Instance?.ActiveSession;
            session?.sessionLog.Remove(entry);
        }

        private static void AddLog(DNP_Session session, DNP_LogEntry.EntryType type,
                                    string speaker, string text)
        {
            session.sessionLog.Add(new DNP_LogEntry
            {
                entryType   = type,
                speakerName = speaker,
                text        = text,
                tick        = Find.TickManager?.TicksGame ?? 0
            });
        }

        // ── Character building ──────────────────────────────────

        // Build character using DNP_ClassRegistry (JSON-loaded classes)
        private static DNP_PlayerCharacter BuildCharacter(Pawn pawn, DNP_RulesetData ruleset,
                                                           string forceClassId = null)
        {
            var cls = !string.IsNullOrEmpty(forceClassId)
                ? DNP_ClassRegistry.Get(forceClassId)
                : DNP_ClassRegistry.SuggestForPawn(pawn);

            var pc = new DNP_PlayerCharacter
            {
                pawn          = pawn,
                characterName = pawn.Name?.ToStringShort ?? "Adventurer",
                classId       = cls?.id ?? "",
                level         = 1,
                xp            = 0
            };

            if (cls != null)
            {
                pc.maxHp         = cls.baseHp;
                pc.hp            = pc.maxHp;
                pc.statStrength  = cls.baseStrength;
                pc.statDexterity = cls.baseDexterity;
                pc.statMind      = cls.baseMind;
                if (ruleset.useColonistSkillBonuses) ApplySkillBonuses(pawn, pc, cls);
            }
            else
            {
                pc.maxHp = pc.hp = 10;
                pc.statStrength = pc.statDexterity = pc.statMind = 5;
            }

            return pc;
        }

        private static void ApplySkillBonuses(Pawn pawn, DNP_PlayerCharacter pc, DNP_ClassData cls)
        {
            if (string.IsNullOrEmpty(cls.linkedRimWorldSkill)) return;
            var skillDef = DefDatabase<SkillDef>.GetNamedSilentFail(cls.linkedRimWorldSkill);
            if (skillDef == null) return;

            int bonus = (pawn.skills?.GetSkill(skillDef)?.Level ?? 0) / 4;
            switch (cls.primaryStat)
            {
                case "Strength":  pc.statStrength  += bonus; break;
                case "Dexterity": pc.statDexterity += bonus; break;
                case "Mind":      pc.statMind      += bonus; break;
            }
        }

        // ── Pawn context helpers (used by Player2 prompts later) ─

        /// <summary>
        /// Builds a plain-text summary of a pawn's state.
        /// Mirrors the EchoColony BuildContext pattern.
        /// </summary>
        public static string BuildPawnContext(Pawn pawn)
        {
            if (pawn == null) return "";
            var sb = new System.Text.StringBuilder();

            // Name + class hint
            sb.AppendLine("Name: " + (pawn.Name?.ToStringShort ?? "Unknown"));

            // Skills (top 3)
            if (pawn.skills != null)
            {
                var topSkills = pawn.skills.skills
                    .OrderByDescending(s => s.Level)
                    .Take(3)
                    .Select(s => s.def.label + " " + s.Level);
                sb.AppendLine("Top skills: " + string.Join(", ", topSkills));
            }

            // Traits — by defName (safe pattern from EchoColony)
            if (pawn.story?.traits != null)
            {
                var traitLabels = pawn.story.traits.allTraits
                    .Select(t => t.LabelCap);
                sb.AppendLine("Traits: " + string.Join(", ", traitLabels));
            }

            // Mood — pawn.needs.mood.CurLevel (0-1)
            float mood = pawn.needs?.mood?.CurLevel ?? 0.5f;
            string moodDesc = mood >= 0.7f ? "good mood"
                            : mood >= 0.4f ? "okay"
                                           : "bad mood";
            sb.AppendLine("Mood: " + moodDesc + " (" + (mood * 100f).ToString("F0") + "%)");

            // Health
            float health = pawn.health?.summaryHealth?.SummaryHealthPercent ?? 1f;
            if (health < 0.8f)
                sb.AppendLine("Health: " + (health * 100f).ToString("F0") + "%");

            // Backstory
            if (pawn.story != null)
            {
                var childhood = pawn.story.AllBackstories
                    .FirstOrDefault(b => b.slot == BackstorySlot.Childhood);
                var adulthood = pawn.story.AllBackstories
                    .FirstOrDefault(b => b.slot == BackstorySlot.Adulthood);
                if (childhood != null || adulthood != null)
                    sb.AppendLine("Backstory: "
                        + (childhood?.title ?? "?") + " / "
                        + (adulthood?.title ?? "?"));
            }

            return sb.ToString().Trim();
        }
    }
}