using System;
using System.Collections;
using System.Linq;
using UnityEngine;
using Verse;
using RimWorld;

namespace DungeonsAndPawns
{
    public class DNP_AIController
    {
        private DNP_Session      _session;
        private DNP_TurnDirector _director;
        private string           _dmSystemPrompt  = "";
        private string           _lastDMNarration = "";

        public bool IsBusy    { get; private set; } = false;
        public bool AIEnabled => DNP_Mod.Settings?.IsConfigured() == true;

        // ── Init ─────────────────────────────────────────────────

        public DNP_AIController(DNP_Session session)
        {
            _session  = session;
            _director = new DNP_TurnDirector(session);
            ApplyDirectorSettings();

            _director.OnActorTurn       += HandleActorTurn;
            _director.OnInactivityNudge += HandleInactivityNudge;
            _director.OnRoundStart      += round =>
                DNP_SessionManager.AddEntry(DNP_LogEntry.EntryType.System, "System",
                    "DNP.Log.RoundStart".Translate(round));

            _dmSystemPrompt = DNP_PromptBuilder.BuildDMSystemPrompt(_session, _director);
        }

        private void ApplyDirectorSettings()
        {
            var cfg = DNP_Mod.Settings;
            if (cfg == null) return;
            _director.Profile              = cfg.directorProfile;
            _director.ColonistInitiative   = cfg.colonistInitiative;
            _director.InactivitySpeed      = cfg.inactivitySpeed;
            _director.EnemyDifficultyBonus = cfg.enemyDifficultyBonus;
        }

        // ── Session start ─────────────────────────────────────────

        public void StartSession()
        {
            _director.StartFreeMode();

            if (!AIEnabled)
            {
                DNP_SessionManager.AddEntry(DNP_LogEntry.EntryType.System, "System",
                    "DNP.Log.AIDisabled".Translate());
                return;
            }

            // When player is DM: give a private briefing instead of public narration
            if (_session.playerIsDM)
                Kick(RunDMBriefing());
            else
                Kick(RunOpeningNarration());
        }

        // ── Player acts ───────────────────────────────────────────

        public void OnPlayerAction(string actionText)
        {
            if (IsBusy) return;

            // When player is DM, their text IS the DM narration — log it as DM
            if (_session.playerIsDM)
            {
                DNP_SessionManager.AddEntry(DNP_LogEntry.EntryType.DMAssist,
                    "DNP.Log.SpeakerDMPlayer".Translate(), actionText);
                _lastDMNarration = actionText;
                _director.ResetInactivity();

                if (AIEnabled)
                    // Skip the DM-AI slot — go straight to colonist turns
                    Kick(RunColonistRoundAfterDM());
                return;
            }

            // Player-as-character mode
            var playerChar = _session.characters.FirstOrDefault(c => c.isPlayerControlled);
            string speaker = playerChar?.characterName ?? (string)"DNP.Log.SpeakerDM".Translate();
            DNP_SessionManager.AddEntry(DNP_LogEntry.EntryType.Player, speaker, actionText);
            _director.ResetInactivity();

            if (!AIEnabled)
            {
                _director.PlayerActed();
                return;
            }

            if (_director.Mode == DNP_TurnDirector.TurnMode.Free)
                _director.PlayerActed();
            else
                _director.CombatPlayerActed();
        }

        // Fires colonist turns directly after a human-DM narration
        // bypassing the DM-AI slot entirely
        private IEnumerator RunColonistRoundAfterDM()
        {
            IsBusy = true;
            yield return new WaitForSeconds(0.5f);

            var colonists = _session.characters
                .Where(c => !c.isPlayerControlled && c.hp > 0)
                .ToList();

            foreach (var pc in colonists)
            {
                // Show "thinking" indicator
                string thinkingKey = pc.characterName + "…";
                var thinkingEntry = DNP_SessionManager.AddEntryTemp(
                    DNP_LogEntry.EntryType.System, pc.characterName, thinkingKey);

                yield return new WaitForSeconds(1.8f);

                var (system, user) = DNP_PromptBuilder.BuildColonistPrompt(
                    pc, _lastDMNarration, _session);

                string raw = null;
                yield return RunLLMCall(system, user, r => raw = r);

                // Remove thinking entry
                DNP_SessionManager.RemoveTempEntry(thinkingEntry);

                if (IsValidResponse(raw))
                {
                    var (clean, tags) = DNP_TagParser.Parse(raw, _session);

                    string display = clean;
                    if (display.StartsWith("\"") && display.EndsWith("\"") && display.Length > 2)
                        display = display.Substring(1, display.Length - 2).Trim();

                    DNP_SessionManager.AddEntry(DNP_LogEntry.EntryType.Player,
                        pc.characterName, display);

                    foreach (var tag in tags)
                        yield return ExecuteColonistTag(pc, tag, display);
                }

                yield return new WaitForSeconds(0.4f);
            }

            IsBusy = false;
        }

