using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using RimWorld;

namespace DungeonsAndPawns
{
    // ─────────────────────────────────────────────────────────────
    // ENEMY EDITOR
    // ─────────────────────────────────────────────────────────────
    public class DNP_EnemyEditorWindow : Window
    {
        public override Vector2 InitialSize => new Vector2(660f, 560f);

        private List<DNP_EnemyData> _enemies;
        private int    _sel     = 0;
        private Vector2 _listScroll;

        // Edit buffers
        private string _bName, _bHP, _bArmor, _bDmg, _bBonus, _bBehavior, _bXP;
        private List<(string itemId, string chance)> _bLoot = new List<(string, string)>();

        private static readonly string[] Behaviors = { "aggressive", "cautious", "ranged", "support" };

        public DNP_EnemyEditorWindow()
        {
            doCloseX      = true;
            doCloseButton = false;
            resizeable    = false;
            draggable     = true;

            _enemies = DNP_ContentRegistry.AllEnemies.ToList();
            SelectEnemy(0);
        }

        private DNP_EnemyData Current => _sel < _enemies.Count ? _enemies[_sel] : null;

        private void SelectEnemy(int idx)
        {
            _sel = idx;
            var e = Current;
            if (e == null) return;
            _bName     = e.enemyName;
            _bHP       = e.hp.ToString();
            _bArmor    = e.armor.ToString();
            _bDmg      = e.attackDamage.ToString();
            _bBonus    = e.attackBonus.ToString();
            _bBehavior = e.behaviorTag;
            _bXP       = e.xpReward.ToString();
            _bLoot     = e.lootTable?.Select(l => (l.itemId, l.dropChance.ToString("F2")))
                          .ToList() ?? new List<(string, string)>();
        }

        public override void DoWindowContents(Rect inRect)
        {
            // Title
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, 30f),
                "DNP.Editor.Enemies".Translate());
            inRect.yMin += 36f;
            Text.Font = GameFont.Small;

            float listW = 180f;
            float gap   = 10f;
            var listRect = new Rect(inRect.x, inRect.y, listW, inRect.height);
            var editRect = new Rect(inRect.x + listW + gap, inRect.y,
                inRect.width - listW - gap, inRect.height);

