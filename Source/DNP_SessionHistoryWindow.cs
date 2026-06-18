using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using Verse;

namespace DungeonsAndPawns
{
    // ─────────────────────────────────────────────────────────────
    // SESSION HISTORY WINDOW
    // Lists all saved session JSON files. Click to read the full
    // log. Delete button removes the file.
    // ─────────────────────────────────────────────────────────────
    public class DNP_SessionHistoryWindow : Window
    {
        public override Vector2 InitialSize => new Vector2(880f, 620f);

        private List<string>     _files        = new List<string>();
        private int              _selectedIdx  = -1;
        private DNP_Session      _loaded       = null;
        private Vector2          _listScroll;
        private Vector2          _logScroll;

        public DNP_SessionHistoryWindow()
        {
            doCloseX      = true;
            doCloseButton = false;
            resizeable    = true;
            draggable     = true;
            RefreshFiles();
        }

        private void RefreshFiles()
        {
            _files = DNP_JsonManager.GetSessionFiles()
                .OrderByDescending(f => File.GetLastWriteTime(f))
                .ToList();
        }

        public override void DoWindowContents(Rect inRect)
        {
            // Title
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, 32f),
                "DNP.History.Title".Translate());
            inRect.yMin += 38f;
            Text.Font = GameFont.Small;

            if (!_files.Any())
            {
                GUI.color = Color.gray;
                Widgets.Label(inRect, "DNP.History.NoSessions".Translate());
                GUI.color = Color.white;
                return;
            }

            float listW = 240f;
            var listRect = new Rect(inRect.x, inRect.y, listW, inRect.height);
            var logRect  = new Rect(inRect.x + listW + 10f, inRect.y,
                inRect.width - listW - 10f, inRect.height);

            DrawList(listRect);
            if (_loaded != null) DrawLog(logRect);
        }

        private void DrawList(Rect rect)
        {
            Widgets.DrawMenuSection(rect);
            var   inner   = rect.ContractedBy(6f);
            float rowH    = 52f;
            float contentH = _files.Count * (rowH + 4f);
            var   vRect   = new Rect(0, 0, inner.width - 16f, Mathf.Max(contentH, inner.height));
            Widgets.BeginScrollView(inner, ref _listScroll, vRect);

            for (int i = 0; i < _files.Count; i++)
            {
                string file  = _files[i];
                bool   sel   = i == _selectedIdx;
                var    row   = new Rect(0, i * (rowH + 4f), vRect.width, rowH);

                Widgets.DrawBoxSolid(row, sel
                    ? new Color(0.18f, 0.14f, 0.06f)
                    : new Color(0.09f, 0.09f, 0.09f));
                Widgets.DrawBox(row, sel ? 2 : 1);

                // Session name from filename
                string fname = Path.GetFileNameWithoutExtension(file);
                string date  = File.GetLastWriteTime(file).ToString("yyyy-MM-dd  HH:mm");

                float delW = 26f;
                Text.Font = GameFont.Small;
                GUI.color = sel ? new Color(1f, 0.85f, 0.4f) : Color.white;
                Widgets.Label(new Rect(6f, i * (rowH + 4f) + 6f,
                    vRect.width - delW - 10f, 22f), fname);
                GUI.color = Color.gray;
                Text.Font = GameFont.Tiny;
                Widgets.Label(new Rect(6f, i * (rowH + 4f) + 28f,
                    vRect.width - delW - 10f, 18f), date);
                GUI.color = Color.white;
                Text.Font = GameFont.Small;

                // Delete button
                GUI.color = new Color(0.8f, 0.3f, 0.2f);
                if (Widgets.ButtonText(new Rect(vRect.width - delW, i * (rowH + 4f) + 14f,
                    delW, 24f), "✕"))
                {
                    Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation(
                        "DNP.History.DeleteConfirm".Translate(fname),
                        () => {
                            File.Delete(file);
                            if (_selectedIdx == i) { _loaded = null; _selectedIdx = -1; }
                            RefreshFiles();
                        }));
                }
                GUI.color = Color.white;

                // Row click loads the session
                if (Widgets.ButtonInvisible(new Rect(0, i * (rowH + 4f),
                    vRect.width - delW - 4f, rowH)))
                {
                    _selectedIdx = i;
                    _loaded = DNP_JsonManager.ImportSession(file);
                }
            }
            Widgets.EndScrollView();
        }

        private void DrawLog(Rect rect)
        {
            Widgets.DrawMenuSection(rect);
            var inner = rect.ContractedBy(8f);

            if (_loaded?.sessionLog == null || !_loaded.sessionLog.Any())
            {
                GUI.color = Color.gray;
                Widgets.Label(inner, "DNP.History.EmptyLog".Translate());
                GUI.color = Color.white;
                return;
            }

            // Session header
            float y = inner.y;
            Text.Font = GameFont.Small;
            GUI.color = new Color(1f, 0.8f, 0.3f);
            string header = "DNP.History.SessionHeader".Translate(
                _loaded.sessionLog.Count);
            Widgets.Label(new Rect(inner.x, y, inner.width, 22f), header);
            GUI.color = Color.white;
            y += 26f;

            // Log scroll
            float entryH = 56f; // approximate — good enough for history view
            float totalH = 0f;
            foreach (var e in _loaded.sessionLog)
            {
                Text.Font = GameFont.Small;
                float h = Text.CalcHeight(e.text, inner.width - 24f) + 34f;
                totalH += Mathf.Max(h, 48f) + 4f;
            }

            var logArea = new Rect(inner.x, y, inner.width, inner.yMax - y);
            var vRect   = new Rect(0, 0, logArea.width - 16f, Mathf.Max(totalH, logArea.height));
            Widgets.BeginScrollView(logArea, ref _logScroll, vRect);

            float ey = 0f;
            foreach (var entry in _loaded.sessionLog)
            {
                Text.Font = GameFont.Small;
                float eh = Mathf.Max(Text.CalcHeight(entry.text, vRect.width - 24f) + 34f, 48f);
                var   er = new Rect(0, ey, vRect.width, eh);

                // Background by type
                Color bg;
                Color sc;
                switch (entry.entryType)
                {
                    case DNP_LogEntry.EntryType.DM:
                        bg = new Color(0.15f, 0.12f, 0.08f);
                        sc = new Color(1f, 0.8f, 0.3f); break;
                    case DNP_LogEntry.EntryType.Player:
                        bg = new Color(0.08f, 0.12f, 0.15f);
                        sc = new Color(0.6f, 0.85f, 1f); break;
                    case DNP_LogEntry.EntryType.DiceRoll:
                        bg = new Color(0.10f, 0.16f, 0.10f);
                        sc = new Color(0.4f, 1f, 0.5f); break;
                    default:
                        bg = new Color(0.10f, 0.10f, 0.10f);
                        sc = Color.gray; break;
                }

                Widgets.DrawBoxSolid(er.ContractedBy(2f), bg);

                Text.Font = GameFont.Tiny;
                GUI.color = sc;
                Widgets.Label(new Rect(8f, ey + 6f, vRect.width - 16f, 16f),
                    entry.speakerName);
                GUI.color = sc * new Color(1, 1, 1, 0.3f);
                Widgets.DrawLineHorizontal(8f, ey + 24f, vRect.width - 16f);
                GUI.color = Color.white;

                Text.Font = GameFont.Small;
                Widgets.Label(new Rect(8f, ey + 28f, vRect.width - 16f, eh - 34f),
                    entry.text);

                ey += eh + 4f;
            }
            Widgets.EndScrollView();
        }
    }
}