        // ── Encounter ─────────────────────────────────────────────

        public void OnEncounterStarted(DNP_Encounter encounter)
        {
            _director.StartCombat(encounter);
            DNP_SessionManager.AddEntry(DNP_LogEntry.EntryType.System, "System",
                "DNP.Log.InitiativeOrder".Translate(_director.GetInitiativeList()));
        }

        public void OnEncounterEnded() => _director.EndCombat();

        // ── Director events ───────────────────────────────────────

        private void HandleActorTurn(DNP_TurnActor actor)
        {
            if (!AIEnabled)
            {
                if (actor.slot != DNP_TurnSlot.Player)
                    _director.AISlotCompleted();
                return;
            }

            switch (actor.slot)
            {
                case DNP_TurnSlot.DM:
                    Kick(RunDMTurn());
                    break;
                case DNP_TurnSlot.ColonistAI:
                    if (actor.colonistIndex < _session.characters.Count)
                    {
                        var pc = _session.characters[actor.colonistIndex];
                        if (_director.Mode == DNP_TurnDirector.TurnMode.Combat)
                            Kick(RunColonistCombatTurn(pc));
                        else
                            Kick(RunColonistTurn(pc));
                    }
                    else
                        _director.AISlotCompleted();
                    break;
                case DNP_TurnSlot.Enemy:
                    Kick(RunEnemyTurn(actor));
                    break;
                case DNP_TurnSlot.Player:
                    break; // wait for player input
            }
        }

        private void HandleInactivityNudge()
        {
            // One-shot: Director ensures this fires exactly once per wait period
            if (AIEnabled) Kick(RunInactivityNudge());
            // After this, Director hard-pauses. Nothing else fires until player acts.
        }

        // ── Coroutines ────────────────────────────────────────────

        private IEnumerator RunDMBriefing()
        {
            IsBusy = true;
            string userPrompt = DNP_PromptBuilder.BuildDMBriefingPrompt(_session, _director);
            string result     = null;

            yield return RunLLMCall(_dmSystemPrompt, userPrompt, r => result = r);

            if (IsValidResponse(result))
            {
                // Briefing is logged as a special System-like entry, not DM narration
                // so players (if any) don't see it as world narration
                var (clean, _) = DNP_TagParser.Parse(result, _session);
                DNP_SessionManager.AddEntry(DNP_LogEntry.EntryType.DMBriefing,
                    "DNP.Log.SpeakerBriefing".Translate(), clean);
            }

            IsBusy = false;
            // DM mode — now waiting for the DM to narrate
        }

        private IEnumerator RunOpeningNarration()
        {
            IsBusy = true;
            string userPrompt = DNP_PromptBuilder.BuildOpeningPrompt(_session, _director);
            string result     = null;

            yield return RunLLMCall(_dmSystemPrompt, userPrompt, r => result = r);

            if (IsValidResponse(result))
            {
                var (openClean, openTags) = DNP_TagParser.Parse(result, _session);
                _lastDMNarration = openClean;
                DNP_SessionManager.AddEntry(DNP_LogEntry.EntryType.DMAssist,
                    "DNP.Log.SpeakerDMAssist".Translate(), openClean);
                foreach (var t in openTags) yield return ExecuteDMTag(t);
            }
            else if (result?.StartsWith("⚠") == true)
                DNP_SessionManager.AddEntry(DNP_LogEntry.EntryType.System, "System", result);

            IsBusy = false;
            // Opening done — Director is in free mode, player can now type
        }

        private IEnumerator RunDMTurn()
        {
            IsBusy = true;
            string lastPlayer = GetLastPlayerAction();
            string userPrompt = _director.Mode == DNP_TurnDirector.TurnMode.Free
                ? DNP_PromptBuilder.BuildDMFreePrompt(_session, lastPlayer, _director)
                : DNP_PromptBuilder.BuildDMCombatPrompt(_session, _director, lastPlayer);

            string raw = null;
            yield return RunLLMCall(_dmSystemPrompt, userPrompt, r => raw = r);

            if (IsValidResponse(raw))
            {
                var (clean, tags) = DNP_TagParser.Parse(raw, _session);
                _lastDMNarration = clean;
                DNP_SessionManager.AddEntry(DNP_LogEntry.EntryType.DMAssist,
                    "DNP.Log.SpeakerDMAssist".Translate(), clean);

                foreach (var tag in tags)
                    yield return ExecuteDMTag(tag);
            }
            else if (raw?.StartsWith("⚠") == true)
                DNP_SessionManager.AddEntry(DNP_LogEntry.EntryType.System, "System", raw);

            IsBusy = false;
            _director.AISlotCompleted();
        }

