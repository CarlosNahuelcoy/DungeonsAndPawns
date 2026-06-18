using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;
using Verse;
using RimWorld;

namespace DungeonsAndPawns
{
    // ─────────────────────────────────────────────────────────────
    // LLM PROVIDER ENUM
    // ─────────────────────────────────────────────────────────────
    public enum DNP_LLMProvider
    {
        Player2,
        OpenAI,
        OpenRouter,
        Gemini,
        Custom
    }

    // ─────────────────────────────────────────────────────────────
    // LLM SETTINGS — persisted via RimWorld ModSettings
    // File: <RimWorldData>/Config/Mod_CarlosNahuelcoy.DungeonsAndPawns.xml
    // ─────────────────────────────────────────────────────────────
    public class DNP_LLMSettings : ModSettings
    {
        public DNP_LLMProvider provider = DNP_LLMProvider.Player2;

        // Player2
        public string player2ApiKey = "";

        // OpenAI
        public string openAiApiKey = "";
        public string openAiModel  = "gpt-4o-mini";

        // OpenRouter
        public string openRouterApiKey = "";
        public string openRouterModel  = "mistralai/mistral-7b-instruct";

        // Gemini
        public string geminiApiKey = "";
        public string geminiModel  = "gemini-2.0-flash-001";

        // Custom / Local
        public string customEndpoint  = "http://localhost:1234/v1/chat/completions";
        public string customApiKey    = "";
        public string customModelName = "";

        // Shared
        public int  maxTokens = 800;
        public bool debugMode = false;

