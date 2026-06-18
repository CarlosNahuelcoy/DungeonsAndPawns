using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using RimWorld;

namespace DungeonsAndPawns
{
    // ─────────────────────────────────────────────────────────────
    // MAIN SESSION WINDOW
    // Three panels: LEFT character sheets | CENTER log | RIGHT DM tools
    // ─────────────────────────────────────────────────────────────
    public class DNP_SessionWindow : Window
    {
        private readonly DNP_Session    session;
        private DNP_RulesetData ruleset;

        private Vector2 logScrollPos;
        private int     _lastLogCount = 0;
        private int     selectedCharIndex = 0;
        private string  dmInputText       = "";

        // Dice animation — improved
        private bool   showDiceAnim    = false;
        private float  diceAnimTime    = 0f;      // seconds since animation started
        private int    diceResult      = 0;
        private string diceRollerName  = "";       // who rolled
        private const float DICE_SPIN_DURATION = 1.8f;  // seconds spinning
        private const float DICE_SHOW_DURATION = 2.5f;  // total before auto-dismiss
        private static readonly int[] SPIN_FACES = { 1,6,3,18,7,14,2,20,11,9,4,17,8,13,5,16 };

        // DM console overlay (shown when player is a character, not DM)
        private bool _showDMConsole = false;

        public override Vector2 InitialSize => new Vector2(1000f, 700f);

        public DNP_SessionWindow(DNP_Session session)
        {
            this.session = session;
            this.ruleset = DNP_ContentRegistry.GetRuleset(session.rulesetId) ?? DNP_ContentRegistry.FirstRuleset;

            forcePause              = false;
            doCloseX                = true;
            doCloseButton           = false;
            closeOnAccept           = false; // prevent Enter from closing window
            absorbInputAroundWindow = false;
            resizeable              = true;
            draggable               = true;
        }

        private float _lastUpdateTime = 0f; // for inactivity tick

        public override void WindowUpdate()
        {
            base.WindowUpdate();
            float now   = Time.realtimeSinceStartup;
            float delta = _lastUpdateTime > 0f ? now - _lastUpdateTime : 0f;
            _lastUpdateTime = now;
            if (delta > 0f && delta < 1f)
                DNP_GameComponent.Instance?.AIController?.Tick(delta);
        }

        private void SubmitInput()
        {
            if (string.IsNullOrWhiteSpace(dmInputText)) return;

            var  ctrl         = DNP_GameComponent.Instance?.AIController;
            bool playerIsChar = session.characters.Any(c => c.isPlayerControlled)
                             && !session.playerIsDM;
            bool aiIsBusy     = ctrl?.IsBusy == true;
            bool canAct       = playerIsChar
                ? (ctrl?.Director?.IsPlayerTurnActive == true && !aiIsBusy)
                : !aiIsBusy;

            if (!canAct) return;

            string text = dmInputText;
            dmInputText  = "";
            _inputScroll = Vector2.zero;

            if (text.Trim() == "[debug_combat]")
            { TriggerDebugCombat(ctrl); return; }

            ctrl?.OnPlayerAction(text);
        }

        public override void DoWindowContents(Rect inRect)
        {
            DrawTitleBar(inRect);
            inRect.yMin += 36f;

            bool playerIsChar = session.characters.Any(c => c.isPlayerControlled)
                             && !session.playerIsDM;

            float leftW   = 220f;
            float rightW  = 240f;
            float centerW = inRect.width - leftW - rightW - 16f;

            if (playerIsChar)
            {
                // Player-character mode: log takes full right space, no DM panel
                float logW = inRect.width - leftW - 8f;
                DrawCharacterPanel(new Rect(inRect.x,             inRect.y, leftW, inRect.height));
                DrawNarrativeLog  (new Rect(inRect.x + leftW + 8f, inRect.y, logW, inRect.height));
            }
            else
            {
                DrawCharacterPanel(new Rect(inRect.x,              inRect.y, leftW,   inRect.height));
                DrawNarrativeLog  (new Rect(inRect.x + leftW + 8f, inRect.y, centerW, inRect.height));
                DrawDMControls    (new Rect(inRect.xMax - rightW,  inRect.y, rightW,  inRect.height));
            }

            if (_showDMConsole)
                DrawDMConsoleOverlay(inRect);

            if (showDiceAnim)
                DrawDiceAnimation(inRect);

            // Enter key to send — only during keyboard events, not polling
            if (Event.current.type == EventType.KeyDown
                && (Event.current.keyCode == KeyCode.Return
                    || Event.current.keyCode == KeyCode.KeypadEnter)
                && !string.IsNullOrWhiteSpace(dmInputText))
            {
                Event.current.Use();
                SubmitInput();
            }
        }

        // ── Title Bar ─────────────────────────────────────────

        private void DrawTitleBar(Rect rect)
        {
            string dmLabel = session.playerIsDM
                ? "DNP.Session.YouDM".Translate()
                : "DNP.Session.DMLabel".Translate(session.dmPawn?.Name?.ToStringShort ?? "?");

            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(rect.x + 4f, rect.y, rect.width - 270f, 32f),
                "DNP.Session.Title".Translate() + "   |   " + dmLabel);
            Text.Font = GameFont.Small;

            float btnY = rect.y + 2f;

