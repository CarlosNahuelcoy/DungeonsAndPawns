using UnityEngine;
using Verse;
using RimWorld;

namespace DungeonsAndPawns
{
    public class DNP_WorldWindow : Window
    {
        private DNP_WorldData world;
        private bool simpleMode;
        private int  activeTab = 0;

        private Vector2 scrollWorld;
        private Vector2 scrollCampaign;
        private Vector2 scrollAI;
        private Vector2 scrollFiles;

        // Files sidebar
        private System.Collections.Generic.List<string> _worldFiles
            = new System.Collections.Generic.List<string>();

        public override Vector2 InitialSize => new Vector2(940f, 680f);

        public DNP_WorldWindow(bool startInSimple = true)
        {
            var comp = DNP_GameComponent.Instance;
            if (comp != null)
            {
                if (comp.World == null) comp.World = new DNP_WorldData();
                world = comp.World;
            }
            else world = new DNP_WorldData();

            simpleMode    = startInSimple;
            doCloseX      = true;
            doCloseButton = false;
            resizeable    = true;
            draggable     = true;

            _worldFiles = DNP_JsonManager.GetWorldFiles();
        }

        public override void DoWindowContents(Rect inRect)
        {
            // ── Left sidebar: existing worlds ─────────────────
            float sideW = 180f;
            DrawWorldsSidebar(new Rect(inRect.x, inRect.y, sideW, inRect.height));

            // ── Main editor area ──────────────────────────────
            var editorRect = new Rect(inRect.x + sideW + 8f, inRect.y,
                inRect.width - sideW - 8f, inRect.height);
            DrawEditor(editorRect);
        }

        private void DrawWorldsSidebar(Rect rect)
        {
            Widgets.DrawMenuSection(rect);
            var   inner = rect.ContractedBy(6f);
            float y     = inner.y;

            Text.Font = GameFont.Tiny;
            GUI.color = new Color(0.7f, 0.55f, 0.2f);
            Widgets.Label(new Rect(inner.x, y, inner.width, 16f),
                "DNP.World.ExistingWorlds".Translate());
            GUI.color = Color.white;
            y += 20f;

            // New world button
            if (Widgets.ButtonText(new Rect(inner.x, y, inner.width, 26f),
                "+ " + "DNP.World.NewWorld".Translate()))
            {
                world = new DNP_WorldData();
                if (DNP_GameComponent.Instance != null)
                    DNP_GameComponent.Instance.World = world;
            }
            y += 30f;

            // Divider
            GUI.color = new Color(0.35f, 0.3f, 0.15f);
            Widgets.DrawLineHorizontal(inner.x, y, inner.width);
            GUI.color = Color.white;
            y += 8f;

            // File list
            float listH = inner.yMax - y;
            var   lRect = new Rect(inner.x, y, inner.width, listH);
            float rowH  = 44f;
            float contentH = _worldFiles.Count * (rowH + 3f);
            var   vRect = new Rect(0, 0, inner.width - 16f, Mathf.Max(contentH, listH));
            Widgets.BeginScrollView(lRect, ref scrollFiles, vRect);

            float wy = 0f;
            foreach (var file in _worldFiles)
            {
                string fname   = System.IO.Path.GetFileNameWithoutExtension(file);
                var    preview = TryLoadPreview(file);
                string label   = preview?.worldName ?? fname;
                string sub     = preview?.genre ?? "";

                bool isCurrent = world?.worldName == label;
                var  row       = new Rect(0, wy, vRect.width, rowH);

                Widgets.DrawBoxSolid(row,
                    isCurrent ? new Color(0.18f, 0.14f, 0.06f) : new Color(0.09f, 0.09f, 0.09f));
                Widgets.DrawBox(row, isCurrent ? 2 : 1);

                if (isCurrent) { GUI.color = new Color(1f, 0.85f, 0.3f); }
                Text.Font = GameFont.Tiny;
                Widgets.Label(new Rect(6f, wy + 5f, vRect.width - 8f, 18f), label);
                GUI.color = Color.gray;
                Widgets.Label(new Rect(6f, wy + 24f, vRect.width - 8f, 14f), sub);
                GUI.color = Color.white;

                if (!isCurrent && Widgets.ButtonInvisible(row))
                {
                    var loaded = DNP_JsonManager.ImportWorld(file);
                    if (loaded != null)
                    {
                        world = loaded;
                        if (DNP_GameComponent.Instance != null)
                            DNP_GameComponent.Instance.World = loaded;
                    }
                }
                wy += rowH + 3f;
            }
            Widgets.EndScrollView();
        }

