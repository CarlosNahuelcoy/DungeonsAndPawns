using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Verse;
using RimWorld;

namespace DungeonsAndPawns
{
    // ─────────────────────────────────────────────────────────────
    // MOD ENTRY POINT
    // ─────────────────────────────────────────────────────────────
    [StaticConstructorOnStartup]
    public static class DNP_Startup
    {
        static DNP_Startup()
        {
            var harmony = new Harmony("CarlosNahuelcoy.DungeonsAndPawns");
            harmony.PatchAll(Assembly.GetExecutingAssembly());
            Log.Message("[DungeonsAndPawns] Dungeons & Pawns loaded. Roll for initiative.");

            DNP_ClassLoader.Load();
            DNP_ContentLoader.Load();

            // Restore saved Player2 key so the user doesn't have to reconnect every session
            DNP_Player2Auth.TryRestoreFromSettings();
        }
    }

    // ─────────────────────────────────────────────────────────────
    // MAIN TAB WINDOW
    // Accessed via the D&P button in RimWorld's bottom bar.
    // LLM settings are in the Mod list (Options → Mods → D&P).
    // ─────────────────────────────────────────────────────────────
    public class DNP_MainTabWindow : MainTabWindow
    {
        public override Vector2 RequestedTabSize => new Vector2(260f, 256f);

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, 36f),
                "DNP.MainTab.Title".Translate());
            inRect.yMin += 40f;
            Text.Font = GameFont.Small;

            var active = DNP_SessionManager.ActiveSession;

            if (active != null && active.isActive)
            {
                Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, 24f),
                    "DNP.MainTab.SessionActive".Translate());
                inRect.yMin += 28f;

                if (Widgets.ButtonText(new Rect(inRect.x, inRect.y, inRect.width, 32f),
                    "DNP.MainTab.OpenSession".Translate()))
                    Find.WindowStack.Add(new DNP_SessionWindow(active));
                inRect.yMin += 36f;

                if (Widgets.ButtonText(new Rect(inRect.x, inRect.y, inRect.width, 32f),
                    "DNP.MainTab.EndSession".Translate()))
                    DNP_SessionManager.EndSession(success: true);
                inRect.yMin += 36f;
            }
            else
            {
                Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, 24f),
                    "DNP.MainTab.NoSession".Translate());
                inRect.yMin += 28f;

                if (Widgets.ButtonText(new Rect(inRect.x, inRect.y, inRect.width, 36f),
                    "DNP.MainTab.NewSession".Translate()))
                    Find.WindowStack.Add(new DNP_SetupDialog());
                inRect.yMin += 40f;
            }

            if (Widgets.ButtonText(new Rect(inRect.x, inRect.y, inRect.width, 32f),
                "DNP.MainTab.WorldCampaign".Translate()))
                Find.WindowStack.Add(new DNP_WorldWindow());
            inRect.yMin += 36f;

            if (Widgets.ButtonText(new Rect(inRect.x, inRect.y, inRect.width, 32f),
                "DNP.MainTab.SessionHistory".Translate()))
                Find.WindowStack.Add(new DNP_SessionHistoryWindow());
        }
    }
}