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

        // Get database tables - SPT 4.0.4 API
        // DatabaseService doesn't have direct getters in 4.0.4
        // We need to access via reflection or use a different service
        var databaseServiceType = _databaseService.GetType();
        var getItemsMethod = databaseServiceType.GetMethod("GetItems");
        var getHandbookMethod = databaseServiceType.GetMethod("GetHandbook");
        var getPricesMethod = databaseServiceType.GetMethod("GetPrices");
        var getQuestsMethod = databaseServiceType.GetMethod("GetQuests");
        var getTradersMethod = databaseServiceType.GetMethod("GetTraders");
        var getLocationsMethod = databaseServiceType.GetMethod("GetLocations");
        var getGlobalsMethod = databaseServiceType.GetMethod("GetGlobals");

        if (getItemsMethod == null)
        {
            _logger.Error($"[{ModShortName}] DatabaseService.GetItems() method not found! Available methods:");
            foreach (var method in databaseServiceType.GetMethods().Take(10))
            {
                _logger.Error($"[{ModShortName}]   - {method.Name}");
            }
            throw new Exception("DatabaseService API mismatch - GetItems() not found");
        }

        dynamic itemDb = getItemsMethod.Invoke(_databaseService, null);
        dynamic handbook = getHandbookMethod?.Invoke(_databaseService, null);
        dynamic fleaPriceTable = getPricesMethod?.Invoke(_databaseService, null);
        dynamic quests = getQuestsMethod?.Invoke(_databaseService, null);
        dynamic traders = getTradersMethod?.Invoke(_databaseService, null);
        dynamic locations = getLocationsMethod?.Invoke(_databaseService, null);
        dynamic globals = getGlobalsMethod?.Invoke(_databaseService, null);

        if (config.Debug)
        {
            _logger.Info($"[{ModShortName}] Database tables retrieved successfully");
        }

        // Traverse the items directory and process consumables
        var itemsPath = Path.Combine(pathToMod, "items");
        if (Directory.Exists(itemsPath))
        {
            _logger.Info($"[{ModShortName}] Processing items from: {itemsPath}");
            TraverseDirectory(itemsPath, config, itemDb, handbook, fleaPriceTable, quests, traders, locations, globals);
        }
        else
        {
            _logger.Warning($"[{ModShortName}] Items directory not found: {itemsPath}");
        }

        // TEMPORARILY DISABLED - Testing if WTT is causing item conflicts
        // Use WTT library to add hideout craft recipes (from db/CustomHideoutRecipes)
        //try
        //{
        //    _logger.Info($"[{ModShortName}] Adding hideout craft recipes...");
        //    await _wttCommon.CustomHideoutRecipeService.CreateHideoutRecipes(Assembly.GetExecutingAssembly());
        //    _logger.Success($"[{ModShortName}] Hideout craft recipes added successfully!");
        //}
        //catch (Exception ex)
        //{
        //    _logger.Error($"[{ModShortName}] Failed to add hideout craft recipes: {ex.Message}");
        //    if (config.RealDebug)
        //    {
        //        _logger.Error($"[{ModShortName}] Stack trace: {ex.StackTrace}");
        //    }
        //}

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
        // TEMPORARILY DISABLED FOR TESTING
        //if (consumableFile.Trader != null)
        //{
        //    AddToTrader(newConsumableId, consumableFile.Trader, traders);
        //}

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

            // NOTE: We do NOT set a custom Name (_name) property on cloned items
            // The client-side game code uses _name to determine the C# class type
            // Custom items must keep the original _name from their clone origin
            // so the game knows what C# class to use (e.g., all stims keep their parent's _name)

            // Verify and fix the Name and Parent fields
            var nameField = itemType.GetProperty("Name");
            var parentField = itemType.GetProperty("Parent");

            // The Parent field gets corrupted during JSON serialization - we need to restore it
            if (parentField != null)
            {
                var originalParent = parentField.GetValue(originalItem);
                parentField.SetValue(clonedItem, originalParent);

                if (config.Debug)
                {
                    var currentName = nameField?.GetValue(clonedItem);
                    var currentParent = parentField.GetValue(clonedItem);
                    _logger.Info($"[{ModShortName}] Item Name (_name) after clone: {currentName}");
                    _logger.Info($"[{ModShortName}] Item Parent (_parent) after restore: {currentParent}");
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
                                            // SPT 4.0.4: ItemDistribution needs proper type instantiation
                                            // Get the element type from the list
                                            var itemDistributionType = itemDistribution.GetType();
                                            Type? elementType = null;

                                            if (itemDistributionType.IsGenericType)
                                            {
                                                var genericArgs = itemDistributionType.GetGenericArguments();
                                                if (genericArgs.Length > 0)
                                                {
                                                    elementType = genericArgs[0];
                                                }
                                            }

                                            if (elementType != null)
                                            {
                                                // Create new instance of ItemDistribution
                                                var newEntry = Activator.CreateInstance(elementType);

                                                if (newEntry != null)
                                                {
                                                    // Set the Tpl property (MongoId)
                                                    var tplProp = elementType.GetProperty("Tpl");
                                                    if (tplProp != null)
                                                    {
                                                        var tplType = tplProp.PropertyType;
                                                        // Create MongoId from string
                                                        var mongoIdConstructor = tplType.GetConstructor(new[] { typeof(string) });
                                                        if (mongoIdConstructor != null)
                                                        {
                                                            var tplValue = mongoIdConstructor.Invoke(new object[] { newConsumableId });
                                                            tplProp.SetValue(newEntry, tplValue);
                                                        }
                                                    }

                                                    // Set the RelativeProbability property (needs to be float?)
                                                    var relProbProp = elementType.GetProperty("RelativeProbability");
                                                    if (relProbProp != null)
                                                    {
                                                        // Convert int to float for RelativeProbability
                                                        relProbProp.SetValue(newEntry, (float)spawnRelativeProbability);
                                                    }

                                                    // Add to the list
                                                    var addMethod = itemDistributionType.GetMethod("Add");
                                                    if (addMethod != null)
                                                    {
                                                        addMethod.Invoke(itemDistribution, new[] { newEntry });
                                                    }
                                                }
                                            }
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
                    // Get the item type from the list
                    var itemsType = items.GetType();
                    Type? itemType = null;

                    if (itemsType.IsGenericType)
                    {
                        var genericArgs = itemsType.GetGenericArguments();
                        if (genericArgs.Length > 0)
                        {
                            itemType = genericArgs[0];
                        }
                    }

                    if (itemType != null)
                    {
                        var newItem = Activator.CreateInstance(itemType);

                        if (newItem != null)
                        {
                            // Set _id (MongoId)
                            var idProp = itemType.GetProperty("Id");
                            if (idProp != null)
                            {
                                var idType = idProp.PropertyType;
                                var mongoIdConstructor = idType.GetConstructor(new[] { typeof(string) });
                                if (mongoIdConstructor != null)
                                {
                                    var idValue = mongoIdConstructor.Invoke(new object[] { newConsumableId });
                                    idProp.SetValue(newItem, idValue);
                                }
                            }

                            // Set Template (MongoId) - SPT 4.0.4: Property is "Template" not "Tpl"
                            var templateProp = itemType.GetProperty("Template");
                            if (templateProp != null)
                            {
                                var templateType = templateProp.PropertyType;
                                var mongoIdConstructor = templateType.GetConstructor(new[] { typeof(string) });
                                if (mongoIdConstructor != null)
                                {
                                    var templateValue = mongoIdConstructor.Invoke(new object[] { newConsumableId });
                                    templateProp.SetValue(newItem, templateValue);
                                }
                            }

                            // Set parentId and slotId
                            var parentIdProp = itemType.GetProperty("ParentId");
                            if (parentIdProp != null)
                            {
                                parentIdProp.SetValue(newItem, "hideout");
                            }

                            var slotIdProp = itemType.GetProperty("SlotId");
                            if (slotIdProp != null)
                            {
                                slotIdProp.SetValue(newItem, "hideout");
                            }

                            // Set upd.UnlimitedCount and upd.StackObjectsCount
                            var updProp = itemType.GetProperty("Upd");
                            if (updProp != null)
                            {
                                var updValue = updProp.GetValue(newItem);
                                if (updValue != null)
                                {
                                    var updType = updValue.GetType();

                                    var unlimitedCountProp = updType.GetProperty("UnlimitedCount");
                                    if (unlimitedCountProp != null)
                                    {
                                        unlimitedCountProp.SetValue(updValue, false);
                                    }

                                    var stackCountProp = updType.GetProperty("StackObjectsCount");
                                    if (stackCountProp != null)
                                    {
                                        stackCountProp.SetValue(updValue, traderData.AmountForSale);
                                    }
                                }
                            }

                            // Add to list using reflection to avoid dynamic type issues
                            var addMethod = itemsType.GetMethod("Add");
                            if (addMethod != null)
                            {
                                addMethod.Invoke(items, new[] { newItem });
                                _logger.Info($"[{ModShortName}] Added {newConsumableId} to trader {traderData.TraderId}");
                            }
                        }
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
                    // Get the value type from the dictionary
                    var barterSchemeType = barterScheme.GetType();
                    Type? valueType = null;

                    if (barterSchemeType.IsGenericType)
                    {
                        var genericArgs = barterSchemeType.GetGenericArguments();
                        if (genericArgs.Length >= 2)
                        {
                            valueType = genericArgs[1]; // Dictionary<TKey, TValue> - get TValue
                        }
                    }

                    if (valueType != null && valueType.IsGenericType)
                    {
                        // valueType is List<List<BarterScheme>>
                        // Get the inner list type: List<BarterScheme>
                        var outerListElementType = valueType.GetGenericArguments()[0]; // List<BarterScheme>

                        if (outerListElementType.IsGenericType)
                        {
                            var barterSchemeElementType = outerListElementType.GetGenericArguments()[0]; // BarterScheme

                            // Create List<List<BarterScheme>>
                            var outerListType = typeof(List<>).MakeGenericType(outerListElementType);
                            var outerList = Activator.CreateInstance(outerListType);

                            // Create List<BarterScheme>
                            var innerListType = typeof(List<>).MakeGenericType(barterSchemeElementType);
                            var innerList = Activator.CreateInstance(innerListType);

                            if (outerList != null && innerList != null)
                            {
                                // Create BarterScheme object
                                var barterSchemeObj = Activator.CreateInstance(barterSchemeElementType);

                                if (barterSchemeObj != null)
                                {
                                    // Set count property (needs to be double?)
                                    var countProp = barterSchemeElementType.GetProperty("Count");
                                    if (countProp != null)
                                    {
                                        // Convert int to double for Count
                                        countProp.SetValue(barterSchemeObj, (double)traderData.Price);
                                    }

                                    // Set Tpl property (MongoId for roubles)
                                    var tplProp = barterSchemeElementType.GetProperty("Tpl");
                                    if (tplProp != null)
                                    {
                                        var tplType = tplProp.PropertyType;
                                        var mongoIdConstructor = tplType.GetConstructor(new[] { typeof(string) });
                                        if (mongoIdConstructor != null)
                                        {
                                            var tplValue = mongoIdConstructor.Invoke(new object[] { "5449016a4bdc2d6f028b456f" }); // Roubles
                                            tplProp.SetValue(barterSchemeObj, tplValue);
                                        }
                                    }

                                    // Add to inner list
                                    var innerAddMethod = innerListType.GetMethod("Add");
                                    if (innerAddMethod != null)
                                    {
                                        innerAddMethod.Invoke(innerList, new[] { barterSchemeObj });
                                    }

                                    // Add inner list to outer list
                                    var outerAddMethod = outerListType.GetMethod("Add");
                                    if (outerAddMethod != null)
                                    {
                                        outerAddMethod.Invoke(outerList, new[] { innerList });
                                    }

                                    // Add to dictionary using reflection
                                    // Create MongoId key
                                    var keyType = barterSchemeType.GetGenericArguments()[0];
                                    var keyConstructor = keyType.GetConstructor(new[] { typeof(string) });
                                    if (keyConstructor != null)
                                    {
                                        var key = keyConstructor.Invoke(new object[] { newConsumableId });

                                        // Use reflection to set dictionary item to avoid dynamic type issues
                                        var indexerProperty = barterSchemeType.GetProperty("Item");
                                        if (indexerProperty != null)
                                        {
                                            indexerProperty.SetValue(barterScheme, outerList, new[] { key });
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.Warning($"[{ModShortName}] Failed to add barter scheme for {newConsumableId}: {ex.Message}");
                }
            }

            // Add loyalty level requirement
            // SPT 4.0.4: Property is called "LoyalLevelItems" not "LoyaltyLevelItems"
            dynamic loyaltyLevelItems = null;
            try
            {
                var assortType = assort.GetType();
                var loyaltyProp = assortType.GetProperty("LoyalLevelItems");
                if (loyaltyProp != null)
                {
                    loyaltyLevelItems = loyaltyProp.GetValue(assort);
                }
            }
            catch (Exception ex)
            {
                _logger.Warning($"[{ModShortName}] Exception getting LoyalLevelItems: {ex.Message}");
            }

            if (loyaltyLevelItems != null)
            {
                try
                {
                    // Use reflection to set dictionary item with MongoId key
                    var loyaltyDictType = loyaltyLevelItems.GetType();

                    // Get the key type (should be MongoId)
                    Type? keyType = null;
                    if (loyaltyDictType.IsGenericType)
                    {
                        var genericArgs = loyaltyDictType.GetGenericArguments();
                        if (genericArgs.Length > 0)
                        {
                            keyType = genericArgs[0];
                        }
                    }

                    if (keyType != null)
                    {
                        // Create MongoId key from string
                        var mongoIdConstructor = keyType.GetConstructor(new[] { typeof(string) });
                        if (mongoIdConstructor != null)
                        {
                            var key = mongoIdConstructor.Invoke(new object[] { newConsumableId });

                            // Set dictionary value using indexer property
                            var indexerProperty = loyaltyDictType.GetProperty("Item");
                            if (indexerProperty != null)
                            {
                                indexerProperty.SetValue(loyaltyLevelItems, traderData.LoyaltyReq, new[] { key });
                            }
                        }
                    }
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

            // Get the property type to determine dictionary type
            var dictType = effectsProp.PropertyType;

            // If the dictionary is null, create a new instance
            if (existingEffects == null)
            {
                if (config.Debug)
                {
                    _logger.Warning($"[{ModShortName}] {propertyName} is null on props, creating new instance");
                }

                // Create a new dictionary instance of the correct type
                existingEffects = Activator.CreateInstance(dictType);
                effectsProp.SetValue(props, existingEffects);
            }
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

                        // Use dictionary indexer to set/update value (avoids "key already exists" error)
                        if (convertedValue != null)
                        {
                            var indexerProperty = dictType.GetProperty("Item");
                            if (indexerProperty != null)
                            {
                                indexerProperty.SetValue(existingEffects, convertedValue, new[] { key });

                                if (config.RealDebug)
                                {
                                    _logger.Info($"[{ModShortName}] Set effect {effect.Key} in {propertyName}");
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        if (config.Debug)
                        {
                            var innerMsg = ex.InnerException != null ? $" Inner: {ex.InnerException.Message}" : "";
                            _logger.Warning($"[{ModShortName}] Failed to add effect {effect.Key} to {propertyName}: {ex.Message}{innerMsg}");
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

                        // Use dictionary indexer to set/update value (avoids "key already exists" error)
                        if (convertedValue != null)
                        {
                            var indexerProperty = dictType.GetProperty("Item");
                            if (indexerProperty != null)
                            {
                                indexerProperty.SetValue(existingEffects, convertedValue, new[] { key });

                                if (config.RealDebug)
                                {
                                    _logger.Info($"[{ModShortName}] Set effect {effect.Key} in {propertyName}");
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        if (config.Debug)
                        {
                            var innerMsg = ex.InnerException != null ? $" Inner: {ex.InnerException.Message}" : "";
                            _logger.Warning($"[{ModShortName}] Failed to add effect {effect.Key} to {propertyName}: {ex.Message}{innerMsg}");
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