        private DNP_WorldData TryLoadPreview(string file)
        {
            try
            {
                var node = SimpleJSON.JSON.Parse(System.IO.File.ReadAllText(file));
                if (node == null) return null;
                return new DNP_WorldData
                {
                    worldName = node["worldName"]?.Value ?? "",
                    genre     = node["genre"]?.Value ?? ""
                };
            }
            catch { return null; }
        }

        private void DrawEditor(Rect inRect)
        {
            float x = inRect.x;
            float y = inRect.y;

            // ── Row 1: Title ──────────────────────────────────
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(x, y, inRect.width - 240f, 30f),
                "DNP.World.WindowTitle".Translate());
            Text.Font = GameFont.Small;

            // Save + Export flush right
            float bw = 110f;
            if (Widgets.ButtonText(new Rect(inRect.xMax - bw, y + 1f, bw, 28f),
                "DNP.World.ExportJSON".Translate()))
                DNP_JsonManager.ExportWorld(world);
            if (Widgets.ButtonText(new Rect(inRect.xMax - bw * 2f - 6f, y + 1f, bw, 28f),
                "DNP.World.Save".Translate()))
                SaveWorld();

            y += 34f;

            // ── Row 2: Mode toggle ────────────────────────────
            Text.Font = GameFont.Tiny;
            GUI.color = Color.gray;
            Widgets.Label(new Rect(x, y + 7f, 46f, 18f), "MODO:");
            GUI.color = Color.white;

            float mw = 108f;
            GUI.color = simpleMode
                ? new Color(0.9f, 0.75f, 0.2f)
                : new Color(0.55f, 0.55f, 0.55f);
            if (Widgets.ButtonText(new Rect(x + 50f, y, mw, 28f),
                "DNP.World.ModeSimple".Translate()))
            {
                simpleMode = true;
                activeTab  = 0;
            }

            GUI.color = !simpleMode
                ? new Color(0.9f, 0.75f, 0.2f)
                : new Color(0.55f, 0.55f, 0.55f);
            if (Widgets.ButtonText(new Rect(x + 50f + mw + 6f, y, mw + 10f, 28f),
                "DNP.World.ModeDetailed".Translate()))
                simpleMode = false;
            GUI.color = Color.white;

            Text.Font = GameFont.Small;
            y += 34f;

            inRect.yMin = y;

            // ── Simple mode: no tabs, just the world fields ───
            if (simpleMode)
            {
                DrawSimpleContent(inRect);
            }
            else
            {
                // ── Detailed mode: three tabs ─────────────────
                DrawTabBar(inRect);
                inRect.yMin += 38f;

                switch (activeTab)
                {
                    case 0: DrawWorldTabDetailed(inRect);   break;
                    case 1: DrawCampaignTab(inRect);        break;
                    case 2: DrawAITab(inRect);              break;
                }
            }
        }

        // ── SIMPLE MODE ───────────────────────────────────────
        // One scrollable page: name, genre, tone, summary.
        // Campaign name + objective at the bottom for quick setup.

        private void DrawSimpleContent(Rect rect)
        {
            float contentH = 560f;
            var   view     = new Rect(0, 0, rect.width - 20f, contentH);
            Widgets.BeginScrollView(rect, ref scrollWorld, view);
            float y = 8f;
            float w = view.width;

            world.worldName = Field(w, ref y, "DNP.World.FieldWorldName".Translate(),
                world.worldName, 34f);
            y += 4f;

            // Genre + Tone side by side
            float half = (w - 12f) / 2f;
            Text.Font = GameFont.Tiny;
            Widgets.Label(new Rect(0,          y, half, 18f),
                ((string)"DNP.World.FieldGenre".Translate()).ToUpper());
            Widgets.Label(new Rect(half + 12f, y, half, 18f),
                ((string)"DNP.World.FieldTone".Translate()).ToUpper());
            y += 20f;
            Text.Font = GameFont.Small;
            world.genre = Widgets.TextField(new Rect(0,          y, half, 32f), world.genre);
            world.tone  = Widgets.TextField(new Rect(half + 12f, y, half, 32f), world.tone);
            y += 46f;

            world.summary = TextArea(w, ref y, "DNP.World.FieldSummary".Translate(),
                world.summary, 180f, "DNP.World.HintSummary".Translate());

            // Divider
            GUI.color = new Color(0.4f, 0.35f, 0.2f);
            Widgets.DrawLineHorizontal(0, y, w);
            GUI.color = Color.white;
            y += 12f;

            // Campaign quick-setup
            Text.Font = GameFont.Tiny;
            GUI.color = Color.gray;
            Widgets.Label(new Rect(0, y, w, 16f),
                "DNP.World.CampaignQuick".Translate());
            GUI.color = Color.white;
            y += 20f;
            Text.Font = GameFont.Small;

            world.campaignName      = Field(w, ref y,
                "DNP.World.FieldCampaignName".Translate(),
                world.campaignName, 32f,
                "DNP.World.HintCampaignName".Translate());

            world.campaignObjective = TextArea(w, ref y,
                "DNP.World.FieldObjective".Translate(),
                world.campaignObjective, 80f,
                "DNP.World.HintObjective".Translate());

            // Hint to switch to detailed
            Text.Font = GameFont.Tiny;
            GUI.color = new Color(0.5f, 0.5f, 0.5f);
            Widgets.Label(new Rect(0, y, w, 20f), "DNP.World.SwitchToDetailed".Translate());
            GUI.color = Color.white;

            Widgets.EndScrollView();
        }

