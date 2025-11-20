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
    public override Dictionary<string, SemanticVersioning.Range>? ModDependencies { get; init; }
    public override string? Url { get; init; } = "https://github.com/AlmightyTank/ConsumablesGalore";
    public override bool? IsBundleMod { get; init; } = false;
    public override string? License { get; init; } = "MIT";
}

[Injectable(TypePriority = OnLoadOrder.PostDBModLoader + 1)]
public class ConsumablesGaloreMain(
    ISptLogger<ConsumablesGaloreMain> logger,
    ModHelper modHelper,
    DatabaseService databaseService) : IOnLoad
{
    private const string ModShortName = "Consumables Galore";
    private readonly ISptLogger<ConsumablesGaloreMain> _logger = logger;
    private readonly ModHelper _modHelper = modHelper;
    private readonly DatabaseService _databaseService = databaseService;

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
            TraverseDirectory(itemsDirectory, config, itemDb, handbook, fleaPriceTable, quests, traders, production, locations, globals);
        }
        else
        {
            _logger.Warning($"[{ModShortName}] Items directory not found at: {itemsDirectory}");
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
        dynamic production,
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

                ProcessConsumableItem(consumableFile, config, itemDb, handbook, fleaPriceTable, quests, traders, production, locations, globals);
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
        dynamic production,
        dynamic locations,
        dynamic globals)
    {
        var originalConsumable = consumableFile.CloneOrigin;
        var newConsumableId = consumableFile.Id;

        // Find handbook parent ID
        string? handbookParentId = null;
        foreach (var item in handbook.Items)
        {
            if (item.Id == originalConsumable)
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
            if (item.Id == originalConsumable && consumableFile.HandBookPrice?.ToString() == "asOriginal")
            {
                handbookPrice = (double)item.Price;
                break;
            }
        }

        // Create the item clone
        try
        {
            CreateItemClone(originalConsumable, newConsumableId, consumableFile, itemDb, handbookParentId, fleaPrice, handbookPrice, handbook, fleaPriceTable);
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

        // Add craft
        // SPT 4.0.4: Temporarily disabled - requires SPT's JsonUtil for proper enum deserialization
        // System.Text.Json.JsonSerializer doesn't handle HideoutAreas enum conversion from integers
        // TODO: Inject and use SPT's JsonUtil service for craft deserialization
        if (consumableFile.Craft != null && config.Debug)
        {
            _logger.Warning($"[{ModShortName}] Craft recipes not yet fully supported in SPT 4.0.4 - requires SPT JsonUtil service");
        }
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
        dynamic fleaPriceTable)
    {
        // Get the original item - this is already a dynamic SPT TemplateItem
        dynamic originalItem = itemDb[originalConsumable];

        try
        {
            // Simply assign the original item to the new ID
            // SPT's itemDb accepts the same type that it returns
            _logger.Info($"[{ModShortName}] Assigning original item to new ID...");
            itemDb[newConsumableId] = originalItem;
            _logger.Info($"[{ModShortName}] Assignment successful");

            // Now modify the properties in-place
            _logger.Info($"[{ModShortName}] Modifying item properties...");

            // Inspect the item type to find the correct property names
            var itemType = originalItem.GetType();
            _logger.Info($"[{ModShortName}] Item type: {itemType.Name}");
            var properties = itemType.GetProperties();
            var propertyNames = new List<string>();
            foreach (var prop in properties)
            {
                propertyNames.Add(prop.Name);
            }
            _logger.Info($"[{ModShortName}] Available properties: {string.Join(", ", propertyNames)}");

            // SPT 4.0.4 uses Pascal case properties: Id, Name, Parent, Type, Properties
            // Modify Id using reflection since it's a MongoId type
            var idField = itemType.GetProperty("Id");
            if (idField != null)
            {
                // Create a new MongoId from the string
                var mongoIdType = idField.PropertyType;
                var mongoIdConstructor = mongoIdType.GetConstructor(new[] { typeof(string) });
                if (mongoIdConstructor != null)
                {
                    var newMongoId = mongoIdConstructor.Invoke(new object[] { newConsumableId });
                    idField.SetValue(originalItem, newMongoId);
                    _logger.Info($"[{ModShortName}] Set Id to {newConsumableId}");
                }
            }

            // Get Properties (was _props in SPT 3.x)
            var propsField = itemType.GetProperty("Properties");
            if (propsField == null)
            {
                throw new Exception($"Could not find Properties property on type {itemType.Name}");
            }

            // Get props using reflection
            dynamic props = propsField.GetValue(originalItem);
            if (props != null)
            {
                // Inspect properties on the Properties object
                var propsType = props.GetType();
                var propsProperties = propsType.GetProperties();
                var propsPropertyNames = new List<string>();
                foreach (var prop in propsProperties)
                {
                    propsPropertyNames.Add(prop.Name);
                }
                _logger.Info($"[{ModShortName}] Properties object has: {string.Join(", ", propsPropertyNames.Take(20))}...");

                props.StimulatorBuffs = newConsumableId;

                if (consumableFile.BackgroundColor != null)
                {
                    props.BackgroundColor = consumableFile.BackgroundColor;
                }

                // TODO: SPT 4.0.4 changed EffectsHealth and EffectsDamage to use enum keys instead of strings
                // This requires a complete restructure of how we define effects in the JSON files
                // For now, skip setting these to get basic items working
                if (consumableFile.EffectsHealth != null)
                {
                    _logger.Warning($"[{ModShortName}] EffectsHealth not yet supported in SPT 4.0.4 - requires data structure migration");
                }

                if (consumableFile.EffectsDamage != null)
                {
                    _logger.Warning($"[{ModShortName}] EffectsDamage not yet supported in SPT 4.0.4 - requires data structure migration");
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
            dynamic locales = _databaseService.GetLocales();
            foreach (var locale in consumableFile.Locales)
            {
                var lang = locale.Key;
                var localeData = locale.Value;

                try
                {
                    dynamic langLocale = locales[lang];
                    if (langLocale != null)
                    {
                        langLocale[$"{newConsumableId} Name"] = localeData.Name;
                        langLocale[$"{newConsumableId} ShortName"] = localeData.ShortName;
                        langLocale[$"{newConsumableId} Description"] = localeData.Description;
                    }
                }
                catch
                {
                    // Language locale doesn't exist, skip
                }
            }
        }
        }
        catch (Exception ex)
        {
            throw new Exception($"Error in CreateItemClone at specific step: {ex.Message}", ex);
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
        // SPT 4.0.4: Temporarily disabled - locations API structure needs investigation
        // TODO: Find correct property name for locations dictionary
        return;

        var lootComposedKey = newConsumableId + "_composedkey";
        var mapsList = new List<string> { "bigmap", "woods", "factory4_day", "factory4_night", "interchange", "laboratory", "lighthouse", "rezervbase", "shoreline", "tarkovstreets", "sandbox" };

        dynamic locationsDict = locations;
        foreach (var locationKvp in locationsDict)
        {
            var locationName = locationKvp.Key.ToString();
            var location = locationKvp.Value;

            if (!mapsList.Contains(locationName)) continue;

            // Process loose loot
            if (location.looseLoot?.spawnpoints != null)
            {
                foreach (var point in location.looseLoot.spawnpoints)
                {
                    if (point.template?.Items == null) continue;

                    foreach (var item in point.template.Items)
                    {
                        if (item._tpl?.ToString() == originalConsumable)
                        {
                            var originalItemId = item._id.ToString();
                            double? originRelativeProb = null;

                            if (point.itemDistribution != null)
                            {
                                foreach (var dist in point.itemDistribution)
                                {
                                    if (dist.composedKey?.key?.ToString() == originalItemId)
                                    {
                                        originRelativeProb = (double)dist.relativeProbability;

                                        // Add new item to template
                                        point.template.Items.Add(new
                                        {
                                            _id = lootComposedKey,
                                            _tpl = newConsumableId
                                        });
                                        break;
                                    }
                                }

                                if (originRelativeProb.HasValue)
                                {
                                    var newProbability = Math.Max((int)Math.Round(originRelativeProb.Value * spawnWeight), 1);
                                    point.itemDistribution.Add(new
                                    {
                                        composedKey = new { key = lootComposedKey },
                                        relativeProbability = newProbability
                                    });
                                }
                            }
                        }
                    }
                }
            }

            // Process static loot
            if (location.staticLoot != null)
            {
                foreach (var containerKvp in location.staticLoot)
                {
                    var containerName = containerKvp.Key;
                    var container = containerKvp.Value;

                    if (container.itemDistribution == null) continue;

                    for (int i = 0; i < container.itemDistribution.Count; i++)
                    {
                        var entry = container.itemDistribution[i];
                        if (entry.tpl?.ToString() == originalConsumable)
                        {
                            var originProbability = (double)entry.relativeProbability;
                            var spawnRelativeProbability = Math.Max((int)Math.Round(originProbability * spawnWeight), 1);

                            if (config.RealDebug)
                            {
                                _logger.Warning($"[{ModShortName}] Adding {newConsumableId} to container {containerName} with probability {spawnRelativeProbability}");
                            }

                            container.itemDistribution.Add(new
                            {
                                tpl = newConsumableId,
                                relativeProbability = spawnRelativeProbability
                            });
                            break;
                        }
                    }
                }
            }
        }
    }

    private void AddToTrader(string newConsumableId, TraderData traderData, dynamic traders)
    {
        // SPT 4.0.4: Temporarily disabled - Items.Add type checking is too strict
        // TODO: Find correct way to add items to trader assort
        return;

        var trader = traders[traderData.TraderId];

        if (trader == null)
        {
            _logger.Warning($"[{ModShortName}] Trader {traderData.TraderId} not found!");
            return;
        }

        // SPT 4.0.4: Assort (capital A) instead of assort
        // SPT 4.0.4: Items, BarterScheme, LoyaltyLevelItems (Pascal case)
        // Add item to assort - serialize to JSON then deserialize to bypass type checking
        dynamic assort = trader.Assort;
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
        assort.Items.Add(itemNode);

        // Add barter scheme (price)
        assort.BarterScheme[newConsumableId] = new[]
        {
            new[]
            {
                new
                {
                    count = traderData.Price,
                    _tpl = "5449016a4bdc2d6f028b456f" // Roubles
                }
            }
        };

        // Add loyalty level requirement
        assort.LoyaltyLevelItems[newConsumableId] = traderData.LoyaltyReq;
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
}
