using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using RimWorld;

namespace DungeonsAndPawns
{
    // ─────────────────────────────────────────────────────────────
    // SESSION — full active game state
    // ─────────────────────────────────────────────────────────────
    public class DNP_Session : IExposable
    {
        public string sessionId;
        public string rulesetId;
        public string scenarioId;

        public bool isActive;
        public bool playerIsDM;
        public Pawn dmPawn;

        public List<DNP_PlayerCharacter> characters = new List<DNP_PlayerCharacter>();
        public List<DNP_LogEntry>        sessionLog = new List<DNP_LogEntry>();

        public DNP_Encounter activeEncounter;
        public int turnIndex;

        public void ExposeData()
        {
            Scribe_Values.Look(ref sessionId,       "sessionId");
            Scribe_Values.Look(ref rulesetId,       "rulesetId");
            Scribe_Values.Look(ref scenarioId,      "scenarioId");
            Scribe_Values.Look(ref isActive,        "isActive");
            Scribe_Values.Look(ref playerIsDM,      "playerIsDM");
            Scribe_References.Look(ref dmPawn,      "dmPawn");
            Scribe_Collections.Look(ref characters, "characters", LookMode.Deep);
            Scribe_Collections.Look(ref sessionLog, "sessionLog", LookMode.Deep);
            Scribe_Deep.Look(ref activeEncounter,   "activeEncounter");
            Scribe_Values.Look(ref turnIndex,       "turnIndex");
        }
    }

    // ─────────────────────────────────────────────────────────────
    // PLAYER CHARACTER — one colonist's RPG sheet
    // ─────────────────────────────────────────────────────────────
    public class DNP_PlayerCharacter : IExposable
    {
        public Pawn   pawn;
        public string characterName;
        public string classId;  // matches DNP_ClassData.id
        public int    hp;
        public int    maxHp;
        public int    level;
        public int    xp;

        public int statStrength;
        public int statDexterity;
        public int statMind;

        public bool isPlayerControlled = false; // true for the human player's character

        // Inventory — list of item IDs the character is carrying
        public List<string> inventory = new List<string>();
        public List<string> activeStatusEffects = new List<string>();

        public void ExposeData()
        {
            Scribe_References.Look(ref pawn,                  "pawn");
            Scribe_Values.Look(ref characterName,             "characterName");
            Scribe_Values.Look(ref classId,                   "classId");
            Scribe_Values.Look(ref hp,                        "hp");
            Scribe_Values.Look(ref maxHp,                     "maxHp");
            Scribe_Values.Look(ref level,                     "level");
            Scribe_Values.Look(ref xp,                        "xp");
            Scribe_Values.Look(ref statStrength,              "statStrength");
            Scribe_Values.Look(ref statDexterity,             "statDexterity");
            Scribe_Values.Look(ref statMind,                  "statMind");
            Scribe_Values.Look(ref isPlayerControlled,        "isPlayerControlled",   false);
            Scribe_Collections.Look(ref inventory,            "inventory",            LookMode.Value);
            Scribe_Collections.Look(ref activeStatusEffects,  "activeStatusEffects",  LookMode.Value);
        }

