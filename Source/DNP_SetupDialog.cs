using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using RimWorld;

namespace DungeonsAndPawns
{
    public class DNP_SetupDialog : Window
    {
        private readonly List<Pawn> allColonists;

        private List<Pawn>                 selectedPlayers   = new List<Pawn>();
        private Pawn                       selectedDM        = null;
        private bool                       playerIsDM        = true;
        private int                        maxPlayers        = 4;
        private Dictionary<string,string>  classAssignments  = new Dictionary<string,string>();
        private Pawn                       focusedPawn       = null;

        // Which pawn the human player will control (null = observe only)
        private Pawn                       playerCharPawn    = null;

        private DNP_WorldData selectedWorld = null;
        private List<string>  worldFiles    = new List<string>();
        private Vector2       worldScroll;
        private Vector2       classScroll;

        // Bigger window to give everything room to breathe
        public override Vector2 InitialSize => new Vector2(980f, 700f);

        public DNP_SetupDialog()
        {
            doCloseX      = true;
            doCloseButton = false;
            forcePause    = true;

            allColonists = (Find.CurrentMap?.mapPawns?.FreeColonists
                ?? System.Linq.Enumerable.Empty<Pawn>())
                .Where(p =>
                    p != null && !p.Dead && !p.Downed
                    && p.health.capacities.CapableOf(PawnCapacityDefOf.Talking)
                    && p.ageTracker.AgeBiologicalYears >= 10
                    && p.health.summaryHealth.SummaryHealthPercent >= 0.2f)
                .ToList();

            var ruleset = DNP_ContentRegistry.FirstRuleset;
            if (ruleset != null) maxPlayers = ruleset.maxPlayers;

            var comp = DNP_GameComponent.Instance;
            if (comp?.World != null
                && !string.IsNullOrWhiteSpace(comp.World.worldName)
                && comp.World.worldName != "Unnamed World")
                selectedWorld = comp.World;

            worldFiles = DNP_JsonManager.GetWorldFiles();

            foreach (var p in allColonists)
            {
                var sug = DNP_ClassRegistry.SuggestForPawn(p);
                if (sug != null) classAssignments[p.ThingID] = sug.id;
            }
        }

