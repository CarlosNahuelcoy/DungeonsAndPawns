# Dungeons & Pawns

A tabletop RPG system built inside RimWorld. Play D&D campaigns with your colonists, powered by AI narration and colonist personalities driven by their real RimWorld traits and backstories.

## What is this?

Dungeons & Pawns lets you run tabletop RPG sessions with your colony. Up to 4 colonists as players, each assigned a class, fighting enemies, rolling dice, and reacting to the world around them in character.

You can play two ways. Let the AI act as Dungeon Master — setting scenes, narrating combat, voicing NPCs, and driving the story forward while you play alongside your colonists. Or take the DM seat yourself and write your own narration while the AI handles each colonist's reactions, decisions, and personality.

Each colonist plays according to their actual RimWorld traits. A volatile Warrior charges without thinking. A careful Cleric heals with frustration when ignored. A pyromaniac Ranger should probably not be near the torches.

## Features

- 6 default classes: Warrior, Mage, Rogue, Cleric, Ranger, Paladin
- D&D 5e 2024 ruleset included, with support for custom rulesets
- Combat system with initiative order, d20 rolls, critical hits, fumbles, status effects, and loot
- Full world and campaign editor with lore, factions, locations, and AI narrator instructions
- In-game content editor for classes, enemies, items, and scenarios — no file editing required
- Session history saved to JSON and reviewable in-game at any time
- Colonist personalities shaped by RimWorld traits, backstories, and skills
- Supported AI providers: Player2, Gemini, OpenRouter, OpenAI, and any OpenAI-compatible local model

## Project structure

```
DungeonsAndPawns/
├── .vscode/
│   ├── build.bat
│   ├── extensions.json
│   ├── launch.json
│   └── tasks.json
├── About/
│   └── About.xml
├── Source/
│   ├── DungeonsAndPawns.csproj   ← build project
│   ├── DungeonsAndPawns.sln      ← solution file
│   ├── Main.cs
│   ├── DNP_AIController.cs
│   ├── DNP_ClassEditorWindow.cs
│   ├── DNP_ClassRegistry.cs
│   ├── DNP_ContentEditorWindows.cs
│   ├── DNP_ContentRegistry.cs
│   ├── DNP_DataModels.cs
│   ├── DNP_Dice.cs
│   ├── DNP_JsonManager.cs
│   ├── DNP_LLMBridge.cs
│   ├── DNP_LLMSettings.cs
│   ├── DNP_Player2Auth.cs
│   ├── DNP_PromptBuilder.cs
│   ├── DNP_SessionHistoryWindow.cs
│   ├── DNP_SessionManager.cs
│   ├── DNP_SessionWindow.cs
│   ├── DNP_SetupDialog.cs
│   ├── DNP_TagParser.cs
│   ├── DNP_TurnDirector.cs
│   ├── DNP_WorldData.cs
│   ├── DNP_WorldWindow.cs
│   └── SimpleJSON.cs
└── 1.6/
    ├── Assemblies/
    ├── Defs/
    │   ├── DNP_CoreDefs.xml
    │   └── DNP_ThoughtsAndUI.xml
    └── Languages/
        ├── English/
        ├── Spanish/
        └── SpanishLatin/
```

## Building from source

### Prerequisites

- [.NET SDK](https://dotnet.microsoft.com/download) (version 6 or later)
- [VS Code](https://code.visualstudio.com/) with the [C# Dev Kit](https://marketplace.visualstudio.com/items?itemName=ms-dotnettools.csdevkit) extension
- RimWorld installed (the build uses `Krafs.Rimworld.Ref` so no manual path configuration is needed)

### Steps

1. Clone the repository into your RimWorld `Mods/` folder
2. Open the `DungeonsAndPawns` folder in VS Code
3. Press **F5** or run the `build dll` task — this compiles the DLL into `1.6/Assemblies/` and launches RimWorld

The `Krafs.Rimworld.Ref` NuGet package provides RimWorld assembly references automatically. No manual DLL copying required.

If you prefer the command line:
```
dotnet build Source/DungeonsAndPawns.csproj
```

## Contributing

Pull requests are welcome. If you want to discuss a feature, fix a bug, or share feedback before opening a PR, come find me on Discord.

[discord.gg/nuqAeXCNBQ](https://discord.gg/nuqAeXCNBQ)

## Support

If you'd like to support continued development:

[ko-fi.com/gerik_uylerk](https://ko-fi.com/gerik_uylerk)
[patreon.com/gerikuylerk](https://www.patreon.com/gerikuylerk)

## License

GPL v3. If you fork this mod, Player2 must remain a supported AI provider option.