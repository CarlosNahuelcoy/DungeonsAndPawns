using System.Collections.Generic;
using System.Linq;
using System.Text;
using Verse;
using RimWorld;

namespace DungeonsAndPawns
{
    // ─────────────────────────────────────────────────────────────
    // PROMPT BUILDER
    // Constructs system + user prompts for every type of AI call.
    // Design principles:
    //   1. System prompt: stable identity + world (built once per session)
    //   2. User prompt:   sliding window of recent log + current state
    //   3. Combat prompt: always includes full encounter state
    //   4. Colonist prompt: personality anchor repeated every call
    //   5. Never send full session history — max RECENT_LOG_TURNS turns
    // ─────────────────────────────────────────────────────────────
    public static class DNP_PromptBuilder
    {
        // How many recent log entries to include for context
        private const int RECENT_LOG_TURNS = 6;

        // ── Language ──────────────────────────────────────────────

        // Returns a language directive for any prompt
        private static string LanguageDirective()
        {
            string lang = (Prefs.LangFolderName ?? "English").ToLowerInvariant();
            if (lang == "english")
                return string.Empty;
            return "Language: " + lang;
        }

        // ── DM system prompt ─────────────────────────────────────
        // Built once at session start. Stable identity + world.

        public static string BuildDMSystemPrompt(DNP_Session session, DNP_TurnDirector director)
        {
            var sb  = new StringBuilder();
            var world = DNP_GameComponent.Instance?.World;

            sb.AppendLine("You are the Dungeon Master of a tabletop RPG session set in the RimWorld universe.");
            sb.AppendLine("Your role: narrate the world, voice NPCs and enemies, react to player actions, drive the story forward.");
            sb.AppendLine(LanguageDirective());
            sb.AppendLine("Keep responses to 2-4 sentences unless the moment demands more.");
            sb.AppendLine("CRITICAL RULES — never break these:");
            sb.AppendLine("1. Always narrate in THIRD PERSON. Never speak AS a player character.");
            sb.AppendLine("   WRONG: 'My lungs burn as I adjust my jacket...'");
            sb.AppendLine("   RIGHT: 'Shouta adjusts her jacket, lungs burning...'");
            sb.AppendLine("2. Never break the fourth wall or mention game mechanics.");
            sb.AppendLine("3. Never invent combat outcomes — only narrate results provided to you.");
            sb.AppendLine("4. Stay consistent with the established setting at all times.");
            sb.AppendLine("5. ROLL JUDGMENT: When a player action has uncertain outcome, emit");
            sb.AppendLine("   [ROLL:skill:context] and write ONE short setup sentence.");
            sb.AppendLine("   Never narrate the outcome before the roll. The roll resolves it.");

            // Rule 6: explicitly list party member names so the DM never NPC-tags them
            var partyNames = string.Join(", ", session.characters.Select(c => c.characterName));
            sb.AppendLine("6. PARTY MEMBERS ARE NOT NPCS. The party members are: " + partyNames + ".");
            sb.AppendLine("   NEVER use [NPC:NAME:...] for any party member name.");
            sb.AppendLine("   [NPC] is ONLY for named characters who are NOT in the party.");
            sb.AppendLine("   Party members speak for themselves — do not voice them.");
            sb.AppendLine("");
            sb.AppendLine(DNP_TagParser.BuildDMTagInstructions(session));

            // Director profile tone
            switch (director.Profile)
            {
                case DNP_DirectorProfile.Narrator:
                    sb.AppendLine("Tone: Story-first. Give players space to breathe. Focus on character and atmosphere over action.");
                    break;
                case DNP_DirectorProfile.Tactical:
                    sb.AppendLine("Tone: Combat-first. Precise and tense. Every action has weight. Danger is real and immediate.");
                    break;
                case DNP_DirectorProfile.Chaotic:
                    sb.AppendLine("Tone: Unpredictable. The world feels alive and dangerous. Events can surprise anyone.");
                    break;
            }

            // World
            if (world != null && !string.IsNullOrWhiteSpace(world.worldName)
                && world.worldName != "Unnamed World")
            {
                sb.AppendLine("\n=== WORLD ===");
                sb.Append(world.BuildAIContext());
            }

            // Campaign objective
            if (world != null && !string.IsNullOrWhiteSpace(world.campaignObjective))
            {
                sb.AppendLine("\n=== CAMPAIGN OBJECTIVE ===");
                sb.AppendLine(world.campaignObjective);
            }

            // User AI instructions
            if (world != null && !string.IsNullOrWhiteSpace(world.aiInstructions))
            {
                sb.AppendLine("\n=== NARRATOR INSTRUCTIONS ===");
                sb.AppendLine(world.aiInstructions);
            }

            // Party — compact but complete
            sb.AppendLine("\n=== PARTY ===");
            foreach (var pc in session.characters)
            {
                var cls = DNP_ClassRegistry.Get(pc.classId);
                sb.AppendLine("- " + pc.characterName
                    + " (" + (cls?.className ?? "Adventurer") + " Lv." + pc.level + ")"
                    + " — " + pc.GetPlaystyleHint()
                    + (pc.isPlayerControlled ? " [PLAYER — human controlled]" : " [AI colonist]"));

                // Narrative style tells the DM exactly how this archetype behaves
                if (cls != null && !string.IsNullOrWhiteSpace(cls.aiNarrativeStyle))
                    sb.AppendLine("  Archetype style: " + cls.aiNarrativeStyle);

                if (pc.pawn != null)
                    sb.AppendLine("  " + DNP_SessionManager.BuildPawnContext(pc.pawn));
            }

            // Colono DM personality
            if (!session.playerIsDM && session.dmPawn != null)
            {
                sb.AppendLine("\n=== YOUR PERSONA ===");
                sb.AppendLine("You are narrating through the lens of " + session.dmPawn.Name.ToStringShort
                    + ". Their traits shape your style:");
                sb.AppendLine(DNP_SessionManager.BuildPawnContext(session.dmPawn));
            }

            return sb.ToString().Trim();
        }

