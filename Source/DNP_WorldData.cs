using System.Collections.Generic;
using Verse;

namespace DungeonsAndPawns
{
    // ─────────────────────────────────────────────────────────────
    // WORLD DATA — the lore/setting document for a campaign.
    // Persists with the save file via GameComponent.
    // Also exportable/importable as JSON for sharing.
    // ─────────────────────────────────────────────────────────────
    public class DNP_WorldData : IExposable
    {
        // FIX: stable identifier used for the JSON filename on disk.
        // Previously ExportWorld() built the filename from worldName +
        // a timestamp, so saving the same world twice created two
        // different files instead of overwriting one — this is what
        // caused duplicate world entries in the sidebar/picker. The id
        // is assigned once (the first time a world is exported) and
        // reused forever after, completely independent of worldName,
        // so renaming a world no longer creates a new file either.
        public string id = "";

        public string worldName    = "Unnamed World";
        public string genre        = "";   // "medieval fantasy", "sci-fi", "post-apocalyptic"...
        public string tone         = "";   // "grim", "heroic", "dark humor", "survival"...
        public string summary      = "";   // free-text world description
        public string history      = "";   // lore, backstory, important past events
        public string factions     = "";   // groups in the world and their goals
        public string locations    = "";   // notable places
        public string rules        = "";   // what exists / doesn't exist (magic, tech, etc.)
        public string aiInstructions = ""; // direct instructions to the AI narrator

        // Campaign-level goal (the "Adventure Path" objective)
        public string campaignName      = "";
        public string campaignObjective = ""; // "Destroy the artifact", "Find the signal source"...
        public string campaignNotes     = ""; // DM's private notes for the campaign arc

        public void ExposeData()
        {
            // FIX: id must be persisted with the save file too — otherwise
            // a world saved during play, then saved/loaded with the game,
            // would lose its id on reload and ExportWorld would treat it
            // as "new" again the next time the editor saves it, recreating
            // the exact duplicate-file problem this field exists to fix.
            Scribe_Values.Look(ref id,                "id",               "");
            Scribe_Values.Look(ref worldName,        "worldName",        "Unnamed World");
            Scribe_Values.Look(ref genre,            "genre",            "");
            Scribe_Values.Look(ref tone,             "tone",             "");
            Scribe_Values.Look(ref summary,          "summary",          "");
            Scribe_Values.Look(ref history,          "history",          "");
            Scribe_Values.Look(ref factions,         "factions",         "");
            Scribe_Values.Look(ref locations,        "locations",        "");
            Scribe_Values.Look(ref rules,            "rules",            "");
            Scribe_Values.Look(ref aiInstructions,   "aiInstructions",   "");
            Scribe_Values.Look(ref campaignName,     "campaignName",     "");
            Scribe_Values.Look(ref campaignObjective,"campaignObjective","");
            Scribe_Values.Look(ref campaignNotes,    "campaignNotes",    "");
        }

        // Builds the full context block sent to Player2 before any prompt
        public string BuildAIContext()
        {
            var sb = new System.Text.StringBuilder();

            if (!string.IsNullOrWhiteSpace(worldName))
                sb.AppendLine("# World: " + worldName);

            if (!string.IsNullOrWhiteSpace(genre))
                sb.AppendLine("Genre: " + genre);

            if (!string.IsNullOrWhiteSpace(tone))
                sb.AppendLine("Tone: " + tone);

            if (!string.IsNullOrWhiteSpace(summary))
            {
                sb.AppendLine("");
                sb.AppendLine("## Setting");
                sb.AppendLine(summary);
            }

            if (!string.IsNullOrWhiteSpace(history))
            {
                sb.AppendLine("");
                sb.AppendLine("## History");
                sb.AppendLine(history);
            }

            if (!string.IsNullOrWhiteSpace(factions))
            {
                sb.AppendLine("");
                sb.AppendLine("## Factions");
                sb.AppendLine(factions);
            }

            if (!string.IsNullOrWhiteSpace(locations))
            {
                sb.AppendLine("");
                sb.AppendLine("## Notable Locations");
                sb.AppendLine(locations);
            }

            if (!string.IsNullOrWhiteSpace(rules))
            {
                sb.AppendLine("");
                sb.AppendLine("## World Rules");
                sb.AppendLine(rules);
            }

            if (!string.IsNullOrWhiteSpace(campaignName) || !string.IsNullOrWhiteSpace(campaignObjective))
            {
                sb.AppendLine("");
                sb.AppendLine("## Campaign");
                if (!string.IsNullOrWhiteSpace(campaignName))
                    sb.AppendLine("Name: " + campaignName);
                if (!string.IsNullOrWhiteSpace(campaignObjective))
                    sb.AppendLine("Objective: " + campaignObjective);
            }

            if (!string.IsNullOrWhiteSpace(aiInstructions))
            {
                sb.AppendLine("");
                sb.AppendLine("## Narrator Instructions");
                sb.AppendLine(aiInstructions);
            }

            return sb.ToString().Trim();
        }

        // Quick summary for the session window header
        public string ShortSummary()
        {
            if (!string.IsNullOrWhiteSpace(campaignName))
                return campaignName + (string.IsNullOrWhiteSpace(campaignObjective)
                    ? "" : " — " + campaignObjective);
            if (!string.IsNullOrWhiteSpace(worldName) && worldName != "Unnamed World")
                return worldName;
            return "No world set";
        }
    }
}