        private IEnumerator RunColonistTurn(DNP_PlayerCharacter pc)
        {
            IsBusy = true;
            yield return new WaitForSeconds(0.6f);

            var (system, user) = DNP_PromptBuilder.BuildColonistPrompt(
                pc, _lastDMNarration, _session);

            string raw = null;
            yield return RunLLMCall(system, user, r => raw = r);

            if (IsValidResponse(raw))
            {
                var (clean, tags) = DNP_TagParser.Parse(raw, _session);

                // Strip wrapping quotes — LLM sometimes wraps entire response in "..."
                string display = clean;
                if (display.StartsWith("\"") && display.EndsWith("\"") && display.Length > 2)
                    display = display.Substring(1, display.Length - 2).Trim();

                // Show clean text as the colonist's action
                DNP_SessionManager.AddEntry(DNP_LogEntry.EntryType.Player,
                    pc.characterName, display);

                // Execute tags
                foreach (var tag in tags)
                    yield return ExecuteColonistTag(pc, tag, clean);
            }

            IsBusy = false;
            _director.AISlotCompleted();
        }

        private IEnumerator RunEnemyTurn(DNP_TurnActor actor)
        {
            IsBusy = true;

            var enemy = _session.activeEncounter?.enemies
                .FirstOrDefault(e => e.instanceName == actor.enemyInstanceId);

            if (enemy == null || enemy.hp <= 0)
            {
                IsBusy = false;
                _director.AISlotCompleted();
                yield break;
            }

            var enemyDef = DNP_ContentRegistry.GetEnemy(enemy.enemyId);
            var ruleset  = DNP_ContentRegistry.GetRuleset(_session.rulesetId)
                        ?? DNP_ContentRegistry.FirstRuleset;

            var targets = _session.characters.Where(c => c.hp > 0).ToList();
            if (targets.Count == 0) { IsBusy = false; _director.AISlotCompleted(); yield break; }

            var target = targets[Rand.RangeInclusive(0, targets.Count - 1)];

            // Difficulty bonus applied here
            var modDef = new DNP_EnemyData
            {
                id           = enemyDef?.id           ?? enemy.enemyId,
                enemyName    = enemyDef?.enemyName    ?? enemy.instanceName,
                hp           = enemyDef?.hp           ?? 8,
                armor        = enemyDef?.armor        ?? 0,
                attackDamage = enemyDef?.attackDamage ?? 3,
                attackBonus  = (enemyDef?.attackBonus ?? 1) + _director.EnemyDifficultyBonus,
                behaviorTag  = enemyDef?.behaviorTag  ?? "aggressive"
            };

            string combatResult = DNP_CombatResolver.EnemyAttack(enemy, modDef, target, ruleset);
            DNP_SessionManager.AddEntry(DNP_LogEntry.EntryType.System, "System", combatResult);

            string narration = null;
            string userPrompt = DNP_PromptBuilder.BuildEnemyActionPrompt(
                _session, enemy, modDef, combatResult, _director);
            yield return RunLLMCall(_dmSystemPrompt, userPrompt, r => narration = r);

            if (IsValidResponse(narration))
            {
                var (dmC2, dmT2) = DNP_TagParser.Parse(narration, _session);
                _lastDMNarration = dmC2;
                DNP_SessionManager.AddEntry(DNP_LogEntry.EntryType.DMAssist,
                    "DNP.Log.SpeakerDMAssist".Translate(), dmC2);
                foreach (var t in dmT2) yield return ExecuteDMTag(t);
            }

            if (DNP_CombatResolver.IsEncounterOver(_session.activeEncounter))
            {
                DNP_SessionManager.AddEntry(DNP_LogEntry.EntryType.System, "System",
                    "DNP.Log.EncounterWon".Translate());
                // Distribute loot before ending combat
                DNP_SessionManager.DistributeLoot(_session, _session.activeEncounter);
                _director.EndCombat();
                IsBusy = false;
                yield break;
            }

            if (DNP_CombatResolver.IsPartyWiped(_session))
            {
                DNP_SessionManager.AddEntry(DNP_LogEntry.EntryType.System, "System",
                    "DNP.Log.PartyWiped".Translate());
                _director.EndCombat();
                IsBusy = false;
                yield break;
            }

            IsBusy = false;
            _director.AISlotCompleted(); // ← critical
        }

