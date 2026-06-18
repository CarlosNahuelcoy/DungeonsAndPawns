using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using RimWorld;

namespace DungeonsAndPawns
{
    // ─────────────────────────────────────────────────────────────
    // DIRECTOR PROFILE — DM personality preset
    // ─────────────────────────────────────────────────────────────
    public enum DNP_DirectorProfile
    {
        Narrator,   // Story-first. Player gets more space, AI colonists react.
        Tactical,   // Combat-first. Strict initiative, mechanical precision.
        Chaotic     // Unpredictable. AI colonists take initiative, events interrupt.
    }

    public enum DNP_ColonistInitiative
    {
        Reactive,    // Only when DM calls them
        Active,      // Every 3 player turns
        VeryActive   // Every 2 player turns
    }

    public enum DNP_InactivitySpeed
    {
        Patient,  // 180s
        Normal,   // 90s
        Urgent    // 45s
    }

    // ─────────────────────────────────────────────────────────────
    // TURN SLOT — who acts next
    // ─────────────────────────────────────────────────────────────
    public enum DNP_TurnSlot
    {
        Player,      // Human player writes their action
        ColonistAI,  // One AI colonist acts
        DM,          // DM narrates the round result / advances story
        Enemy        // Enemy acts (resolved by code, narrated by DM-AI)
    }

    public class DNP_TurnActor
    {
        public DNP_TurnSlot slot;
        public int          colonistIndex; // used when slot == ColonistAI
        public string       enemyInstanceId; // used when slot == Enemy
        public int          initiativeRoll;
        public string       displayName;
    }

    // ─────────────────────────────────────────────────────────────
    // TURN DIRECTOR
    // Pure logic — no AI calls. Decides whose turn it is,
    // enforces balance rules, tracks combat state.
    // ─────────────────────────────────────────────────────────────
    public class DNP_TurnDirector
    {
        // ── Configuration (set from settings + colonist traits) ──

        public DNP_DirectorProfile    Profile           = DNP_DirectorProfile.Narrator;
        public DNP_ColonistInitiative ColonistInitiative = DNP_ColonistInitiative.Active;
        public DNP_InactivitySpeed    InactivitySpeed   = DNP_InactivitySpeed.Normal;
        public int                    EnemyDifficultyBonus = 0; // -2 / 0 / +2 / +4

        // ── State ────────────────────────────────────────────────

        public enum TurnMode { Free, Combat }
        public TurnMode Mode { get; private set; } = TurnMode.Free;

        public int  RoundNumber          { get; private set; } = 0;
        public bool IsPlayerTurn         { get; private set; } = true;
        public bool WaitingForPlayerInput { get; private set; } = true;

        // Current actor in the queue
        private List<DNP_TurnActor> _order    = new List<DNP_TurnActor>();
        private int                 _orderIdx = 0;

        // Balance tracking
        private int   _consecutiveAITurns  = 0;
        private int   _playerTurnsSinceAIInitiative = 0;
        private float _inactivityTimer     = 0f;
        private bool  _inactivityPromptSent = false;

        // Max consecutive AI turns before forcing player slot
        private int MaxConsecutiveAI => Profile == DNP_DirectorProfile.Chaotic ? 3 : 2;

        // Inactivity threshold in seconds
        private float InactivityThreshold =>
            InactivitySpeed == DNP_InactivitySpeed.Patient  ? 180f :
            InactivitySpeed == DNP_InactivitySpeed.Urgent   ?  45f : 90f;

        // Initiative threshold for AI colonists acting on their own
        private int ColonistInitiativeTrigger =>
            ColonistInitiative == DNP_ColonistInitiative.VeryActive ? 2 :
            ColonistInitiative == DNP_ColonistInitiative.Reactive    ? 999 : 3;

        // ── Session reference ────────────────────────────────────

        private DNP_Session _session;

        // ── Events ───────────────────────────────────────────────

        // Fired when it's time for a specific actor to go
        public event Action<DNP_TurnActor> OnActorTurn;

        // Fired when the Director wants an inactivity nudge from DM-AI
        public event Action OnInactivityNudge;

        // Fired when a new round begins
        public event Action<int> OnRoundStart;

        // ── Init ─────────────────────────────────────────────────

        public DNP_TurnDirector(DNP_Session session)
        {
            _session = session;
            ApplyColonistDMTraits();
        }

