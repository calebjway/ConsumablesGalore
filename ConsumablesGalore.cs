using SPTarkov.Server.Core.Models.Spt.Mod;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers;
using SPTarkov.Server.Core.Models.Eft.Hideout;
using SPTarkov.Server.Core.Models.Utils;
using SPTarkov.Server.Core.Services;
using System.Dynamic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using ConsumablesGalore.Models;
using WTTServerCommonLib;

namespace ConsumablesGalore;

public record ModMetadata : AbstractModMetadata
{
    public override string ModGuid { get; init; } = "com.musicmaniac.consumablesgalore";
    public override string Name { get; init; } = "Consumables Galore";
    public override string Author { get; init; } = "MusicManiac, Updated by AmightyTank";
    public override List<string>? Contributors { get; init; }
    public override SemanticVersioning.Version Version { get; init; } = new("2.0.0");
    public override SemanticVersioning.Range SptVersion { get; init; } = new("~4.0.0");
    public override List<string>? Incompatibilities { get; init; }
    public override Dictionary<string, SemanticVersioning.Range>? ModDependencies { get; init; } = new()
    {
        ["com.wtt.commonlib"] = new(">=1.0.0")
    };
    public override string? Url { get; init; } = "https://github.com/AlmightyTank/ConsumablesGalore";
    public override bool? IsBundleMod { get; init; } = false;
    public override string? License { get; init; } = "MIT";
}