        // ── Director settings ─────────────────────────────────────
        public DNP_DirectorProfile    directorProfile      = DNP_DirectorProfile.Narrator;
        public DNP_ColonistInitiative colonistInitiative   = DNP_ColonistInitiative.Active;
        public DNP_InactivitySpeed    inactivitySpeed      = DNP_InactivitySpeed.Normal;
        public int                    enemyDifficultyBonus = 0; // -2, 0, +2, +4

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref provider,         "provider",         DNP_LLMProvider.Player2);
            Scribe_Values.Look(ref player2ApiKey,    "player2ApiKey",    "");
            Scribe_Values.Look(ref openAiApiKey,     "openAiApiKey",     "");
            Scribe_Values.Look(ref openAiModel,      "openAiModel",      "gpt-4o-mini");
            Scribe_Values.Look(ref openRouterApiKey, "openRouterApiKey", "");
            Scribe_Values.Look(ref openRouterModel,  "openRouterModel",  "mistralai/mistral-7b-instruct");
            Scribe_Values.Look(ref geminiApiKey,     "geminiApiKey",     "");
            Scribe_Values.Look(ref geminiModel,      "geminiModel",      "gemini-2.0-flash-001");
            Scribe_Values.Look(ref customEndpoint,   "customEndpoint",   "http://localhost:1234/v1/chat/completions");
            Scribe_Values.Look(ref customApiKey,     "customApiKey",     "");
            Scribe_Values.Look(ref customModelName,  "customModelName",  "");
            Scribe_Values.Look(ref maxTokens,        "maxTokens",        800);
            Scribe_Values.Look(ref debugMode,        "debugMode",        false);
            Scribe_Values.Look(ref directorProfile,      "directorProfile",      DNP_DirectorProfile.Narrator);
            Scribe_Values.Look(ref colonistInitiative,   "colonistInitiative",   DNP_ColonistInitiative.Active);
            Scribe_Values.Look(ref inactivitySpeed,      "inactivitySpeed",      DNP_InactivitySpeed.Normal);
            Scribe_Values.Look(ref enemyDifficultyBonus, "enemyDifficultyBonus", 0);
        }

        public bool IsConfigured()
        {
            switch (provider)
            {
                case DNP_LLMProvider.Player2:    return DNP_Player2Auth.IsAuthenticated;
                case DNP_LLMProvider.OpenAI:     return !string.IsNullOrEmpty(openAiApiKey);
                case DNP_LLMProvider.OpenRouter: return !string.IsNullOrEmpty(openRouterApiKey);
                case DNP_LLMProvider.Gemini:     return !string.IsNullOrEmpty(geminiApiKey);
                case DNP_LLMProvider.Custom:     return !string.IsNullOrEmpty(customEndpoint);
                default:                         return false;
            }
        }
    }

    // ─────────────────────────────────────────────────────────────
    // MOD CLASS
    // Registers settings and provides the window in the Mod list.
    // ─────────────────────────────────────────────────────────────
    public class DNP_Mod : Mod
    {
        public static DNP_LLMSettings Settings;

        private Vector2 _scroll;
        private bool    _showP2Key   = false;
        private bool    _showOAIKey  = false;
        private bool    _showORKey   = false;
        private bool    _showGemKey  = false;
        private bool    _showCustKey = false;
        private string  _testStatus  = "";
        private bool    _testRunning = false;

        // Gemini model fetcher state
        private bool                _gemFetching    = false;
        private string              _gemFetchStatus = "";
        private List<string>        _gemModels      = new List<string>();
        private string              _gemPending     = "";  // selected but not confirmed
        private Vector2             _gemModelScroll;
        private string              _gemModelSearch = "";

        public DNP_Mod(ModContentPack content) : base(content)
        {
            Settings = GetSettings<DNP_LLMSettings>();
        }

        public override string SettingsCategory() => "Dungeons & Pawns";

        public override void DoSettingsWindowContents(Rect inRect)
        {
            var viewRect = new Rect(0, 0, inRect.width - 20f, 1100f);
            Widgets.BeginScrollView(inRect, ref _scroll, viewRect);

            var list = new Listing_Standard();
            list.Begin(viewRect);

            // ── Provider ──────────────────────────────────────────
            SectionHeader(list, "DNP.Settings.SectionProvider".Translate(), new Color(0.6f, 1f, 0.6f));
            list.Label("DNP.Settings.ProviderLabel".Translate());
            list.Gap(4f);

            Rect btnRow = list.GetRect(30f);
            float bw    = (btnRow.width - 16f) / 5f;
            DrawProviderBtn(new Rect(btnRow.x + (bw+4f)*0, btnRow.y, bw, 30f), DNP_LLMProvider.Player2,    "Player2");
            DrawProviderBtn(new Rect(btnRow.x + (bw+4f)*1, btnRow.y, bw, 30f), DNP_LLMProvider.OpenAI,     "OpenAI");
            DrawProviderBtn(new Rect(btnRow.x + (bw+4f)*2, btnRow.y, bw, 30f), DNP_LLMProvider.OpenRouter, "OpenRouter");
            DrawProviderBtn(new Rect(btnRow.x + (bw+4f)*3, btnRow.y, bw, 30f), DNP_LLMProvider.Gemini,     "Gemini");
            DrawProviderBtn(new Rect(btnRow.x + (bw+4f)*4, btnRow.y, bw, 30f), DNP_LLMProvider.Custom,     "Custom");
            list.Gap(10f);

            switch (Settings.provider)
            {
                case DNP_LLMProvider.Player2:    DrawPlayer2Section(list);    break;
                case DNP_LLMProvider.OpenAI:     DrawOpenAISection(list);     break;
                case DNP_LLMProvider.OpenRouter: DrawOpenRouterSection(list); break;
                case DNP_LLMProvider.Gemini:     DrawGeminiSection(list);     break;
                case DNP_LLMProvider.Custom:     DrawCustomSection(list);     break;
            }

            list.GapLine();

            // ── Shared ────────────────────────────────────────────
            SectionHeader(list, "DNP.Settings.SectionShared".Translate(), new Color(0.7f, 0.9f, 1f));
            list.Label("DNP.Settings.MaxTokens".Translate(Settings.maxTokens));
            Settings.maxTokens = (int)list.Slider(Settings.maxTokens, 100, 1000);
            list.Gap(4f);
            list.CheckboxLabeled("DNP.Settings.DebugMode".Translate(), ref Settings.debugMode,
                "DNP.Settings.DebugModeTooltip".Translate());

            list.GapLine();

            // ── Director ──────────────────────────────────────────
            SectionHeader(list, "DNP.Settings.SectionDirector".Translate(), new Color(1f, 0.8f, 0.5f));

            list.Label("DNP.Settings.DirectorProfile".Translate());
            list.Gap(2f);
            Rect profileRow = list.GetRect(30f);
            float pbw = (profileRow.width - 8f) / 3f;
            DrawProfileBtn(new Rect(profileRow.x,           profileRow.y, pbw, 30f),
                DNP_DirectorProfile.Narrator,  "DNP.Director.Narrator".Translate());
            DrawProfileBtn(new Rect(profileRow.x + pbw + 4f, profileRow.y, pbw, 30f),
                DNP_DirectorProfile.Tactical,  "DNP.Director.Tactical".Translate());
            DrawProfileBtn(new Rect(profileRow.x + pbw*2+8f, profileRow.y, pbw, 30f),
                DNP_DirectorProfile.Chaotic,   "DNP.Director.Chaotic".Translate());
            list.Gap(4f);

            GUI.color = Color.gray;
            list.Label(GetProfileDescription());
            GUI.color = Color.white;
            list.Gap(8f);

            list.Label("DNP.Settings.ColonistInitiative".Translate());
            list.Gap(2f);
            Rect initRow = list.GetRect(30f);
            float ibw = (initRow.width - 8f) / 3f;
            DrawInitiativeBtn(new Rect(initRow.x,           initRow.y, ibw, 30f),
                DNP_ColonistInitiative.Reactive,   "DNP.Director.Reactive".Translate());
            DrawInitiativeBtn(new Rect(initRow.x + ibw + 4f, initRow.y, ibw, 30f),
                DNP_ColonistInitiative.Active,     "DNP.Director.Active".Translate());
            DrawInitiativeBtn(new Rect(initRow.x + ibw*2+8f, initRow.y, ibw, 30f),
                DNP_ColonistInitiative.VeryActive, "DNP.Director.VeryActive".Translate());
            list.Gap(8f);

            list.Label("DNP.Settings.InactivitySpeed".Translate());
            list.Gap(2f);
            Rect speedRow = list.GetRect(30f);
            float sbw2 = (speedRow.width - 8f) / 3f;
            DrawSpeedBtn(new Rect(speedRow.x,            speedRow.y, sbw2, 30f),
                DNP_InactivitySpeed.Patient, "DNP.Director.Patient".Translate());
            DrawSpeedBtn(new Rect(speedRow.x + sbw2 + 4f, speedRow.y, sbw2, 30f),
                DNP_InactivitySpeed.Normal,  "DNP.Director.Normal".Translate());
            DrawSpeedBtn(new Rect(speedRow.x + sbw2*2+8f, speedRow.y, sbw2, 30f),
                DNP_InactivitySpeed.Urgent,  "DNP.Director.Urgent".Translate());
            list.Gap(8f);

            list.Label("DNP.Settings.EnemyDifficulty".Translate());
            list.Gap(2f);
            Rect diffRow = list.GetRect(30f);
            float dbw = (diffRow.width - 12f) / 4f;
            DrawDiffBtn(new Rect(diffRow.x,            diffRow.y, dbw, 30f), -2, "DNP.Director.Easy".Translate());
            DrawDiffBtn(new Rect(diffRow.x + dbw + 4f, diffRow.y, dbw, 30f),  0, "DNP.Director.Normal".Translate());
            DrawDiffBtn(new Rect(diffRow.x + dbw*2+8f, diffRow.y, dbw, 30f), +2, "DNP.Director.Hard".Translate());
            DrawDiffBtn(new Rect(diffRow.x + dbw*3+12f,diffRow.y, dbw, 30f), +4, "DNP.Director.Brutal".Translate());
            list.Gap(4f);
            GUI.color = Color.gray;
            list.Label("DNP.Settings.EnemyDifficultyHint".Translate());
            GUI.color = Color.white;

            list.GapLine();

            // ── Content ───────────────────────────────────────────
            SectionHeader(list, "DNP.Settings.SectionContent".Translate(), new Color(0.5f, 0.75f, 1f));

            Rect contentRow = list.GetRect(30f);
            float cbw = (contentRow.width - 8f) / 3f;

            if (Widgets.ButtonText(new Rect(contentRow.x,            contentRow.y, cbw, 30f),
                "DNP.Settings.EditEnemies".Translate()))
                Find.WindowStack.Add(new DNP_EnemyEditorWindow());

            if (Widgets.ButtonText(new Rect(contentRow.x + cbw + 4f, contentRow.y, cbw, 30f),
                "DNP.Settings.EditItems".Translate()))
                Find.WindowStack.Add(new DNP_ItemEditorWindow());

            if (Widgets.ButtonText(new Rect(contentRow.x + cbw*2+8f, contentRow.y, cbw, 30f),
                "DNP.Settings.EditScenarios".Translate()))
                Find.WindowStack.Add(new DNP_ScenarioEditorWindow());

            list.Gap(4f);

            list.GapLine();

            // ── Connection test ───────────────────────────────────
            SectionHeader(list, "DNP.Settings.SectionTest".Translate(), new Color(1f, 0.9f, 0.6f));

            if (_testRunning)
            {
                GUI.color = Color.yellow;
                list.Label("DNP.Settings.TestRunning".Translate());
                GUI.color = Color.white;
            }
            else if (list.ButtonText("DNP.Settings.TestButton".Translate()))
            {
                RunTest();
            }

            if (!string.IsNullOrEmpty(_testStatus))
            {
                GUI.color = _testStatus.StartsWith("✓") ? Color.green : Color.red;
                list.Label(_testStatus);
                GUI.color = Color.white;
            }

            list.End();
            Widgets.EndScrollView();
        }

        // ── Player2 ───────────────────────────────────────────────

        private void DrawPlayer2Section(Listing_Standard list)
        {
            bool auth = DNP_Player2Auth.IsAuthenticated;

            if (auth)
            {
                GUI.color = Color.green;
                list.Label("  ✓ " + "DNP.Settings.P2Connected".Translate());
                GUI.color = Color.gray;
                if (!string.IsNullOrEmpty(DNP_Player2Auth.ConnectionMethod))
                    list.Label("  " + DNP_Player2Auth.ConnectionMethod);
                GUI.color = Color.white;
                list.Gap(4f);
                if (list.ButtonText("DNP.Settings.P2Disconnect".Translate()))
                    DNP_Player2Auth.Disconnect();
            }
            else
            {
                GUI.color = Color.yellow;
                list.Label("  " + "DNP.Settings.P2NotConnected".Translate());
                GUI.color = Color.white;
                list.Gap(4f);

                if (DNP_Player2Auth.IsAuthenticating)
                {
                    GUI.color = new Color(1f, 0.9f, 0.3f);
                    string code = DNP_Player2Auth.PendingUserCode;
                    list.Label(string.IsNullOrEmpty(code)
                        ? "  " + "DNP.Settings.P2Connecting".Translate()
                        : "  " + "DNP.Settings.P2WaitingCode".Translate(code));
                    GUI.color = Color.white;
                }
                else
                {
                    Rect row  = list.GetRect(32f);
                    float half = (row.width - 8f) / 2f;

                    if (Widgets.ButtonText(new Rect(row.x, row.y, half, 32f),
                        "DNP.Settings.P2ConnectApp".Translate()))
                        KickCoroutine(DNP_Player2Auth.AuthenticateViaApp(ok =>
                        {
                            if (!ok) Messages.Message("DNP.Settings.P2AppNotFound".Translate(),
                                MessageTypeDefOf.RejectInput, false);
                        }));

                    if (Widgets.ButtonText(new Rect(row.x + half + 8f, row.y, half, 32f),
                        "DNP.Settings.P2ConnectBrowser".Translate()))
                        KickCoroutine(DNP_Player2Auth.AuthenticateViaBrowser());

                    list.Gap(2f);
                    GUI.color = Color.gray;
                    list.Label("  " + "DNP.Settings.P2ConnectHint".Translate());
                    GUI.color = Color.white;
                }
            }

            list.Gap(6f);
            GUI.color = Color.gray;
            list.Label("DNP.Settings.P2ManualKey".Translate());
            GUI.color = Color.white;
            DrawKeyField(list, ref Settings.player2ApiKey, ref _showP2Key);

            // If manual key is entered, treat as authenticated
            if (!auth && !string.IsNullOrEmpty(Settings.player2ApiKey))
                DNP_Player2Auth.SetKeyFromSettings(Settings.player2ApiKey);
        }

        // ── OpenAI ────────────────────────────────────────────────

        private void DrawOpenAISection(Listing_Standard list)
        {
            list.Label("DNP.Settings.APIKey".Translate());
            DrawKeyField(list, ref Settings.openAiApiKey, ref _showOAIKey);
            list.Label("DNP.Settings.Model".Translate());
            Settings.openAiModel = list.TextEntry(Settings.openAiModel);
            list.Gap(2f);
            GUI.color = Color.gray;
            list.Label("Endpoint: https://api.openai.com/v1/chat/completions");
            list.Label("DNP.Settings.OAIModelHint".Translate());
            GUI.color = Color.white;
        }

        // ── OpenRouter ────────────────────────────────────────────

        private void DrawOpenRouterSection(Listing_Standard list)
        {
            list.Label("DNP.Settings.APIKey".Translate());
            DrawKeyField(list, ref Settings.openRouterApiKey, ref _showORKey);
            list.Label("DNP.Settings.Model".Translate());
            Settings.openRouterModel = list.TextEntry(Settings.openRouterModel);
            list.Gap(2f);
            GUI.color = Color.gray;
            list.Label("Endpoint: https://openrouter.ai/api/v1/chat/completions");
            list.Label("DNP.Settings.ORModelHint".Translate());
            GUI.color = Color.white;
        }

        // ── Gemini ────────────────────────────────────────────────

        private void DrawGeminiSection(Listing_Standard list)
        {
            list.Label("DNP.Settings.APIKey".Translate());
            DrawKeyField(list, ref Settings.geminiApiKey, ref _showGemKey);
            list.Gap(4f);

            // Active model display
            GUI.color = new Color(0.3f, 0.8f, 0.3f);
            list.Label("DNP.Settings.GeminiActiveModel".Translate(Settings.geminiModel));
            GUI.color = Color.white;
            list.Gap(4f);

            if (string.IsNullOrEmpty(Settings.geminiApiKey))
            {
                GUI.color = Color.yellow;
                list.Label("DNP.Settings.GeminiNeedKey".Translate());
                GUI.color = Color.white;
                return;
            }

            // Fetch button
            bool canFetch = !_gemFetching && !string.IsNullOrEmpty(Settings.geminiApiKey);
            if (!canFetch) GUI.color = new Color(0.5f, 0.5f, 0.5f);
            if (Widgets.ButtonText(list.GetRect(28f),
                _gemFetching
                    ? "DNP.Settings.GeminiFetching".Translate()
                    : "DNP.Settings.GeminiFetchModels".Translate())
                && canFetch)
            {
                _gemFetching    = true;
                _gemFetchStatus = "";
                _gemModels.Clear();
                _gemPending     = "";
                KickCoroutine(FetchGeminiModels(Settings.geminiApiKey));
            }
            GUI.color = Color.white;

            // Fetch status
            if (!string.IsNullOrEmpty(_gemFetchStatus))
            {
                GUI.color = _gemFetchStatus.StartsWith("✓") ? Color.green
                          : _gemFetchStatus.StartsWith("✗") ? Color.red
                          : Color.yellow;
                list.Label(_gemFetchStatus);
                GUI.color = Color.white;
            }

            // Model list
            if (_gemModels.Any())
            {
                list.Gap(4f);

                // Search
                Text.Font = GameFont.Tiny;
                GUI.color = Color.gray;
                list.Label("DNP.Settings.GeminiSearch".Translate());
                GUI.color = Color.white;
                Text.Font = GameFont.Small;
                _gemModelSearch = list.TextEntry(_gemModelSearch);
                list.Gap(2f);

                var filtered = string.IsNullOrEmpty(_gemModelSearch)
                    ? _gemModels
                    : _gemModels.Where(m => m.IndexOf(_gemModelSearch,
                        StringComparison.OrdinalIgnoreCase) >= 0).ToList();

                Text.Font = GameFont.Tiny;
                GUI.color = Color.gray;
                list.Label("DNP.Settings.GeminiModelCount".Translate(filtered.Count));
                GUI.color = Color.white;
                Text.Font = GameFont.Small;

                float rowH    = 28f;
                float listH   = Mathf.Min(rowH * 7f, rowH * filtered.Count);
                var   outer   = list.GetRect(listH + 4f);
                var   inner   = new Rect(0, 0, outer.width - 20f, rowH * filtered.Count);
                Widgets.BeginScrollView(outer, ref _gemModelScroll, inner);

                for (int i = 0; i < filtered.Count; i++)
                {
                    string m     = filtered[i];
                    bool   isAct = m == Settings.geminiModel;
                    bool   isPen = m == _gemPending;
                    var    row   = new Rect(0, i * rowH, inner.width, rowH - 2f);

                    Color bg = isAct ? new Color(0.1f, 0.3f, 0.1f)
                             : isPen ? new Color(0.28f, 0.2f, 0.04f)
                             : new Color(0.09f, 0.09f, 0.09f);
                    Widgets.DrawBoxSolid(row, bg);
                    Widgets.DrawBox(row, isAct || isPen ? 1 : 0);

                    GUI.color = isAct ? Color.green : isPen ? new Color(1f, 0.9f, 0.3f) : Color.white;
                    Text.Font = GameFont.Tiny;
                    Widgets.Label(new Rect(6f, i * rowH + 8f, inner.width - 60f, 18f), m);

                    if (isAct)
                    {
                        GUI.color = Color.green;
                        Widgets.Label(new Rect(inner.width - 56f, i * rowH + 8f, 54f, 18f),
                            "✓ " + "DNP.Settings.GeminiActive".Translate());
                    }
                    else if (isPen)
                    {
                        GUI.color = new Color(1f, 0.9f, 0.3f);
                        Widgets.Label(new Rect(inner.width - 56f, i * rowH + 8f, 54f, 18f),
                            "DNP.Settings.GeminiPending".Translate());
                    }
                    GUI.color = Color.white;
                    Text.Font = GameFont.Small;

                    if (!isAct && Widgets.ButtonInvisible(row))
                        _gemPending = isPen ? "" : m;
                }
                Widgets.EndScrollView();
                list.Gap(4f);

                // Confirm / cancel pending
                if (!string.IsNullOrEmpty(_gemPending) && _gemPending != Settings.geminiModel)
                {
                    float hw = (list.ColumnWidth - 4f) / 2f;
                    Rect  confirmRow = list.GetRect(30f);

                    GUI.color = new Color(0.3f, 0.65f, 0.3f);
                    if (Widgets.ButtonText(new Rect(confirmRow.x, confirmRow.y, hw, 30f),
                        "DNP.Settings.GeminiConfirm".Translate(_gemPending)))
                    {
                        Settings.geminiModel = _gemPending;
                        _gemPending          = "";
                        _gemFetchStatus      = "✓ " + Settings.geminiModel;
                        Messages.Message("DNP.Settings.GeminiModelSaved".Translate(Settings.geminiModel),
                            MessageTypeDefOf.TaskCompletion, false);
                    }
                    GUI.color = new Color(0.6f, 0.3f, 0.2f);
                    if (Widgets.ButtonText(new Rect(confirmRow.x + hw + 4f, confirmRow.y, hw, 30f),
                        "DNP.Settings.GeminiCancel".Translate()))
                        _gemPending = "";
                    GUI.color = Color.white;
                }
            }

            // Manual fallback
            list.Gap(4f);
            Text.Font = GameFont.Tiny;
            GUI.color = Color.gray;
            list.Label("DNP.Settings.GeminiManualHint".Translate());
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            Settings.geminiModel = list.TextEntry(Settings.geminiModel);
        }

        private IEnumerator FetchGeminiModels(string apiKey)
        {
            string url = "https://generativelanguage.googleapis.com/v1beta/models?key=" + apiKey;
            var req = UnityWebRequest.Get(url);
            req.timeout = 10;
            yield return req.SendWebRequest();

            bool err = req.result != UnityWebRequest.Result.Success;
            _gemFetching = false;

            if (err)
            {
                _gemFetchStatus = "✗ " + req.error;
                yield break;
            }

            try
            {
                var    node   = SimpleJSON.JSON.Parse(req.downloadHandler.text);
                var    arr    = node["models"].AsArray;
                var    models = new List<string>();

                foreach (SimpleJSON.JSONNode m in arr)
                {
                    // Only include models that support generateContent
                    bool canGenerate = false;
                    foreach (SimpleJSON.JSONNode method in m["supportedGenerationMethods"].AsArray)
                        if (method.Value == "generateContent") { canGenerate = true; break; }

                    if (!canGenerate) continue;

                    string name = m["name"].Value ?? "";
                    // Strip "models/" prefix
                    if (name.StartsWith("models/")) name = name.Substring(7);
                    if (!string.IsNullOrEmpty(name))
                        models.Add(name);
                }

                // Sort: flash first, then pro, then others
                models = models
                    .OrderBy(m => m.Contains("flash") ? 0 : m.Contains("pro") ? 1 : 2)
                    .ThenBy(m => m)
                    .ToList();

                _gemModels      = models;
                _gemFetchStatus = models.Any()
                    ? "✓ " + "DNP.Settings.GeminiFetchOK".Translate(models.Count)
                    : "✗ " + "DNP.Settings.GeminiFetchEmpty".Translate();
            }
            catch (Exception ex)
            {
                _gemFetchStatus = "✗ Parse error: " + ex.Message;
            }
        }

        // ── Custom ────────────────────────────────────────────────

        private void DrawCustomSection(Listing_Standard list)
        {
            list.Label("DNP.Settings.CustomEndpoint".Translate());
            Settings.customEndpoint = list.TextEntry(Settings.customEndpoint);
            list.Label("DNP.Settings.APIKey".Translate()
                + " (" + "DNP.Settings.CustomKeyOptional".Translate() + ")");
            DrawKeyField(list, ref Settings.customApiKey, ref _showCustKey);
            list.Label("DNP.Settings.Model".Translate()
                + " (" + "DNP.Settings.CustomModelOptional".Translate() + ")");
            Settings.customModelName = list.TextEntry(Settings.customModelName);
            list.Gap(4f);
            GUI.color = Color.gray;
            list.Label("DNP.Settings.CustomHint".Translate());
            list.Label("  LMStudio:  http://localhost:1234/v1/chat/completions");
            list.Label("  Ollama:    http://localhost:11434/v1/chat/completions");
            list.Label("  Groq:      https://api.groq.com/openai/v1/chat/completions");
            GUI.color = Color.white;
        }

        // ── Helpers ───────────────────────────────────────────────

        private void DrawProviderBtn(Rect rect, DNP_LLMProvider p, string label)
        {
            bool active = Settings.provider == p;
            GUI.color = active ? new Color(0.3f, 0.8f, 0.4f) : Color.white;
            if (Widgets.ButtonText(rect, label)) Settings.provider = p;
            GUI.color = Color.white;
        }

        /// <summary>
        /// A simple API key field — plain text or masked, with Show/Hide toggle.
        /// Uses standard TextField — no TextFieldNumeric which is for numbers only.
        /// </summary>
        private void DrawKeyField(Listing_Standard list, ref string key, ref bool show)
        {
            Rect row   = list.GetRect(28f);
            float btnW = 68f;
            var   fld  = new Rect(row.x, row.y, row.width - btnW - 4f, 28f);
            var   btn  = new Rect(row.xMax - btnW, row.y, btnW, 28f);

            if (show)
            {
                key = Widgets.TextField(fld, key);
            }
            else
            {
                // Draw masked display, but put an invisible TextField on top so
                // the user can still click and type (RimWorld has no password field)
                Widgets.DrawBoxSolid(fld, new Color(0.08f, 0.08f, 0.08f));
                Widgets.DrawBox(fld);
                if (!string.IsNullOrEmpty(key))
                {
                    GUI.color = new Color(0.6f, 0.6f, 0.6f);
                    Widgets.Label(new Rect(fld.x + 5f, fld.y + 6f, fld.width - 10f, fld.height),
                        new string('●', Math.Min(key.Length, 36)));
                    GUI.color = Color.white;
                }
                // Invisible editable field on top
                GUI.color = Color.clear;
                key = Widgets.TextField(fld, key);
                GUI.color = Color.white;
            }

            string btnLabel = show
                ? "DNP.Settings.KeyHide".Translate()
                : "DNP.Settings.KeyShow".Translate();
            if (Widgets.ButtonText(btn, btnLabel)) show = !show;
        }

        private void DrawProfileBtn(Rect rect, DNP_DirectorProfile p, string label)
        {
            bool active = Settings.directorProfile == p;
            GUI.color = active ? new Color(1f, 0.75f, 0.3f) : Color.white;
            if (Widgets.ButtonText(rect, label)) Settings.directorProfile = p;
            GUI.color = Color.white;
        }

        private void DrawInitiativeBtn(Rect rect, DNP_ColonistInitiative v, string label)
        {
            bool active = Settings.colonistInitiative == v;
            GUI.color = active ? new Color(0.4f, 0.8f, 1f) : Color.white;
            if (Widgets.ButtonText(rect, label)) Settings.colonistInitiative = v;
            GUI.color = Color.white;
        }

        private void DrawSpeedBtn(Rect rect, DNP_InactivitySpeed v, string label)
        {
            bool active = Settings.inactivitySpeed == v;
            GUI.color = active ? new Color(0.6f, 1f, 0.6f) : Color.white;
            if (Widgets.ButtonText(rect, label)) Settings.inactivitySpeed = v;
            GUI.color = Color.white;
        }

        private void DrawDiffBtn(Rect rect, int bonus, string label)
        {
            bool active = Settings.enemyDifficultyBonus == bonus;
            GUI.color = active ? new Color(1f, 0.5f, 0.4f) : Color.white;
            if (Widgets.ButtonText(rect, label)) Settings.enemyDifficultyBonus = bonus;
            GUI.color = Color.white;
        }

        private string GetProfileDescription()
        {
            switch (Settings.directorProfile)
            {
                case DNP_DirectorProfile.Narrator:
                    return "DNP.Director.NarratorDesc".Translate();
                case DNP_DirectorProfile.Tactical:
                    return "DNP.Director.TacticalDesc".Translate();
                case DNP_DirectorProfile.Chaotic:
                    return "DNP.Director.ChaoticDesc".Translate();
                default: return "";
            }
        }

        private void SectionHeader(Listing_Standard list, string title, Color color)
        {
            GUI.color = color;
            list.Label("═══ " + title + " ═══");
            GUI.color = Color.white;
            list.Gap(2f);
        }

        private void RunTest()
        {
            // Use Find.Root which IS a MonoBehaviour in RimWorld/Unity
            if (Find.Root == null)
            {
                _testStatus = "✗ " + "DNP.Settings.NeedActiveGame".Translate();
                return;
            }
            _testRunning = true;
            _testStatus  = "";
            Find.Root.StartCoroutine(DNP_LLMBridge.TestConnection(result =>
            {
                _testRunning = false;
                _testStatus  = result;
            }));
        }

        /// <summary>
        /// Kicks off a coroutine safely regardless of game state.
        /// Uses Find.Root (a MonoBehaviour) when in-game, otherwise queues via LongEventHandler.
        /// </summary>
        public static void KickCoroutine(IEnumerator routine)
        {
            if (Find.Root != null)
                Find.Root.StartCoroutine(routine);
            else
                LongEventHandler.QueueLongEvent(
                    () => Find.Root?.StartCoroutine(routine),
                    null, false, null);
        }
    }
}