using System;
using Verse;
using RimWorld;

namespace DungeonsAndPawns
{
    // ─────────────────────────────────────────────────────────────
    // DICE ROLLER — all randomness goes through here
    // ─────────────────────────────────────────────────────────────
    public static class DNP_Dice
    {
        public static int Roll(int sides)
        {
            if (sides <= 0) return 0;
            return Rand.RangeInclusive(1, sides);
        }

        public static int RollMultiple(int count, int sides)
        {
            int total = 0;
            for (int i = 0; i < count; i++) total += Roll(sides);
            return total;
        }

        public static int StatBonus(int statValue, int interval = 2)
        {
            return (statValue - 10) / interval;
        }

        public static DNP_RollResult AttackRoll(int attackBonus, int targetArmor, int sides = 20)
        {
            int  raw    = Roll(sides);
            int  total  = raw + attackBonus;
            bool crit   = raw == sides;
            bool fumble = raw == 1;
            bool hit    = crit || (!fumble && total >= targetArmor);

            return new DNP_RollResult
            {
                rawRoll     = raw,
                bonus       = attackBonus,
                total       = total,
                success     = hit,
                isCritical  = crit,
                isFumble    = fumble,
                description = BuildDesc(raw, attackBonus, total, sides, hit, crit, fumble)
            };
        }

        public static DNP_RollResult CheckRoll(int bonus, int difficultyClass, int sides = 20)
        {
            int  raw     = Roll(sides);
            int  total   = raw + bonus;
            bool crit    = raw == sides;
            bool fumble  = raw == 1;
            bool success = crit || (!fumble && total >= difficultyClass);

            return new DNP_RollResult
            {
                rawRoll     = raw,
                bonus       = bonus,
                total       = total,
                success     = success,
                isCritical  = crit,
                isFumble    = fumble,
                description = BuildDesc(raw, bonus, total, sides, success, crit, fumble)
            };
        }

        private static string BuildDesc(int raw, int bonus, int total, int sides,
                                         bool success, bool crit, bool fumble)
        {
            string label   = crit ? "CRITICAL!" : fumble ? "FUMBLE!" : success ? "Success" : "Failure";
            string bonusStr = bonus >= 0 ? "+" + bonus : bonus.ToString();
            return "[d" + sides + ": " + raw + " " + bonusStr + " = " + total + "]  " + label;
        }
    }

    public class DNP_RollResult
    {
        public int    rawRoll;
        public int    bonus;
        public int    total;
        public bool   success;
        public bool   isCritical;
        public bool   isFumble;
        public string description;
    }

    // ─────────────────────────────────────────────────────────────
    // COMBAT RESOLVER
    // ─────────────────────────────────────────────────────────────
    public static class DNP_CombatResolver
    {
        /// <summary>
        /// Attack using a pre-rolled d20 result (from the Director's roll system).
        /// Used when advantage/disadvantage was already applied externally.
        /// </summary>
        public static string PlayerAttackWithRoll(DNP_PlayerCharacter attacker,
            DNP_EnemyInstance target, DNP_EnemyData enemyDef,
            DNP_RulesetData ruleset, int preRolledD20)
        {
            var classDef    = DNP_ClassRegistry.Get(attacker.classId);
            int attackBonus = 0;

            if (classDef != null)
            {
                int statVal = classDef.primaryStat == "Dexterity" ? attacker.statDexterity
                            : classDef.primaryStat == "Mind"      ? attacker.statMind
                                                                   : attacker.statStrength;
                attackBonus = DNP_Dice.StatBonus(statVal, ruleset.statBonusInterval);
            }

            if (ruleset.useColonistSkillBonuses && attacker.pawn != null)
            {
                var skill = attacker.pawn.skills?.GetSkill(SkillDefOf.Melee);
                if (skill != null) attackBonus += skill.Level / 5;
            }

            // Use the pre-rolled value instead of rolling again
            int  total      = preRolledD20 + attackBonus;
            bool isCritical = preRolledD20 == 20;
            bool isFumble   = preRolledD20 == 1;
            bool hit        = isCritical || (!isFumble && total > enemyDef.armor);

            string rollDesc = preRolledD20 + (attackBonus != 0
                ? (attackBonus > 0 ? "+" : "") + attackBonus + "=" + total
                : "");

            if (!hit)
                return attacker.characterName + " attacks " + target.instanceName
                    + ": " + rollDesc + " — Miss.";

            int dmg = DNP_Dice.Roll(6);
            if (isCritical) dmg *= 2;

            target.hp = Math.Max(0, target.hp - dmg);
            string note = target.hp <= 0
                ? " " + target.instanceName + " is defeated!"
                : " (" + target.instanceName + " HP: " + target.hp + "/" + target.maxHp + ")";
            string critTag = isCritical ? " CRITICAL!" : "";

            return attacker.characterName + " attacks " + target.instanceName
                + ": " + rollDesc + critTag + " — " + dmg + " damage." + note;
        }