        // ── DM user prompt — free mode ────────────────────────────
        // Includes sliding window of recent log + current state.

        public static string BuildDMFreePrompt(DNP_Session session, string lastPlayerAction,
                                                DNP_TurnDirector director)
        {
            var sb = new StringBuilder();

            string recentLog = BuildRecentLog(session, RECENT_LOG_TURNS);
            if (!string.IsNullOrEmpty(recentLog))
            {
                sb.AppendLine("=== RECENT EVENTS ===");
                sb.AppendLine(recentLog);
                sb.AppendLine("");
            }

            sb.AppendLine(BuildSessionState(session, director));
            sb.AppendLine("");

            if (!string.IsNullOrEmpty(lastPlayerAction))
            {
                sb.AppendLine("Player action: \"" + lastPlayerAction + "\"");
                sb.AppendLine("");
                sb.AppendLine("Read the player\'s intent. If their action has an uncertain outcome "
                    + "(physical challenge, social manipulation, stealth, perception, etc.), "
                    + "respond with ONE short sentence setting the scene, then add [ROLL:skill:CONTEXT]. "
                    + "Do NOT narrate the outcome before the roll happens. "
                    + "If no roll is needed (just dialogue or observation), narrate normally.");
            }
            else
            {
                sb.AppendLine("Advance the story. Describe what the party sees or encounters next.");
            }

            return sb.ToString().Trim();
        }

        // ── DM user prompt — combat ───────────────────────────────
        // Always includes full encounter state. Explicit no-invent rule.

        public static string BuildDMCombatPrompt(DNP_Session session, DNP_TurnDirector director,
                                                   string combatEventDescription)
        {
            var sb = new StringBuilder();

            sb.AppendLine(BuildFullCombatState(session, director));
            sb.AppendLine("");
            sb.AppendLine("=== WHAT JUST HAPPENED (mechanically resolved) ===");
            sb.AppendLine(combatEventDescription);
            sb.AppendLine("");
            sb.AppendLine("Round " + director.RoundNumber + ". Narrate this moment vividly in 1-3 sentences. "
                + "The outcome is already determined — describe it, do not change it. "
                + "Use the battlefield state above to stay accurate.");

            return sb.ToString().Trim();
        }