        // Apply trait-based modifiers if a colonist is the DM
        private void ApplyColonistDMTraits()
        {
            if (_session.playerIsDM || _session.dmPawn == null) return;
            var pawn = _session.dmPawn;
            if (pawn?.story?.traits == null) return;

            foreach (var t in pawn.story.traits.allTraits)
            {
                string def = t.def.defName;
                if (t.def == TraitDefOf.Brawler)    { NudgeToward(DNP_DirectorProfile.Tactical); }
                if (t.def == TraitDefOf.Kind)        { EnemyDifficultyBonus = Math.Max(-2, EnemyDifficultyBonus - 1); }
                if (t.def == TraitDefOf.Abrasive)    { EnemyDifficultyBonus = Math.Min(4,  EnemyDifficultyBonus + 1); }
                if (def == "Neurotic")               { NudgeToward(DNP_DirectorProfile.Narrator); }
                if (def == "Optimistic")             { NudgeToward(DNP_DirectorProfile.Chaotic); }
            }
        }

        private void NudgeToward(DNP_DirectorProfile target)
        {
            // Only nudge if not already set by user; treat it as a soft push
            if (Profile == target) return;
            // Move one step closer
            int current = (int)Profile;
            int dest    = (int)target;
            Profile = (DNP_DirectorProfile)((current + dest) / 2);
        }

        // ── Free mode ────────────────────────────────────────────

        public void StartFreeMode()
        {
            Mode              = TurnMode.Free;
            IsPlayerTurn      = true;
            WaitingForPlayerInput = true;
            _inactivityTimer  = 0f;
            _inactivityPromptSent = false;
            _consecutiveAITurns   = 0;
            _playerTurnsSinceAIInitiative = 0;

            Log.Message("[D&P Director] Free mode started.");
        }

        // Called when the player submits their action in free mode
        public void PlayerActed()
        {
            _inactivityTimer      = 0f;
            _inactivityPromptSent = false;
            _consecutiveAITurns   = 0;
            _playerTurnsSinceAIInitiative++;
            WaitingForPlayerInput = false;

            // After player acts → DM narrates, then colonists if appropriate
            IsPlayerTurn = false;
            ScheduleFreeRound();
        }

        // Builds the sequence for one free-mode round
        private void ScheduleFreeRound()
        {
            _order.Clear();
            _orderIdx = 0;

            // DM slot always first after player
            _order.Add(new DNP_TurnActor
            {
                slot = DNP_TurnSlot.DM,
                displayName = "DM"
            });

            // Colonist AI slots
            // Reactive = only when DM explicitly calls them (rare)
            // Active = every ColonistInitiativeTrigger player turns
            // VeryActive = every 2 player turns
            // In free mode, colonists ALWAYS get a slot — the trigger controls
            // whether they take initiative or just react to the DM narration.
            bool colonistsAct = ColonistInitiative != DNP_ColonistInitiative.Reactive
                && _playerTurnsSinceAIInitiative >= ColonistInitiativeTrigger;

            // Always include colonists — at minimum they respond to what the DM narrated
            // (the prompt builder gives them context to react to)
            bool includeColonists = colonistsAct
                || ColonistInitiative == DNP_ColonistInitiative.VeryActive;

            // For Active mode, always include at least one colonist per round
            if (!includeColonists && ColonistInitiative == DNP_ColonistInitiative.Active)
                includeColonists = true; // colonists always participate in free mode

            if (includeColonists)
            {
                _playerTurnsSinceAIInitiative = 0;
                for (int i = 0; i < _session.characters.Count; i++)
                {
                    var pc = _session.characters[i];
                    // Skip the player-controlled character (they act themselves)
                    if (pc.isPlayerControlled) continue;
                    _order.Add(new DNP_TurnActor
                    {
                        slot          = DNP_TurnSlot.ColonistAI,
                        colonistIndex = i,
                        displayName   = pc.characterName
                    });
                }
            }

            AdvanceOrder();
        }

        // ── Combat mode ───────────────────────────────────────────

        public void StartCombat(DNP_Encounter encounter)
        {
            Mode         = TurnMode.Combat;
            RoundNumber  = 1;
            _order.Clear();
            _orderIdx = 0;

            // Roll initiative for everyone
            var actors = new List<DNP_TurnActor>();

            // Player
            actors.Add(new DNP_TurnActor
            {
                slot           = DNP_TurnSlot.Player,
                initiativeRoll = DNP_Dice.Roll(20) + GetPlayerInitBonus(),
                displayName    = "Player"
            });

            // AI colonists
            for (int i = 0; i < _session.characters.Count; i++)
            {
                var pc    = _session.characters[i];
                int bonus = pc.statDexterity / 2;
                actors.Add(new DNP_TurnActor
                {
                    slot           = DNP_TurnSlot.ColonistAI,
                    colonistIndex  = i,
                    initiativeRoll = DNP_Dice.Roll(20) + bonus,
                    displayName    = pc.characterName
                });
            }

            // Enemies
            foreach (var enemy in encounter.enemies)
            {
                var def    = DNP_ContentRegistry.GetEnemy(enemy.enemyId);
                int bonus  = def != null ? def.attackBonus : 0;
                actors.Add(new DNP_TurnActor
                {
                    slot            = DNP_TurnSlot.Enemy,
                    enemyInstanceId = enemy.instanceName,
                    initiativeRoll  = DNP_Dice.Roll(20) + bonus,
                    displayName     = enemy.instanceName
                });
            }

            // Sort descending by initiative
            actors = actors.OrderByDescending(a => a.initiativeRoll).ToList();

            // Insert DM slot at the END of each round (narrates round result)
            // We'll insert it dynamically when we wrap around
            _order = actors;

            Log.Message("[D&P Director] Combat started. Initiative order: "
                + string.Join(", ", _order.Select(a => a.displayName + "(" + a.initiativeRoll + ")")));

            OnRoundStart?.Invoke(RoundNumber);
            AdvanceOrder();
        }