        // ── DETAILED: WORLD TAB ───────────────────────────────

        private void DrawWorldTabDetailed(Rect rect)
        {
            float contentH = 980f;
            var   view     = new Rect(0, 0, rect.width - 20f, contentH);
            Widgets.BeginScrollView(rect, ref scrollWorld, view);
            float y = 8f;
            float w = view.width;

            world.worldName = Field(w, ref y, "DNP.World.FieldWorldName".Translate(), world.worldName, 34f);
            world.genre     = Field(w, ref y, "DNP.World.FieldGenre".Translate(),     world.genre,     34f, "DNP.World.HintGenre".Translate());
            world.tone      = Field(w, ref y, "DNP.World.FieldTone".Translate(),      world.tone,      34f, "DNP.World.HintTone".Translate());
            y += 8f;
            world.summary   = TextArea(w, ref y, "DNP.World.FieldSummary".Translate(),   world.summary,   140f, "DNP.World.HintSummary".Translate());
            world.history   = TextArea(w, ref y, "DNP.World.FieldHistory".Translate(),   world.history,   110f, "DNP.World.HintHistory".Translate());
            world.factions  = TextArea(w, ref y, "DNP.World.FieldFactions".Translate(),  world.factions,  110f, "DNP.World.HintFactions".Translate());
            world.locations = TextArea(w, ref y, "DNP.World.FieldLocations".Translate(), world.locations, 110f, "DNP.World.HintLocations".Translate());
            world.rules     = TextArea(w, ref y, "DNP.World.FieldRules".Translate(),     world.rules,      90f, "DNP.World.HintRules".Translate());

            Widgets.EndScrollView();
        }

        // ── DETAILED: CAMPAIGN TAB ────────────────────────────

        private void DrawCampaignTab(Rect rect)
        {
            var   view = new Rect(0, 0, rect.width - 20f, 720f);
            Widgets.BeginScrollView(rect, ref scrollCampaign, view);
            float y = 8f;
            float w = view.width;

            GUI.color = Color.gray;
            Text.Font = GameFont.Tiny;
            string exp  = "DNP.World.CampaignExplainer".Translate();
            float  expH = Text.CalcHeight(exp, w);
            Widgets.Label(new Rect(0, y, w, expH), exp);
            GUI.color = Color.white;
            y += expH + 14f;

            Text.Font = GameFont.Small;
            world.campaignName      = Field(w, ref y, "DNP.World.FieldCampaignName".Translate(), world.campaignName,      34f, "DNP.World.HintCampaignName".Translate());
            world.campaignObjective = TextArea(w, ref y, "DNP.World.FieldObjective".Translate(), world.campaignObjective, 100f, "DNP.World.HintObjective".Translate());
            world.campaignNotes     = TextArea(w, ref y, "DNP.World.FieldDMNotes".Translate(),   world.campaignNotes,     220f, "DNP.World.HintDMNotes".Translate());

            Widgets.EndScrollView();
        }

        // ── DETAILED: AI TAB ──────────────────────────────────