        private IEnumerator RunInactivityNudge()
        {
            string userPrompt = DNP_PromptBuilder.BuildInactivityNudgePrompt(_session, _director);
            string result     = null;
            yield return RunLLMCall(_dmSystemPrompt, userPrompt, r => result = r);

            if (IsValidResponse(result))
            {
                var (clean, _) = DNP_TagParser.Parse(result, _session);
                _lastDMNarration = clean;
                DNP_SessionManager.AddEntry(DNP_LogEntry.EntryType.DMAssist,
                    "DNP.Log.SpeakerDMAssist".Translate(), clean);
            }
        }

        // ── Core LLM call ─────────────────────────────────────────
        // Drives DNP_LLMBridge.Send by manually stepping the inner
        // coroutine. Waits up to 45 seconds for a response.

        private IEnumerator RunLLMCall(string system, string user, Action<string> onResult)
        {
            string result = null;
            bool   done   = false;

            var bridge = DNP_LLMBridge.Send(system, user, r => { result = r; done = true; });

            // Step through the bridge coroutine frame by frame
            while (!done)
            {
                if (!bridge.MoveNext())
                    break; // bridge finished its iteration
                yield return bridge.Current;
            }

            // Small grace period in case callback fires after last MoveNext
            float wait = 0f;
            while (!done && wait < 3f)
            {
                yield return null;
                wait += Time.deltaTime;
            }

            onResult?.Invoke(result ?? "⚠ No response received.");
        }

        // ── Tick ──────────────────────────────────────────────────

        public void Tick(float realDelta)
        {
            if (AIEnabled) _director.Tick(realDelta);
        }

        // ── Helpers ───────────────────────────────────────────────

        private bool IsValidResponse(string r) =>
            !string.IsNullOrWhiteSpace(r) && !r.StartsWith("⚠");

        private void Kick(IEnumerator routine) => DNP_Mod.KickCoroutine(routine);

        private string GetLastPlayerAction()
        {
            for (int i = _session.sessionLog.Count - 1; i >= 0; i--)
            {
                var e = _session.sessionLog[i];
                if (e.entryType == DNP_LogEntry.EntryType.Player) return e.text;
            }
            return "";
        }

        // ── Item use ──────────────────────────────────────────────

        public void UseItem(DNP_PlayerCharacter pc, DNP_ItemData item)
        {
            string effectDesc = ApplyItemEffect(pc, item);
            pc.inventory.Remove(item.id);

            DNP_SessionManager.AddEntry(DNP_LogEntry.EntryType.Player,
                pc.characterName,
                pc.characterName + " uses " + item.itemName + ". " + effectDesc);

            if (AIEnabled)
                Kick(RunItemNarration(pc.characterName, item.itemName, effectDesc));
        }

        private string ApplyItemEffect(DNP_PlayerCharacter pc, DNP_ItemData item)
        {
            switch (item.consumableEffect)
            {
                case "HealHP":
                    int healed = System.Math.Min(item.consumableValue, pc.maxHp - pc.hp);
                    pc.hp = System.Math.Min(pc.maxHp, pc.hp + item.consumableValue);
                    return "Restored " + healed + " HP. (" + pc.hp + "/" + pc.maxHp + ")";

                case "AddBuff":
                    pc.activeStatusEffects.Add(item.itemName + "_buff");
                    return "Gained a buff for one turn.";

                default:
                    // Weapon/armor — damage/armor bonus handled in combat resolver
                    if (item.damageBonus > 0)
                        return "+" + item.damageBonus + " damage on next attack.";
                    if (item.armorBonus > 0)
                        return "+" + item.armorBonus + " armor this combat.";
                    return "Used " + item.itemName + ".";
            }
        }

        private IEnumerator RunItemNarration(string charName, string itemName, string effect)
        {
            string userPrompt = DNP_PromptBuilder.BuildItemUsePrompt(
                _session, _director, charName, itemName, effect);

            string result = null;
            yield return RunLLMCall(_dmSystemPrompt, userPrompt, r => result = r);

            if (IsValidResponse(result))
                DNP_SessionManager.AddEntry(DNP_LogEntry.EntryType.DMAssist,
                    "DNP.Log.SpeakerDMAssist".Translate(), result);
        }

        // ── Tag execution ─────────────────────────────────────────