        // ── Colonist-AI prompt ────────────────────────────────────
        // Personality anchor repeated every call.

        public static (string system, string user) BuildColonistPrompt(
            DNP_PlayerCharacter pc, string lastDMNarration, DNP_Session session)
        {
            var    cls         = DNP_ClassRegistry.Get(pc.classId);
            string personality = pc.GetPlaystyleHint();
            string archetype   = cls != null && !string.IsNullOrWhiteSpace(cls.aiNarrativeStyle)
                ? cls.aiNarrativeStyle : "";

            var sys = new StringBuilder();

            // ── Identity: colonist playing a character ────────────
            sys.AppendLine("You are " + pc.characterName
                + ", a RimWorld colonist playing a tabletop RPG session with your fellow colonists.");
            sys.AppendLine("You are playing the role of a "
                + (cls?.className ?? "adventurer") + ".");
            sys.AppendLine("You enjoy the game and get genuinely into character —"
                + " the story feels real and exciting to you, even though you know it's a game.");
            sys.AppendLine("You take the adventure seriously and play to win,"
                + " but you're also having fun with your friends.");
            sys.AppendLine("");

            sys.AppendLine("YOUR CHARACTER'S PERSONALITY (how you play your role):");
            sys.AppendLine(personality);

            if (pc.pawn?.story != null)
            {
                var backstories = pc.pawn.story.AllBackstories.ToList();
                if (backstories.Any())
                    sys.AppendLine("Your background: "
                        + string.Join(" / ", backstories.Select(b => b.title)));
                var traits = pc.pawn.story.traits?.allTraits;
                if (traits != null && traits.Any())
                    sys.AppendLine("Your traits: "
                        + string.Join(", ", traits.Select(t => t.LabelCap)));
            }

            if (!string.IsNullOrEmpty(archetype))
            {
                sys.AppendLine("");
                sys.AppendLine("YOUR " + (cls?.className ?? "CLASS").ToUpper() + " STYLE:");
                sys.AppendLine(archetype);
                sys.AppendLine("Your personality shapes HOW you play this class — not WHETHER."
                    + " An impulsive Cleric still heals, but does it with urgency and frustration.");
            }

            if (cls != null && !string.IsNullOrEmpty(cls.flavorText))
                sys.AppendLine("\nYour character's motto: \"" + cls.flavorText + "\"");

            sys.AppendLine("");
            sys.AppendLine("HOW TO RESPOND:");
            sys.AppendLine(LanguageDirective());
            sys.AppendLine("- Speak in first person as your character, 1-2 sentences per turn.");
            sys.AppendLine("- Stay in the spirit of the game — describe actions and feelings"
                + " as if they are really happening to your character.");
            sys.AppendLine("- Light game references are natural: 'I'll try to sneak past'"
                + " or 'think I should roll for this?' feel authentic for someone playing D&D.");
            sys.AppendLine("- When your action is risky or uncertain, add a tag at the end:");
            sys.AppendLine("    [ROLL:skill]            — for any uncertain action");
            sys.AppendLine("    [ROLL:skill:CONTEXT]    — same, with brief context");
            sys.AppendLine("    [ROLL:attack:ENEMYNAME] — when attacking a specific enemy");
            sys.AppendLine("    [WAIT]                  — when you pass or just speak");
            sys.AppendLine("- The tag is invisible to others — never mention it, just add it.");
            sys.AppendLine("- Never narrate what other characters do.");
            sys.AppendLine("- Never mention HP or dice results — the DM handles those.");

            // User — recent context + who else is present
            var usr = new StringBuilder();

            // Recent log window for colonist too
            string recentLog = BuildRecentLog(session, 4);
            if (!string.IsNullOrEmpty(recentLog))
            {
                usr.AppendLine("Recent events:");
                usr.AppendLine(recentLog);
                usr.AppendLine("");
            }

            if (!string.IsNullOrEmpty(lastDMNarration))
                usr.AppendLine("DM just said: \"" + lastDMNarration + "\"");

            // Brief party status
            var others = session.characters.Where(c => c != pc && c.hp > 0).ToList();
            if (others.Any())
                usr.AppendLine("Party present: "
                    + string.Join(", ", others.Select(c =>
                        c.characterName + (c.hp < c.maxHp / 2 ? " (wounded)" : ""))));

            // Combat state if relevant
            if (session.activeEncounter != null)
            {
                var alive = session.activeEncounter.enemies.Where(e => e.hp > 0).ToList();
                if (alive.Any())
                    usr.AppendLine("Enemies still standing: "
                        + string.Join(", ", alive.Select(e => e.instanceName)));
            }

            usr.AppendLine("");
            // Anchor: who they are + what they're playing
            string reminder = "You are " + pc.characterName + " playing your "
                + (cls?.className ?? "character") + ". "
                + personality.Split('.')[0] + ".";
            if (!string.IsNullOrEmpty(archetype))
                reminder += " As a " + (cls?.className ?? "adventurer") + ": "
                    + archetype.Split('.')[0].ToLower() + ".";
            usr.AppendLine(reminder);
            usr.AppendLine("What do you do or say? (Add a tag if your action is uncertain.)");

            return (sys.ToString().Trim(), usr.ToString().Trim());
        }

