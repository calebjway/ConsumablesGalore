# Consumables Galore

A server-side mod for SPT (Single Player Tarkov) that adds custom consumable items to the game.

## Version 2.0.0 - SPT 4.0.4

This mod has been migrated from TypeScript (SPT 3.x) to C# (SPT 4.0.4).

## Features

- Dynamically loads custom consumable items from JSON files in the `items/` directory
- Supports creating enhanced versions of existing consumables with custom effects
- Automatically adds items to:
  - Flea market with custom pricing
  - Handbook with custom pricing
  - Quest requirements (optional)
  - Loot spawns across all maps (optional)
  - Trader inventories (optional)
  - Hideout crafting recipes (optional)
- Update checker to notify you of new releases

## Installation

### Option 1: Direct Copy (Recommended)

1. Build the mod:
   ```bash
   dotnet build ConsumablesGalore.csproj -c Release
   ```

2. Copy the SPT folder contents to your SPT installation:
   - Copy from: `bin/Release/SPT/*`
   - To: `[Your SPT Install]/`
   - The folder structure will merge automatically (user/mods/ConsumablesGalore/)

3. Start your SPT server

### Option 2: Manual Copy

If you prefer, you can copy just the mod folder:
   - Copy from: `bin/Release/SPT/user/mods/ConsumablesGalore/`
   - To: `[Your SPT Install]/user/mods/ConsumablesGalore/`

## Configuration

Edit `config/config.json` to customize mod behavior:

```json
{
  "debug": false,      // Enable debug logging
  "realDebug": false   // Enable verbose debug logging
}
```

## Adding Custom Items

Custom items are defined as JSON files in the `items/` directory. Each file defines a new consumable item based on an existing one.

### Example Item JSON Structure

See any file in the `items/` directory for examples. Key properties:

- `cloneOrigin`: The ID of the original item to clone
- `id`: Unique ID for the new item
- `fleaPrice`: Price on flea market (number or "asOriginal" or multiplier ≤10)
- `handBookPrice`: Price in handbook (number or "asOriginal" or multiplier ≤10)
- `includeInSameQuestsAsOrigin`: Add to quests that require the original item
- `addSpawnsInSamePlacesAsOrigin`: Add spawn locations where original spawns
- `spawnWeightComparedToOrigin`: Spawn probability multiplier
- `effects_health`: Health effects when consumed
- `effects_damage`: Damage mitigation effects
- `Buffs`: Stimulator buff effects
- `locales`: Translations (name, shortName, description)
- `trader`: Trader inventory configuration (optional)
- `craft`: Hideout crafting recipe (optional)

## Migration from SPT 3.x

If you're upgrading from the TypeScript version (SPT 3.x):

1. All original TypeScript files have been moved to `Old_ModFiles/`
2. Item JSON files in the `items/` directory remain unchanged and fully compatible
3. Config format is the same (standard JSON, not JSON5)
4. The mod will automatically check the old version for update notifications

## Technical Details

- **Language**: C# (.NET 9)
- **Architecture**: Server-side mod using SPT's dependency injection system
- **Load Order**: PostDBModLoader + 1 (loads after database is initialized)
- **Item Loading**: Recursively scans `items/` directory for all .json files

## Credits

- **Original Author**: MusicManiac
- **Updated by**: AmightyTank
- **Migration to 4.0.4**: Based on migration guide from CustomRaidTimes mod

## License

MIT License

## Links

- GitHub: https://github.com/AlmightyTank/ConsumablesGalore
- Report Issues: https://github.com/AlmightyTank/ConsumablesGalore/issues