        // ── Layout ────────────────────────────────────────────

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, 34f),
                "DNP.Setup.Title".Translate());
            inRect.yMin += 42f;
            Text.Font = GameFont.Small;

            // Fixed column widths with generous gutters
            float leftW   = 260f;
            float rightW  = 250f;
            float centerW = inRect.width - leftW - rightW - 32f;
            float h       = inRect.height - 60f;
            float y0      = inRect.y;

            DrawColonistPanel(new Rect(inRect.x,                      y0, leftW,   h));
            DrawClassPanel   (new Rect(inRect.x + leftW + 16f,        y0, centerW, h));
            DrawOptionsPanel (new Rect(inRect.xMax - rightW,          y0, rightW,  h));
            DrawStartButton  (new Rect(inRect.x, inRect.yMax - 52f, inRect.width, 48f));
        }

        // ── LEFT: Colonist picker ─────────────────────────────

        private void DrawColonistPanel(Rect rect)
        {
            Widgets.DrawMenuSection(rect);
            var   inner = rect.ContractedBy(10f);
            float y     = inner.y;

            // Header
            Text.Font = GameFont.Tiny;
            Widgets.Label(new Rect(inner.x, y, inner.width, 18f),
                "DNP.Setup.SelectPlayers".Translate(maxPlayers));
            y += 26f;
            Text.Font = GameFont.Small;

            // Card dimensions — fixed, no relative accumulation
            const float CARD_H      = 80f;
            const float PORTRAIT_S  = 56f;
            const float PORTRAIT_X  = 6f;
            const float TEXT_X      = 70f;
            const float BTN_W       = 48f;
            const float BTN_H       = 28f;
            const float GAP         = 6f;

            foreach (var pawn in allColonists)
            {
                bool isPlayer = selectedPlayers.Contains(pawn);
                bool isDM     = !playerIsDM && selectedDM == pawn;
                bool isFocus  = focusedPawn == pawn;

                // Card rect (absolute)
                var card = new Rect(inner.x, y, inner.width, CARD_H);

                // Background
                Color bg = isFocus  ? new Color(0.28f, 0.23f, 0.10f)
                         : isPlayer ? new Color(0.11f, 0.18f, 0.11f)
                                    : new Color(0.09f, 0.09f, 0.09f);
                Widgets.DrawBoxSolid(card, bg);
                Widgets.DrawBox(card, isFocus || isPlayer ? 2 : 1);

                // How many buttons on the right?
                float totalBtnW = !playerIsDM ? BTN_W * 2f + 6f : BTN_W;
                float textW     = inner.width - TEXT_X - totalBtnW - 14f;

                // Click zone (left part of card)
                if (Widgets.ButtonInvisible(new Rect(inner.x, y, inner.width - totalBtnW - 14f, CARD_H)))
                    focusedPawn = isFocus ? null : pawn;

                // Portrait
                Widgets.ThingIcon(new Rect(inner.x + PORTRAIT_X, y + (CARD_H - PORTRAIT_S) / 2f,
                    PORTRAIT_S, PORTRAIT_S), pawn);

                // Name — upper third
                Text.Font = GameFont.Small;
                Widgets.Label(new Rect(inner.x + TEXT_X, y + 14f, textW, 22f),
                    pawn.Name.ToStringShort);

                // Class hint — below name
                Text.Font = GameFont.Tiny;
                GUI.color = isPlayer ? new Color(0.5f, 0.9f, 0.5f) : Color.gray;
                Widgets.Label(new Rect(inner.x + TEXT_X, y + 40f, textW, 18f),
                    GetAssignedClassName(pawn));

                // Health if hurt
                float healthPct = pawn.health.summaryHealth.SummaryHealthPercent;
                if (healthPct < 0.99f)
                {
                    GUI.color = healthPct < 0.5f ? Color.red : new Color(1f, 0.7f, 0.2f);
                    Widgets.Label(new Rect(inner.x + TEXT_X, y + 58f, textW, 16f),
                        Mathf.RoundToInt(healthPct * 100f) + "% HP");
                }
                GUI.color = Color.white;

                // Buttons — right side, vertically centered
                float btnTop = y + (CARD_H - BTN_H) / 2f;

                // P button
                float pBtnX = inner.xMax - BTN_W - 4f;
                if (!playerIsDM) pBtnX -= BTN_W + 6f;

                GUI.color = isPlayer ? new Color(0.3f, 0.78f, 0.3f) : new Color(0.5f, 0.5f, 0.5f);
                if (Widgets.ButtonText(new Rect(pBtnX, btnTop, BTN_W, BTN_H),
                    isPlayer ? "✓ P" : "P"))
                {
                    if (isPlayer) { selectedPlayers.Remove(pawn); if (focusedPawn == pawn) focusedPawn = null; }
                    else if (selectedPlayers.Count < maxPlayers) { selectedPlayers.Add(pawn); focusedPawn = pawn; }
                }
                GUI.color = Color.white;

                // DM button
                if (!playerIsDM)
                {
                    GUI.color = isDM ? new Color(0.85f, 0.62f, 0.15f) : new Color(0.5f, 0.5f, 0.5f);
                    if (Widgets.ButtonText(new Rect(inner.xMax - BTN_W - 4f, btnTop, BTN_W, BTN_H),
                        isDM ? "✓DM" : "DM"))
                    {
                        selectedDM = isDM ? null : pawn;
                        selectedPlayers.Remove(pawn);
                        if (focusedPawn == pawn) focusedPawn = null;
                    }
                    GUI.color = Color.white;
                }

                y += CARD_H + GAP;
            }
        }

        private string GetAssignedClassName(Pawn pawn)
        {
            if (!classAssignments.TryGetValue(pawn.ThingID, out string id)) return "";
            var cls = DNP_ClassRegistry.Get(id);
            return cls != null ? "→ " + cls.className : "";
        }

        // ── CENTER: Class selector ────────────────────────────

        private void DrawClassPanel(Rect rect)
        {
            Widgets.DrawMenuSection(rect);
            var   inner = rect.ContractedBy(10f);
            float y     = inner.y;

            // Header
            Text.Font = GameFont.Tiny;
            if (focusedPawn != null)
            {
                GUI.color = new Color(0.9f, 0.75f, 0.2f);
                Widgets.Label(new Rect(inner.x, y, inner.width, 18f),
                    ((string)"DNP.Setup.ClassFor".Translate(
                        focusedPawn.Name.ToStringShort)).ToUpper());
                GUI.color = Color.white;
            }
            else
            {
                GUI.color = Color.gray;
                Widgets.Label(new Rect(inner.x, y, inner.width, 18f),
                    "DNP.Setup.ClassSelectPawn".Translate());
                GUI.color = Color.white;
            }
            y += 26f;
            Text.Font = GameFont.Small;

            // Card dimensions — all absolute
            const float CARD_H  = 128f;  // enough for 4 rows with breathing room
            const float GAP     = 8f;
            const float EDIT_W  = 56f;
            const float EDIT_H  = 28f;
            const float CHECK_X = 6f;
            const float TEXT_X  = 30f;  // left of text block

            var classes  = DNP_ClassRegistry.All;
            float listH  = inner.height - 46f;
            float totalH = classes.Count * (CARD_H + GAP) + 4f;

            var listRect = new Rect(inner.x, y, inner.width, listH);
            var viewRect = new Rect(0, 0, inner.width - 18f, totalH);
            Widgets.BeginScrollView(listRect, ref classScroll, viewRect);

            float vy = 4f;
            foreach (var cls in classes)
            {
                bool isAssigned  = focusedPawn != null
                    && classAssignments.TryGetValue(focusedPawn.ThingID, out string aid)
                    && aid == cls.id;
                bool isSuggested = focusedPawn != null
                    && DNP_ClassRegistry.SuggestForPawn(focusedPawn)?.id == cls.id;

                var card = new Rect(0, vy, viewRect.width, CARD_H);
                Color bg = isAssigned ? new Color(0.12f, 0.25f, 0.10f) : new Color(0.09f, 0.09f, 0.09f);
                Widgets.DrawBoxSolid(card, bg);
                Widgets.DrawBox(card, isAssigned ? 2 : 1);

                // Click to assign (all except edit button)
                if (focusedPawn != null)
                    if (Widgets.ButtonInvisible(new Rect(0, vy, viewRect.width - EDIT_W - 8f, CARD_H)))
                        classAssignments[focusedPawn.ThingID] = cls.id;

                // Row positions — evenly spaced with padding
                float r1 = vy + 10f;   // class name
                float r2 = vy + 36f;   // primary stat + suggested badge
                float r3 = vy + 60f;   // HP/STR/DEX/MND
                float r4 = vy + 84f;   // description
                float tw = viewRect.width - TEXT_X - EDIT_W - 12f;

                // Checkmark (large, left side)
                if (isAssigned)
                {
                    GUI.color = new Color(0.4f, 1f, 0.4f);
                    Text.Font = GameFont.Medium;
                    Widgets.Label(new Rect(CHECK_X, vy + (CARD_H - 28f) / 2f, 22f, 28f), "✓");
                    GUI.color = Color.white;
                }

                // Row 1: class name
                Text.Font = GameFont.Small;
                Widgets.Label(new Rect(TEXT_X, r1, tw, 22f), cls.className);

                // Row 2: primary stat (colored) | "★ Suggested" badge
                Text.Font = GameFont.Tiny;
                GUI.color = StatColor(cls.primaryStat);
                Widgets.Label(new Rect(TEXT_X, r2, 80f, 18f), cls.primaryStat);
                GUI.color = Color.white;

                if (isSuggested)
                {
                    GUI.color = new Color(0.9f, 0.75f, 0.2f);
                    Widgets.Label(new Rect(TEXT_X + 84f, r2, 100f, 18f),
                        "DNP.Setup.ClassSuggested".Translate());
                    GUI.color = Color.white;
                }

                // Row 3: HP / STR / DEX / MND
                Widgets.Label(new Rect(TEXT_X, r3, tw, 18f),
                    "HP " + cls.baseHp
                    + "    STR " + cls.baseStrength
                    + "    DEX " + cls.baseDexterity
                    + "    MND " + cls.baseMind);

                // Row 4: description — two lines max, no hard truncation mid-word
                if (!string.IsNullOrEmpty(cls.description))
                {
                    GUI.color = new Color(0.58f, 0.58f, 0.58f);
                    // Allow up to 80 chars so text fits on two Tiny lines
                    string desc = cls.description.Length > 78
                        ? cls.description.Substring(0, 76) + "…"
                        : cls.description;
                    Widgets.Label(new Rect(TEXT_X, r4, tw, 36f), desc);
                    GUI.color = Color.white;
                }

                // Edit button — vertically centered right side
                Text.Font = GameFont.Small;
                float editY = vy + (CARD_H - EDIT_H) / 2f;
                if (Widgets.ButtonText(new Rect(viewRect.width - EDIT_W - 4f, editY, EDIT_W, EDIT_H),
                    "DNP.Setup.ClassEdit".Translate()))
                    OpenClassEditor(cls, isNew: false);

                vy += CARD_H + GAP;
            }

            Widgets.EndScrollView();

            // New class button pinned to bottom
            if (Widgets.ButtonText(new Rect(inner.x, inner.yMax - 36f, inner.width, 32f),
                "+ " + "DNP.Setup.ClassNew".Translate()))
                OpenNewClassEditor();
        }
        private Color StatColor(string stat)
        {
            switch (stat)
            {
                case "Strength":  return new Color(1f,   0.55f, 0.35f);
                case "Dexterity": return new Color(0.4f, 0.85f, 0.4f);
                case "Mind":      return new Color(0.5f, 0.7f,  1f);
                default:          return Color.gray;
            }
        }

        private void OpenClassEditor(DNP_ClassData cls, bool isNew)
        {
            Find.WindowStack.Add(new DNP_ClassEditorWindow(cls, isNew, onSaved: (saved) =>
            {
                if (focusedPawn != null && !isNew)
                    classAssignments[focusedPawn.ThingID] = saved.id;
            }));
        }

        private void OpenNewClassEditor()
        {
            var blank = new DNP_ClassData
            {
                id = "", className = "", description = "", flavorText = "",
                baseHp = 10, hpPerLevel = 4,
                baseStrength = 5, baseDexterity = 5, baseMind = 5,
                primaryStat = "Strength", linkedRimWorldSkill = ""
            };
            Find.WindowStack.Add(new DNP_ClassEditorWindow(blank, isNew: true, onSaved: (saved) =>
            {
                if (focusedPawn != null)
                    classAssignments[focusedPawn.ThingID] = saved.id;
            }));
        }

        // ── RIGHT: DM mode + World ────────────────────────────

        private void DrawOptionsPanel(Rect rect)
        {
            Widgets.DrawMenuSection(rect);
            var   inner = rect.ContractedBy(12f);
            float y     = inner.y;

            // DM section
            Text.Font = GameFont.Tiny;
            Widgets.Label(new Rect(inner.x, y, inner.width, 18f),
                "DNP.Setup.DungeonMaster".Translate());
            y += 24f;
            Text.Font = GameFont.Small;

            if (Widgets.RadioButtonLabeled(new Rect(inner.x, y, inner.width, 28f),
                "DNP.Setup.PlayerIsDM".Translate(), playerIsDM))
            { playerIsDM = true; playerCharPawn = null; }
            y += 34f;

            if (Widgets.RadioButtonLabeled(new Rect(inner.x, y, inner.width, 28f),
                "DNP.Setup.ColonistIsDM".Translate(), !playerIsDM))
                playerIsDM = false;
            y += 42f;

            // ── Your Character (only when a colonist is DM) ───────
            if (!playerIsDM)
            {
                GUI.color = new Color(0.4f, 0.35f, 0.2f);
                Widgets.DrawLineHorizontal(inner.x, y, inner.width);
                GUI.color = Color.white;
                y += 12f;

                Text.Font = GameFont.Tiny;
                GUI.color = new Color(0.5f, 0.9f, 0.5f);
                Widgets.Label(new Rect(inner.x, y, inner.width, 18f),
                    "DNP.Setup.YourCharacter".Translate());
                GUI.color = Color.white;
                y += 22f;
                Text.Font = GameFont.Small;

                // Auto-select first available colonist if none selected
                if (playerCharPawn == null && selectedPlayers.Any())
                    playerCharPawn = selectedPlayers.First();

                foreach (var pawn in selectedPlayers)
                {
                    bool isMine = playerCharPawn == pawn;
                    var  row    = new Rect(inner.x, y, inner.width, 30f);

                    Color rowBg = isMine
                        ? new Color(0.12f, 0.24f, 0.12f)
                        : new Color(0.09f, 0.09f, 0.09f);
                    Widgets.DrawBoxSolid(row, rowBg);
                    Widgets.DrawBox(row, isMine ? 2 : 1);

                    Widgets.ThingIcon(new Rect(inner.x + 3f, y + 3f, 24f, 24f), pawn);

                    GUI.color = isMine ? new Color(0.5f, 1f, 0.5f) : Color.white;
                    string lbl = isMine ? "★ " + pawn.Name.ToStringShort : pawn.Name.ToStringShort;
                    Widgets.Label(new Rect(inner.x + 32f, y + 7f, inner.width - 32f, 18f), lbl);
                    GUI.color = Color.white;

                    if (Widgets.ButtonInvisible(row))
                        playerCharPawn = pawn; // always select, never deselect
                    y += 34f;
                }

                if (!selectedPlayers.Any())
                {
                    GUI.color = Color.gray;
                    Text.Font = GameFont.Tiny;
                    Widgets.Label(new Rect(inner.x, y, inner.width, 28f),
                        "DNP.Setup.SelectPlayersFirst".Translate());
                    GUI.color = Color.white;
                    Text.Font = GameFont.Small;
                    y += 32f;
                }
            }

            // Divider
            GUI.color = new Color(0.4f, 0.35f, 0.2f);
            Widgets.DrawLineHorizontal(inner.x, y, inner.width);
            GUI.color = Color.white;
            y += 14f;

            // World section
            Text.Font = GameFont.Tiny;
            Widgets.Label(new Rect(inner.x, y, inner.width, 18f),
                "DNP.Setup.World".Translate());
            y += 24f;
            Text.Font = GameFont.Small;

            // File worlds — show all JSON files, always clickable
            if (worldFiles.Any())
            {
                float rowH  = 48f;
                float listH = Mathf.Min(inner.yMax - y - 80f, worldFiles.Count * (rowH + 4f));
                var   lRect = new Rect(inner.x, y, inner.width, listH);
                var   vRect = new Rect(0, 0, inner.width - 16f, worldFiles.Count * (rowH + 4f));
                Widgets.BeginScrollView(lRect, ref worldScroll, vRect);

                float wy = 0f;
                foreach (var file in worldFiles)
                {
                    var    preview = LoadWorldPreview(file);
                    string pName   = preview?.worldName
                        ?? System.IO.Path.GetFileNameWithoutExtension(file);

                    // Compare by worldName since preview is a new object each frame
                    bool isSel = selectedWorld != null
                        && selectedWorld.worldName == pName;

                    var wRow = new Rect(0, wy, vRect.width, rowH);

                    // Background
                    Color rowBg = isSel
                        ? new Color(0.18f, 0.14f, 0.06f)
                        : new Color(0.09f, 0.09f, 0.09f);
                    Widgets.DrawBoxSolid(wRow, rowBg);
                    Widgets.DrawBox(wRow, isSel ? 2 : 1);

                    if (isSel)
                    {
                        GUI.color = new Color(1f, 0.8f, 0.3f);
                        Widgets.Label(new Rect(4f, wy + 4f, 14f, 20f), "▶");
                        GUI.color = Color.white;
                    }

                    Text.Font = GameFont.Small;
                    GUI.color = isSel ? new Color(1f, 0.9f, 0.6f) : Color.white;
                    Widgets.Label(new Rect(20f, wy + 6f, vRect.width - 26f, 20f), pName);
                    GUI.color = Color.white;

                    Text.Font = GameFont.Tiny;
                    GUI.color = Color.gray;
                    string hint = (preview?.genre ?? "")
                        + (!string.IsNullOrEmpty(preview?.campaignName)
                            ? "  ·  " + preview.campaignName : "");
                    Widgets.Label(new Rect(20f, wy + 28f, vRect.width - 26f, 16f), hint);
                    GUI.color = Color.white;

                    // Full row is clickable
                    if (Widgets.ButtonInvisible(wRow))
                    {
                        var loaded = DNP_JsonManager.ImportWorld(file);
                        if (loaded != null)
                        {
                            selectedWorld = loaded;
                            // Also update GameComponent so it's available in session
                            if (DNP_GameComponent.Instance != null)
                                DNP_GameComponent.Instance.World = loaded;
                        }
                    }
                    wy += rowH + 4f;
                }
                Widgets.EndScrollView();
                y += listH + 8f;
            }

            if (!worldFiles.Any())
            {
                GUI.color = Color.gray;
                Text.Font = GameFont.Tiny;
                Widgets.Label(new Rect(inner.x, y, inner.width, 32f),
                    "DNP.Setup.NoWorlds".Translate());
                GUI.color = Color.white;
                y += 38f;
            }

            // Show selected world name as confirmation
            if (selectedWorld != null)
            {
                Text.Font = GameFont.Tiny;
                GUI.color = new Color(0.5f, 0.9f, 0.5f);
                Widgets.Label(new Rect(inner.x, inner.yMax - 70f, inner.width, 18f),
                    "✓ " + selectedWorld.worldName);
                GUI.color = Color.white;
            }

            // Create world button — full width, opens editor with option to load existing
            Text.Font = GameFont.Small;
            if (Widgets.ButtonText(new Rect(inner.x, inner.yMax - 48f, inner.width, 30f),
                "DNP.Setup.CreateWorld".Translate()))
            {
                Find.WindowStack.Add(new DNP_WorldModeDialog(onCreated: w =>
                {
                    selectedWorld = w;
                    worldFiles    = DNP_JsonManager.GetWorldFiles(); // refresh list
                    if (DNP_GameComponent.Instance != null)
                        DNP_GameComponent.Instance.World = w;
                }));
            }
        }

        private Dictionary<string, DNP_WorldData> _fileCache
            = new Dictionary<string, DNP_WorldData>();

        private DNP_WorldData LoadWorldPreview(string file)
        {
            if (!_fileCache.ContainsKey(file))
            {
                var w = DNP_JsonManager.ImportWorld(file);
                if (w != null) _fileCache[file] = w;
            }
            return _fileCache.ContainsKey(file) ? _fileCache[file] : null;
        }

        // ── Start button ──────────────────────────────────────

        private void DrawStartButton(Rect rect)
        {
            bool canStart = selectedPlayers.Count >= 1
                         && (playerIsDM || selectedDM != null);

            if (!canStart) GUI.color = Color.gray;

            if (Widgets.ButtonText(rect, "DNP.Setup.BeginSession".Translate()) && canStart)
            {
                if (selectedWorld != null && DNP_GameComponent.Instance != null)
                    DNP_GameComponent.Instance.World = selectedWorld;

                string rulesetId = DNP_ContentRegistry.FirstRuleset?.id ?? "standard";

                DNP_SessionManager.StartSession(
                    selectedPlayers, playerIsDM, selectedDM,
                    rulesetId, null, classAssignments, playerCharPawn);

                Close();
                var session = DNP_SessionManager.ActiveSession;
                if (session != null)
                    Find.WindowStack.Add(new DNP_SessionWindow(session));
            }

            GUI.color = Color.white;

            if (!canStart)
            {
                Text.Font = GameFont.Tiny;
                string reason = selectedPlayers.Count < 1
                    ? "DNP.Setup.NeedPlayer".Translate()
                    : "DNP.Setup.NeedDM".Translate();
                Widgets.Label(new Rect(rect.x, rect.y - 20f, rect.width, 18f), reason);
            }
        }
    }

    // ─────────────────────────────────────────────────────────────
    // WORLD MODE DIALOG
    // ─────────────────────────────────────────────────────────────
    public class DNP_WorldModeDialog : Window
    {
        private System.Action<DNP_WorldData> onCreated;
        public override Vector2 InitialSize => new Vector2(480f, 300f);

        public DNP_WorldModeDialog(System.Action<DNP_WorldData> onCreated)
        {
            this.onCreated = onCreated;
            doCloseX = true; doCloseButton = false; forcePause = true;
        }

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, 36f),
                "DNP.World.ChooseMode".Translate());
            inRect.yMin += 44f;

            float half = (inRect.width - 16f) / 2f;
            DrawCard(new Rect(inRect.x,           inRect.y, half, inRect.height - 8f),
                "DNP.World.ModeSimple".Translate(),   "DNP.World.ModeSimpleDesc".Translate(),
                () => { Close(); Find.WindowStack.Add(new DNP_WorldWindow(true)); });
            DrawCard(new Rect(inRect.x + half + 16f, inRect.y, half, inRect.height - 8f),
                "DNP.World.ModeDetailed".Translate(), "DNP.World.ModeDetailedDesc".Translate(),
                () => { Close(); Find.WindowStack.Add(new DNP_WorldWindow(false)); });
        }

        private void DrawCard(Rect rect, string title, string desc, System.Action onClick)
        {
            Widgets.DrawMenuSection(rect);
            var inner = rect.ContractedBy(12f);
            float y = inner.y;

            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(inner.x, y, inner.width, 28f), title);
            y += 32f;

            Text.Font = GameFont.Small;
            GUI.color = Color.gray;
            float dh = Text.CalcHeight(desc, inner.width);
            Widgets.Label(new Rect(inner.x, y, inner.width, dh), desc);
            GUI.color = Color.white;

            if (Widgets.ButtonText(new Rect(inner.x, inner.yMax - 32f, inner.width, 30f), title))
                onClick();
        }
    }
}