        // ── Enemy narration prompt ────────────────────────────────

        public static string BuildEnemyActionPrompt(DNP_Session session,
            DNP_EnemyInstance enemy, DNP_EnemyData def,
            string combatResult, DNP_TurnDirector director)
        {
            var sb = new StringBuilder();
            sb.AppendLine(BuildFullCombatState(session, director));
            sb.AppendLine("");
            sb.AppendLine("=== ENEMY ACTION ===");
            sb.AppendLine(enemy.instanceName + " acts. Mechanical result: " + combatResult);
            sb.AppendLine("");
            sb.AppendLine("Narrate this in 1-2 sentences. Behavior style: "
                + (def?.behaviorTag ?? "aggressive") + ". "
                + "Do not change the outcome. Stay consistent with the battlefield above.");

            return sb.ToString().Trim();
        }

        // ── DM Briefing ───────────────────────────────────────────
        // Called when the human player IS the DM.
        // Gives private tips — not shown as world narration.

        public static string BuildDMBriefingPrompt(DNP_Session session,
                                                     DNP_TurnDirector director)
        {
            var sb  = new StringBuilder();
            var world = DNP_GameComponent.Instance?.World;

            sb.AppendLine("The human player is the Dungeon Master for this session.");
            sb.AppendLine("Give them a SHORT private briefing — context only, no directions.");
            sb.AppendLine(LanguageDirective());
            sb.AppendLine("");
            sb.AppendLine("Party playing today:");
            foreach (var pc in session.characters)
            {
                var cls = DNP_ClassRegistry.Get(pc.classId);
                sb.AppendLine("- " + pc.characterName
                    + " (" + (cls?.className ?? "Adventurer") + ")"
                    + " — " + pc.GetPlaystyleHint().Split(',')[0]);
            }
            sb.AppendLine("");

            if (world != null && !string.IsNullOrWhiteSpace(world.campaignObjective))
            {
                sb.AppendLine("Campaign objective: " + world.campaignObjective);
                sb.AppendLine("");
            }

            if (world != null && !string.IsNullOrWhiteSpace(world.campaignNotes))
            {
                sb.AppendLine("DM notes: " + world.campaignNotes);
                sb.AppendLine("");
            }

            sb.AppendLine("Give the DM ONE suggested opening sentence to get the scene started.");
            sb.AppendLine("Nothing else. No bullet points. No character directions. Just one evocative line.");

            return sb.ToString().Trim();
        }

        public static string BuildOpeningPrompt(DNP_Session session, DNP_TurnDirector director)
        {
            var sb = new StringBuilder();

            var scenario = !string.IsNullOrEmpty(session.scenarioId)
                ? DNP_ContentRegistry.GetScenario(session.scenarioId) : null;

            if (scenario != null && !string.IsNullOrEmpty(scenario.openingNarration))
            {
                sb.AppendLine("Use this as inspiration for the opening (expand it, don't copy verbatim):");
                sb.AppendLine("\"" + scenario.openingNarration + "\"");
            }
            else
                sb.AppendLine("Set the opening scene. Establish the atmosphere and give the party "
                    + "their first clear situation to react to.");

            return sb.ToString().Trim();
        }