[Injectable(TypePriority = OnLoadOrder.PostDBModLoader + 1)]
public class ConsumablesGaloreMain(
    ISptLogger<ConsumablesGaloreMain> logger,
    ModHelper modHelper,
    DatabaseService databaseService,
    WTTServerCommonLib.WTTServerCommonLib wttCommon) : IOnLoad
{
    private const string ModShortName = "Consumables Galore";
    private readonly ISptLogger<ConsumablesGaloreMain> _logger = logger;
    private readonly ModHelper _modHelper = modHelper;
    private readonly DatabaseService _databaseService = databaseService;
    private readonly WTTServerCommonLib.WTTServerCommonLib _wttCommon = wttCommon;

    public async Task OnLoad()
    {
        _logger.Info($"[{ModShortName}] MusicManiac-Consumables-Galore started loading");

        // Get absolute path to mod folder
        var pathToMod = _modHelper.GetAbsolutePathToModFolder(Assembly.GetExecutingAssembly());

        // Load configuration
        var config = _modHelper.GetJsonDataFromFile<ModConfig>(pathToMod, "config/config.json");

        // Check for updates
        await CheckForUpdates(pathToMod);

        // Get database tables
        var tables = _databaseService.GetTables();
        var itemDb = tables.Templates.Items;
        var handbook = tables.Templates.Handbook;
        var fleaPriceTable = tables.Templates.Prices;
        var quests = tables.Templates.Quests;
        var traders = tables.Traders;
        var production = tables.Hideout.Production.Recipes;
        var locations = _databaseService.GetLocations();
        var globals = tables.Globals;

        // Process all JSON files in the items directory
        var itemsDirectory = Path.Combine(pathToMod, "items");
        if (Directory.Exists(itemsDirectory))
        {
            TraverseDirectory(itemsDirectory, config, itemDb, handbook, fleaPriceTable, quests, traders, locations, globals);
        }
        else
        {
            _logger.Warning($"[{ModShortName}] Items directory not found at: {itemsDirectory}");
        }

        // Use WTT library to add hideout craft recipes (from db/CustomHideoutRecipes)
        try
        {
            _logger.Info($"[{ModShortName}] Adding hideout craft recipes...");
            await _wttCommon.CustomHideoutRecipeService.CreateHideoutRecipes(Assembly.GetExecutingAssembly());
            _logger.Success($"[{ModShortName}] Hideout craft recipes added successfully!");
        }
        catch (Exception ex)
        {
            _logger.Error($"[{ModShortName}] Failed to add hideout craft recipes: {ex.Message}");
            if (config.RealDebug)
            {
                _logger.Error($"[{ModShortName}] Stack trace: {ex.StackTrace}");
            }
        }

        _logger.Success($"[{ModShortName}] MusicManiac-Consumables-Galore finished loading");
    }

    private void TraverseDirectory(
        string directory,
        ModConfig config,
        dynamic itemDb,
        dynamic handbook,
        dynamic fleaPriceTable,
        dynamic quests,
        dynamic traders,
        dynamic locations,
        dynamic globals)
    {
        var files = Directory.GetFiles(directory, "*.json", SearchOption.AllDirectories);

        foreach (var filePath in files)
        {
            try
            {
                if (config.Debug)
                {
                    _logger.Info($"[{ModShortName}] Processing file: {filePath}");
                }

                var fileContent = File.ReadAllText(filePath);
                var consumableFile = System.Text.Json.JsonSerializer.Deserialize<ConsumableItemData>(fileContent);

                if (consumableFile == null)
                {
                    _logger.Warning($"[{ModShortName}] Failed to deserialize file: {filePath}");
                    continue;
                }

                ProcessConsumableItem(consumableFile, config, itemDb, handbook, fleaPriceTable, quests, traders, locations, globals);
            }
            catch (Exception ex)
            {
                _logger.Error($"[{ModShortName}] Failed to process file {filePath}: {ex.Message}");
                if (config.RealDebug)
                {
                    _logger.Error($"[{ModShortName}] Stack trace: {ex.StackTrace}");
                }
            }
        }
    }

    private void ProcessConsumableItem(
        ConsumableItemData consumableFile,
        ModConfig config,
        dynamic itemDb,
        dynamic handbook,
        dynamic fleaPriceTable,
        dynamic quests,
        dynamic traders,
        dynamic locations,
        dynamic globals)
    {
        var originalConsumable = consumableFile.CloneOrigin;
        var newConsumableId = consumableFile.Id;

        // Find handbook parent ID
        string? handbookParentId = null;
        foreach (var item in handbook.Items)
        {
            // SPT 4.0.4: Id is MongoId, need to convert to string for comparison
            var itemIdStr = item.Id?.ToString();
            if (itemIdStr == originalConsumable)
            {
                // Convert ParentId to string and check if it's null or empty
                handbookParentId = item.ParentId?.ToString();
                if (string.IsNullOrEmpty(handbookParentId))
                {
                    _logger.Warning($"[{ModShortName}] Item {originalConsumable} has null or empty ParentId in handbook");
                    handbookParentId = null;
                }
                break;
            }
        }

        // Calculate flea price
        double fleaPrice = CalculatePrice(consumableFile.FleaPrice, fleaPriceTable[originalConsumable]);

        // Calculate handbook price
        double handbookPrice = CalculatePrice(consumableFile.HandBookPrice, fleaPriceTable[originalConsumable]);
        foreach (var item in handbook.Items)
        {
            // SPT 4.0.4: Id is MongoId, need to convert to string for comparison
            var itemIdStr = item.Id?.ToString();
            if (itemIdStr == originalConsumable && consumableFile.HandBookPrice?.ToString() == "asOriginal")
            {
                handbookPrice = (double)item.Price;
                break;
            }
        }

        // Create the item clone
        try
        {
            CreateItemClone(originalConsumable, newConsumableId, consumableFile, itemDb, handbookParentId, fleaPrice, handbookPrice, handbook, fleaPriceTable, config);
        }
        catch (Exception ex)
        {
            throw new Exception($"Failed to create item clone for {newConsumableId}: {ex.Message}", ex);
        }

        // Add buffs
        if (consumableFile.Buffs != null)
        {
            try
            {
                // Get the type of the Buffs property to deserialize to
                var buffsDict = globals.Configuration.Health.Effects.Stimulator.Buffs;
                var buffsDictType = buffsDict.GetType();

                // Get the value type (should be something like List<BuffType>)
                var genericArgs = buffsDictType.GetGenericArguments();
                var buffsValueType = System.Linq.Enumerable.Last(genericArgs); // Gets the TValue from IDictionary<TKey, TValue>

                // Serialize our buffs and convert integers to doubles, then deserialize to SPT's type
                var buffsJson = JsonSerializer.Serialize(consumableFile.Buffs);
                if (config.RealDebug)
                {
                    _logger.Info($"[{ModShortName}] Buffs JSON before conversion: {buffsJson}");
                }
                buffsJson = ConvertIntegersToDoublesInJson(buffsJson);
                if (config.RealDebug)
                {
                    _logger.Info($"[{ModShortName}] Buffs JSON after conversion: {buffsJson}");
                    _logger.Info($"[{ModShortName}] Target buffs type: {buffsValueType.FullName}");
                }
                var convertedBuffs = JsonSerializer.Deserialize(buffsJson, buffsValueType);

                globals.Configuration.Health.Effects.Stimulator.Buffs[newConsumableId] = convertedBuffs;
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to add buffs for {newConsumableId}: {ex.Message}", ex);
            }
        }

        // Add to quests
        if (consumableFile.IncludeInSameQuestsAsOrigin)
        {
            AddToQuests(originalConsumable, newConsumableId, quests, config);
        }

        // Add spawn points
        if (consumableFile.AddSpawnsInSamePlacesAsOrigin)
        {
            AddSpawnPoints(originalConsumable, newConsumableId, consumableFile.SpawnWeightComparedToOrigin, locations, config);
        }

        // Add to trader
        if (consumableFile.Trader != null)
        {
            AddToTrader(newConsumableId, consumableFile.Trader, traders);
        }

        // Note: Craft recipes are now handled by WTT-ServerCommonLib's CustomHideoutRecipeService
        // They are loaded from the db/CustomHideoutRecipes folder after all items are processed
    }

    private double CalculatePrice(object? priceValue, double originalPrice)
    {
        if (priceValue == null)
        {
            return originalPrice;
        }

        if (priceValue is JsonElement jsonElement)
        {
            if (jsonElement.ValueKind == JsonValueKind.String)
            {
                var strValue = jsonElement.GetString();
                if (strValue == "asOriginal")
                {
                    return originalPrice;
                }
            }
            else if (jsonElement.ValueKind == JsonValueKind.Number)
            {
                var numValue = jsonElement.GetDouble();
                if (numValue <= 10)
                {
                    return originalPrice * numValue;
                }
                return numValue;
            }
        }

        return originalPrice;
    }

    private void CreateItemClone(
        string originalConsumable,
        string newConsumableId,
        ConsumableItemData consumableFile,
        dynamic itemDb,
        string? handbookParentId,
        double fleaPrice,
        double handbookPrice,
        dynamic handbook,
        dynamic fleaPriceTable,
        ModConfig config)
    {
        // Get the original item - this is already a dynamic SPT TemplateItem
        dynamic originalItem = itemDb[originalConsumable];

        try
        {
            // Clone the item by serializing and deserializing
            if (config.Debug)
            {
                _logger.Info($"[{ModShortName}] Cloning original item...");
            }
            var itemJson = JsonSerializer.Serialize(originalItem);
            var itemType = originalItem.GetType();

            // SPT 4.0.4: The Prototype property cannot be null, but the serialized JSON may contain "Prototype": null
            // We need to recursively remove all null-valued properties from the JSON before deserializing
            var jsonNode = JsonNode.Parse(itemJson);
            if (jsonNode != null)
            {
                RemoveNullProperties(jsonNode);
                itemJson = jsonNode.ToJsonString();
            }

            if (config.RealDebug)
            {
                _logger.Info($"[{ModShortName}] Item JSON snippet (after null cleanup): {itemJson.Substring(0, Math.Min(500, itemJson.Length))}");
            }

            dynamic clonedItem;
            try
            {
                clonedItem = JsonSerializer.Deserialize(itemJson, itemType)!;
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to deserialize cloned item: {ex.InnerException?.Message ?? ex.Message}", ex);
            }

            if (config.Debug)
            {
                _logger.Info($"[{ModShortName}] Clone successful");
            }

            // Assign the cloned item to the new ID
            itemDb[newConsumableId] = clonedItem;
            if (config.Debug)
            {
                _logger.Info($"[{ModShortName}] Assigned cloned item to new ID");
            }

            // Now modify the properties on the cloned item
            if (config.Debug)
            {
                _logger.Info($"[{ModShortName}] Modifying item properties...");
            }

            // Inspect the item type to find the correct property names
            if (config.RealDebug)
            {
                _logger.Info($"[{ModShortName}] Item type: {itemType.Name}");
                var properties = itemType.GetProperties();
                var propertyNames = new List<string>();
                foreach (var prop in properties)
                {
                    propertyNames.Add(prop.Name);
                }
                _logger.Info($"[{ModShortName}] Available properties: {string.Join(", ", propertyNames)}");
            }

            // SPT 4.0.4 uses Pascal case properties: Id, Name, Parent, Type, Properties
            // Modify Id using reflection since it's a MongoId type
            var idField = itemType.GetProperty("Id");
            if (idField != null)
            {
                try
                {
                    if (string.IsNullOrEmpty(newConsumableId))
                    {
                        throw new Exception($"newConsumableId is null or empty when trying to set item Id");
                    }
                    // Create a new MongoId from the string
                    var mongoIdType = idField.PropertyType;
                    var mongoIdConstructor = mongoIdType.GetConstructor(new[] { typeof(string) });
                    if (mongoIdConstructor != null)
                    {
                        var newMongoId = mongoIdConstructor.Invoke(new object[] { newConsumableId });
                        idField.SetValue(clonedItem, newMongoId);
                        if (config.Debug)
                        {
                            _logger.Info($"[{ModShortName}] Set Id to {newConsumableId}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    throw new Exception($"Failed to set item Id: {ex.InnerException?.Message ?? ex.Message}", ex);
                }
            }

            // Get Properties (was _props in SPT 3.x)
            var propsField = itemType.GetProperty("Properties");
            if (propsField == null)
            {
                throw new Exception($"Could not find Properties property on type {itemType.Name}");
            }

            // Get props using reflection
            dynamic props = propsField.GetValue(clonedItem);
            if (props != null)
            {
                // Inspect properties on the Properties object
                var propsType = props.GetType();

                if (config.RealDebug)
                {
                    var propsProperties = propsType.GetProperties();
                    var propsPropertyNames = new List<string>();
                    foreach (var prop in propsProperties)
                    {
                        propsPropertyNames.Add(prop.Name);
                    }
                    _logger.Info($"[{ModShortName}] Properties object has: {string.Join(", ", propsPropertyNames.Take(20))}...");
                }

                // SPT 4.0.4: StimulatorBuffs might be a MongoId type
                try
                {
                    if (string.IsNullOrEmpty(newConsumableId))
                    {
                        throw new Exception($"newConsumableId is null or empty when trying to set StimulatorBuffs");
                    }

                    var stimBuffsProp = propsType.GetProperty("StimulatorBuffs");
                    if (stimBuffsProp != null)
                    {
                        var stimBuffsType = stimBuffsProp.PropertyType;

                        // Check if it's a MongoId type
                        if (stimBuffsType.Name.Contains("MongoId") || stimBuffsType.FullName?.Contains("MongoId") == true)
                        {
                            // Create MongoId from string
                            var mongoIdConstructor = stimBuffsType.GetConstructor(new[] { typeof(string) });
                            if (mongoIdConstructor != null)
                            {
                                var mongoIdValue = mongoIdConstructor.Invoke(new object[] { newConsumableId });
                                stimBuffsProp.SetValue(props, mongoIdValue);
                            }
                        }
                        else
                        {
                            // It's a string, just assign directly
                            props.StimulatorBuffs = newConsumableId;
                        }
                    }
                    else
                    {
                        // Property doesn't exist or is dynamic, try direct assignment
                        props.StimulatorBuffs = newConsumableId;
                    }
                }
                catch (Exception ex)
                {
                    throw new Exception($"Failed to set StimulatorBuffs: {ex.InnerException?.Message ?? ex.Message}", ex);
                }

                if (consumableFile.BackgroundColor != null)
                {
                    props.BackgroundColor = consumableFile.BackgroundColor;
                }

                // SPT 4.0.4: EffectsHealth and EffectsDamage use enum keys instead of strings
                // Attempt to set using reflection and dynamic conversion
                if (consumableFile.EffectsHealth != null)
                {
                    try
                    {
                        SetEffects(props, "EffectsHealth", consumableFile.EffectsHealth, config);
                    }
                    catch (Exception ex)
                    {
                        _logger.Warning($"[{ModShortName}] Failed to set EffectsHealth: {ex.Message}");
                        if (config.RealDebug)
                        {
                            _logger.Warning($"[{ModShortName}] EffectsHealth structure may have changed in SPT 4.0.4");
                        }
                    }
                }

                if (consumableFile.EffectsDamage != null)
                {
                    try
                    {
                        SetEffects(props, "EffectsDamage", consumableFile.EffectsDamage, config);
                    }
                    catch (Exception ex)
                    {
                        _logger.Warning($"[{ModShortName}] Failed to set EffectsDamage: {ex.Message}");
                        if (config.RealDebug)
                        {
                            _logger.Warning($"[{ModShortName}] EffectsDamage structure may have changed in SPT 4.0.4");
                        }
                    }
                }

                if (consumableFile.MaxResource.HasValue)
                {
                    props.MaxHpResource = consumableFile.MaxResource.Value;
                }

                if (consumableFile.HpResourceRate.HasValue)
                {
                    props.HpResourceRate = consumableFile.HpResourceRate.Value;
                }

                if (consumableFile.MedUseTime.HasValue)
                {
                    props.MedUseTime = consumableFile.MedUseTime.Value;
                }

                if (consumableFile.Prefab != null)
                {
                    props.Prefab = consumableFile.Prefab;
                }

                if (consumableFile.UsePrefab != null)
                {
                    props.UsePrefab = consumableFile.UsePrefab;
                }

                if (consumableFile.ItemSound != null)
                {
                    props.ItemSound = consumableFile.ItemSound;
                }
            }

        // Add to handbook
        if (handbookParentId != null)
        {
            // Clone an existing handbook entry using JsonNode
            var firstItem = handbook.Items[0];
            var handbookJson = JsonSerializer.Serialize(firstItem);
            var handbookNode = JsonNode.Parse(handbookJson)!;

            // Preserve the original Id and ParentId node structures
            var originalHandbookIdNode = handbookNode["Id"]?.DeepClone();
            var originalHandbookParentIdNode = handbookNode["ParentId"]?.DeepClone();

            // Modify properties
            handbookNode["Price"] = (int)handbookPrice;

            // Restore original Id and ParentId structures for deserialization
            if (originalHandbookIdNode != null)
            {
                handbookNode["Id"] = originalHandbookIdNode;
            }
            if (originalHandbookParentIdNode != null)
            {
                handbookNode["ParentId"] = originalHandbookParentIdNode;
            }

            // Deserialize back to the original type
            var modifiedHandbookJson = handbookNode.ToJsonString();
            var handbookEntry = JsonSerializer.Deserialize(modifiedHandbookJson, firstItem.GetType())!;

            // Use reflection to set correct Id and ParentId with MongoId
            var handbookType = handbookEntry.GetType();
            var idProp = handbookType.GetProperty("Id");
            var parentIdProp = handbookType.GetProperty("ParentId");

            if (idProp != null)
            {
                var mongoIdType = idProp.PropertyType;
                var constructor = mongoIdType.GetConstructor(new[] { typeof(string) });
                if (constructor != null)
                {
                    if (string.IsNullOrEmpty(newConsumableId))
                    {
                        throw new Exception($"newConsumableId is null or empty when trying to create MongoId for handbook Id");
                    }
                    try
                    {
                        var mongoIdValue = constructor.Invoke(new object[] { newConsumableId });
                        idProp.SetValue(handbookEntry, mongoIdValue);
                    }
                    catch (Exception ex)
                    {
                        throw new Exception($"Failed to create MongoId for handbook Id with value '{newConsumableId}': {ex.InnerException?.Message ?? ex.Message}", ex);
                    }
                }
            }

            if (parentIdProp != null && handbookParentId != null)
            {
                var mongoIdType = parentIdProp.PropertyType;
                var constructor = mongoIdType.GetConstructor(new[] { typeof(string) });
                if (constructor != null)
                {
                    if (string.IsNullOrEmpty(handbookParentId))
                    {
                        throw new Exception($"handbookParentId is null or empty when trying to create MongoId for handbook ParentId");
                    }
                    try
                    {
                        var mongoIdValue = constructor.Invoke(new object[] { handbookParentId });
                        parentIdProp.SetValue(handbookEntry, mongoIdValue);
                    }
                    catch (Exception ex)
                    {
                        throw new Exception($"Failed to create MongoId for handbook ParentId with value '{handbookParentId}': {ex.InnerException?.Message ?? ex.Message}", ex);
                    }
                }
            }

            handbook.Items.Add(handbookEntry);
        }

        // Add to flea prices
        fleaPriceTable[newConsumableId] = (int)fleaPrice;

        // Add locales
        if (consumableFile.Locales != null)
        {
            // SPT 4.0.4: Locales are lazy-loaded, use AddTransformer
            var locales = _databaseService.GetLocales().Global;
            foreach (var localeKvp in consumableFile.Locales)
            {
                var lang = localeKvp.Key;
                var localeData = localeKvp.Value;

                try
                {
                    if (locales.TryGetValue(lang, out var lazyLocale))
                    {
                        // Capture values to avoid closure issues
                        var itemId = newConsumableId;
                        var name = localeData.Name;
                        var shortName = localeData.ShortName;
                        var description = localeData.Description;

                        lazyLocale.AddTransformer(dict =>
                        {
                            dict[$"{itemId} Name"] = name;
                            dict[$"{itemId} ShortName"] = shortName;
                            dict[$"{itemId} Description"] = description;
                            return dict;
                        });
                    }
                }
                catch (Exception ex)
                {
                    if (config.RealDebug)
                    {
                        _logger.Warning($"[{ModShortName}] Failed to add locale for {lang}: {ex.Message}");
                    }
                }
            }
        }
        }
        catch (Exception ex)
        {
            var innerMsg = ex.InnerException?.Message ?? ex.Message;
            var innerStack = ex.InnerException?.StackTrace ?? ex.StackTrace;
            throw new Exception($"Error in CreateItemClone: {innerMsg}\nStack: {innerStack}", ex);
        }
    }

    private void AddToQuests(string originalConsumable, string newConsumableId, dynamic quests, ModConfig config)
    {
        foreach (var questKvp in quests)
        {
            var quest = questKvp.Value;

            if (quest.Conditions?.AvailableForFinish == null) continue;

            foreach (var condition in quest.Conditions.AvailableForFinish)
            {
                // SPT 4.0.4: ConditionType (capital C) instead of conditionType
                var conditionType = condition.ConditionType?.ToString();

                // SPT 4.0.4: Target (capital T) instead of target
                if ((conditionType == "HandoverItem" || conditionType == "FindItem") && condition.Target != null)
                {
                    try
                    {
                        // SPT 4.0.4: Target is ListOrT<string>, use reflection to access
                        var targetList = condition.Target;
                        var targetType = targetList.GetType();

                        // ListOrT<string> has a Count property and indexer
                        var countProp = targetType.GetProperty("Count");
                        if (countProp != null)
                        {
                            int count = (int)countProp.GetValue(targetList);
                            bool hasOriginal = false;

                            // Use indexer to access elements
                            for (int i = 0; i < count; i++)
                            {
                                var item = targetList[i];
                                if (item != null && item.ToString() == originalConsumable)
                                {
                                    hasOriginal = true;
                                    break;
                                }
                            }

                            if (hasOriginal)
                            {
                                if (config.Debug)
                                {
                                    _logger.Info($"[{ModShortName}] Found {originalConsumable} as find/handover item in quest, adding {newConsumableId} to it");
                                }
                                // Use Add method via reflection
                                var addMethod = targetType.GetMethod("Add");
                                if (addMethod != null)
                                {
                                    addMethod.Invoke(targetList, new object[] { newConsumableId });
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        if (config.Debug)
                        {
                            _logger.Warning($"[{ModShortName}] Failed to process quest condition Target: {ex.Message}");
                        }
                    }
                }
            }
        }
    }

    private void AddSpawnPoints(string originalConsumable, string newConsumableId, double spawnWeight, dynamic locations, ModConfig config)
    {
        try
        {
            var lootComposedKey = newConsumableId + "_composedkey";
            var mapsList = new List<string> { "bigmap", "woods", "factory4_day", "factory4_night", "interchange", "laboratory", "lighthouse", "rezervbase", "shoreline", "tarkovstreets", "sandbox" };

            if (config.Debug)
            {
                _logger.Info($"[{ModShortName}] Adding spawn points for {newConsumableId}");
            }

            // SPT 4.0.4: Locations object has properties for each map, need to iterate through properties
            var locationsType = locations.GetType();
            var properties = locationsType.GetProperties();

            foreach (var prop in properties)
            {
                var locationName = prop.Name.ToLower();

                if (!mapsList.Contains(locationName)) continue;

                var location = prop.GetValue(locations);
                if (location == null) continue;

                // Process loose loot - try both lowercase and PascalCase property names
                dynamic looseLoot = null;
                try
                {
                    looseLoot = location.LooseLoot ?? location.looseLoot;
                }
                catch
                {
                    // Property doesn't exist, skip
                }

                if (looseLoot != null)
                {
                    dynamic spawnPoints = null;
                    try
                    {
                        spawnPoints = looseLoot.SpawnPoints ?? looseLoot.spawnpoints ?? looseLoot.Spawnpoints;
                    }
                    catch
                    {
                        // Property doesn't exist
                    }

                    if (spawnPoints != null)
                    {
                        foreach (var point in spawnPoints)
                        {
                            dynamic template = null;
                            try
                            {
                                template = point.Template ?? point.template;
                            }
                            catch { }

                            dynamic items = null;
                            if (template != null)
                            {
                                try
                                {
                                    items = template.Items ?? template.items;
                                }
                                catch { }
                            }

                            if (items == null) continue;

                            foreach (var item in items)
                            {
                                string tpl = null;
                                try
                                {
                                    tpl = item.Tpl?.ToString() ?? item._tpl?.ToString() ?? item.tpl?.ToString();
                                }
                                catch { }

                                if (tpl == originalConsumable)
                                {
                                    string originalItemId = null;
                                    try
                                    {
                                        originalItemId = item.Id?.ToString() ?? item._id?.ToString() ?? item.id?.ToString();
                                    }
                                    catch { }

                                    if (string.IsNullOrEmpty(originalItemId)) continue;

                                    double? originRelativeProb = null;

                                    dynamic itemDistribution = null;
                                    try
                                    {
                                        itemDistribution = point.ItemDistribution ?? point.itemDistribution;
                                    }
                                    catch { }

                                    if (itemDistribution != null)
                                    {
                                        foreach (var dist in itemDistribution)
                                        {
                                            string distKey = null;
                                            try
                                            {
                                                var composedKey = dist.ComposedKey ?? dist.composedKey;
                                                if (composedKey != null)
                                                {
                                                    distKey = composedKey.Key?.ToString() ?? composedKey.key?.ToString();
                                                }
                                            }
                                            catch { }

                                            if (distKey == originalItemId)
                                            {
                                                try
                                                {
                                                    var relProb = dist.RelativeProbability ?? dist.relativeProbability;
                                                    originRelativeProb = Convert.ToDouble(relProb);
                                                }
                                                catch { }

                                                // Add new item to template
                                                if (items != null)
                                                {
                                                    try
                                                    {
                                                        // Create new item using anonymous object
                                                        var newItem = new
                                                        {
                                                            _id = lootComposedKey,
                                                            _tpl = newConsumableId
                                                        };
                                                        // Serialize and deserialize to match the collection's type
                                                        var itemJson = JsonSerializer.Serialize(newItem);
                                                        var itemNode = JsonNode.Parse(itemJson);
                                                        ((dynamic)items).Add(itemNode);
                                                    }
                                                    catch (Exception ex)
                                                    {
                                                        if (config.Debug)
                                                        {
                                                            _logger.Warning($"[{ModShortName}] Failed to add item to template: {ex.Message}");
                                                        }
                                                    }
                                                }
                                                break;
                                            }
                                        }

                                        if (originRelativeProb.HasValue && itemDistribution != null)
                                        {
                                            try
                                            {
                                                var newProbability = Math.Max((int)Math.Round(originRelativeProb.Value * spawnWeight), 1);
                                                var newDist = new
                                                {
                                                    composedKey = new { key = lootComposedKey },
                                                    relativeProbability = newProbability
                                                };
                                                var distJson = JsonSerializer.Serialize(newDist);
                                                var distNode = JsonNode.Parse(distJson);
                                                ((dynamic)itemDistribution).Add(distNode);

                                                if (config.Debug)
                                                {
                                                    _logger.Info($"[{ModShortName}] Added {newConsumableId} to loose loot in {locationName} with probability {newProbability}");
                                                }
                                            }
                                            catch (Exception ex)
                                            {
                                                if (config.Debug)
                                                {
                                                    _logger.Warning($"[{ModShortName}] Failed to add to item distribution: {ex.Message}");
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }

                // Process static loot
                dynamic staticLoot = null;
                try
                {
                    staticLoot = location.StaticLoot ?? location.staticLoot;
                }
                catch
                {
                    // Property doesn't exist
                }

                if (staticLoot != null)
                {
                    // SPT 4.0.4: StaticLoot is LazyLoad<Dictionary<MongoId, StaticLootDetails>>
                    // Need to access .Value to get the actual dictionary
                    dynamic staticLootDict = null;
                    try
                    {
                        // Try to get the Value property (LazyLoad wrapper)
                        var staticLootType = staticLoot.GetType();
                        var valueProp = staticLootType.GetProperty("Value");
                        if (valueProp != null)
                        {
                            staticLootDict = valueProp.GetValue(staticLoot);
                        }
                        else
                        {
                            // Already a dictionary
                            staticLootDict = staticLoot;
                        }
                    }
                    catch
                    {
                        staticLootDict = staticLoot;
                    }

                    if (staticLootDict != null)
                    {
                        foreach (var containerKvp in staticLootDict)
                        {
                            var containerName = containerKvp.Key?.ToString() ?? "unknown";
                            var container = containerKvp.Value;

                            dynamic itemDistribution = null;
                            try
                            {
                                itemDistribution = container.ItemDistribution ?? container.itemDistribution;
                            }
                            catch { }

                            if (itemDistribution == null) continue;

                            try
                            {
                                int count = itemDistribution.Count;
                                for (int i = 0; i < count; i++)
                                {
                                    var entry = itemDistribution[i];

                                    string entryTpl = null;
                                    try
                                    {
                                        entryTpl = entry.Tpl?.ToString() ?? entry.tpl?.ToString() ?? entry._tpl?.ToString();
                                    }
                                    catch { }

                                    if (entryTpl == originalConsumable)
                                    {
                                        double originProbability = 0;
                                        try
                                        {
                                            var relProb = entry.RelativeProbability ?? entry.relativeProbability;
                                            originProbability = Convert.ToDouble(relProb);
                                        }
                                        catch { }

                                        var spawnRelativeProbability = Math.Max((int)Math.Round(originProbability * spawnWeight), 1);

                                        if (config.RealDebug)
                                        {
                                            _logger.Info($"[{ModShortName}] Adding {newConsumableId} to container {containerName} with probability {spawnRelativeProbability}");
                                        }

                                        try
                                        {
                                            var newEntry = new
                                            {
                                                tpl = newConsumableId,
                                                relativeProbability = spawnRelativeProbability
                                            };
                                            var entryJson = JsonSerializer.Serialize(newEntry);
                                            var entryNode = JsonNode.Parse(entryJson);
                                            ((dynamic)itemDistribution).Add(entryNode);
                                        }
                                        catch (Exception ex)
                                        {
                                            if (config.Debug)
                                            {
                                                _logger.Warning($"[{ModShortName}] Failed to add to static loot: {ex.Message}");
                                            }
                                        }
                                        break;
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                if (config.Debug)
                                {
                                    _logger.Warning($"[{ModShortName}] Error processing static loot container {containerName}: {ex.Message}");
                                }
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"[{ModShortName}] Failed to add spawn points for {newConsumableId}: {ex.Message}");
            if (config.RealDebug)
            {
                _logger.Error($"[{ModShortName}] Stack trace: {ex.StackTrace}");
            }
        }
    }

    private void AddToTrader(string newConsumableId, TraderData traderData, dynamic traders)
    {
        try
        {
            dynamic trader = null;
            try
            {
                trader = traders[traderData.TraderId];
            }
            catch (Exception ex)
            {
                _logger.Warning($"[{ModShortName}] Failed to access trader {traderData.TraderId}: {ex.Message}");
                return;
            }

            if (trader == null)
            {
                _logger.Warning($"[{ModShortName}] Trader {traderData.TraderId} not found!");
                return;
            }

            // SPT 4.0.4: Try both Assort and assort
            dynamic assort = null;
            try
            {
                assort = trader.Assort ?? trader.assort;
            }
            catch
            {
                _logger.Warning($"[{ModShortName}] Could not access trader assort");
                return;
            }

            if (assort == null)
            {
                _logger.Warning($"[{ModShortName}] Trader {traderData.TraderId} has no assort!");
                return;
            }

            // Get Items collection - try both Pascal and lowercase
            dynamic items = null;
            try
            {
                items = assort.Items ?? assort.items;
            }
            catch { }

            if (items != null)
            {
                try
                {
                    // Clone an existing item from the trader to match the type structure
                    if (items.Count > 0)
                    {
                        var firstItem = items[0];
                        var itemType = firstItem.GetType();
                        var itemJson = JsonSerializer.Serialize(firstItem);
                        var itemNode = JsonNode.Parse(itemJson)!;

                        // Modify properties to match our new item
                        itemNode["_id"] = newConsumableId;
                        itemNode["_tpl"] = newConsumableId;
                        itemNode["parentId"] = "hideout";
                        itemNode["slotId"] = "hideout";

                        // Create or modify upd node
                        if (itemNode["upd"] == null)
                        {
                            itemNode["upd"] = new JsonObject();
                        }
                        var updNode = itemNode["upd"].AsObject();
                        updNode["UnlimitedCount"] = false;
                        updNode["StackObjectsCount"] = traderData.AmountForSale;

                        // Deserialize back to the correct type
                        var modifiedItemJson = itemNode.ToJsonString();
                        var newItem = JsonSerializer.Deserialize(modifiedItemJson, itemType);

                        items.Add(newItem);
                        _logger.Info($"[{ModShortName}] Added {newConsumableId} to trader {traderData.TraderId}");
                    }
                    else
                    {
                        // No existing items to clone from, try direct JsonNode approach
                        var itemJson = JsonSerializer.Serialize(new
                        {
                            _id = newConsumableId,
                            _tpl = newConsumableId,
                            parentId = "hideout",
                            slotId = "hideout",
                            upd = new
                            {
                                UnlimitedCount = false,
                                StackObjectsCount = traderData.AmountForSale
                            }
                        });
                        var itemNode = JsonNode.Parse(itemJson);
                        items.Add(itemNode);
                    }
                }
                catch (Exception ex)
                {
                    _logger.Warning($"[{ModShortName}] Failed to add item to trader {traderData.TraderId}: {ex.Message}");
                }
            }

            // Add barter scheme (price) - try both Pascal and lowercase
            dynamic barterScheme = null;
            try
            {
                barterScheme = assort.BarterScheme ?? assort.barterScheme ?? assort.Barter_scheme;
            }
            catch { }

            if (barterScheme != null)
            {
                try
                {
                    var barterData = new object[][]
                    {
                        new object[]
                        {
                            new
                            {
                                count = traderData.Price,
                                _tpl = "5449016a4bdc2d6f028b456f" // Roubles
                            }
                        }
                    };

                    // Serialize to JsonNode to match dictionary value type
                    var barterJson = JsonSerializer.Serialize(barterData);
                    var barterNode = JsonNode.Parse(barterJson);
                    barterScheme[newConsumableId] = barterNode;
                }
                catch (Exception ex)
                {
                    _logger.Warning($"[{ModShortName}] Failed to add barter scheme for {newConsumableId}: {ex.Message}");
                }
            }

            // Add loyalty level requirement - try both Pascal and lowercase
            dynamic loyaltyLevelItems = null;
            try
            {
                loyaltyLevelItems = assort.LoyaltyLevelItems ?? assort.loyaltyLevelItems ?? assort.Loyalty_level_items;
            }
            catch { }

            if (loyaltyLevelItems != null)
            {
                try
                {
                    loyaltyLevelItems[newConsumableId] = traderData.LoyaltyReq;
                }
                catch (Exception ex)
                {
                    _logger.Warning($"[{ModShortName}] Failed to add loyalty level for {newConsumableId}: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"[{ModShortName}] Failed to add {newConsumableId} to trader: {ex.Message}");
        }
    }

    private async Task CheckForUpdates(string modPath)
    {
        try
        {
            var packageJsonPath = Path.Combine(modPath, "Old_ModFiles", "package.json");
            if (!File.Exists(packageJsonPath))
            {
                _logger.Info($"[{ModShortName}] Update check skipped - package.json not found in Old_ModFiles");
                return;
            }

            var packageJson = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object>>(File.ReadAllText(packageJsonPath));
            if (packageJson == null || !packageJson.ContainsKey("version"))
            {
                return;
            }

            var localVersion = packageJson["version"].ToString() ?? "1.4.3";

            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Add("User-Agent", "SPT-Mod");
            httpClient.DefaultRequestHeaders.Add("Accept", "application/vnd.github.v3+json");

            var response = await httpClient.GetStringAsync("https://api.github.com/repos/AlmightyTank/ConsumablesGalore/releases/latest");
            var release = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(response);

            if (release == null || !release.ContainsKey("tag_name"))
            {
                return;
            }

            var tagName = release["tag_name"].GetString() ?? "";
            var latestVersion = tagName.Contains("-") ? tagName.Split("-").Last() : tagName.Replace("v", "");

            var comparison = CompareVersions(localVersion, latestVersion);

            if (comparison < 0)
            {
                var htmlUrl = release.ContainsKey("html_url") ? release["html_url"].GetString() : "";
                _logger.Warning($"[{ModShortName}] New version available: v{latestVersion}. You're using v{localVersion}. Visit: {htmlUrl}");
            }
            else if (comparison > 0)
            {
                _logger.Info($"[{ModShortName}] You are using a newer version (v{localVersion}) than the latest release (v{latestVersion}).");
            }
            else
            {
                _logger.Info($"[{ModShortName}] You're using the latest version (v{localVersion})");
            }
        }
        catch (Exception ex)
        {
            _logger.Error($"[{ModShortName}] Failed to check for updates: {ex.Message}");
        }
    }

    private int CompareVersions(string a, string b)
    {
        var aParts = a.Split('.').Select(int.Parse).ToArray();
        var bParts = b.Split('.').Select(int.Parse).ToArray();

        for (int i = 0; i < Math.Min(aParts.Length, bParts.Length); i++)
        {
            if (aParts[i] != bParts[i])
            {
                return aParts[i] - bParts[i];
            }
        }

        return aParts.Length - bParts.Length;
    }

    /// <summary>
    /// Forces all whole numbers in JSON to have .0 decimal point
    /// This ensures System.Text.Json deserializes them as double instead of int
    /// Examples: 10 -> 10.0, -5 -> -5.0, but 10.5 stays 10.5
    /// </summary>
    private string ForceNumbersToDoubleFormat(string json)
    {
        // Use regex to find all whole numbers in JSON and add .0 to them
        // Pattern explanation:
        // (?<=[\[\{,:]\s*) - preceded by [, {, comma, or colon with optional whitespace
        // -?\d+ - optional minus sign followed by one or more digits
        // (?![\.eE\d]) - NOT followed by decimal point, scientific notation, or more digits
        // (?=\s*[\]\},]) - followed by optional whitespace then ], }, or comma

        var pattern = @"(?<=[\[\{,:\s])-?\d+(?![\.eE\d])(?=\s*[\]\},\s])";
        var result = System.Text.RegularExpressions.Regex.Replace(json, pattern, match => match.Value + ".0");
        return result;
    }

    /// <summary>
    /// Converts integer values to doubles in JSON by appending .0 to whole numbers
    /// This ensures that System.Text.Json deserializes them as doubles instead of ints
    /// </summary>
    private string ConvertIntegersToDoublesInJson(string json)
    {
        return ForceNumbersToDoubleFormat(json);
    }

    /// <summary>
    /// Sets effects (health or damage) on item properties
    /// Handles conversion from string keys to enum keys for SPT 4.0.4
    /// </summary>
    private void SetEffects(dynamic props, string propertyName, object effectsData, ModConfig config)
    {
        try
        {
            var propsType = props.GetType();
            var effectsProp = propsType.GetProperty(propertyName);

            if (effectsProp == null)
            {
                if (config.Debug)
                {
                    _logger.Warning($"[{ModShortName}] Property {propertyName} not found on item properties");
                }
                return;
            }

            // Get the existing effects dictionary from props
            dynamic existingEffects = effectsProp.GetValue(props);

            if (existingEffects == null)
            {
                if (config.Debug)
                {
                    _logger.Warning($"[{ModShortName}] {propertyName} is null on props");
                }
                return;
            }

            // Get the dictionary type and its key/value types
            var dictType = existingEffects.GetType();
            var genericArgs = dictType.GetGenericArguments();

            if (genericArgs.Length != 2)
            {
                _logger.Warning($"[{ModShortName}] {propertyName} is not a dictionary type");
                return;
            }

            var keyType = genericArgs[0];
            var valueType = genericArgs[1];

            if (config.RealDebug)
            {
                _logger.Info($"[{ModShortName}] {propertyName} key type: {keyType.Name}, value type: {valueType.Name}");
            }

            // Convert our Dictionary<string, EffectValue> or Dictionary<string, EffectDuration> to the target type
            if (effectsData is Dictionary<string, EffectValue> healthEffects)
            {
                foreach (var effect in healthEffects)
                {
                    try
                    {
                        // Try to convert string key to enum if keyType is an enum
                        object key;
                        if (keyType.IsEnum)
                        {
                            // Try to parse the string as an enum
                            key = Enum.Parse(keyType, effect.Key, ignoreCase: true);
                        }
                        else
                        {
                            key = effect.Key;
                        }

                        // Serialize the value and deserialize to the target type
                        var valueJson = JsonSerializer.Serialize(effect.Value);
                        var convertedValue = JsonSerializer.Deserialize(valueJson, valueType);

                        // Add to the dictionary using reflection
                        var addMethod = dictType.GetMethod("Add", new[] { keyType, valueType });
                        if (addMethod != null && convertedValue != null)
                        {
                            addMethod.Invoke(existingEffects, new[] { key, convertedValue });

                            if (config.RealDebug)
                            {
                                _logger.Info($"[{ModShortName}] Added effect {effect.Key} to {propertyName}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        if (config.Debug)
                        {
                            _logger.Warning($"[{ModShortName}] Failed to add effect {effect.Key} to {propertyName}: {ex.Message}");
                        }
                    }
                }
            }
            else if (effectsData is Dictionary<string, EffectDuration> damageEffects)
            {
                foreach (var effect in damageEffects)
                {
                    try
                    {
                        // Try to convert string key to enum if keyType is an enum
                        object key;
                        if (keyType.IsEnum)
                        {
                            key = Enum.Parse(keyType, effect.Key, ignoreCase: true);
                        }
                        else
                        {
                            key = effect.Key;
                        }

                        // Serialize the value and deserialize to the target type
                        var valueJson = JsonSerializer.Serialize(effect.Value);
                        var convertedValue = JsonSerializer.Deserialize(valueJson, valueType);

                        // Add to the dictionary using reflection
                        var addMethod = dictType.GetMethod("Add", new[] { keyType, valueType });
                        if (addMethod != null && convertedValue != null)
                        {
                            addMethod.Invoke(existingEffects, new[] { key, convertedValue });

                            if (config.RealDebug)
                            {
                                _logger.Info($"[{ModShortName}] Added effect {effect.Key} to {propertyName}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        if (config.Debug)
                        {
                            _logger.Warning($"[{ModShortName}] Failed to add effect {effect.Key} to {propertyName}: {ex.Message}");
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Warning($"[{ModShortName}] Error setting {propertyName}: {ex.Message}");
            if (config.RealDebug)
            {
                _logger.Warning($"[{ModShortName}] Stack trace: {ex.StackTrace}");
            }
        }
    }

    private void RemoveNullProperties(JsonNode node)
    {
        if (node is JsonObject obj)
        {
            var keysToRemove = new List<string>();
            foreach (var kvp in obj)
            {
                if (kvp.Value == null)
                {
                    keysToRemove.Add(kvp.Key);
                }
                else if (kvp.Value is JsonObject || kvp.Value is JsonArray)
                {
                    RemoveNullProperties(kvp.Value);
                }
            }
            foreach (var key in keysToRemove)
            {
                obj.Remove(key);
            }
        }
        else if (node is JsonArray array)
        {
            foreach (var item in array)
            {
                if (item != null)
                {
                    RemoveNullProperties(item);
                }
            }
        }
    }
}