            DrawList(listRect);
            if (Current != null) DrawEditor(editRect);
        }

        private void DrawList(Rect rect)
        {
            Widgets.DrawMenuSection(rect);
            var inner = rect.ContractedBy(6f);
            float y   = inner.y;

            // New button
            if (Widgets.ButtonText(new Rect(inner.x, y, inner.width, 26f),
                "+ " + "DNP.Editor.NewEnemy".Translate()))
            {
                var neo = new DNP_EnemyData
                {
                    id = "enemy_" + System.Guid.NewGuid().ToString("N").Substring(0, 6),
                    enemyName = "New Enemy", hp = 8, armor = 0,
                    attackDamage = 3, attackBonus = 1,
                    behaviorTag = "aggressive", xpReward = 25,
                    lootTable = new List<DNP_LootEntry>()
                };
                _enemies.Add(neo);
                SelectEnemy(_enemies.Count - 1);
            }
            y += 30f;

            float listH = inner.yMax - y;
            var   vRect = new Rect(0, 0, inner.width - 16f, _enemies.Count * 36f);
            Widgets.BeginScrollView(new Rect(inner.x, y, inner.width, listH),
                ref _listScroll, vRect);

            for (int i = 0; i < _enemies.Count; i++)
            {
                var e   = _enemies[i];
                var row = new Rect(0, i * 36f, vRect.width, 32f);
                bool sel = i == _sel;

                Widgets.DrawBoxSolid(row, sel
                    ? new Color(0.28f, 0.18f, 0.08f)
                    : new Color(0.09f, 0.09f, 0.09f));
                Widgets.DrawBox(row, sel ? 2 : 1);

                GUI.color = sel ? new Color(1f, 0.8f, 0.4f) : Color.white;
                Text.Font = GameFont.Small;
                Widgets.Label(new Rect(8f, i * 36f + 7f, vRect.width - 16f, 20f),
                    e.enemyName);
                GUI.color = Color.white;

                if (Widgets.ButtonInvisible(row)) SelectEnemy(i);
            }
            Widgets.EndScrollView();
        }

        private void DrawEditor(Rect rect)
        {
            var   inner  = rect.ContractedBy(8f);
            float y      = inner.y;
            float fw     = inner.width;
            float halfW  = (fw - 8f) / 2f;

            F(inner.x, ref y, fw, "DNP.Editor.Name".Translate(), ref _bName);

            // HP + Armor side by side
            Label(inner.x, y, halfW, "DNP.Editor.HP".Translate());
            Label(inner.x + halfW + 8f, y, halfW, "DNP.Editor.Armor".Translate());
            y += 22f;
            _bHP    = Widgets.TextField(new Rect(inner.x,             y, halfW, 30f), _bHP);
            _bArmor = Widgets.TextField(new Rect(inner.x + halfW + 8f, y, halfW, 30f), _bArmor);
            y += 40f;

            // Damage + Bonus
            Label(inner.x, y, halfW, "DNP.Editor.Damage".Translate());
            Label(inner.x + halfW + 8f, y, halfW, "DNP.Editor.AttackBonus".Translate());
            y += 22f;
            _bDmg   = Widgets.TextField(new Rect(inner.x,             y, halfW, 30f), _bDmg);
            _bBonus = Widgets.TextField(new Rect(inner.x + halfW + 8f, y, halfW, 30f), _bBonus);
            y += 40f;

            // Behavior dropdown-style buttons
            Label(inner.x, y, fw, "DNP.Editor.Behavior".Translate());
            y += 22f;
            float bw2 = (fw - 12f) / 4f;
            for (int i = 0; i < Behaviors.Length; i++)
            {
                bool active = _bBehavior == Behaviors[i];
                GUI.color = active ? new Color(0.8f, 0.55f, 0.15f) : Color.white;
                if (Widgets.ButtonText(new Rect(inner.x + i * (bw2 + 4f), y, bw2, 26f),
                    Behaviors[i]))
                    _bBehavior = Behaviors[i];
                GUI.color = Color.white;
            }
            y += 34f;

            // Loot
            Label(inner.x, y, fw, "DNP.Editor.Loot".Translate());
            y += 24f;
            for (int i = 0; i < _bLoot.Count; i++)
            {
                float removeW = 22f;
                float chanceW = 50f;
                float idW     = fw - chanceW - removeW - 12f;
                var (lid, lch) = _bLoot[i];
                lid = Widgets.TextField(new Rect(inner.x,             y, idW,     24f), lid);
                lch = Widgets.TextField(new Rect(inner.x + idW + 4f, y, chanceW, 24f), lch);
                _bLoot[i] = (lid, lch);

                GUI.color = new Color(0.8f, 0.3f, 0.2f);
                if (Widgets.ButtonText(new Rect(inner.x + idW + chanceW + 8f, y, removeW, 24f), "✕"))
                    { _bLoot.RemoveAt(i); break; }
                GUI.color = Color.white;
                y += 28f;
            }
            if (Widgets.ButtonText(new Rect(inner.x, y, 120f, 24f),
                "+ " + "DNP.Editor.AddLoot".Translate()))
                _bLoot.Add(("", "0.30"));
            y += 32f;

            // Save / Delete
            y = rect.yMax - 36f;
            GUI.color = new Color(0.3f, 0.65f, 0.3f);
            if (Widgets.ButtonText(new Rect(inner.x, y, halfW, 30f),
                "DNP.Editor.Save".Translate()))
                SaveCurrent();
            GUI.color = new Color(0.7f, 0.25f, 0.2f);
            if (Widgets.ButtonText(new Rect(inner.x + halfW + 8f, y, halfW, 30f),
                "DNP.Editor.Delete".Translate()))
                DeleteCurrent();
            GUI.color = Color.white;
        }

        private void SaveCurrent()
        {
            var e = Current;
            if (e == null) return;
            e.enemyName   = _bName;
            e.hp          = int.TryParse(_bHP,    out int hp)  ? hp  : e.hp;
            e.armor       = int.TryParse(_bArmor, out int ar)  ? ar  : e.armor;
            e.attackDamage= int.TryParse(_bDmg,   out int dm)  ? dm  : e.attackDamage;
            e.attackBonus = int.TryParse(_bBonus, out int bn)  ? bn  : e.attackBonus;
            e.behaviorTag = _bBehavior;
            e.xpReward    = int.TryParse(_bXP,    out int xp)  ? xp  : e.xpReward;
            e.lootTable   = _bLoot
                .Where(l => !string.IsNullOrWhiteSpace(l.itemId))
                .Select(l => new DNP_LootEntry
                {
                    itemId     = l.itemId.Trim(),
                    dropChance = float.TryParse(l.chance,
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out float ch) ? ch : 0.3f
                }).ToList();

            DNP_JsonManager.ExportEnemy(e);
            Messages.Message("DNP.Editor.Saved".Translate(e.enemyName),
                MessageTypeDefOf.TaskCompletion, false);
        }

        private void DeleteCurrent()
        {
            var e = Current;
            if (e == null) return;
            DNP_JsonManager.DeleteEnemy(e.id);
            _enemies.RemoveAt(_sel);
            _sel = Mathf.Clamp(_sel, 0, _enemies.Count - 1);
            if (_enemies.Any()) SelectEnemy(_sel);
        }

        private void F(float x, ref float y, float w, string label, ref string val)
        {
            Label(x, y, w, label); y += 22f;
            val = Widgets.TextField(new Rect(x, y, w, 30f), val); y += 40f;
        }

        private void Label(float x, float y, float w, string text)
        {
            Text.Font = GameFont.Tiny;
            GUI.color = new Color(0.65f, 0.65f, 0.65f);
            Widgets.Label(new Rect(x, y, w, 20f), ((string)text).ToUpper());
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
        }
    }

    // ─────────────────────────────────────────────────────────────
    // ITEM EDITOR
    // ─────────────────────────────────────────────────────────────
    public class DNP_ItemEditorWindow : Window
    {
        public override Vector2 InitialSize => new Vector2(580f, 540f);

        private List<DNP_ItemData> _items;
        private int    _sel = 0;
        private Vector2 _listScroll;

        private string _bName, _bType, _bEffect, _bValue, _bDmgBonus, _bArmorBonus;

        private static readonly string[] ItemTypes   = { "Consumable", "Weapon", "Armor" };
        private static readonly string[] HealEffects = { "HealHP", "AddBuff", "none" };

        public DNP_ItemEditorWindow()
        {
            doCloseX = true; doCloseButton = false;
            resizeable = false; draggable = true;
            _items = DNP_ContentRegistry.AllItems.ToList();
            SelectItem(0);
        }

        private DNP_ItemData Current => _sel < _items.Count ? _items[_sel] : null;

        private void SelectItem(int idx)
        {
            _sel = idx;
            var i = Current;
            if (i == null) return;
            _bName       = i.itemName;
            _bType       = i.itemType;
            _bEffect     = i.consumableEffect ?? "none";
            _bValue      = i.consumableValue.ToString();
            _bDmgBonus   = i.damageBonus.ToString();
            _bArmorBonus = i.armorBonus.ToString();
        }

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, 30f),
                "DNP.Editor.Items".Translate());
            inRect.yMin += 36f;
            Text.Font = GameFont.Small;

            float listW = 170f;
            DrawItemList(new Rect(inRect.x, inRect.y, listW, inRect.height));
            if (Current != null)
                DrawItemEditor(new Rect(inRect.x + listW + 10f, inRect.y,
                    inRect.width - listW - 10f, inRect.height));
        }

        private void DrawItemList(Rect rect)
        {
            Widgets.DrawMenuSection(rect);
            var inner = rect.ContractedBy(6f);
            float y   = inner.y;

            if (Widgets.ButtonText(new Rect(inner.x, y, inner.width, 26f),
                "+ " + "DNP.Editor.NewItem".Translate()))
            {
                var neo = new DNP_ItemData
                {
                    id = "item_" + System.Guid.NewGuid().ToString("N").Substring(0, 6),
                    itemName = "New Item", itemType = "Consumable",
                    consumableEffect = "HealHP", consumableValue = 6
                };
                _items.Add(neo);
                SelectItem(_items.Count - 1);
            }
            y += 30f;

            float lh = inner.yMax - y;
            var vRect = new Rect(0, 0, inner.width - 16f, _items.Count * 36f);
            Widgets.BeginScrollView(new Rect(inner.x, y, inner.width, lh),
                ref _listScroll, vRect);

            for (int i = 0; i < _items.Count; i++)
            {
                var item = _items[i];
                var row  = new Rect(0, i * 36f, vRect.width, 32f);
                bool sel = i == _sel;

                Widgets.DrawBoxSolid(row, sel
                    ? new Color(0.12f, 0.2f, 0.12f) : new Color(0.09f, 0.09f, 0.09f));
                Widgets.DrawBox(row, sel ? 2 : 1);

                GUI.color = sel ? new Color(0.5f, 1f, 0.5f) : Color.white;
                Widgets.Label(new Rect(8f, i * 36f + 7f, vRect.width - 16f, 20f),
                    item.itemName);
                GUI.color = Color.white;

                if (Widgets.ButtonInvisible(row)) SelectItem(i);
            }
            Widgets.EndScrollView();
        }

        private void DrawItemEditor(Rect rect)
        {
            var   inner = rect.ContractedBy(8f);
            float y     = inner.y;
            float fw    = inner.width;
            float half  = (fw - 8f) / 2f;

            F(inner.x, ref y, fw, "DNP.Editor.Name".Translate(), ref _bName);

            // Type buttons
            LabelTiny(inner.x, y, fw, "DNP.Editor.ItemType".Translate()); y += 22f;
            float tw = (fw - 8f) / 3f;
            for (int i = 0; i < ItemTypes.Length; i++)
            {
                bool active = _bType == ItemTypes[i];
                GUI.color = active ? new Color(0.4f, 0.7f, 1f) : Color.white;
                if (Widgets.ButtonText(new Rect(inner.x + i * (tw + 4f), y, tw, 26f), ItemTypes[i]))
                    _bType = ItemTypes[i];
                GUI.color = Color.white;
            }
            y += 34f;

            if (_bType == "Consumable")
            {
                LabelTiny(inner.x, y, fw, "DNP.Editor.Effect".Translate()); y += 22f;
                float ew = (fw - 8f) / 3f;
                for (int i = 0; i < HealEffects.Length; i++)
                {
                    bool active = _bEffect == HealEffects[i];
                    GUI.color = active ? new Color(1f, 0.8f, 0.3f) : Color.white;
                    if (Widgets.ButtonText(new Rect(inner.x + i * (ew + 4f), y, ew, 26f), HealEffects[i]))
                        _bEffect = HealEffects[i];
                    GUI.color = Color.white;
                }
                y += 34f;
                F(inner.x, ref y, fw, "DNP.Editor.Value".Translate(), ref _bValue);
            }
            else
            {
                LabelTiny(inner.x, y, half, "DNP.Editor.DmgBonus".Translate());
                LabelTiny(inner.x + half + 8f, y, half, "DNP.Editor.ArmorBonus".Translate());
                y += 18f;
                _bDmgBonus   = Widgets.TextField(new Rect(inner.x, y, half, 30f), _bDmgBonus);
                _bArmorBonus = Widgets.TextField(new Rect(inner.x + half + 8f, y, half, 30f), _bArmorBonus);
                y += 40f;
            }

            y = rect.yMax - 36f;
            GUI.color = new Color(0.3f, 0.65f, 0.3f);
            if (Widgets.ButtonText(new Rect(inner.x, y, half, 30f),
                "DNP.Editor.Save".Translate()))
                SaveItem();
            GUI.color = new Color(0.7f, 0.25f, 0.2f);
            if (Widgets.ButtonText(new Rect(inner.x + half + 8f, y, half, 30f),
                "DNP.Editor.Delete".Translate()))
                DeleteItem();
            GUI.color = Color.white;
        }

        private void SaveItem()
        {
            var item = Current;
            if (item == null) return;
            item.itemName         = _bName;
            item.itemType         = _bType;
            item.consumableEffect = _bEffect == "none" ? "" : _bEffect;
            item.consumableValue  = int.TryParse(_bValue, out int v) ? v : 0;
            item.damageBonus      = int.TryParse(_bDmgBonus, out int d) ? d : 0;
            item.armorBonus       = int.TryParse(_bArmorBonus, out int a) ? a : 0;
            DNP_JsonManager.ExportItem(item);
            Messages.Message("DNP.Editor.Saved".Translate(item.itemName),
                MessageTypeDefOf.TaskCompletion, false);
        }

        private void DeleteItem()
        {
            var item = Current;
            if (item == null) return;
            DNP_JsonManager.DeleteItem(item.id);
            _items.RemoveAt(_sel);
            _sel = Mathf.Clamp(_sel, 0, _items.Count - 1);
            if (_items.Any()) SelectItem(_sel);
        }

        private void F(float x, ref float y, float w, string label, ref string val)
        {
            LabelTiny(x, y, w, label); y += 22f;
            val = Widgets.TextField(new Rect(x, y, w, 30f), val); y += 40f;
        }

        private void LabelTiny(float x, float y, float w, string text)
        {
            Text.Font = GameFont.Tiny;
            GUI.color = new Color(0.65f, 0.65f, 0.65f);
            Widgets.Label(new Rect(x, y, w, 20f), ((string)text).ToUpper());
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
        }
    }

    // ─────────────────────────────────────────────────────────────
    // SCENARIO EDITOR
    // ─────────────────────────────────────────────────────────────
    public class DNP_ScenarioEditorWindow : Window
    {
        public override Vector2 InitialSize => new Vector2(580f, 480f);

        private List<DNP_ScenarioData> _scenarios;
        private int    _sel = 0;
        private Vector2 _listScroll;

        private string _bName, _bOpening;
        private List<(string id, string count)> _bEnemies = new List<(string, string)>();

        public DNP_ScenarioEditorWindow()
        {
            doCloseX = true; doCloseButton = false;
            resizeable = false; draggable = true;
            _scenarios = DNP_ContentRegistry.AllScenarios.ToList();
            SelectScenario(0);
        }

        private DNP_ScenarioData Current => _sel < _scenarios.Count ? _scenarios[_sel] : null;

        private void SelectScenario(int idx)
        {
            _sel = idx;
            var s = Current;
            if (s == null) return;
            _bName    = s.scenarioTitle;
            _bOpening = s.openingNarration;
            // Flatten all encounters into a simple enemy list for editing
            _bEnemies = s.encounters?
                .SelectMany(e => e.enemies ?? new List<DNP_EnemySpawnEntry>())
                .Select(sp => (sp.enemyId, sp.count.ToString()))
                .ToList() ?? new List<(string, string)>();
        }

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, 30f),
                "DNP.Editor.Scenarios".Translate());
            inRect.yMin += 36f;
            Text.Font = GameFont.Small;

            float listW = 170f;
            DrawScenarioList(new Rect(inRect.x, inRect.y, listW, inRect.height));
            if (Current != null)
                DrawScenarioEditor(new Rect(inRect.x + listW + 10f, inRect.y,
                    inRect.width - listW - 10f, inRect.height));
        }

        private void DrawScenarioList(Rect rect)
        {
            Widgets.DrawMenuSection(rect);
            var inner = rect.ContractedBy(6f);
            float y = inner.y;

            if (Widgets.ButtonText(new Rect(inner.x, y, inner.width, 26f),
                "+ " + "DNP.Editor.NewScenario".Translate()))
            {
                var neo = new DNP_ScenarioData
                {
                    id = "scenario_" + System.Guid.NewGuid().ToString("N").Substring(0, 6),
                    scenarioTitle = "New Scenario",
                    openingNarration = "",
                    encounters = new List<DNP_EncounterTemplate>()
                };
                _scenarios.Add(neo);
                SelectScenario(_scenarios.Count - 1);
            }
            y += 30f;

            float lh = inner.yMax - y;
            var vRect = new Rect(0, 0, inner.width - 16f, _scenarios.Count * 36f);
            Widgets.BeginScrollView(new Rect(inner.x, y, inner.width, lh),
                ref _listScroll, vRect);

            for (int i = 0; i < _scenarios.Count; i++)
            {
                var s   = _scenarios[i];
                var row = new Rect(0, i * 36f, vRect.width, 32f);
                bool sel = i == _sel;

                Widgets.DrawBoxSolid(row, sel
                    ? new Color(0.1f, 0.14f, 0.2f) : new Color(0.09f, 0.09f, 0.09f));
                Widgets.DrawBox(row, sel ? 2 : 1);

                GUI.color = sel ? new Color(0.5f, 0.75f, 1f) : Color.white;
                Widgets.Label(new Rect(8f, i * 36f + 7f, vRect.width - 16f, 20f),
                    s.scenarioTitle);
                GUI.color = Color.white;

                if (Widgets.ButtonInvisible(row)) SelectScenario(i);
            }
            Widgets.EndScrollView();
        }

        private void DrawScenarioEditor(Rect rect)
        {
            var   inner = rect.ContractedBy(8f);
            float y     = inner.y;
            float fw    = inner.width;
            float half  = (fw - 8f) / 2f;

            // Name
            LabelTiny(inner.x, y, fw, "DNP.Editor.Name".Translate()); y += 22f;
            _bName = Widgets.TextField(new Rect(inner.x, y, fw, 30f), _bName); y += 40f;

            // Opening narration
            LabelTiny(inner.x, y, fw, "DNP.Editor.Opening".Translate()); y += 22f;
            float taH = 90f;
            _bOpening = Widgets.TextArea(new Rect(inner.x, y, fw, taH), _bOpening); y += taH + 12f;

            // Enemies
            LabelTiny(inner.x, y, fw, "DNP.Editor.EnemyList".Translate()); y += 22f;

            var validIds = DNP_ContentRegistry.AllEnemies.Select(e => e.id).ToList();
            for (int i = 0; i < _bEnemies.Count; i++)
            {
                var (eid, _) = _bEnemies[i];
                float removeW = 22f;
                float idW = fw - removeW - 4f;
                var row = new Rect(inner.x, y, fw, 26f);

                // Show as button-selector cycling through valid enemies
                bool valid = validIds.Contains(eid);
                GUI.color = valid ? Color.white : new Color(1f, 0.5f, 0.4f);
                if (Widgets.ButtonText(new Rect(inner.x, y, idW, 24f), eid))
                {
                    // Cycle to next valid enemy
                    int idx = validIds.IndexOf(eid);
                    eid = validIds.Count > 0
                        ? validIds[(idx + 1) % validIds.Count]
                        : eid;
                    _bEnemies[i] = (eid, "1");
                }
                GUI.color = Color.white;

                GUI.color = new Color(0.8f, 0.3f, 0.2f);
                if (Widgets.ButtonText(new Rect(inner.x + idW + 4f, y, removeW, 24f), "✕"))
                    { _bEnemies.RemoveAt(i); break; }
                GUI.color = Color.white;
                y += 28f;
            }

            if (validIds.Any() && Widgets.ButtonText(new Rect(inner.x, y, 140f, 24f),
                "+ " + "DNP.Editor.AddEnemy".Translate()))
                _bEnemies.Add((validIds[0], "1"));
            y += 32f;

            // Save / Delete
            y = rect.yMax - 36f;
            GUI.color = new Color(0.3f, 0.65f, 0.3f);
            if (Widgets.ButtonText(new Rect(inner.x, y, half, 30f),
                "DNP.Editor.Save".Translate()))
                SaveScenario();
            GUI.color = new Color(0.7f, 0.25f, 0.2f);
            if (Widgets.ButtonText(new Rect(inner.x + half + 8f, y, half, 30f),
                "DNP.Editor.Delete".Translate()))
                DeleteScenario();
            GUI.color = Color.white;
        }

        private void SaveScenario()
        {
            var s = Current;
            if (s == null) return;
            s.scenarioTitle    = _bName;
            s.openingNarration = _bOpening;
            // Pack enemies into a single encounter
            var enc = new DNP_EncounterTemplate
            {
                encounterName = s.scenarioTitle,
                enemies = _bEnemies
                    .Where(e => !string.IsNullOrWhiteSpace(e.id))
                    .Select(e => new DNP_EnemySpawnEntry
                    {
                        enemyId = e.id,
                        count   = int.TryParse(e.count, out int cnt) ? cnt : 1
                    }).ToList()
            };
            s.encounters = new List<DNP_EncounterTemplate> { enc };
            DNP_JsonManager.ExportScenario(s);
            Messages.Message("DNP.Editor.Saved".Translate(s.scenarioTitle),
                MessageTypeDefOf.TaskCompletion, false);
        }

        private void DeleteScenario()
        {
            var s = Current;
            if (s == null) return;
            DNP_JsonManager.DeleteScenario(s.id);
            _scenarios.RemoveAt(_sel);
            _sel = Mathf.Clamp(_sel, 0, _scenarios.Count - 1);
            if (_scenarios.Any()) SelectScenario(_sel);
        }

        private void LabelTiny(float x, float y, float w, string text)
        {
            Text.Font = GameFont.Tiny;
            GUI.color = new Color(0.65f, 0.65f, 0.65f);
            Widgets.Label(new Rect(x, y, w, 20f), ((string)text).ToUpper());
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
        }
    }
}