        private void DrawAITab(Rect rect)
        {
            var   view = new Rect(0, 0, rect.width - 20f, 700f);
            Widgets.BeginScrollView(rect, ref scrollAI, view);
            float y = 8f;
            float w = view.width;

            GUI.color = Color.gray;
            Text.Font = GameFont.Tiny;
            string exp  = "DNP.World.AIExplainer".Translate();
            float  expH = Text.CalcHeight(exp, w);
            Widgets.Label(new Rect(0, y, w, expH), exp);
            GUI.color = Color.white;
            y += expH + 14f;

            Text.Font = GameFont.Tiny;
            Widgets.Label(new Rect(0, y, w, 18f), "DNP.World.AIPresets".Translate());
            y += 22f;

            float bw = (w - 16f) / 3f;
            Text.Font = GameFont.Small;

            if (Widgets.ButtonText(new Rect(0,          y, bw, 30f), "DNP.World.PresetShort".Translate()))
                world.aiInstructions = Append(world.aiInstructions, "DNP.World.PresetShortText".Translate());
            if (Widgets.ButtonText(new Rect(bw + 8f,    y, bw, 30f), "DNP.World.PresetCinematic".Translate()))
                world.aiInstructions = Append(world.aiInstructions, "DNP.World.PresetCinematicText".Translate());
            if (Widgets.ButtonText(new Rect(bw * 2+16f, y, bw, 30f), "DNP.World.PresetDark".Translate()))
                world.aiInstructions = Append(world.aiInstructions, "DNP.World.PresetDarkText".Translate());
            y += 38f;

            if (Widgets.ButtonText(new Rect(0,          y, bw, 30f), "DNP.World.PresetNoMeta".Translate()))
                world.aiInstructions = Append(world.aiInstructions, "DNP.World.PresetNoMetaText".Translate());
            if (Widgets.ButtonText(new Rect(bw + 8f,    y, bw, 30f), "DNP.World.PresetSpanish".Translate()))
                world.aiInstructions = Append(world.aiInstructions, "DNP.World.PresetSpanishText".Translate());
            if (Widgets.ButtonText(new Rect(bw * 2+16f, y, bw, 30f), "DNP.World.PresetClear".Translate()))
                world.aiInstructions = "";
            y += 42f;

            world.aiInstructions = TextArea(w, ref y,
                "DNP.World.FieldAIInstructions".Translate(),
                world.aiInstructions, rect.height - y - 20f,
                "DNP.World.HintAIInstructions".Translate());

            Widgets.EndScrollView();
        }

        // ── Tab bar (detailed only) ───────────────────────────

        private void DrawTabBar(Rect rect)
        {
            float tw = rect.width / 3f;
            DrawTab(new Rect(rect.x,          rect.y, tw - 3f, 34f), "DNP.World.TabWorld".Translate(),    0);
            DrawTab(new Rect(rect.x + tw,     rect.y, tw - 3f, 34f), "DNP.World.TabCampaign".Translate(), 1);
            DrawTab(new Rect(rect.x + tw * 2, rect.y, tw - 3f, 34f), "DNP.World.TabAI".Translate(),       2);
        }

        private void DrawTab(Rect rect, string label, int index)
        {
            Widgets.DrawBoxSolid(rect, activeTab == index
                ? new Color(0.28f, 0.22f, 0.12f)
                : new Color(0.14f, 0.14f, 0.14f));
            Widgets.DrawBox(rect);
            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(rect.x + 8f, rect.y + 7f, rect.width - 16f, 22f), label);
            if (Widgets.ButtonInvisible(rect)) activeTab = index;
        }

        // ── Layout helpers ────────────────────────────────────

        private string Field(float w, ref float y, string label, string value,
                              float height, string placeholder = "")
        {
            Text.Font = GameFont.Tiny;
            Widgets.Label(new Rect(0, y, w, 18f), ((string)label).ToUpper());
            y += 20f;
            Text.Font = GameFont.Small;

            string result = Widgets.TextField(new Rect(0, y, w, height), value);

            if (string.IsNullOrEmpty(value) && !string.IsNullOrEmpty(placeholder))
            {
                GUI.color = new Color(0.45f, 0.45f, 0.45f);
                Widgets.Label(new Rect(6f, y + 7f, w - 12f, height), placeholder);
                GUI.color = Color.white;
            }

            y += height + 16f;
            return result;
        }

        private string TextArea(float w, ref float y, string label, string value,
                                 float height, string hint = "")
        {
            Text.Font = GameFont.Tiny;
            Widgets.Label(new Rect(0, y, w, 18f), ((string)label).ToUpper());
            y += 20f;

            if (!string.IsNullOrEmpty(hint))
            {
                GUI.color = new Color(0.5f, 0.5f, 0.5f);
                float hh = Text.CalcHeight(hint, w);
                Widgets.Label(new Rect(0, y, w, hh), hint);
                GUI.color = Color.white;
                y += hh + 6f;
            }

            Text.Font = GameFont.Small;
            string result = Widgets.TextArea(new Rect(0, y, w, height), value);
            y += height + 18f;
            return result;
        }

        private string Append(string existing, string toAdd)
        {
            if (string.IsNullOrWhiteSpace(existing)) return toAdd;
            return existing.TrimEnd() + "\n" + toAdd;
        }

        private void SaveWorld()
        {
            var comp = DNP_GameComponent.Instance;
            if (comp != null)
            {
                comp.World = world;
                _worldFiles = DNP_JsonManager.GetWorldFiles(); // refresh sidebar
                Messages.Message("DNP.World.Saved".Translate(),
                    MessageTypeDefOf.TaskCompletion, false);
            }
        }

        public override void PostClose()
        {
            SaveWorld();
            base.PostClose();
        }
    }
}