        // ── Roll result narration ─────────────────────────────────

        public static string BuildRollResultNarrationPrompt(DNP_Session session,
            DNP_TurnDirector director, string rollerName, int roll, string context)
        {
            var sb = new StringBuilder();

            // Include combat state if in combat, else just recent log
            if (director.Mode == DNP_TurnDirector.TurnMode.Combat)
                sb.AppendLine(BuildFullCombatState(session, director));
            else
            {
                string recent = BuildRecentLog(session, 4);
                if (!string.IsNullOrEmpty(recent))
                    sb.AppendLine("Recent events:\n" + recent);
                sb.AppendLine(BuildSessionState(session, director));
            }

            sb.AppendLine("");
            sb.AppendLine("=== DICE RESULT ===");
            sb.AppendLine(rollerName + " rolled d20 → " + roll + "  "
                + (roll == 20 ? "— CRITICAL SUCCESS"
                 : roll == 1  ? "— CRITICAL FAILURE"
                 : roll >= 15 ? "— Success"
                 : roll >= 8  ? "— Partial success"
                 :              "— Failure"));
            if (!string.IsNullOrEmpty(context))
                sb.AppendLine("Context: " + context);
            sb.AppendLine("");
            sb.AppendLine("Narrate the outcome in 1-3 sentences. "
                + "The result is determined — describe it vividly. "
                + (roll == 20 ? "This is a critical success — be dramatic and rewarding. "
                 : roll == 1  ? "This is a critical failure — be dramatic and painful. " : "")
                + "Stay consistent with the current scene.");

            return sb.ToString().Trim();
        }

        // ── One-shot inactivity nudge ─────────────────────────────

        public static string BuildInactivityNudgePrompt(DNP_Session session,
                                                          DNP_TurnDirector director)
        {
            var sb = new StringBuilder();

            // Minimal context — just the current moment
            sb.AppendLine(BuildSessionState(session, director));
            sb.AppendLine("");

            sb.AppendLine("The player has gone quiet. Write ONE sentence of atmospheric pressure.");
            sb.AppendLine("Rules: max 12 words, describe something in the environment becoming urgent, "
                + "no questions, no names, no game mechanics.");
            sb.AppendLine("Examples: 'The goblin's blade catches the torchlight.' "
                + "/ 'Footsteps echo closer in the dark.'");

            return sb.ToString().Trim();
        }

        // ── Item use narration ────────────────────────────────────

        public static string BuildItemUsePrompt(DNP_Session session, DNP_TurnDirector director,
            string characterName, string itemName, string effectDescription)
        {
            var sb = new StringBuilder();
            sb.AppendLine(BuildSessionState(session, director));
            sb.AppendLine("");
            sb.AppendLine(characterName + " used: " + itemName);
            sb.AppendLine("Effect: " + effectDescription);
            sb.AppendLine("");
            sb.AppendLine("Narrate this in one short sentence. "
                + "Keep it consistent with the current scene.");

            return sb.ToString().Trim();
        }

        // ── Player attack narration ───────────────────────────────

        // ── Colonist auto-roll narration ──────────────────────────

        public static string BuildColonistRollNarrationPrompt(DNP_PlayerCharacter pc,
            int roll, string colonistAction, DNP_Session session, DNP_TurnDirector director)
        {
            var sb = new StringBuilder();
            sb.AppendLine(BuildSessionState(session, director));
            sb.AppendLine("");
            sb.AppendLine(pc.characterName + " attempted: " + colonistAction);
            sb.AppendLine("Rolled d20 → " + roll + "  "
                + (roll == 20 ? "— CRITICAL SUCCESS"
                 : roll == 1  ? "— CRITICAL FAILURE"
                 : roll >= 15 ? "— Success"
                 : roll >= 8  ? "— Partial"
                 :              "— Failure"));
            sb.AppendLine("");
            sb.AppendLine("Narrate the outcome in one sentence. Third person only. "
                + "Be specific about what " + pc.characterName + " finds or fails to find.");
            return sb.ToString().Trim();
        }

