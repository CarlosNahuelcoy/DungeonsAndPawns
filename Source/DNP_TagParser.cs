using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Verse;

namespace DungeonsAndPawns
{
    // ─────────────────────────────────────────────────────────────
    // TAG RESULT — one parsed tag and its validated data
    // ─────────────────────────────────────────────────────────────
    public class DNP_TagResult
    {
        public enum TagType
        {
            RollSkill,      // [ROLL:skill] or [ROLL:skill:context]
            RollAttack,     // [ROLL:attack:enemy_instance_name]
            RollSave,       // [ROLL:save:context]
            Wait,           // [WAIT]
            Encounter,      // [ENCOUNTER:id1,id2]
            Loot,           // [LOOT:item_id]
            Npc,            // [NPC:name:dialogue]
            Status,         // [STATUS:char_name:effect]
            EndEncounter,   // [END_ENCOUNTER]
            Scene,          // [SCENE:description]
            Unknown
        }

        public TagType              Type;
        public string               Context;      // skill context, save type, scene desc
        public string               TargetName;   // enemy instance name, char name, NPC name
        public List<string>         EnemyIds;     // validated enemy IDs for ENCOUNTER
        public string               ItemId;       // validated item ID for LOOT
        public string               Effect;       // validated effect for STATUS
        public string               NpcDialogue;  // dialogue for NPC tag
    }

    // ─────────────────────────────────────────────────────────────
    // TAG PARSER
    // Extracts structural tags from LLM responses.
    // Validates all data references against live registries.
    // Returns clean text (tags removed) + list of actions.
    // ─────────────────────────────────────────────────────────────
    public static class DNP_TagParser
    {
        // Valid status effects the LLM can apply
        private static readonly HashSet<string> ValidEffects = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase)
        {
            "advantage", "disadvantage", "poisoned", "stunned",
            "burning", "blessed", "weakened", "inspired"
        };