        // Maps RimWorld pawn traits to a D&D playstyle hint for AI prompts.
        // Falls back to stats if no recognisable trait found.
        public string GetPlaystyleHint()
        {
            if (pawn?.story?.traits != null)
            {
                foreach (var t in pawn.story.traits.allTraits)
                {
                    // ── TraitDefOf (always safe) ──────────────────
                    if (t.def == TraitDefOf.Brawler)
                        return "aggressive and hot-headed, charges in first, fights with reckless energy";
                    if (t.def == TraitDefOf.Kind)
                        return "empathetic and gentle, tries to avoid bloodshed, protects allies";
                    if (t.def == TraitDefOf.Abrasive)
                        return "blunt and confrontational, speaks their mind, argues with authority";

                    // ── By defName ────────────────────────────────
                    switch (t.def.defName)
                    {
                        // Personality
                        case "Neurotic":
                            return "anxious and meticulous, overthinks every move, prepares for worst";
                        case "Optimistic":
                            return "bold and upbeat, takes risks, always expects things to work out";
                        case "Wimp":
                            return "timid and risk-averse, hangs back, avoids direct confrontation";
                        case "Volatile":
                            return "explosive and impulsive, acts on instinct, mood drives decisions";
                        case "Calm":
                            return "composed and measured, rarely rattled, thinks under pressure";
                        case "Masochist":
                            return "reckless about personal safety, pushes through pain, unsettling to allies";
                        case "Ascetic":
                            return "stoic and self-disciplined, minimal words, focused on the mission";
                        case "Greedy":
                            return "motivated by reward, keeps an eye on loot, negotiates hard";
                        case "Jealous":
                            return "competitive, dislikes others taking credit, needs to prove themselves";
                        case "Pyromaniac":
                            return "fascinated by destruction, suggests burning things, unpredictable in chaos";

                        // Combat
                        case "Bloodlust":
                            return "relishes combat, pushes for violence, energized by defeating enemies";
                        case "Tough":
                            return "endures punishment that would drop others, stubborn and hard to stop";
                        case "Nimble":
                            return "evasive and quick, avoids heavy hits, fights with movement";
                        case "TriggerHappy":
                            return "fires first and fast, impatient in ranged situations, sometimes reckless";
                        case "Careful":
                        case "CarefulShooter":
                            return "methodical and precise, takes time to aim, rarely wastes an action";

                        // Mental / psychic
                        case "PsychicSensitivity_High":
                        case "PsychicSensitivity":
                            return "intuitive and perceptive, senses things others miss, trusts gut feelings";
                        case "IronWilled":
                            return "mentally tough, resists fear and pressure, keeps the party grounded";
                        case "Industrious":
                            return "disciplined and hard-working, methodical, plans before acting";
                        case "Lazy":
                            return "finds shortcuts, conserves energy, waits for the right moment";
                        case "FastLearner":
                        case "SlowLearner":
                            return "adaptable, picks up on patterns quickly, adjusts tactics mid-fight";

                        // Social
                        case "Charming":
                        case "Charismatic":
                            return "persuasive and likeable, leads with words before weapons";
                        case "Misanthrope":
                            return "distrusts everyone, prefers to act alone, suspicious of motives";
                    }
                }
            }

            // ── Fallback: infer from stats ─────────────────────────
            int str = statStrength;
            int dex = statDexterity;
            int mnd = statMind;
            int top = Math.Max(str, Math.Max(dex, mnd));

            if (top == str && str >= 7)
                return "physically imposing, leads with strength, direct and forceful";
            if (top == mnd && mnd >= 7)
                return "calculative and analytical, observes before acting, relies on knowledge";
            if (top == dex && dex >= 7)
                return "agile and precise, looks for openings, avoids taking hits";

            return "pragmatic and adaptable, reads the situation and responds accordingly";
        }
    }

    // ─────────────────────────────────────────────────────────────
    // LOG ENTRY — one line of narrative history
    // ─────────────────────────────────────────────────────────────
    public class DNP_LogEntry : IExposable
    {
        public enum EntryType { DM, DMAssist, Player, System, DiceRoll, DMBriefing }

        public EntryType entryType;
        public string    speakerName;
        public string    text;
        public int       tick;

        public void ExposeData()
        {
            Scribe_Values.Look(ref entryType,   "entryType");
            Scribe_Values.Look(ref speakerName, "speakerName");
            Scribe_Values.Look(ref text,        "text");
            Scribe_Values.Look(ref tick,        "tick");
        }
    }

    // ─────────────────────────────────────────────────────────────
    // ENCOUNTER — active combat or event
    // ─────────────────────────────────────────────────────────────
    public class DNP_Encounter : IExposable
    {
        public string                   encounterName;
        public List<DNP_EnemyInstance>  enemies    = new List<DNP_EnemyInstance>();
        public bool                     isResolved;

        public void ExposeData()
        {
            Scribe_Values.Look(ref encounterName, "encounterName");
            Scribe_Collections.Look(ref enemies,  "enemies", LookMode.Deep);
            Scribe_Values.Look(ref isResolved,    "isResolved");
        }
    }

    public class DNP_EnemyInstance : IExposable
    {
        public string enemyId;
        public string instanceName;
        public int    hp;
        public int    maxHp;

        public void ExposeData()
        {
            Scribe_Values.Look(ref enemyId,      "enemyId");
            Scribe_Values.Look(ref instanceName, "instanceName");
            Scribe_Values.Look(ref hp,           "hp");
            Scribe_Values.Look(ref maxHp,        "maxHp");
        }
    }

    // ─────────────────────────────────────────────────────────────
    // SESSION SUMMARY — archived after a session ends
    // ─────────────────────────────────────────────────────────────
    public class DNP_SessionSummary : IExposable
    {
        public string       sessionId;
        public string       rulesetDef;
        public bool         success;
        public int          tick;
        public List<string> participants = new List<string>();

        public void ExposeData()
        {
            Scribe_Values.Look(ref sessionId,  "sessionId");
            Scribe_Values.Look(ref rulesetDef, "rulesetDef");
            Scribe_Values.Look(ref success,    "success");
            Scribe_Values.Look(ref tick,       "tick");
            Scribe_Collections.Look(ref participants, "participants", LookMode.Value);
        }
    }
}