        // Advance to the next actor in the queue
        private void AdvanceOrder()
        {
            if (_order.Count == 0) return;

            // Wrap around = new round
            if (_orderIdx >= _order.Count)
            {
                _orderIdx = 0;
                if (Mode == TurnMode.Combat)
                {
                    RoundNumber++;
                    OnRoundStart?.Invoke(RoundNumber);
                }
                else
                {
                    // Free mode: back to player
                    IsPlayerTurn      = true;
                    WaitingForPlayerInput = true;
                    return;
                }
            }

            var actor = _order[_orderIdx];
            _orderIdx++;

            // Enforce balance: no more than MaxConsecutiveAI without player
            if (actor.slot != DNP_TurnSlot.Player && actor.slot != DNP_TurnSlot.DM)
            {
                _consecutiveAITurns++;
                if (_consecutiveAITurns > MaxConsecutiveAI)
                {
                    // Force player slot before continuing
                    _consecutiveAITurns   = 0;
                    IsPlayerTurn          = true;
                    WaitingForPlayerInput = true;
                    Log.Message("[D&P Director] Balance rule: forcing player turn.");
                    return;
                }
            }
            else if (actor.slot == DNP_TurnSlot.Player)
            {
                _consecutiveAITurns   = 0;
                IsPlayerTurn          = true;
                WaitingForPlayerInput = true;
            }

            OnActorTurn?.Invoke(actor);
        }

        // Called by AIController when an AI slot completes
        public void AISlotCompleted()
        {
            AdvanceOrder();
        }

        // Called by AIController when player slot completes in combat
        public void CombatPlayerActed()
        {
            _consecutiveAITurns   = 0;
            IsPlayerTurn          = false;
            WaitingForPlayerInput = false;
            _inactivityTimer      = 0f;
            _inactivityPromptSent = false;
            AdvanceOrder();
        }

        // ── Roll request ──────────────────────────────────────────
        // When a dice roll is required from the human player,
        // the Director pauses everything until PlayerRolled() is called.

        public bool WaitingForPlayerRoll { get; private set; } = false;
        public string PendingRollContext  { get; private set; } = ""; // shown in UI

        // Called by AIController to request a roll from the human player
        public void RequestPlayerRoll(string context)
        {
            WaitingForPlayerRoll  = true;
            PendingRollContext     = context;
            WaitingForPlayerInput = false; // suppress inactivity timer while waiting for roll
        }

        // Called by SessionWindow when the player presses the roll button
        public void PlayerRolled()
        {
            WaitingForPlayerRoll  = false;
            PendingRollContext     = "";
            ResetInactivity();
        }

        // ── End combat ────────────────────────────────────────────

        public void EndCombat()
        {
            _order.Clear();
            _orderIdx = 0;
            StartFreeMode();
            Log.Message("[D&P Director] Combat ended, returning to free mode.");
        }

        // ── Inactivity timer ──────────────────────────────────────
        // Behaviour depends on who's waiting:
        //   Player-as-character → ONE dramatic nudge, then pause forever
        //   Player-as-DM       → same one nudge (they control narration pace)
        // After the nudge fires, the Director stops ticking until the player acts.

        public void Tick(float deltaTime)
        {
            if (!WaitingForPlayerInput) return;
            if (_inactivityPromptSent)  return; // already nudged — hard pause, no more ticking

            _inactivityTimer += deltaTime;

            if (_inactivityTimer >= InactivityThreshold)
            {
                _inactivityPromptSent = true;     // flag: ONLY fires once
                OnInactivityNudge?.Invoke();
                // After this, Tick() returns immediately every frame — no more events
            }
        }

        public void ResetInactivity()
        {
            _inactivityTimer      = 0f;
            _inactivityPromptSent = false;
        }