        private IEnumerator ExecuteColonistTag(DNP_PlayerCharacter pc,
            DNP_TagResult tag, string colonistAction)
        {
            var rollType = _director.DetermineRollType(pc);

            switch (tag.Type)
            {
                case DNP_TagResult.TagType.RollSkill:
                case DNP_TagResult.TagType.RollSave:
                {
                    var (rollDisplay, roll) = _director.RollD20(rollType);
                    DNP_SessionManager.AddEntry(DNP_LogEntry.EntryType.DiceRoll,
                        "DNP.Log.SpeakerDice".Translate(),
                        pc.characterName + " — d20 " + rollDisplay
                        + (string.IsNullOrEmpty(tag.Context) ? "" : " (" + tag.Context + ")"));

                    string prompt = DNP_PromptBuilder.BuildColonistRollNarrationPrompt(
                        pc, roll, colonistAction, _session, _director);
                    string narration = null;
                    yield return RunLLMCall(_dmSystemPrompt, prompt, r => narration = r);
                    if (IsValidResponse(narration))
                    {
                        var (cn, ct) = DNP_TagParser.Parse(narration, _session);
                        _lastDMNarration = cn;
                        DNP_SessionManager.AddEntry(DNP_LogEntry.EntryType.DMAssist,
                            "DNP.Log.SpeakerDM".Translate(), cn);
                        foreach (var t in ct) yield return ExecuteDMTag(t);
                    }
                    break;
                }

                case DNP_TagResult.TagType.RollAttack:
                {
                    var target = _session.activeEncounter?.enemies
                        .FirstOrDefault(e => e.instanceName == tag.TargetName && e.hp > 0);
                    if (target == null) yield break;

                    var ruleset  = DNP_ContentRegistry.GetRuleset(_session.rulesetId)
                                ?? DNP_ContentRegistry.FirstRuleset;
                    var enemyDef = DNP_ContentRegistry.GetEnemy(target.enemyId);
                    var (rollDisplay, roll) = _director.RollD20(rollType);

                    DNP_SessionManager.AddEntry(DNP_LogEntry.EntryType.DiceRoll,
                        "DNP.Log.SpeakerDice".Translate(),
                        pc.characterName + " — Ataque vs " + target.instanceName
                        + " — d20 " + rollDisplay);

                    string combatResult = DNP_CombatResolver.PlayerAttackWithRoll(
                        pc, target, enemyDef, ruleset, roll);
                    DNP_SessionManager.AddEntry(DNP_LogEntry.EntryType.System,
                        "System", combatResult);

                    string prompt = DNP_PromptBuilder.BuildPlayerAttackPrompt(
                        _session, _director, pc, target, roll,
                        rollType.ToString(), combatResult, colonistAction);
                    string narration = null;
                    yield return RunLLMCall(_dmSystemPrompt, prompt, r => narration = r);
                    if (IsValidResponse(narration))
                    {
                        _lastDMNarration = narration;
                        DNP_SessionManager.AddEntry(DNP_LogEntry.EntryType.DMAssist,
                            "DNP.Log.SpeakerDM".Translate(), narration);
                    }

                    // Check combat end
                    if (DNP_CombatResolver.IsEncounterOver(_session.activeEncounter))
                    {
                        DNP_SessionManager.AddEntry(DNP_LogEntry.EntryType.System,
                            "System", "DNP.Log.EncounterWon".Translate());
                        DNP_SessionManager.DistributeLoot(_session, _session.activeEncounter);
                        _director.EndCombat();
                    }
                    break;
                }

                case DNP_TagResult.TagType.Wait:
                    // Nothing to execute — colonist passed
                    break;
            }
        }