        // Primary regex: matches [TAG:...] with any surrounding whitespace/newlines
        private static readonly Regex TagRegex = new Regex(
            @"[\s]*\[(ROLL:[^\]\r\n]+|WAIT|ENCOUNTER:[^\]\r\n]+|LOOT:[^\]\r\n]+|NPC:[^\]\r\n]+|STATUS:[^\]\r\n]+|END_ENCOUNTER|SCENE:[^\]\r\n]+)\][\s]*",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Fallback inline regex for extraction (doesn't consume surrounding whitespace)
        private static readonly Regex TagRegexFallback = new Regex(
            @"\[(ROLL:[^\]\r\n]+|WAIT|ENCOUNTER:[^\]\r\n]+|LOOT:[^\]\r\n]+|NPC:[^\]\r\n]+|STATUS:[^\]\r\n]+|END_ENCOUNTER|SCENE:[^\]\r\n]+)\]",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static (string cleanText, List<DNP_TagResult> tags) Parse(
            string raw, DNP_Session session)
        {
            if (string.IsNullOrEmpty(raw))
                return (raw, new List<DNP_TagResult>());

            var    tags = new List<DNP_TagResult>();
            string text = raw;

            // Step 1: extract all tags using fallback (captures group 1)
            foreach (Match m in TagRegexFallback.Matches(raw))
            {
                string inner = m.Groups[1].Value;
                var    tag   = ParseTag(inner, session);
                if (tag != null)
                    tags.Add(tag);
            }

            // Step 2: remove tags — use primary regex that consumes surrounding whitespace
            // Replace with single space to avoid word-merging, then clean up
            text = TagRegex.Replace(text, " ");

            // Step 3: fallback removal for any tags the primary missed
            text = TagRegexFallback.Replace(text, "");

            // Step 4: clean up artifacts
            text = Regex.Replace(text, @"\n{3,}", "\n\n"); // max 2 consecutive newlines
            text = Regex.Replace(text, @"[ \t]{2,}", " "); // collapse spaces
            text = text.Trim();

            return (text, tags);
        }

        // ── Individual tag parsers ────────────────────────────────

        private static DNP_TagResult ParseTag(string inner, DNP_Session session)
        {
            string upper = inner.ToUpperInvariant();

            // [WAIT]
            if (upper == "WAIT")
                return new DNP_TagResult { Type = DNP_TagResult.TagType.Wait };

            // [END_ENCOUNTER]
            if (upper == "END_ENCOUNTER")
                return new DNP_TagResult { Type = DNP_TagResult.TagType.EndEncounter };

            // [ROLL:type] or [ROLL:type:context]
            if (upper.StartsWith("ROLL:"))
            {
                var parts = inner.Split(':');
                if (parts.Length < 2) return null;
                string rollType = parts[1].ToLowerInvariant();
                string ctx      = parts.Length > 2 ? string.Join(":", parts.Skip(2)) : "";

                switch (rollType)
                {
                    case "skill":
                        return new DNP_TagResult
                        {
                            Type    = DNP_TagResult.TagType.RollSkill,
                            Context = ctx
                        };
                    case "attack":
                        // ctx = enemy instance name — validate it exists in active encounter
                        if (!string.IsNullOrEmpty(ctx))
                        {
                            var enemy = session.activeEncounter?.enemies
                                .FirstOrDefault(e =>
                                    e.instanceName.Equals(ctx, StringComparison.OrdinalIgnoreCase)
                                    && e.hp > 0);
                            if (enemy == null && session.activeEncounter != null)
                            {
                                // Try fuzzy match (LLM might say "Goblin #1" vs "goblin #1")
                                enemy = session.activeEncounter.enemies
                                    .FirstOrDefault(e =>
                                        e.hp > 0 &&
                                        e.instanceName.ToLower().Contains(ctx.ToLower()));
                            }
                            if (enemy != null)
                                return new DNP_TagResult
                                {
                                    Type       = DNP_TagResult.TagType.RollAttack,
                                    TargetName = enemy.instanceName
                                };
                            // No valid enemy — degrade to skill roll
                            return new DNP_TagResult
                            {
                                Type    = DNP_TagResult.TagType.RollSkill,
                                Context = "attack"
                            };
                        }
                        return new DNP_TagResult { Type = DNP_TagResult.TagType.RollSkill };

                    case "save":
                        return new DNP_TagResult
                        {
                            Type    = DNP_TagResult.TagType.RollSave,
                            Context = ctx
                        };

                    default:
                        // Unknown roll type — treat as generic skill
                        return new DNP_TagResult
                        {
                            Type    = DNP_TagResult.TagType.RollSkill,
                            Context = rollType + (string.IsNullOrEmpty(ctx) ? "" : " " + ctx)
                        };
                }
            }

            // [ENCOUNTER:id1,id2,...]
            if (upper.StartsWith("ENCOUNTER:"))
            {
                string idsPart = inner.Substring("ENCOUNTER:".Length);
                var ids = idsPart.Split(',')
                    .Select(s => s.Trim())
                    .Where(s => !string.IsNullOrEmpty(s))
                    .ToList();

                // Validate each ID against registry
                var validIds = ids
                    .Where(id => DNP_ContentRegistry.GetEnemy(id) != null)
                    .ToList();

                if (!validIds.Any())
                {
                    Log.Warning("[D&P TagParser] ENCOUNTER tag had no valid enemy IDs: " + idsPart);
                    return null;
                }

                return new DNP_TagResult
                {
                    Type     = DNP_TagResult.TagType.Encounter,
                    EnemyIds = validIds
                };
            }

            // [LOOT:item_id]
            if (upper.StartsWith("LOOT:"))
            {
                string itemId = inner.Substring("LOOT:".Length).Trim();
                var    item   = DNP_ContentRegistry.GetItem(itemId);
                if (item == null)
                {
                    Log.Warning("[D&P TagParser] LOOT tag has invalid item ID: " + itemId);
                    return null;
                }
                return new DNP_TagResult
                {
                    Type   = DNP_TagResult.TagType.Loot,
                    ItemId = itemId
                };
            }

            // [NPC:name:dialogue]
            if (upper.StartsWith("NPC:"))
            {
                var parts = inner.Split(new[] { ':' }, 3);
                if (parts.Length < 3) return null;

                string npcName = parts[1].Trim();

                // CRITICAL: never accept NPC tags for party members
                bool isPartyMember = session.characters.Any(c =>
                    c.characterName.Equals(npcName, StringComparison.OrdinalIgnoreCase));
                if (isPartyMember)
                {
                    Log.Warning("[D&P TagParser] DM tried to NPC-tag party member '"
                        + npcName + "' — ignored.");
                    return null;
                }

                return new DNP_TagResult
                {
                    Type        = DNP_TagResult.TagType.Npc,
                    TargetName  = npcName,
                    NpcDialogue = parts[2].Trim()
                };
            }

            // [STATUS:char_name:effect]
            if (upper.StartsWith("STATUS:"))
            {
                var parts = inner.Split(new[] { ':' }, 3);
                if (parts.Length < 3) return null;

                string charName = parts[1].Trim();
                string effect   = parts[2].Trim().ToLowerInvariant();

                // Validate character exists in session
                var pc = session.characters.FirstOrDefault(c =>
                    c.characterName.Equals(charName, StringComparison.OrdinalIgnoreCase));
                if (pc == null)
                {
                    Log.Warning("[D&P TagParser] STATUS tag has unknown character: " + charName);
                    return null;
                }

                // Validate effect
                if (!ValidEffects.Contains(effect))
                {
                    Log.Warning("[D&P TagParser] STATUS tag has unknown effect: " + effect);
                    return null;
                }

                return new DNP_TagResult
                {
                    Type       = DNP_TagResult.TagType.Status,
                    TargetName = pc.characterName, // use exact case from session
                    Effect     = effect
                };
            }

            // [SCENE:description]
            if (upper.StartsWith("SCENE:"))
            {
                string desc = inner.Substring("SCENE:".Length).Trim();
                return new DNP_TagResult
                {
                    Type    = DNP_TagResult.TagType.Scene,
                    Context = desc
                };
            }

            Log.Warning("[D&P TagParser] Unknown tag: " + inner);
            return null;
        }

        // ── Prompt injection helpers ──────────────────────────────

        /// <summary>
        /// Builds the tag instruction block to inject into colonist prompts.
        /// Lists only valid IDs from current registries.
        /// </summary>
        public static string BuildColonistTagInstructions(DNP_Session session)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("TAG SYSTEM — use SPARINGLY. Most turns need no tag at all.");
            sb.AppendLine("");
            sb.AppendLine("Only add a tag when your action is PHYSICALLY RISKY or DIRECTLY OPPOSED:");
            sb.AppendLine("  YES: forcing a door, climbing a wall, sneaking past a guard, attacking");
            sb.AppendLine("  NO:  examining something, talking, thinking, moving normally, roleplaying");
            sb.AppendLine("");
            sb.AppendLine("Available tags (one per turn, at the very end, always in English):");
            sb.AppendLine("  [ROLL:skill:CONTEXT]    — risky physical action with real chance of failure");
            sb.AppendLine("  [ROLL:save:CONTEXT]     — resisting something actively dangerous to you");
            sb.AppendLine("  [WAIT]                  — you speak, react, observe, or do nothing risky");

            if (session.activeEncounter != null)
            {
                var alive = session.activeEncounter.enemies.Where(e => e.hp > 0).ToList();
                if (alive.Any())
                {
                    sb.AppendLine("  [ROLL:attack:NAME]      — attacking an enemy in combat. Valid targets:");
                    foreach (var e in alive)
                        sb.AppendLine("    " + e.instanceName);
                }
            }

            sb.AppendLine("");
            sb.AppendLine("If in doubt, use [WAIT] or no tag. The DM requests rolls when they matter.");
            sb.AppendLine("NEVER roll for: searching, investigating, talking, healing prayers, analyzing.");
            return sb.ToString().Trim();
        }

        /// <summary>
        /// Builds the tag instruction block for DM prompts.
        /// </summary>
        public static string BuildDMTagInstructions(DNP_Session session)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("TAGS — embed ONE at the end of your narration if appropriate:");

            // Roll request for player
            sb.AppendLine("  [ROLL:skill:CONTEXT]    — request a skill roll from the player");
            sb.AppendLine("  [ROLL:save:CONTEXT]     — request a saving throw from the player");

            // Encounter — only list valid enemy IDs
            var allEnemies = DNP_ContentRegistry.AllEnemies;
            if (allEnemies.Any())
            {
                sb.AppendLine("  [ENCOUNTER:id1,id2]     — start combat. Valid IDs: "
                    + string.Join(", ", allEnemies.Select(e => e.id)));
            }

            // Loot — only list valid item IDs
            var allItems = DNP_ContentRegistry.AllItems;
            if (allItems.Any())
            {
                sb.AppendLine("  [LOOT:item_id]          — grant item. Valid IDs: "
                    + string.Join(", ", allItems.Select(i => i.id)));
            }

            // Status — valid effects
            sb.AppendLine("  [STATUS:char:effect]    — apply effect. Valid effects: "
                + string.Join(", ", ValidEffects));
            sb.AppendLine("  [NPC:name:dialogue]     — voice an NPC (any name, any dialogue)");
            sb.AppendLine("  [SCENE:description]     — establish new location/scene");
            sb.AppendLine("  [END_ENCOUNTER]         — combat is fully resolved");
            sb.AppendLine("Tags are always in English. Never explain them. Tag goes last.");
            return sb.ToString().Trim();
        }
    }
}