using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace DungeonsAndPawns
{
    // ─────────────────────────────────────────────────────────────
    // CLASS EDITOR WINDOW
    // Floating window for creating or editing a DNP_ClassData.
    // Opened from the class selector in DNP_SetupDialog.
    // On save: writes JSON to disk, reloads registry, calls onSaved.
    // ─────────────────────────────────────────────────────────────
    public class DNP_ClassEditorWindow : Window
    {
        private DNP_ClassData editing;
        private bool isNew;
        private Action<DNP_ClassData> onSaved;

        // Working copies of fields
        private string wId;
        private string wName;
        private string wDesc;
        private string wFlavor;
        private string wNarrativeStyle;
        private string wHp;
        private string wHpPerLevel;
        private string wStr;
        private string wDex;
        private string wMind;
        private string wPrimaryStat;   // "Strength", "Dexterity", "Mind"
        private string wLinkedSkill;

        // Abilities working copy
        private List<DNP_AbilityData> wAbilities = new List<DNP_AbilityData>();

        private Vector2 scroll;
        private string  errorMsg = "";

        private static readonly string[] STATS        = { "Strength", "Dexterity", "Mind" };
        private static readonly string[] TARGET_TYPES = { "Enemy", "Ally", "Self" };
        private static readonly string[] RW_SKILLS    = { "Melee", "Shooting", "Intellectual", "Medicine", "Social", "Crafting", "Construction", "Mining", "Cooking", "Plants", "Animals", "Artistic" };

        public override Vector2 InitialSize => new Vector2(560f, 660f);

        public DNP_ClassEditorWindow(DNP_ClassData cls, bool isNew, Action<DNP_ClassData> onSaved)
        {
            this.isNew   = isNew;
            this.onSaved = onSaved;
            editing      = cls;

            // Copy into working strings
            wId             = cls.id                ?? "";
            wName           = cls.className         ?? "";
            wDesc           = cls.description       ?? "";
            wFlavor         = cls.flavorText        ?? "";
            wNarrativeStyle = cls.aiNarrativeStyle  ?? "";
            wHp             = cls.baseHp.ToString();
            wHpPerLevel  = cls.hpPerLevel.ToString();
            wStr         = cls.baseStrength.ToString();
            wDex         = cls.baseDexterity.ToString();
            wMind        = cls.baseMind.ToString();
            wPrimaryStat = cls.primaryStat ?? "Strength";
            wLinkedSkill = cls.linkedRimWorldSkill ?? "";

            // Deep copy abilities
            foreach (var a in cls.abilities)
                wAbilities.Add(new DNP_AbilityData
                {
                    abilityName   = a.abilityName,
                    description   = a.description,
                    unlockAtLevel = a.unlockAtLevel,
                    statUsed      = a.statUsed,
                    baseDamage    = a.baseDamage,
                    targetType    = a.targetType
                });

            doCloseX      = true;
            doCloseButton = false;
            resizeable    = true;
            draggable     = true;
        }

        public override void DoWindowContents(Rect inRect)
        {
            // ── Title ──
            Text.Font = GameFont.Medium;
            string title = isNew
                ? "DNP.ClassEditor.TitleNew".Translate()
                : "DNP.ClassEditor.TitleEdit".Translate(wName);
            Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width - 110f, 32f), title);
            Text.Font = GameFont.Small;

            // Save button
            if (Widgets.ButtonText(new Rect(inRect.xMax - 104f, inRect.y + 2f, 100f, 28f),
                "DNP.ClassEditor.Save".Translate()))
                TrySave();

            inRect.yMin += 38f;

            // Error message
            if (!string.IsNullOrEmpty(errorMsg))
            {
                GUI.color = new Color(1f, 0.4f, 0.4f);
                Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, 20f), errorMsg);
                GUI.color = Color.white;
                inRect.yMin += 24f;
            }

            // Scrollable content
            float contentH = 520f + wAbilities.Count * 110f;
            var viewRect = new Rect(0, 0, inRect.width - 20f, contentH);
            Widgets.BeginScrollView(inRect, ref scroll, viewRect);
            float y = 4f;
            float w = viewRect.width;

            // ── Identity ──
            SectionHeader(w, ref y, "DNP.ClassEditor.SectionIdentity".Translate());

            if (isNew)
            {
                wId   = LabeledField(w, ref y, "DNP.ClassEditor.FieldId".Translate(),   wId,   28f, "warrior, mage, my_class...");
            }
            else
            {
                // Show ID as read-only when editing
                Text.Font = GameFont.Tiny;
                GUI.color = Color.gray;
                Widgets.Label(new Rect(0, y, w, 18f), "ID: " + wId + "  (read-only)");
                GUI.color = Color.white;
                Text.Font = GameFont.Small;
                y += 22f;
            }

            wName   = LabeledField(w, ref y, "DNP.ClassEditor.FieldName".Translate(),   wName,  28f, "Warrior, Mage...");
            wDesc   = LabeledTextArea(w, ref y, "DNP.ClassEditor.FieldDesc".Translate(),   wDesc,  60f);
            wFlavor = LabeledTextArea(w, ref y, "DNP.ClassEditor.FieldFlavor".Translate(), wFlavor, 48f,
                "DNP.ClassEditor.HintFlavor".Translate());

            wNarrativeStyle = LabeledTextArea(w, ref y,
                "DNP.ClassEditor.FieldNarrativeStyle".Translate(), wNarrativeStyle, 72f,
                "DNP.ClassEditor.HintNarrativeStyle".Translate());

            // ── Stats ──
            SectionHeader(w, ref y, "DNP.ClassEditor.SectionStats".Translate());

            // HP row
            float half = (w - 12f) / 2f;
            wHp        = LabeledFieldInline(new Rect(0, y, half, 28f),        "DNP.ClassEditor.FieldHp".Translate(),       wHp);
            wHpPerLevel = LabeledFieldInline(new Rect(half + 12f, y, half, 28f), "DNP.ClassEditor.FieldHpPerLevel".Translate(), wHpPerLevel);
            y += 38f;

            // STR / DEX / MND row
            float third = (w - 16f) / 3f;
            wStr  = LabeledFieldInline(new Rect(0,              y, third, 28f), "STR", wStr);
            wDex  = LabeledFieldInline(new Rect(third + 8f,     y, third, 28f), "DEX", wDex);
            wMind = LabeledFieldInline(new Rect(third * 2 + 16f, y, third, 28f), "MND", wMind);
            y += 38f;

            // Primary stat
            Text.Font = GameFont.Tiny;
            Widgets.Label(new Rect(0, y, w, 18f), ((string)"DNP.ClassEditor.PrimaryStat".Translate()).ToUpper());
            y += 20f;
            Text.Font = GameFont.Small;
            float statBtnW = (w - 8f) / 3f;
            for (int i = 0; i < STATS.Length; i++)
            {
                bool sel = wPrimaryStat == STATS[i];
                if (sel) GUI.color = new Color(0.3f, 0.7f, 1f);
                if (Widgets.ButtonText(new Rect(i * (statBtnW + 4f), y, statBtnW, 28f), STATS[i]))
                    wPrimaryStat = STATS[i];
                GUI.color = Color.white;
            }
            y += 36f;

            // Linked RimWorld skill
            Text.Font = GameFont.Tiny;
            Widgets.Label(new Rect(0, y, w, 18f), ((string)"DNP.ClassEditor.LinkedSkill".Translate()).ToUpper());
            y += 20f;
            Text.Font = GameFont.Small;
            float skillBtnW = (w - (RW_SKILLS.Length - 1) * 4f) / RW_SKILLS.Length;
            // Two rows of skill buttons
            int perRow = 6;
            for (int i = 0; i < RW_SKILLS.Length; i++)
            {
                int   row    = i / perRow;
                int   col    = i % perRow;
                float bw     = (w - (perRow - 1) * 4f) / perRow;
                bool  sel    = wLinkedSkill == RW_SKILLS[i];
                if (sel) GUI.color = new Color(0.3f, 0.7f, 1f);
                if (Widgets.ButtonText(new Rect(col * (bw + 4f), y + row * 32f, bw, 28f), RW_SKILLS[i]))
                    wLinkedSkill = sel ? "" : RW_SKILLS[i]; // toggle off if same
                GUI.color = Color.white;
            }
            y += (Mathf.CeilToInt((float)RW_SKILLS.Length / perRow)) * 32f + 4f;

            // ── Abilities ──
            SectionHeader(w, ref y, "DNP.ClassEditor.SectionAbilities".Translate());

            Text.Font = GameFont.Tiny;
            GUI.color = Color.gray;
            Widgets.Label(new Rect(0, y, w, 16f), "DNP.ClassEditor.HintAbilities".Translate());
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            y += 20f;

            for (int i = 0; i < wAbilities.Count; i++)
            {
                y = DrawAbilityRow(w, y, i);
            }

            if (Widgets.ButtonText(new Rect(0, y, w, 28f), "+ " + "DNP.ClassEditor.AddAbility".Translate()))
            {
                wAbilities.Add(new DNP_AbilityData
                {
                    abilityName   = "New Ability",
                    description   = "",
                    unlockAtLevel = 1,
                    statUsed      = wPrimaryStat,
                    baseDamage    = 0,
                    targetType    = "Enemy"
                });
            }
            y += 34f;

            Widgets.EndScrollView();
        }

        // ── Ability row ───────────────────────────────────────

        private float DrawAbilityRow(float w, float y, int index)
        {
            var a      = wAbilities[index];
            var bgRect = new Rect(0, y, w, 104f);
            Widgets.DrawBoxSolid(bgRect, new Color(0.12f, 0.12f, 0.12f));
            Widgets.DrawBox(bgRect);
            y += 4f;

            // Delete button
            GUI.color = new Color(0.8f, 0.3f, 0.3f);
            if (Widgets.ButtonText(new Rect(w - 26f, y, 24f, 20f), "✕"))
            {
                wAbilities.RemoveAt(index);
                GUI.color = Color.white;
                return y - 4f + 108f; // skip rest
            }
            GUI.color = Color.white;

            // Name + Level
            float nameW = w - 110f;
            a.abilityName   = LabeledFieldInline(new Rect(2f,       y, nameW, 24f), "Name",    a.abilityName);
            a.unlockAtLevel = IntFieldInline    (new Rect(nameW + 6f, y, 100f, 24f), "Lvl req", a.unlockAtLevel);
            y += 30f;

            // Description
            a.description = LabeledFieldInline(new Rect(2f, y, w - 4f, 22f), "Desc", a.description);
            y += 28f;

            // Stat / Damage / Target
            float col = (w - 12f) / 3f;
            // Stat used
            Text.Font = GameFont.Tiny;
            Widgets.Label(new Rect(2f, y, col, 14f), "STAT");
            y += 14f;
            for (int s = 0; s < STATS.Length; s++)
            {
                bool sel = a.statUsed == STATS[s];
                if (sel) GUI.color = new Color(0.3f, 0.7f, 1f);
                if (Widgets.ButtonText(new Rect(2f + s * (col / 3f + 2f), y, col / 3f, 22f), STATS[s].Substring(0, 3)))
                    a.statUsed = STATS[s];
                GUI.color = Color.white;
            }

            // Damage
            a.baseDamage = IntFieldInline(new Rect(col + 8f, y - 14f, col, 36f), "Damage", a.baseDamage);

            // Target type
            Text.Font = GameFont.Tiny;
            Widgets.Label(new Rect(col * 2 + 14f, y - 14f, col, 14f), "TARGET");
            y += 0f;
            for (int t = 0; t < TARGET_TYPES.Length; t++)
            {
                bool sel = a.targetType == TARGET_TYPES[t];
                if (sel) GUI.color = new Color(0.3f, 0.7f, 1f);
                float tw = col / TARGET_TYPES.Length - 2f;
                if (Widgets.ButtonText(new Rect(col * 2 + 14f + t * (tw + 2f), y, tw, 22f), TARGET_TYPES[t].Substring(0, 3)))
                    a.targetType = TARGET_TYPES[t];
                GUI.color = Color.white;
            }

            Text.Font = GameFont.Small;
            y += 26f + 8f;
            return y;
        }

        // ── Save ──────────────────────────────────────────────

        private void TrySave()
        {
            errorMsg = "";

            // Validate
            string cleanId = wId.Trim().ToLower().Replace(" ", "_");
            if (string.IsNullOrEmpty(cleanId))
            { errorMsg = "DNP.ClassEditor.ErrorNoId".Translate(); return; }

            string cleanName = wName.Trim();
            if (string.IsNullOrEmpty(cleanName))
            { errorMsg = "DNP.ClassEditor.ErrorNoName".Translate(); return; }

            // Check duplicate ID on new class
            if (isNew && DNP_ClassRegistry.Get(cleanId) != null)
            { errorMsg = "DNP.ClassEditor.ErrorDuplicateId".Translate(cleanId); return; }

            int hp, hpLvl, str, dex, mnd;
            if (!int.TryParse(wHp, out hp) || hp <= 0)        { errorMsg = "Invalid HP value."; return; }
            if (!int.TryParse(wHpPerLevel, out hpLvl))         { errorMsg = "Invalid HP/level value."; return; }
            if (!int.TryParse(wStr,  out str))                  { errorMsg = "Invalid STR value."; return; }
            if (!int.TryParse(wDex,  out dex))                  { errorMsg = "Invalid DEX value."; return; }
            if (!int.TryParse(wMind, out mnd))                  { errorMsg = "Invalid MND value."; return; }

            // Build the class
            var cls = new DNP_ClassData
            {
                id                  = cleanId,
                className           = cleanName,
                description         = wDesc.Trim(),
                flavorText          = wFlavor.Trim(),
                aiNarrativeStyle    = wNarrativeStyle.Trim(),
                baseHp              = hp,
                hpPerLevel          = hpLvl,
                baseStrength        = str,
                baseDexterity       = dex,
                baseMind            = mnd,
                primaryStat         = wPrimaryStat,
                linkedRimWorldSkill = wLinkedSkill,
                abilities           = new List<DNP_AbilityData>(wAbilities)
            };

            // Save to disk and reload registry
            DNP_ClassLoader.Save(cls);
            DNP_ClassLoader.Load();

            onSaved?.Invoke(cls);
            Close();
        }

        // ── Layout helpers ────────────────────────────────────

        private void SectionHeader(float w, ref float y, string label)
        {
            GUI.color = new Color(0.5f, 0.4f, 0.2f);
            Widgets.DrawLineHorizontal(0, y, w);
            GUI.color = Color.white;
            y += 3f;
            Text.Font = GameFont.Tiny;
            Widgets.Label(new Rect(0, y, w, 18f), label.ToUpper());
            y += 22f;
            Text.Font = GameFont.Small;
        }

        private string LabeledField(float w, ref float y, string label, string value,
                                     float height, string hint = "")
        {
            Text.Font = GameFont.Tiny;
            Widgets.Label(new Rect(0, y, w, 18f), label.ToUpper());
            y += 20f;
            Text.Font = GameFont.Small;
            string result = Widgets.TextField(new Rect(0, y, w, height), value);
            if (string.IsNullOrEmpty(value) && !string.IsNullOrEmpty(hint))
            {
                GUI.color = new Color(0.45f, 0.45f, 0.45f);
                Widgets.Label(new Rect(4f, y + 5f, w, height), hint);
                GUI.color = Color.white;
            }
            y += height + 10f;
            return result;
        }

        private string LabeledTextArea(float w, ref float y, string label, string value,
                                        float height, string hint = "")
        {
            Text.Font = GameFont.Tiny;
            Widgets.Label(new Rect(0, y, w, 18f), label.ToUpper());
            y += 20f;
            if (!string.IsNullOrEmpty(hint))
            {
                GUI.color = Color.gray;
                float hh = Text.CalcHeight(hint, w);
                Widgets.Label(new Rect(0, y, w, hh), hint);
                GUI.color = Color.white;
                y += hh + 4f;
            }
            Text.Font = GameFont.Small;
            string result = Widgets.TextArea(new Rect(0, y, w, height), value);
            y += height + 10f;
            return result;
        }

        private string LabeledFieldInline(Rect rect, string label, string value)
        {
            Text.Font = GameFont.Tiny;
            Widgets.Label(new Rect(rect.x, rect.y, rect.width, 14f), label.ToUpper());
            Text.Font = GameFont.Small;
            return Widgets.TextField(new Rect(rect.x, rect.y + 14f, rect.width, rect.height - 14f), value);
        }

        private int IntFieldInline(Rect rect, string label, int value)
        {
            string str = LabeledFieldInline(rect, label, value.ToString());
            int result;
            return int.TryParse(str, out result) ? result : value;
        }
    }
}