        private IEnumerator ExecuteDMTag(DNP_TagResult tag)
        {
            switch (tag.Type)
            {
                case DNP_TagResult.TagType.Encounter:
                {
                    // Build encounter from validated enemy IDs
                    var enemies = new System.Collections.Generic.List<DNP_EnemyInstance>();
                    var counts  = new System.Collections.Generic.Dictionary<string, int>();
                    foreach (var id in tag.EnemyIds)
                    {
                        if (!counts.ContainsKey(id)) counts[id] = 0;
                        counts[id]++;
                        var def = DNP_ContentRegistry.GetEnemy(id);
                        int hp  = def?.hp ?? 8;
                        enemies.Add(new DNP_EnemyInstance
                        {
                            enemyId      = id,
                            instanceName = (def?.enemyName ?? id) + " #" + counts[id],
                            maxHp        = hp,
                            hp           = hp
                        });
                    }
                    var encounter = new DNP_Encounter
                    {
                        encounterName = "Encounter",
                        enemies       = enemies
                    };
                    _session.activeEncounter = encounter;
                    OnEncounterStarted(encounter);
                    yield break;
                }

                case DNP_TagResult.TagType.Loot:
                {
                    var item   = DNP_ContentRegistry.GetItem(tag.ItemId);
                    var living = _session.characters.Where(c => c.hp > 0).ToList();
                    if (item != null && living.Any())
                    {
                        var recipient = living[Rand.RangeInclusive(0, living.Count - 1)];
                        if (recipient.inventory == null)
                            recipient.inventory = new System.Collections.Generic.List<string>();
                        recipient.inventory.Add(tag.ItemId);
                        DNP_SessionManager.AddEntry(DNP_LogEntry.EntryType.System, "System",
                            recipient.characterName + " received: " + item.itemName);
                    }
                    yield break;
                }

                case DNP_TagResult.TagType.Status:
                {
                    var pc = _session.characters.FirstOrDefault(c =>
                        c.characterName.Equals(tag.TargetName, StringComparison.OrdinalIgnoreCase));
                    if (pc != null)
                    {
                        if (pc.activeStatusEffects == null)
                            pc.activeStatusEffects = new System.Collections.Generic.List<string>();
                        if (!pc.activeStatusEffects.Contains(tag.Effect))
                        {
                            pc.activeStatusEffects.Add(tag.Effect);
                            DNP_SessionManager.AddEntry(DNP_LogEntry.EntryType.System,
                                "System", pc.characterName + " gains: " + tag.Effect);
                        }
                    }
                    yield break;
                }

                case DNP_TagResult.TagType.Npc:
                {
                    // Log NPC dialogue as a separate entry (no extra quotes — LLM adds them)
                    string npcText = tag.NpcDialogue.Trim('"', '\'', ' ');
                    DNP_SessionManager.AddEntry(DNP_LogEntry.EntryType.Player,
                        tag.TargetName, "\"" + npcText + "\"");
                    yield break;
                }

                case DNP_TagResult.TagType.RollSkill:
                case DNP_TagResult.TagType.RollSave:
                {
                    // DM is requesting a roll from the player
                    var playerChar = _session.characters.FirstOrDefault(c => c.isPlayerControlled);
                    if (playerChar != null)
                        _director.RequestPlayerRoll(tag.Context ?? "Roll d20");
                    else
                    {
                        // No human player — auto-roll for first colonist
                        var first = _session.characters.FirstOrDefault(c => c.hp > 0);
                        if (first != null)
                        {
                            var rollType = _director.DetermineRollType(first);
                            var (rollDisplay, roll) = _director.RollD20(rollType);
                            DNP_SessionManager.AddEntry(DNP_LogEntry.EntryType.DiceRoll,
                                "DNP.Log.SpeakerDice".Translate(),
                                first.characterName + " — d20 " + rollDisplay);
                        }
                    }
                    yield break;
                }

                case DNP_TagResult.TagType.EndEncounter:
                {
                    if (_session.activeEncounter != null)
                    {
                        DNP_SessionManager.DistributeLoot(_session, _session.activeEncounter);
                        _director.EndCombat();
                    }
                    yield break;
                }

                case DNP_TagResult.TagType.Scene:
                {
                    // Just log the scene change — the DM narration already contains the text
                    DNP_SessionManager.AddEntry(DNP_LogEntry.EntryType.System,
                        "System", "— " + tag.Context + " —");
                    yield break;
                }
            }
            yield break;
        }
        // Called when player selects an enemy and presses Attack.
        // Never blocks the Director — completes the player's turn.

        public void ProcessPlayerAttack(DNP_PlayerCharacter pc,
            DNP_EnemyInstance target, string context)
        {
            if (!_director.IsPlayerTurnActive) return;

            var ruleset   = DNP_ContentRegistry.GetRuleset(_session.rulesetId)
                         ?? DNP_ContentRegistry.FirstRuleset;
            var enemyDef  = DNP_ContentRegistry.GetEnemy(target.enemyId);

            // Determine roll type from status effects
            var rollType  = _director.DetermineRollType(pc);
            var (rollDisplay, roll) = _director.RollD20(rollType);

            // Resolve attack mechanically using pre-rolled result
            // We override the internal roll by temporarily patching via the existing API
            string combatResult = DNP_CombatResolver.PlayerAttackWithRoll(pc, target, enemyDef, ruleset, roll);

            // Log it
            string logLine = pc.characterName + " — Ataque — " + rollDisplay;
            if (!string.IsNullOrEmpty(context)) logLine += " — " + context;
            DNP_SessionManager.AddEntry(DNP_LogEntry.EntryType.DiceRoll,
                "DNP.Log.SpeakerDice".Translate(), logLine);
            DNP_SessionManager.AddEntry(DNP_LogEntry.EntryType.System, "System", combatResult);

            // Advance player turn
            _director.CombatPlayerActed();

            // Ask DM-AI to narrate
            if (AIEnabled)
                Kick(RunCombatNarration(pc, target, roll, rollType, combatResult, context));
        }

        // ── Player skill check (free or combat) ──────────────────
        // Two-step: first fetch CD from DM-AI, then compare, then narrate.