            // End session button
            if (Widgets.ButtonText(new Rect(rect.xMax - 124f, btnY, 120f, 28f),
                "DNP.Session.EndSession".Translate()))
            {
                Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                    "DNP.Session.EndConfirm".Translate(),
                    () => { DNP_SessionManager.EndSession(success: true); Close(); }));
            }

            // World info button — always visible
            GUI.color = new Color(0.6f, 0.5f, 0.25f);
            if (Widgets.ButtonText(new Rect(rect.xMax - 234f, btnY, 104f, 28f),
                "🌍 " + "DNP.Session.WorldBtn".Translate()))
                Find.WindowStack.Add(new DNP_WorldWindow());
            GUI.color = Color.white;

            // DM console toggle — only shown when player is a character
            bool playerIsChar = session.characters.Any(c => c.isPlayerControlled)
                             && !session.playerIsDM;
            if (playerIsChar)
            {
                GUI.color = _showDMConsole
                    ? new Color(0.9f, 0.7f, 0.2f)
                    : new Color(0.55f, 0.55f, 0.55f);
                if (Widgets.ButtonText(new Rect(rect.xMax - 340f, btnY, 100f, 28f),
                    "DNP.Session.DMConsoleBtn".Translate()))
                    _showDMConsole = !_showDMConsole;
                GUI.color = Color.white;
            }
        }

        // ── Character Panel (Left) ────────────────────────────

        private void DrawCharacterPanel(Rect rect)
        {
            Widgets.DrawMenuSection(rect);
            var   inner = rect.ContractedBy(6f);
            float y     = inner.y;

            Text.Font = GameFont.Tiny;
            Widgets.Label(new Rect(inner.x, y, inner.width, 18f),
                "DNP.Session.Characters".Translate());
            y += 22f;

            for (int i = 0; i < session.characters.Count; i++)
            {
                var  pc       = session.characters[i];
                var  tabRect  = new Rect(inner.x, y, inner.width, 30f);
                bool selected = selectedCharIndex == i;

                // Background
                Color bg = pc.isPlayerControlled
                    ? new Color(0.12f, 0.22f, 0.12f)
                    : new Color(0.1f, 0.1f, 0.1f);
                Widgets.DrawBoxSolid(tabRect, bg);
                if (selected) Widgets.DrawHighlight(tabRect);
                Widgets.DrawBox(tabRect, selected ? 2 : 1);

                // Label
                Text.Font = GameFont.Small;
                string label = pc.isPlayerControlled
                    ? "★ " + pc.characterName
                    : pc.characterName;

                GUI.color = pc.isPlayerControlled
                    ? new Color(0.5f, 1f, 0.5f)
                    : Color.white;
                Widgets.Label(new Rect(inner.x + 6f, y + 5f, inner.width - 12f, 20f), label);
                GUI.color = Color.white;

                // HP bar underneath name (compact)
                if (pc.maxHp > 0)
                {
                    float pct     = (float)pc.hp / pc.maxHp;
                    var   barBack = new Rect(inner.x + 6f, y + 23f, inner.width - 12f, 4f);
                    var   barFill = new Rect(inner.x + 6f, y + 23f, (inner.width - 12f) * pct, 4f);
                    Widgets.DrawBoxSolid(barBack, new Color(0.2f, 0.2f, 0.2f));
                    Widgets.DrawBoxSolid(barFill,
                        pct > 0.5f ? new Color(0.3f, 0.7f, 0.3f)
                      : pct > 0.25f ? new Color(0.9f, 0.6f, 0.1f)
                      : Color.red);
                }

                if (Widgets.ButtonInvisible(tabRect)) selectedCharIndex = i;
                y += 34f;
            }

            // Legend
            y += 4f;
            Text.Font = GameFont.Tiny;
            GUI.color = new Color(0.4f, 0.9f, 0.4f);
            Widgets.Label(new Rect(inner.x, y, inner.width, 16f),
                "DNP.Session.YouLabel".Translate());
            GUI.color = Color.white;
            y += 20f;

            Text.Font = GameFont.Small;
            if (selectedCharIndex < session.characters.Count)
                DrawCharSheet(new Rect(inner.x, y, inner.width, inner.yMax - y),
                              session.characters[selectedCharIndex]);
        }

        private void DrawCharSheet(Rect rect, DNP_PlayerCharacter pc)
        {
            var   classDef = DNP_ClassRegistry.Get(pc.classId);
            float y        = rect.y + 4f; // top padding

            Text.Font = GameFont.Small;
            if (pc.isPlayerControlled)
            {
                GUI.color = new Color(0.4f, 1f, 0.5f);
                Widgets.Label(new Rect(rect.x, y, rect.width, 22f), pc.characterName + "  ★");
                GUI.color = Color.white;
            }
            else
            {
                Widgets.Label(new Rect(rect.x, y, rect.width, 22f), pc.characterName);
            }
            y += 24f;

            Text.Font = GameFont.Tiny;
            Widgets.Label(new Rect(rect.x, y, rect.width, 18f),
                "DNP.Session.CharLevel".Translate(classDef?.className ?? "Adventurer", pc.level));
            y += 22f;

            int xpNeeded = Mathf.RoundToInt(100 * Mathf.Pow(1.5f, pc.level - 1));
            DrawBar(rect.x, ref y, rect.width, "HP", pc.hp,  pc.maxHp,  Color.red);
            y += 2f;
            DrawBar(rect.x, ref y, rect.width, "XP", pc.xp,  xpNeeded,  new Color(0.9f, 0.8f, 0.1f));
            y += 10f;

            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(rect.x, y, rect.width, 18f),
                "DNP.Session.StatLine".Translate(pc.statStrength, pc.statDexterity, pc.statMind));
            y += 22f;

            GUI.color = Color.gray;
            Text.Font = GameFont.Tiny;
            float hintH = Text.CalcHeight("\"" + pc.GetPlaystyleHint() + "\"", rect.width);
            Widgets.Label(new Rect(rect.x, y, rect.width, hintH), "\"" + pc.GetPlaystyleHint() + "\"");
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            y += hintH + 8f;

            if (pc.activeStatusEffects.Any())
            {
                Widgets.Label(new Rect(rect.x, y, rect.width, 16f),
                    "Status: " + string.Join(", ", pc.activeStatusEffects));
                y += 18f;
            }

            // Inventory
            if (pc.inventory != null && pc.inventory.Any())
            {
                y += 4f;
                Text.Font = GameFont.Tiny;
                Widgets.Label(new Rect(rect.x, y, rect.width, 16f),
                    "DNP.Session.Inventory".Translate());
                y += 18f;
                Text.Font = GameFont.Small;

                foreach (var itemId in pc.inventory.ToList())
                {
                    var itemData = DNP_ContentRegistry.GetItem(itemId);
                    string itemLabel = itemData?.itemName ?? itemId;
                    var    row       = new Rect(rect.x, y, rect.width, 22f);

                    // Use button for consumables
                    bool isConsumable = itemData?.itemType == "Consumable";
                    if (isConsumable)
                    {
                        float useW = 40f;
                        Widgets.Label(new Rect(rect.x, y + 3f, rect.width - useW - 4f, 16f),
                            itemLabel);
                        if (Widgets.ButtonText(new Rect(rect.xMax - useW, y, useW, 20f),
                            "DNP.Session.UseItem".Translate()))
                        {
                            var ctrl = DNP_GameComponent.Instance?.AIController;
                            ctrl?.UseItem(pc, itemData);
                        }
                    }
                    else
                    {
                        GUI.color = Color.gray;
                        Widgets.Label(new Rect(rect.x, y + 3f, rect.width, 16f),
                            "⚔ " + itemLabel);
                        GUI.color = Color.white;
                    }
                    y += 24f;
                }
            }
        }

        private void DrawBar(float x, ref float y, float width, string label, int cur, int max, Color color)
        {
            Text.Font = GameFont.Tiny;
            Widgets.Label(new Rect(x, y, 24f, 14f), label);
            var barRect = new Rect(x + 26f, y, width - 26f, 14f);
            Widgets.DrawBoxSolid(barRect, new Color(0.2f, 0.2f, 0.2f));
            if (max > 0)
                Widgets.DrawBoxSolid(
                    new Rect(barRect.x, barRect.y, barRect.width * Mathf.Clamp01((float)cur / max), barRect.height),
                    color);
            Widgets.DrawBox(barRect);
            Widgets.Label(new Rect(barRect.x + 2f, barRect.y, barRect.width, barRect.height),
                cur + "/" + max);
            y += 16f;
        }

        // ── Narrative Log (Center) ────────────────────────────

        private void DrawNarrativeLog(Rect rect)
        {
            Widgets.DrawMenuSection(rect);
            var inner = rect.ContractedBy(6f);

            var  ctrl         = DNP_GameComponent.Instance?.AIController;
            bool playerIsChar = session.characters.Any(c => c.isPlayerControlled)
                             && !session.playerIsDM;
            bool rollPending  = ctrl?.Director?.WaitingForPlayerRoll == true;
            bool inCombat     = ctrl?.Director?.Mode == DNP_TurnDirector.TurnMode.Combat;

            // Input area height — dynamic based on text content
            // Minimum 2 lines, maximum 6 lines, scrollable beyond that
            const float INPUT_GAP  = 8f;
            const float BTN_ROW_H  = 30f;
            const float LINE_H     = 18f;
            const float MIN_FIELD  = LINE_H * 2f;   // 2 lines min
            const float MAX_FIELD  = LINE_H * 5f;   // 5 lines max before scroll

            float fieldTextH = MIN_FIELD;
            if (!string.IsNullOrEmpty(dmInputText) && (session.playerIsDM || playerIsChar))
            {
                Text.Font = GameFont.Small;
                float measured = Text.CalcHeight(dmInputText, inner.width - 4f);
                fieldTextH = Mathf.Clamp(measured, MIN_FIELD, MAX_FIELD);
            }

            float inputH = (session.playerIsDM || playerIsChar)
                ? (inCombat && playerIsChar ? 120f : BTN_ROW_H + 6f + fieldTextH + 6f)
                : 0f;
            float rollH  = rollPending ? 56f : 0f;
            float totalBottom = inputH + rollH + (inputH > 0 ? INPUT_GAP : 0f);

            var logRect = new Rect(inner.x, inner.y, inner.width,
                inner.height - totalBottom);

            // ── Log scroll ────────────────────────────────────────
            float contentH = 0f;
            foreach (var entry in session.sessionLog)
                contentH += CalcEntryH(entry, logRect.width);
            contentH += 6f; // bottom padding so last entry breathes

            var viewRect = new Rect(0, 0, logRect.width - 16f,
                Mathf.Max(contentH, logRect.height));
            Widgets.BeginScrollView(logRect, ref logScrollPos, viewRect);

            float ey = 0f;
            foreach (var entry in session.sessionLog)
            {
                float eh = CalcEntryH(entry, viewRect.width);
                DrawLogEntry(new Rect(0, ey, viewRect.width, eh), entry);
                ey += eh;
            }
            Widgets.EndScrollView();

            // Auto-scroll to bottom only when a new entry is added
            int currentCount = session.sessionLog.Count;
            if (currentCount != _lastLogCount)
            {
                _lastLogCount  = currentCount;
                logScrollPos.y = contentH + 999f;
            }

            // ── Roll button ────────────────────────────────────────
            if (rollPending)
                DrawRollButton(new Rect(inner.x, logRect.yMax + INPUT_GAP, inner.width, rollH), ctrl);

            // ── Text input ────────────────────────────────────────
            if (session.playerIsDM || playerIsChar)
                DrawPlayerInput(new Rect(inner.x, inner.yMax - inputH, inner.width, inputH),
                    ctrl, playerIsChar);
        }

        private void DrawRollButton(Rect rect, DNP_AIController ctrl)
        {
            string context = ctrl.Director.PendingRollContext;
            var    playerChar = session.characters.FirstOrDefault(c => c.isPlayerControlled);
            string rollerName = playerChar?.characterName ?? "Player";

            // Context hint above button
            if (!string.IsNullOrEmpty(context))
            {
                Text.Font = GameFont.Tiny;
                GUI.color = new Color(0.9f, 0.75f, 0.2f);
                Widgets.Label(new Rect(rect.x, rect.y + 2f, rect.width, 16f), context);
                GUI.color = Color.white;
            }

            // Pulsing border effect
            float pulse = Mathf.Abs(Mathf.Sin(Time.realtimeSinceStartup * 3f));
            var   btnRect = new Rect(rect.x, rect.y + 18f, rect.width, 34f);
            GUI.color = Color.Lerp(new Color(0.8f, 0.6f, 0.1f), new Color(1f, 0.85f, 0.2f), pulse);
            Widgets.DrawBox(btnRect, 2);
            GUI.color = Color.white;

            Text.Font = GameFont.Small;
            if (Widgets.ButtonText(btnRect, "🎲 " + "DNP.Session.RollD20".Translate()))
            {
                int result = DNP_Dice.Roll(20);
                TriggerDice(result, rollerName);
                ctrl.PlayerRolled(result, rollerName, context);
            }
        }

        // Selected enemy target for attack
        private string _selectedEnemyId = null;

        private Vector2 _inputScroll = Vector2.zero;

        private void DrawPlayerInput(Rect rect, DNP_AIController ctrl, bool playerIsChar)
        {
            bool inCombat   = ctrl?.Director?.Mode == DNP_TurnDirector.TurnMode.Combat;
            bool myTurn     = ctrl?.Director?.IsPlayerTurnActive == true;
            bool aiIsBusy   = ctrl?.IsBusy == true;
            bool canAct     = playerIsChar && myTurn && !aiIsBusy;

            // ── Combat mode: attack panel ──────────────────────────
            if (inCombat && playerIsChar)
            {
                DrawCombatActionPanel(rect, ctrl, canAct);
                return;
            }

            // ── Free mode ─────────────────────────────────────────
            // Row 1: action buttons
            float btnH = 28f;
            float btnW = (rect.width - 6f) / 2f;
            float btnY = rect.y + 2f;

            bool skillEnabled = canAct || !playerIsChar;
            if (!skillEnabled) GUI.color = new Color(0.5f, 0.5f, 0.5f);
            if (Widgets.ButtonText(new Rect(rect.x, btnY, btnW, btnH),
                "🔍 " + "DNP.Session.SkillCheck".Translate()) && skillEnabled)
            {
                var pc = session.characters.FirstOrDefault(c => c.isPlayerControlled);
                if (pc != null && ctrl != null)
                {
                    string ctx = dmInputText.Trim();
                    dmInputText  = "";
                    _inputScroll = Vector2.zero;
                    ctrl.ProcessPlayerSkillCheck(pc, ctx);
                }
            }
            GUI.color = Color.white;

            string btnLabel  = playerIsChar
                ? (string)"DNP.Session.YourAction".Translate()
                : (string)"DNP.Session.Narrate".Translate();
            bool sendEnabled = !string.IsNullOrWhiteSpace(dmInputText)
                            && (canAct || !playerIsChar) && !aiIsBusy;
            if (!sendEnabled) GUI.color = new Color(0.5f, 0.5f, 0.5f);
            if (Widgets.ButtonText(new Rect(rect.x + btnW + 6f, btnY, btnW, btnH),
                btnLabel) && sendEnabled)
            {
                string text = dmInputText;
                dmInputText  = "";
                _inputScroll = Vector2.zero;

                // Debug command
                if (text.Trim() == "[debug_combat]")
                { TriggerDebugCombat(ctrl); }
                else if (ctrl != null && playerIsChar)
                    ctrl.OnPlayerAction(text);
                else
                    ctrl?.OnPlayerAction(text);

                GUI.color = Color.white;
                return;
            }
            GUI.color = Color.white;

            // Row 2: scrollable textarea — grows with content, scrolls at max
            float fieldY = btnY + btnH + 4f;
            float fieldH = rect.yMax - fieldY - 4f;
            fieldH = Mathf.Max(fieldH, 36f);

            var fieldOuter = new Rect(rect.x, fieldY, rect.width, fieldH);

            // Measure text height for inner scroll content
            Text.Font = GameFont.Small;
            float textH = Mathf.Max(
                Text.CalcHeight(dmInputText + "\n", fieldOuter.width - 20f),
                fieldH);
            var fieldInner = new Rect(0, 0, fieldOuter.width - 16f, textH);

            if (!canAct && playerIsChar) GUI.color = new Color(0.5f, 0.5f, 0.5f);

            Widgets.BeginScrollView(fieldOuter, ref _inputScroll, fieldInner);
            string newText = Widgets.TextArea(
                new Rect(0, 0, fieldInner.width, Mathf.Max(textH, fieldH)),
                dmInputText);
            Widgets.EndScrollView();

            GUI.color = Color.white;

            // Auto-scroll to bottom as user types
            if (newText != dmInputText)
            {
                dmInputText = newText;
                _inputScroll.y = textH + 999f;
            }
        }

        private void DrawCombatActionPanel(Rect rect, DNP_AIController ctrl, bool canAct)
        {
            var pc = session.characters.FirstOrDefault(c => c.isPlayerControlled);
            if (pc == null) return;

            float y = rect.y + 2f;

            // Who is whose turn indicator
            Text.Font = GameFont.Tiny;
            if (canAct)
            {
                GUI.color = new Color(0.5f, 1f, 0.5f);
                Widgets.Label(new Rect(rect.x, y, 200f, 16f),
                    "DNP.Session.YourTurn".Translate());
            }
            else
            {
                GUI.color = Color.gray;
                var cur = ctrl?.Director?.CurrentActor;
                string whose = cur != null ? cur.displayName : "...";
                Widgets.Label(new Rect(rect.x, y, 200f, 16f),
                    "DNP.Session.WaitingTurn".Translate(whose));
            }
            GUI.color = Color.white;
            y += 18f;

            // Enemy target selector
            var enemies = session.activeEncounter?.enemies.Where(e => e.hp > 0).ToList();
            if (enemies != null && enemies.Any())
            {
                Text.Font = GameFont.Tiny;
                GUI.color = Color.gray;
                Widgets.Label(new Rect(rect.x, y, rect.width, 16f),
                    "DNP.Session.SelectTarget".Translate());
                y += 16f;

                float bw = (rect.width - (enemies.Count - 1) * 4f) / enemies.Count;
                for (int i = 0; i < enemies.Count; i++)
                {
                    var   e    = enemies[i];
                    bool  sel  = _selectedEnemyId == e.instanceName;
                    float hpPct = (float)e.hp / e.maxHp;
                    var   btn  = new Rect(rect.x + i * (bw + 4f), y, bw, 26f);

                    Color btnColor = sel
                        ? new Color(0.8f, 0.3f, 0.2f)
                        : new Color(0.35f, 0.2f, 0.15f);
                    Widgets.DrawBoxSolid(btn, btnColor);
                    Widgets.DrawBox(btn, sel ? 2 : 1);

                    Text.Font = GameFont.Tiny;
                    GUI.color = Color.white;
                    string label = e.instanceName + " " + e.hp + "/" + e.maxHp;
                    Widgets.Label(new Rect(btn.x + 3f, btn.y + 5f, btn.width - 6f, 16f), label);

                    if (canAct && Widgets.ButtonInvisible(btn))
                        _selectedEnemyId = sel ? null : e.instanceName;
                }
                y += 30f;
            }
            GUI.color = Color.white;
            Text.Font = GameFont.Small;

            // Context field
            float contextW = rect.width - 150f;
            var contextRect = new Rect(rect.x, y, contextW, 28f);
            if (!canAct) GUI.color = new Color(0.5f, 0.5f, 0.5f);
            dmInputText = Widgets.TextField(contextRect, dmInputText);
            GUI.color = Color.white;

            // Attack button
            bool hasTarget = !string.IsNullOrEmpty(_selectedEnemyId);
            bool attackOk  = canAct && hasTarget;
            if (!attackOk) GUI.color = new Color(0.5f, 0.5f, 0.5f);
            if (Widgets.ButtonText(new Rect(rect.x + contextW + 4f, y, 70f, 28f),
                "⚔ " + "DNP.Session.Attack".Translate()) && attackOk)
            {
                var target = session.activeEncounter.enemies
                    .FirstOrDefault(e => e.instanceName == _selectedEnemyId);
                if (target != null)
                {
                    string ctx = dmInputText.Trim();
                    dmInputText      = "";
                    _selectedEnemyId = null;
                    ctrl.ProcessPlayerAttack(pc, target, ctx);
                }
            }
            GUI.color = Color.white;

            // Skill check button
            if (!canAct) GUI.color = new Color(0.5f, 0.5f, 0.5f);
            if (Widgets.ButtonText(new Rect(rect.x + contextW + 78f, y, 72f, 28f),
                "🔍 " + "DNP.Session.Skill".Translate()) && canAct)
            {
                string ctx = dmInputText.Trim();
                dmInputText = "";
                ctrl.ProcessPlayerSkillCheck(pc, ctx);
            }
            GUI.color = Color.white;
        }

        private void TriggerDebugCombat(DNP_AIController ctrl)
        {
            // Spin up a test encounter with 2 goblins
            var goblinDef = DNP_ContentRegistry.GetEnemy("goblin");
            int hp = goblinDef?.hp ?? 5;

            var encounter = new DNP_Encounter
            {
                encounterName = "[DEBUG] Test Combat",
                enemies = new System.Collections.Generic.List<DNP_EnemyInstance>
                {
                    new DNP_EnemyInstance
                    {
                        enemyId      = "goblin",
                        instanceName = "Goblin #1",
                        maxHp = hp, hp = hp
                    },
                    new DNP_EnemyInstance
                    {
                        enemyId      = "goblin",
                        instanceName = "Goblin #2",
                        maxHp = hp, hp = hp
                    }
                }
            };
            session.activeEncounter = encounter;
            ctrl?.OnEncounterStarted(encounter);

            DNP_SessionManager.AddEntry(DNP_LogEntry.EntryType.System, "System",
                "[DEBUG] Combat iniciado. Goblin #1 y Goblin #2 (HP " + hp + "). "
                + "Director: " + ctrl?.Director?.GetInitiativeList());
        }

        private float CalcEntryH(DNP_LogEntry entry, float width)
        {
            Text.Font = GameFont.Small;
            // 10 top + 18 speaker + 4 gap + 2 line + 6 gap + text + 10 bottom
            return Mathf.Max(Text.CalcHeight(entry.text, width - 24f) + 52f, 60f);
        }

        private void DrawLogEntry(Rect rect, DNP_LogEntry entry)
        {
            Color bg;
            if      (entry.entryType == DNP_LogEntry.EntryType.DM)          bg = new Color(0.15f, 0.10f, 0.05f);
            else if (entry.entryType == DNP_LogEntry.EntryType.DMAssist)    bg = new Color(0.06f, 0.11f, 0.06f);
            else if (entry.entryType == DNP_LogEntry.EntryType.Player)      bg = new Color(0.08f, 0.12f, 0.15f);
            else if (entry.entryType == DNP_LogEntry.EntryType.DiceRoll)    bg = new Color(0.10f, 0.16f, 0.10f);
            else if (entry.entryType == DNP_LogEntry.EntryType.DMBriefing)  bg = new Color(0.08f, 0.12f, 0.18f);
            else                                                             bg = new Color(0.10f, 0.10f, 0.10f);

            Widgets.DrawBoxSolid(rect.ContractedBy(2f), bg);

            // Left border — visual shorthand for who is speaking
            if (entry.entryType == DNP_LogEntry.EntryType.DMAssist)
            {
                // Human DM — bright green border = "you"
                Widgets.DrawBoxSolid(new Rect(rect.x + 2f, rect.y + 2f, 3f, rect.height - 4f),
                    new Color(0.35f, 0.85f, 0.35f, 0.9f));
            }
            else if (entry.entryType == DNP_LogEntry.EntryType.DM)
            {
                // AI DM narrator — golden border = "assistant"
                Widgets.DrawBoxSolid(new Rect(rect.x + 2f, rect.y + 2f, 3f, rect.height - 4f),
                    new Color(0.85f, 0.6f, 0.1f, 0.7f));
            }

            if (entry.entryType == DNP_LogEntry.EntryType.DMBriefing)
            {
                GUI.color = new Color(0.3f, 0.6f, 0.9f, 0.7f);
                Widgets.DrawBox(rect.ContractedBy(2f), 1);
                GUI.color = Color.white;
            }

            Color speakerColor;
            if      (entry.entryType == DNP_LogEntry.EntryType.DM)          speakerColor = new Color(1f,   0.75f, 0.2f);
            else if (entry.entryType == DNP_LogEntry.EntryType.DMAssist)    speakerColor = new Color(0.4f, 0.95f, 0.4f);
            else if (entry.entryType == DNP_LogEntry.EntryType.DiceRoll)    speakerColor = new Color(0.4f, 1f,   0.5f);
            else if (entry.entryType == DNP_LogEntry.EntryType.DMBriefing)  speakerColor = new Color(0.5f, 0.8f, 1f);
            else                                                             speakerColor = new Color(0.6f, 0.85f, 1f);

            // Speaker name
            Text.Font = GameFont.Tiny;
            GUI.color = speakerColor;
            string speakerLabel = entry.speakerName;
            // Add subtle tag to AI DM to be explicit
            if (entry.entryType == DNP_LogEntry.EntryType.DM)
                speakerLabel += "  ✦";
            Widgets.Label(new Rect(rect.x + 10f, rect.y + 10f, rect.width - 20f, 18f),
                speakerLabel);
            GUI.color = Color.white;

            // Separator line
            GUI.color = new Color(speakerColor.r, speakerColor.g, speakerColor.b, 0.2f);
            Widgets.DrawLineHorizontal(rect.x + 10f, rect.y + 30f, rect.width - 20f);
            GUI.color = Color.white;

            // Entry text
            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(rect.x + 10f, rect.y + 36f, rect.width - 20f,
                rect.height - 44f), entry.text);
        }

        private void DrawDMControls(Rect rect)
        {
            Widgets.DrawMenuSection(rect);
            var   inner = rect.ContractedBy(8f);
            float y     = inner.y;

            Text.Font = GameFont.Tiny;
            Widgets.Label(new Rect(inner.x, y, inner.width, 18f), "DNP.Session.DMTools".Translate());
            y += 20f;

            SectionHeader(inner, ref y, "DNP.Session.PlayerDice".Translate());
            DiceBtn(inner, ref y, "DNP.Dice.PlayerD20".Translate(), 20, new Color(0.4f, 0.7f, 1f));
            DiceBtn(inner, ref y, "DNP.Dice.PlayerD12".Translate(), 12, new Color(0.4f, 0.7f, 1f));
            DiceBtn(inner, ref y, "DNP.Dice.PlayerD8".Translate(),   8, new Color(0.4f, 0.7f, 1f));
            DiceBtn(inner, ref y, "DNP.Dice.PlayerD6".Translate(),   6, new Color(0.4f, 0.7f, 1f));
            DiceBtn(inner, ref y, "DNP.Dice.PlayerD4".Translate(),   4, new Color(0.4f, 0.7f, 1f));
            y += 4f;

            SectionHeader(inner, ref y, "DNP.Session.DMDice".Translate());
            DiceBtn(inner, ref y, "DNP.Dice.DmD20".Translate(), 20, new Color(1f, 0.5f, 0.3f));
            DiceBtn(inner, ref y, "DNP.Dice.DmD10".Translate(), 10, new Color(1f, 0.5f, 0.3f));
            DiceBtn(inner, ref y, "DNP.Dice.DmD6".Translate(),   6, new Color(1f, 0.5f, 0.3f));
            DiceBtn(inner, ref y, "DNP.Dice.DmD4".Translate(),   4, new Color(1f, 0.5f, 0.3f));
            y += 4f;

            // Request roll from a player character
            SectionHeader(inner, ref y, "DNP.Session.RequestRoll".Translate());
            if (Widgets.ButtonText(new Rect(inner.x, y, inner.width, 28f),
                "DNP.Session.RequestRollBtn".Translate()))
            {
                Find.WindowStack.Add(new DNP_RequestRollDialog(session));
                if (_showDMConsole) _showDMConsole = false;
            }
            y += 32f;

            SectionHeader(inner, ref y, "DNP.Session.Encounter".Translate());
            if (session.activeEncounter == null)
            {
                if (Widgets.ButtonText(new Rect(inner.x, y, inner.width, 28f), "DNP.Session.StartEncounter".Translate()))
                    Find.WindowStack.Add(new DNP_StartEncounterDialog(session));
                y += 32f;
            }
            else
            {
                DrawActiveEncounter(inner, ref y);
            }

            SectionHeader(inner, ref y, "DNP.Session.SessionTools".Translate());
            if (Widgets.ButtonText(new Rect(inner.x, y, inner.width, 28f), "DNP.Session.AwardXP".Translate()))
                Find.WindowStack.Add(new DNP_AwardXPDialog(session, ruleset));
            y += 32f;

            if (Widgets.ButtonText(new Rect(inner.x, y, inner.width, 28f), "DNP.Session.HealParty".Translate()))
            {
                foreach (var pc in session.characters) pc.hp = pc.maxHp;
                DNP_SessionManager.AddEntry(DNP_LogEntry.EntryType.System, "System",
                    "DNP.Session.HealPartyLog".Translate());
            }
            y += 32f;

            if (Widgets.ButtonText(new Rect(inner.x, y, inner.width, 28f), "DNP.Session.ExportSession".Translate()))
                DNP_JsonManager.ExportSession(session);
            y += 32f;
        }

        private void SectionHeader(Rect parent, ref float y, string label)
        {
            GUI.color = new Color(0.5f, 0.4f, 0.2f);
            Widgets.DrawLineHorizontal(parent.x, y, parent.width);
            GUI.color = Color.white;
            y += 2f;
            Text.Font = GameFont.Tiny;
            Widgets.Label(new Rect(parent.x, y, parent.width, 16f), label);
            y += 18f;
        }

        private void DiceBtn(Rect parent, ref float y, string label, int sides,
                              Color? tint = null, string rollerName = "")
        {
            if (tint.HasValue) GUI.color = tint.Value;
            bool clicked = Widgets.ButtonText(new Rect(parent.x, y, parent.width, 24f), label);
            GUI.color = Color.white;

            if (clicked)
            {
                int    result   = DNP_Dice.Roll(sides);
                bool   isDM     = tint.HasValue && tint.Value.r > 0.8f;
                string speaker  = "DNP.Log.SpeakerDice".Translate();
                string prefix   = isDM ? "[DM] " : "[Player] ";
                DNP_SessionManager.AddEntry(DNP_LogEntry.EntryType.DiceRoll, speaker,
                    prefix + "d" + sides + " → " + result);

                // Only d20 gets the cinematic animation with crit/fumble
                if (sides == 20)
                {
                    string name = string.IsNullOrEmpty(rollerName)
                        ? (isDM ? "DM" : "Player")
                        : rollerName;
                    TriggerDice(result, name);
                }
                else
                {
                    TriggerDice(result, ""); // plain animation, no crit/fumble
                }
            }
            y += 26f;
        }

        private void DrawActiveEncounter(Rect parent, ref float y)
        {
            Text.Font = GameFont.Tiny;
            Widgets.Label(new Rect(parent.x, y, parent.width, 16f),
                "⚔ " + session.activeEncounter.encounterName);
            y += 18f;

            foreach (var enemy in session.activeEncounter.enemies)
            {
                if (enemy.hp <= 0) GUI.color = Color.gray;
                Widgets.Label(new Rect(parent.x, y, parent.width, 16f),
                    enemy.instanceName + ": " + enemy.hp + "/" + enemy.maxHp);
                GUI.color = Color.white;
                y += 16f;
            }
            y += 4f;

            if (Widgets.ButtonText(new Rect(parent.x, y, parent.width, 28f), "DNP.Session.ResolveEncounter".Translate()))
            {
                session.activeEncounter.isResolved = true;
                session.activeEncounter = null;
                DNP_SessionManager.AddEntry(DNP_LogEntry.EntryType.System, "System", "DNP.Session.EncounterResolved".Translate());
                DNP_GameComponent.Instance?.AIController?.OnEncounterEnded();
            }
            y += 32f;
        }

        // ── DM Console (player-character mode) ───────────────

        private void DrawDMConsoleOverlay(Rect inRect)
        {
            // Floating panel from the right
            float w = 260f;
            float h = inRect.height;
            var overlay = new Rect(inRect.xMax - w - 36f, inRect.y, w, h);

            Widgets.DrawBoxSolid(overlay, new Color(0.06f, 0.06f, 0.06f, 0.97f));
            Widgets.DrawBox(overlay, 2);

            // Header
            Text.Font = GameFont.Tiny;
            GUI.color = new Color(0.9f, 0.7f, 0.2f);
            Widgets.Label(new Rect(overlay.x + 8f, overlay.y + 6f, overlay.width - 16f, 18f),
                "DNP.Session.DMConsole".Translate());
            GUI.color = Color.white;

            var inner = new Rect(overlay.x + 8f, overlay.y + 28f, overlay.width - 16f, overlay.height - 36f);
            float y = inner.y;
            DrawDMControls(new Rect(inner.x, y, inner.width, inner.height));
        }

        // ── Dice ──────────────────────────────────────────────

        private void TriggerDice(int result, string rollerName = "")
        {
            diceResult      = result;
            diceRollerName  = rollerName;
            showDiceAnim    = true;
            diceAnimTime    = 0f;
        }

        private void DrawDiceAnimation(Rect inRect)
        {
            diceAnimTime += Time.deltaTime;
            if (diceAnimTime > DICE_SHOW_DURATION + 0.5f)
            { showDiceAnim = false; return; }

            bool spinning  = diceAnimTime < DICE_SPIN_DURATION;
            bool revealing = !spinning;

            // Backdrop
            Widgets.DrawBoxSolid(inRect, new Color(0f, 0f, 0f, 0.72f));

            float size    = 200f;
            float centerX = inRect.center.x - size / 2f;
            float centerY = inRect.center.y - size / 2f - 20f;
            var   box     = new Rect(centerX, centerY, size, size);

            // Box color: normal → gold on 20 → red on 1
            Color boxColor = spinning ? new Color(0.18f, 0.13f, 0.06f)
                : diceResult == 20    ? new Color(0.28f, 0.22f, 0.04f)
                : diceResult == 1     ? new Color(0.22f, 0.04f, 0.04f)
                :                       new Color(0.15f, 0.10f, 0.05f);

            Color borderColor = spinning ? new Color(0.5f, 0.4f, 0.2f)
                : diceResult == 20    ? new Color(1f,   0.85f, 0.1f)
                : diceResult == 1     ? new Color(1f,   0.2f,  0.2f)
                :                       new Color(0.7f, 0.55f, 0.2f);

            Widgets.DrawBoxSolid(box, boxColor);
            GUI.color = borderColor;
            Widgets.DrawBox(box, 3);
            GUI.color = Color.white;

            // Number display
            int display = spinning
                ? SPIN_FACES[Mathf.FloorToInt(diceAnimTime * 12f) % SPIN_FACES.Length]
                : diceResult;

            // Pulse scale on reveal
            float scale = revealing ? 1f + Mathf.Sin((diceAnimTime - DICE_SPIN_DURATION) * 8f) * 0.04f : 1f;
            float numSize = 72f * scale;

            Text.Font = GameFont.Medium;
            GUI.color = spinning ? new Color(0.5f, 0.5f, 0.5f)
                : diceResult == 20 ? new Color(1f, 0.9f, 0.2f)
                : diceResult == 1  ? new Color(1f, 0.3f, 0.3f)
                :                    Color.white;

            var numRect = new Rect(box.center.x - numSize / 2f, box.center.y - numSize / 2f,
                numSize, numSize);
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(numRect, display.ToString());
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color   = Color.white;

            // D20 label above box
            if (!string.IsNullOrEmpty(diceRollerName))
            {
                Text.Font = GameFont.Tiny;
                GUI.color = Color.gray;
                Widgets.Label(new Rect(box.x, box.y - 22f, box.width, 20f),
                    diceRollerName + " — d20");
                GUI.color = Color.white;
            }

            // CRÍTICO / PIFIA banner below number
            if (revealing)
            {
                float bannerY = box.yMax + 12f;

                if (diceResult == 20)
                {
                    Text.Font = GameFont.Medium;
                    GUI.color = new Color(1f, 0.9f, 0.1f);
                    Text.Anchor = TextAnchor.MiddleCenter;
                    Widgets.Label(new Rect(box.x, bannerY, box.width, 36f),
                        "DNP.Dice.Critical".Translate());
                    Text.Anchor = TextAnchor.UpperLeft;
                    GUI.color = Color.white;
                }
                else if (diceResult == 1)
                {
                    Text.Font = GameFont.Medium;
                    GUI.color = new Color(1f, 0.25f, 0.25f);
                    Text.Anchor = TextAnchor.MiddleCenter;
                    Widgets.Label(new Rect(box.x, bannerY, box.width, 36f),
                        "DNP.Dice.Fumble".Translate());
                    Text.Anchor = TextAnchor.UpperLeft;
                    GUI.color = Color.white;
                }
                else
                {
                    // Dismiss hint
                    Text.Font = GameFont.Tiny;
                    GUI.color = new Color(0.45f, 0.45f, 0.45f);
                    Text.Anchor = TextAnchor.MiddleCenter;
                    Widgets.Label(new Rect(box.x, bannerY + 6f, box.width, 20f),
                        "DNP.Dice.DismissHint".Translate());
                    Text.Anchor = TextAnchor.UpperLeft;
                    GUI.color = Color.white;
                }

                if (Input.GetMouseButtonDown(0))
                    showDiceAnim = false;
            }
        }

    // ─────────────────────────────────────────────────────────────
    // START ENCOUNTER DIALOG
    // ─────────────────────────────────────────────────────────────
    public class DNP_StartEncounterDialog : Window
    {
        private readonly DNP_Session session;
        private Dictionary<DNP_EnemyData, int> counts = new Dictionary<DNP_EnemyData, int>();
        private string encounterName = "DNP.Encounter.DefaultName".Translate();

        public override Vector2 InitialSize => new Vector2(420f, 500f);

        public DNP_StartEncounterDialog(DNP_Session session)
        {
            this.session = session;
            foreach (var def in DNP_ContentRegistry.AllEnemies)
                counts[def] = 0;
            doCloseX = true;
        }

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, 30f), "DNP.Encounter.Title".Translate());
            inRect.yMin += 34f;

            encounterName = Widgets.TextField(
                new Rect(inRect.x, inRect.y, inRect.width, 28f), encounterName);
            inRect.yMin += 32f;

            Text.Font = GameFont.Small;
            foreach (var kv in counts.ToList())
            {
                Widgets.Label(new Rect(inRect.x, inRect.y, 160f, 28f), kv.Key.enemyName);
                Widgets.Label(new Rect(inRect.x + 165f, inRect.y, 30f, 28f), kv.Value.ToString());

                if (Widgets.ButtonText(new Rect(inRect.x + 200f, inRect.y, 28f, 28f), "-") && kv.Value > 0)
                    counts[kv.Key]--;
                if (Widgets.ButtonText(new Rect(inRect.x + 232f, inRect.y, 28f, 28f), "+"))
                    counts[kv.Key]++;

                inRect.yMin += 30f;
            }

            if (Widgets.ButtonText(new Rect(inRect.x, inRect.yMax - 36f, inRect.width, 32f), "DNP.Encounter.Start".Translate()))
            {
                var encounter = new DNP_Encounter { encounterName = encounterName };
                foreach (var kv in counts)
                {
                    for (int i = 1; i <= kv.Value; i++)
                        encounter.enemies.Add(new DNP_EnemyInstance
                        {
                            enemyId      = kv.Key.id,
                            instanceName = kv.Key.enemyName + " #" + i,
                            maxHp        = kv.Key.hp,
                            hp           = kv.Key.hp
                        });
                }
                session.activeEncounter = encounter;
                DNP_SessionManager.AddEntry(DNP_LogEntry.EntryType.DM, "DM",
                    "DNP.Encounter.LogBegin".Translate(encounterName, encounter.enemies.Count));
                DNP_GameComponent.Instance?.AIController?.OnEncounterStarted(encounter);
                Close();
            }
        }
    }

    // ─────────────────────────────────────────────────────────────
    // AWARD XP DIALOG
    // ─────────────────────────────────────────────────────────────
    public class DNP_AwardXPDialog : Window
    {
        private readonly DNP_Session    session;
        private DNP_RulesetData ruleset;
        private string xpInput = "25";

        public override Vector2 InitialSize => new Vector2(300f, 180f);

        public DNP_AwardXPDialog(DNP_Session session, DNP_RulesetData ruleset)
        {
            this.session = session;
            this.ruleset = ruleset;
            doCloseX     = true;
        }

        public override void DoWindowContents(Rect inRect)
        {
            Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, 24f), "DNP.XP.Label".Translate());
            xpInput = Widgets.TextField(new Rect(inRect.x, inRect.y + 28f, inRect.width, 28f), xpInput);

            if (Widgets.ButtonText(new Rect(inRect.x, inRect.yMax - 36f, inRect.width, 32f), "DNP.XP.Award".Translate())
                && int.TryParse(xpInput, out int xp))
            {
                foreach (var pc in session.characters)
                {
                    pc.xp += xp;
                    int needed = Mathf.RoundToInt(ruleset.baseXpPerLevel
                        * Mathf.Pow(ruleset.xpMultiplierPerLevel, pc.level - 1));
                    if (pc.xp >= needed)
                    {
                        pc.xp -= needed;
                        pc.level++;
                        var cd = DNP_ClassRegistry.Get(pc.classId);
                        if (cd != null) { pc.maxHp += cd.hpPerLevel; pc.hp += cd.hpPerLevel; }
                        DNP_SessionManager.AddEntry(DNP_LogEntry.EntryType.System, "System",
                            "DNP.XP.LogLevelUp".Translate(pc.characterName, pc.level));
                    }
                }
                DNP_SessionManager.AddEntry(DNP_LogEntry.EntryType.System, "System",
                    "DNP.XP.LogAwarded".Translate(xp));
                Close();
            }
        }
    }

    // ─────────────────────────────────────────────────────────────
    // REQUEST ROLL DIALOG
    // DM chooses which character should roll and adds context.
    // If the character is player-controlled → roll button appears in log.
    // If the character is AI-controlled    → rolls automatically.
    // ─────────────────────────────────────────────────────────────
    public class DNP_RequestRollDialog : Window
    {
        private readonly DNP_Session session;
        private int    selectedCharIdx = 0;
        private string context         = "";

        public override Vector2 InitialSize => new Vector2(360f, 260f);

        public DNP_RequestRollDialog(DNP_Session session)
        {
            this.session  = session;
            doCloseX      = true;
            doCloseButton = false;
            forcePause    = false;

            // Default to player character if exists
            int pi = session.characters.FindIndex(c => c.isPlayerControlled);
            if (pi >= 0) selectedCharIdx = pi;
        }

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, 30f),
                "DNP.Session.RequestRoll".Translate());
            inRect.yMin += 34f;
            Text.Font = GameFont.Small;

            // Character selector
            Text.Font = GameFont.Tiny;
            Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, 18f),
                "DNP.Roll.WhoRolls".Translate());
            inRect.yMin += 20f;
            Text.Font = GameFont.Small;

            float btnW = (inRect.width - (session.characters.Count - 1) * 4f)
                       / Mathf.Max(session.characters.Count, 1);
            for (int i = 0; i < session.characters.Count; i++)
            {
                var  pc      = session.characters[i];
                bool sel     = selectedCharIdx == i;
                var  btnRect = new Rect(inRect.x + i * (btnW + 4f), inRect.y, btnW, 28f);
                GUI.color    = sel
                    ? (pc.isPlayerControlled ? new Color(0.3f, 0.8f, 0.4f) : new Color(0.4f, 0.6f, 0.9f))
                    : Color.white;
                if (Widgets.ButtonText(btnRect,
                    (pc.isPlayerControlled ? "★ " : "") + pc.characterName))
                    selectedCharIdx = i;
                GUI.color = Color.white;
            }
            inRect.yMin += 32f;

            // Context field
            Text.Font = GameFont.Tiny;
            Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, 18f),
                "DNP.Roll.Context".Translate());
            inRect.yMin += 20f;
            Text.Font = GameFont.Small;
            context = Widgets.TextField(new Rect(inRect.x, inRect.y, inRect.width, 28f), context);
            inRect.yMin += 36f;

            // Info line
            if (selectedCharIdx < session.characters.Count)
            {
                var pc = session.characters[selectedCharIdx];
                Text.Font = GameFont.Tiny;
                GUI.color = Color.gray;
                string info = pc.isPlayerControlled
                    ? "DNP.Roll.WillShowButton".Translate(pc.characterName)
                    : "DNP.Roll.WillAutoRoll".Translate(pc.characterName);
                Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, 28f), info);
                GUI.color = Color.white;
                inRect.yMin += 30f;
            }

            // Confirm button
            if (Widgets.ButtonText(new Rect(inRect.x, inRect.yMax - 32f, inRect.width, 30f),
                "DNP.Roll.Request".Translate()))
            {
                if (selectedCharIdx < session.characters.Count)
                {
                    var pc   = session.characters[selectedCharIdx];
                    var ctrl = DNP_GameComponent.Instance?.AIController;

                    if (pc.isPlayerControlled)
                    {
                        string rollContext = string.IsNullOrEmpty(context)
                            ? (string)"DNP.Roll.DefaultContext".Translate()
                            : context;
                        ctrl?.Director?.RequestPlayerRoll(rollContext);
                        DNP_SessionManager.AddEntry(DNP_LogEntry.EntryType.System, "System",
                            "DNP.Roll.RequestedLog".Translate(pc.characterName));
                    }
                    else
                    {
                        string rollContext = string.IsNullOrEmpty(context)
                            ? (string)"DNP.Roll.DefaultContext".Translate()
                            : context;
                        ctrl?.AutoRollForColonist(pc, rollContext);
                    }
                }
                Close();
            }
        }
    }
}}