        public static string BuildPlayerAttackPrompt(DNP_Session session,
            DNP_TurnDirector director, DNP_PlayerCharacter pc,
            DNP_EnemyInstance target, int roll, string rollType,
            string combatResult, string context)
        {
            var sb = new StringBuilder();
            sb.AppendLine(BuildFullCombatState(session, director));
            sb.AppendLine("");
            sb.AppendLine("=== ATTACK ===");
            sb.AppendLine(pc.characterName + " attacks "
                + (target?.instanceName ?? "an enemy") + ".");
            if (!string.IsNullOrEmpty(context))
                sb.AppendLine("Player intention: " + context);
            sb.AppendLine("Roll: " + rollType + " d20 → " + roll);
            sb.AppendLine("Mechanical result: " + combatResult);
            sb.AppendLine("");
            sb.AppendLine("Narrate this attack in 1-2 sentences. "
                + "Third person only. Do not change the outcome. "
                + "Be specific about " + pc.characterName
                + "'s fighting style based on their class and personality.");

            return sb.ToString().Trim();
        }

        // ── Difficulty Class fetch ────────────────────────────────
        // Ask DM-AI for a CD BEFORE showing the roll result.
        // Response must be a single number only.

        public static string BuildDifficultyClassPrompt(DNP_Session session,
            DNP_TurnDirector director, string playerAction)
        {
            var sb = new StringBuilder();
            sb.AppendLine(BuildSessionState(session, director));
            sb.AppendLine("");
            sb.AppendLine("The player wants to: " + playerAction);
            sb.AppendLine("");
            sb.AppendLine("What is the Difficulty Class (DC) for this action?");
            sb.AppendLine("Consider the current context and environment.");
            sb.AppendLine("Reply with ONLY a single integer between 5 and 25. Nothing else.");
            sb.AppendLine("Examples: 8  /  12  /  17  /  20");

            return sb.ToString().Trim();
        }

        // ── Skill check narration ─────────────────────────────────

        public static string BuildSkillCheckNarrationPrompt(DNP_Session session,
            DNP_TurnDirector director, DNP_PlayerCharacter pc,
            int roll, int cd, bool success, string context)
        {
            var sb = new StringBuilder();

            string recent = BuildRecentLog(session, 4);
            if (!string.IsNullOrEmpty(recent))
                sb.AppendLine("Recent events:\n" + recent + "\n");

            sb.AppendLine(BuildSessionState(session, director));
            sb.AppendLine("");
            sb.AppendLine("=== SKILL CHECK ===");
            sb.AppendLine(pc.characterName + " attempts: " + context);
            sb.AppendLine("Rolled: " + roll + " vs DC " + cd
                + " → " + (success ? "SUCCESS" : "FAILURE")
                + (roll == 20 ? " (CRITICAL)" : roll == 1 ? " (FUMBLE)" : ""));
            sb.AppendLine("");
            sb.AppendLine("Narrate the outcome in 1-3 sentences. Third person only. "
                + "The result is already determined — do not change it. "
                + (roll == 20 ? "Critical success — be dramatic and rewarding. "
                 : roll == 1  ? "Critical failure — be dramatic and painful. "
                 : success    ? "Success — describe finding/achieving what they attempted. "
                 :              "Failure — describe not finding/failing what they attempted. "));

            return sb.ToString().Trim();
        }

        /// <summary>
        /// Sliding window of recent log entries formatted for AI context.
        /// Skips System entries — only DM, Player, and DiceRoll.
        /// </summary>
        private static string BuildRecentLog(DNP_Session session, int maxTurns)
        {
            if (session.sessionLog == null || session.sessionLog.Count == 0)
                return "";

            var relevant = session.sessionLog
                .Where(e => e.entryType != DNP_LogEntry.EntryType.System)
                .Reverse().Take(maxTurns).Reverse()
                .ToList();

            if (!relevant.Any()) return "";

            var sb = new StringBuilder();
            foreach (var entry in relevant)
            {
                string prefix = entry.entryType == DNP_LogEntry.EntryType.DM      ? "[DM] "
                              : entry.entryType == DNP_LogEntry.EntryType.DiceRoll ? "[Dice] "
                              : "[" + entry.speakerName + "] ";
                // Truncate very long entries to avoid token bloat
                string text = entry.text.Length > 200
                    ? entry.text.Substring(0, 197) + "…"
                    : entry.text;
                sb.AppendLine(prefix + text);
            }
            return sb.ToString().Trim();
        }