        public static string PlayerAttack(DNP_PlayerCharacter attacker, DNP_EnemyInstance target,
                                          DNP_EnemyData enemyDef, DNP_RulesetData ruleset)
        {
            var classDef    = DNP_ClassRegistry.Get(attacker.classId);
            int attackBonus = 0;

            if (classDef != null)
            {
                int statVal = classDef.primaryStat == "Dexterity" ? attacker.statDexterity
                            : classDef.primaryStat == "Mind"      ? attacker.statMind
                                                                   : attacker.statStrength;
                attackBonus = DNP_Dice.StatBonus(statVal, ruleset.statBonusInterval);
            }

            if (ruleset.useColonistSkillBonuses && attacker.pawn != null)
            {
                var skill = attacker.pawn.skills?.GetSkill(SkillDefOf.Melee);
                if (skill != null) attackBonus += skill.Level / 5;
            }

            var roll = DNP_Dice.AttackRoll(attackBonus, enemyDef.armor, ruleset.checkDiceSides);
            if (!roll.success)
                return attacker.characterName + " attacks " + target.instanceName + ": " + roll.description;

            int dmg = DNP_Dice.Roll(6);
            if (roll.isCritical) dmg *= 2;

            target.hp = Math.Max(0, target.hp - dmg);
            string note = target.hp <= 0
                ? " " + target.instanceName + " is defeated!"
                : " (" + target.instanceName + " HP: " + target.hp + "/" + target.maxHp + ")";

            return attacker.characterName + " attacks " + target.instanceName + ": "
                + roll.description + " — " + dmg + " damage." + note;
        }

        public static string EnemyAttack(DNP_EnemyInstance attacker, DNP_EnemyData enemyDef,
                                          DNP_PlayerCharacter target, DNP_RulesetData ruleset)
        {
            var roll = DNP_Dice.AttackRoll(enemyDef.attackBonus, 10, ruleset.checkDiceSides);
            if (!roll.success)
                return attacker.instanceName + " attacks " + target.characterName + ": " + roll.description;

            int dmg = enemyDef.attackDamage;
            if (roll.isCritical) dmg = (int)(dmg * 1.5f);

            target.hp = Math.Max(0, target.hp - dmg);
            string note = target.hp <= 0
                ? " " + target.characterName + " is DOWN!"
                : " (" + target.characterName + " HP: " + target.hp + "/" + target.maxHp + ")";

            return attacker.instanceName + " attacks " + target.characterName + ": "
                + roll.description + " — " + dmg + " damage." + note;
        }

        public static bool IsEncounterOver(DNP_Encounter encounter)
        {
            foreach (var e in encounter.enemies)
                if (e.hp > 0) return false;
            return true;
        }

        public static bool IsPartyWiped(DNP_Session session)
        {
            foreach (var pc in session.characters)
                if (pc.hp > 0) return false;
            return true;
        }
    }
}