        public void ProcessPlayerSkillCheck(DNP_PlayerCharacter pc, string context)
        {
            if (!_director.IsPlayerTurnActive) return;

            var rollType = _director.DetermineRollType(pc);
            var (rollDisplay, roll) = _director.RollD20(rollType);

            string logLine = pc.characterName + " — Habilidad — " + rollDisplay;
            if (!string.IsNullOrEmpty(context)) logLine += " — " + context;
            DNP_SessionManager.AddEntry(DNP_LogEntry.EntryType.DiceRoll,
                "DNP.Log.SpeakerDice".Translate(), logLine);

            // Advance turn before the async narration
            if (_director.Mode == DNP_TurnDirector.TurnMode.Combat)
                _director.CombatPlayerActed();
            else
                _director.PlayerActed();

            if (AIEnabled)
                Kick(RunSkillCheckNarration(pc, roll, rollType, context));
        }

        private IEnumerator RunCombatNarration(DNP_PlayerCharacter pc,
            DNP_EnemyInstance target, int roll,
            DNP_TurnDirector.RollType rollType, string combatResult, string context)
        {
            IsBusy = true;
            string userPrompt = DNP_PromptBuilder.BuildPlayerAttackPrompt(
                _session, _director, pc, target, roll, rollType.ToString(), combatResult, context);

            string result = null;
            yield return RunLLMCall(_dmSystemPrompt, userPrompt, r => result = r);

            if (IsValidResponse(result))
            {
                var (dmC, dmT) = DNP_TagParser.Parse(result, _session);
                _lastDMNarration = dmC;
                DNP_SessionManager.AddEntry(DNP_LogEntry.EntryType.DMAssist,
                    "DNP.Log.SpeakerDMAssist".Translate(), dmC);
                foreach (var t in dmT) yield return ExecuteDMTag(t);
            }
            IsBusy = false;
            _director.AISlotCompleted();
        }

        private IEnumerator RunSkillCheckNarration(DNP_PlayerCharacter pc,
            int roll, DNP_TurnDirector.RollType rollType, string context)
        {
            IsBusy = true;

            // Step 1: Ask DM-AI for CD (single number, no result shown yet)
            int cd = 12; // safe default
            if (AIEnabled)
            {
                string cdPrompt = DNP_PromptBuilder.BuildDifficultyClassPrompt(
                    _session, _director, context);
                string cdResult = null;
                yield return RunLLMCall(_dmSystemPrompt, cdPrompt, r => cdResult = r);

                if (!string.IsNullOrEmpty(cdResult))
                {
                    // Parse first number found in response
                    foreach (var word in cdResult.Split(' ', '\n'))
                    {
                        int parsed;
                        if (int.TryParse(word.Trim(), out parsed)
                            && parsed >= 1 && parsed <= 30)
                        { cd = parsed; break; }
                    }
                }
            }

            // Step 2: Compare roll to CD
            bool success = roll >= cd;
            string outcome = success
                ? (roll == 20 ? "CRITICAL SUCCESS" : "Success (rolled " + roll + " vs CD " + cd + ")")
                : (roll == 1  ? "CRITICAL FAILURE" : "Failure (rolled " + roll + " vs CD " + cd + ")");

            DNP_SessionManager.AddEntry(DNP_LogEntry.EntryType.System, "System",
                "CD " + cd + " — " + outcome);

            // Step 3: DM narrates result
            string userPrompt = DNP_PromptBuilder.BuildSkillCheckNarrationPrompt(
                _session, _director, pc, roll, cd, success, context);

            string narration = null;
            yield return RunLLMCall(_dmSystemPrompt, userPrompt, r => narration = r);

            if (IsValidResponse(narration))
            {
                var (dmC2, dmT2) = DNP_TagParser.Parse(narration, _session);
                _lastDMNarration = dmC2;
                DNP_SessionManager.AddEntry(DNP_LogEntry.EntryType.DMAssist,
                    "DNP.Log.SpeakerDMAssist".Translate(), dmC2);
                foreach (var t in dmT2) yield return ExecuteDMTag(t);
            }
            IsBusy = false;
            _director.AISlotCompleted();
        }

        // ── Colonist AI combat turn ───────────────────────────────