        // ── Roll type determination ───────────────────────────────
        // Reads status effects to decide Normal/Advantage/Disadvantage.
        // Called by AIController before any d20 roll.

        public enum RollType { Normal, Advantage, Disadvantage }

        public RollType DetermineRollType(DNP_PlayerCharacter pc)
        {
            if (pc?.activeStatusEffects == null) return RollType.Normal;

            bool hasAdv  = pc.activeStatusEffects.Any(e =>
                e.Contains("advantage") || e.Contains("backstab") || e.Contains("buff"));
            bool hasDis  = pc.activeStatusEffects.Any(e =>
                e.Contains("disadvantage") || e.Contains("debuff"));

            // Wounded penalty (below 25% HP)
            if (!hasDis && pc.maxHp > 0 && pc.hp < pc.maxHp / 4)
                hasDis = true;

            if (hasAdv && !hasDis) return RollType.Advantage;
            if (hasDis && !hasAdv) return RollType.Disadvantage;
            return RollType.Normal;
        }

        /// <summary>
        /// Roll d20 respecting advantage/disadvantage.
        /// Returns (displayString, effectiveResult).
        /// </summary>
        public (string display, int result) RollD20(RollType rollType)
        {
            int r1 = DNP_Dice.Roll(20);
            if (rollType == RollType.Normal)
                return ("d20 → " + r1, r1);

            int r2    = DNP_Dice.Roll(20);
            bool adv  = rollType == RollType.Advantage;
            int  best = adv ? Math.Max(r1, r2) : Math.Min(r1, r2);
            string label = adv ? "ventaja" : "desventaja";
            return ("d20 (" + label + ") [" + r1 + "," + r2 + "] → " + best, best);
        }

        // ── Colonist combat intent ────────────────────────────────
        // Determines what an AI colonist does on their combat turn.
        // Pure logic based on class — no AI call needed.

        public enum CombatIntent { Attack, Heal, UseAbility }

        public (CombatIntent intent, DNP_EnemyInstance target, DNP_PlayerCharacter healTarget)
            GetColonistCombatIntent(DNP_PlayerCharacter pc, DNP_Session session)
        {
            var cls = DNP_ClassRegistry.Get(pc.classId);
            var livingEnemies = session.activeEncounter?.enemies
                .Where(e => e.hp > 0).ToList()
                ?? new List<DNP_EnemyInstance>();
            var woundedAllies = session.characters
                .Where(c => c.hp > 0 && c.hp < c.maxHp * 0.5f && c != pc).ToList();

            // Cleric: heal wounded ally first
            if (cls?.id == "cleric" && woundedAllies.Any())
            {
                var healTarget = woundedAllies.OrderBy(c => c.hp).First();
                return (CombatIntent.Heal, null, healTarget);
            }

            // Paladin: heal if someone is critical
            if (cls?.id == "paladin" && woundedAllies.Any(c => c.hp < c.maxHp * 0.25f))
            {
                var healTarget = woundedAllies.OrderBy(c => c.hp).First();
                return (CombatIntent.Heal, null, healTarget);
            }

            if (!livingEnemies.Any())
                return (CombatIntent.Attack, null, null);

            DNP_EnemyInstance target;
            switch (cls?.id ?? "")
            {
                case "rogue":
                    // Rogue targets weakest enemy (easier kill for backstab)
                    target = livingEnemies.OrderBy(e => e.hp).First();
                    break;
                case "ranger":
                    // Ranger targets weakest enemy too (precision)
                    target = livingEnemies.OrderBy(e => e.hp).First();
                    break;
                default:
                    // Warrior, Mage, Paladin: target most dangerous (most HP)
                    target = livingEnemies.OrderByDescending(e => e.hp).First();
                    break;
            }

            return (CombatIntent.Attack, target, null);
        }

        // ── Is it the player's turn to act? ──────────────────────

        public bool IsPlayerTurnActive =>
            IsPlayerTurn && WaitingForPlayerInput && !WaitingForPlayerRoll;

        // ── Helpers ───────────────────────────────────────────────

        private int GetPlayerInitBonus()
        {
            var playerChar = _session.characters.FirstOrDefault(c => c.isPlayerControlled);
            return playerChar != null ? DNP_Dice.StatBonus(playerChar.statDexterity) : 0;
        }

        public DNP_TurnActor CurrentActor =>
            (_order.Count > 0 && _orderIdx - 1 >= 0 && _orderIdx - 1 < _order.Count)
            ? _order[_orderIdx - 1] : null;

        public string GetInitiativeList() =>
            string.Join(" → ", _order.Select(a => a.displayName));
    }
}