        /// <summary>
        /// Compact current state — used in free mode prompts.
        /// </summary>
        private static string BuildSessionState(DNP_Session session, DNP_TurnDirector director)
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== CURRENT STATE ===");
            sb.AppendLine("Mode: " + (director.Mode == DNP_TurnDirector.TurnMode.Combat
                ? "COMBAT — Round " + director.RoundNumber
                : "FREE — Exploration/Roleplay"));

            if (session.activeEncounter != null)
            {
                var alive = session.activeEncounter.enemies.Where(e => e.hp > 0).ToList();
                if (alive.Any())
                    sb.AppendLine("Active encounter: " + session.activeEncounter.encounterName
                        + " | Enemies: "
                        + string.Join(", ", alive.Select(e =>
                            e.instanceName + " (" + e.hp + "/" + e.maxHp + " HP)")));
            }

            sb.AppendLine("PARTY:");
            foreach (var pc in session.characters)
            {
                var cls = DNP_ClassRegistry.Get(pc.classId);

                // HP state
                string hpState = pc.hp <= 0 ? "DOWN"
                    : pc.hp < pc.maxHp / 4   ? "CRITICAL (" + pc.hp + "/" + pc.maxHp + " HP)"
                    : pc.hp < pc.maxHp / 2   ? "wounded (" + pc.hp + "/" + pc.maxHp + " HP)"
                    :                           "OK (" + pc.hp + "/" + pc.maxHp + " HP)";

                string line = "  " + pc.characterName
                    + " [" + (cls?.className ?? "Adventurer") + " Lv." + pc.level + "]"
                    + " — " + hpState
                    + (pc.isPlayerControlled ? " ★PLAYER" : " [AI]");

                // Status effects inline
                if (pc.activeStatusEffects != null && pc.activeStatusEffects.Any())
                    line += " | Effects: " + string.Join(", ", pc.activeStatusEffects);

                sb.AppendLine(line);
            }

            return sb.ToString().Trim();
        }

        /// <summary>
        /// Full combat state — used in every combat prompt.
        /// Gives the AI precise battlefield awareness.
        /// </summary>
        private static string BuildFullCombatState(DNP_Session session, DNP_TurnDirector director)
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== BATTLEFIELD STATE ===");
            sb.AppendLine("Round: " + director.RoundNumber);
            sb.AppendLine("Initiative order: " + director.GetInitiativeList());
            sb.AppendLine("");

            // Party — full HP visible
            sb.AppendLine("PARTY:");
            foreach (var pc in session.characters)
            {
                var cls = DNP_ClassRegistry.Get(pc.classId);
                string hpStr = pc.hp <= 0
                    ? "DOWN"
                    : pc.hp + "/" + pc.maxHp + " HP"
                      + (pc.hp < pc.maxHp / 2 ? " (WOUNDED)" : "");
                sb.AppendLine("  " + pc.characterName
                    + " [" + (cls?.className ?? "?") + "]"
                    + " — " + hpStr
                    + (pc.isPlayerControlled ? " ★ PLAYER" : ""));
            }

            // Enemies — full HP visible
            if (session.activeEncounter != null)
            {
                sb.AppendLine("");
                sb.AppendLine("ENEMIES (" + session.activeEncounter.encounterName + "):");
                foreach (var e in session.activeEncounter.enemies)
                {
                    string hpStr = e.hp <= 0
                        ? "DEFEATED"
                        : e.hp + "/" + e.maxHp + " HP"
                          + (e.hp < e.maxHp / 2 ? " (WOUNDED)" : "");
                    sb.AppendLine("  " + e.instanceName + " — " + hpStr);
                }
            }

            return sb.ToString().Trim();
        }
    }
}