        private IEnumerator RunColonistCombatTurn(DNP_PlayerCharacter pc)
        {
            IsBusy = true;
            yield return new WaitForSeconds(0.5f);

            var (intent, target, healTarget) =
                _director.GetColonistCombatIntent(pc, _session);

            var ruleset  = DNP_ContentRegistry.GetRuleset(_session.rulesetId)
                        ?? DNP_ContentRegistry.FirstRuleset;
            var rollType = _director.DetermineRollType(pc);
            var (rollDisplay, roll) = _director.RollD20(rollType);

            string combatResult;
            string logLine;

            if (intent == DNP_TurnDirector.CombatIntent.Heal && healTarget != null)
            {
                int healed = Mathf.Min(6, healTarget.maxHp - healTarget.hp);
                healTarget.hp += healed;
                combatResult  = pc.characterName + " heals " + healTarget.characterName
                    + " for " + healed + " HP.";
                logLine = pc.characterName + " — Curación → " + healTarget.characterName
                    + " +" + healed + " HP";
            }
            else if (target != null)
            {
                var enemyDef = DNP_ContentRegistry.GetEnemy(target.enemyId);
                combatResult = DNP_CombatResolver.PlayerAttackWithRoll(pc, target, enemyDef, ruleset, roll);
                logLine = pc.characterName + " — Ataque — " + rollDisplay;
            }
            else
            {
                combatResult = pc.characterName + " waits.";
                logLine = pc.characterName + " — Espera";
            }

            DNP_SessionManager.AddEntry(DNP_LogEntry.EntryType.DiceRoll,
                "DNP.Log.SpeakerDice".Translate(), logLine);
            DNP_SessionManager.AddEntry(DNP_LogEntry.EntryType.System, "System", combatResult);

            // Narrate
            string userPrompt = intent == DNP_TurnDirector.CombatIntent.Heal
                ? DNP_PromptBuilder.BuildDMCombatPrompt(_session, _director, combatResult)
                : DNP_PromptBuilder.BuildPlayerAttackPrompt(
                    _session, _director, pc, target, roll,
                    rollType.ToString(), combatResult, "");

            string rawNarration = null;
            yield return RunLLMCall(_dmSystemPrompt, userPrompt, r => rawNarration = r);

            if (IsValidResponse(rawNarration))
            {
                var (clean, tags) = DNP_TagParser.Parse(rawNarration, _session);
                _lastDMNarration = clean;
                DNP_SessionManager.AddEntry(DNP_LogEntry.EntryType.DMAssist,
                    "DNP.Log.SpeakerDMAssist".Translate(), clean);
                foreach (var tag in tags)
                    yield return ExecuteDMTag(tag);
            }

            // Check win/loss
            if (DNP_CombatResolver.IsEncounterOver(_session.activeEncounter))
            {
                DNP_SessionManager.AddEntry(DNP_LogEntry.EntryType.System, "System",
                    "DNP.Log.EncounterWon".Translate());
                DNP_SessionManager.DistributeLoot(_session, _session.activeEncounter);
                _director.EndCombat();
                IsBusy = false;
                yield break;
            }

            if (DNP_CombatResolver.IsPartyWiped(_session))
            {
                DNP_SessionManager.AddEntry(DNP_LogEntry.EntryType.System, "System",
                    "DNP.Log.PartyWiped".Translate());
                _director.EndCombat();
                IsBusy = false;
                yield break;
            }

            IsBusy = false;
            _director.AISlotCompleted();
        }

        // Called by SessionWindow when the human player presses the roll button
        public void PlayerRolled(int result, string rollerName, string context)
        {
            _director.PlayerRolled();

            DNP_SessionManager.AddEntry(DNP_LogEntry.EntryType.DiceRoll,
                "DNP.Log.SpeakerDice".Translate(),
                rollerName + " — d20 → " + result
                + (result == 20 ? " ★ CRÍTICO" : result == 1 ? " ✗ PIFIA" : ""));

            if (AIEnabled)
                Kick(RunRollNarration(result, rollerName, context));
        }

        // Called for AI colonists — rolls automatically, no player button needed
        public void AutoRollForColonist(DNP_PlayerCharacter pc, string context)
        {
            int result = DNP_Dice.Roll(20);

            DNP_SessionManager.AddEntry(DNP_LogEntry.EntryType.DiceRoll,
                "DNP.Log.SpeakerDice".Translate(),
                pc.characterName + " — d20 → " + result
                + (result == 20 ? " ★ CRÍTICO" : result == 1 ? " ✗ PIFIA" : ""));

            if (AIEnabled)
                Kick(RunRollNarration(result, pc.characterName, context));
        }

        private IEnumerator RunRollNarration(int roll, string rollerName, string context)
        {
            IsBusy = true;
            string userPrompt = DNP_PromptBuilder.BuildRollResultNarrationPrompt(
                _session, _director, rollerName, roll, context);

            string result = null;
            yield return RunLLMCall(_dmSystemPrompt, userPrompt, r => result = r);

            if (IsValidResponse(result))
            {
                var (dmC, dmT) = DNP_TagParser.Parse(result, _session);
                _lastDMNarration = dmC;
                DNP_SessionManager.AddEntry(DNP_LogEntry.EntryType.DMAssist,
                    "DNP.Log.SpeakerDMAssist".Translate(), dmC);
                foreach (var t in dmT) yield return ExecuteDMTag(t);
            }
            IsBusy = false;
            _director.AISlotCompleted();
        }

        public DNP_TurnDirector Director => _director;
    }
}