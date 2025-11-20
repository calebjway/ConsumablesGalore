using System.Text.Json.Serialization;

namespace ConsumablesGalore.Models;

public class ConsumableItemData
{
    [JsonPropertyName("cloneOrigin")]
    public string CloneOrigin { get; set; } = string.Empty;

    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("fleaPrice")]
    public object? FleaPrice { get; set; }

    [JsonPropertyName("handBookPrice")]
    public object? HandBookPrice { get; set; }

    [JsonPropertyName("includeInSameQuestsAsOrigin")]
    public bool IncludeInSameQuestsAsOrigin { get; set; }

    [JsonPropertyName("addSpawnsInSamePlacesAsOrigin")]
    public bool AddSpawnsInSamePlacesAsOrigin { get; set; }

    [JsonPropertyName("spawnWeightComparedToOrigin")]
    public double SpawnWeightComparedToOrigin { get; set; } = 1.0;

    [JsonPropertyName("MaxResource")]
    public int? MaxResource { get; set; }

    [JsonPropertyName("hpResourceRate")]
    public int? HpResourceRate { get; set; }

    [JsonPropertyName("BackgroundColor")]
    public string? BackgroundColor { get; set; }

    [JsonPropertyName("effects_health")]
    public Dictionary<string, EffectValue>? EffectsHealth { get; set; }

    [JsonPropertyName("effects_damage")]
    public Dictionary<string, EffectDuration>? EffectsDamage { get; set; }

    [JsonPropertyName("medUseTime")]
    public int? MedUseTime { get; set; }

    [JsonPropertyName("Prefab")]
    public Dictionary<string, string>? Prefab { get; set; }

    [JsonPropertyName("UsePrefab")]
    public Dictionary<string, string>? UsePrefab { get; set; }

    [JsonPropertyName("ItemSound")]
    public string? ItemSound { get; set; }

    [JsonPropertyName("Buffs")]
    public List<BuffData>? Buffs { get; set; }

    [JsonPropertyName("locales")]
    public Dictionary<string, LocaleData>? Locales { get; set; }

    [JsonPropertyName("trader")]
    public TraderData? Trader { get; set; }

    [JsonPropertyName("craft")]
    public object? Craft { get; set; }
}

public class EffectValue
{
    [JsonPropertyName("value")]
    public double Value { get; set; }
}

public class EffectDuration
{
    [JsonPropertyName("delay")]
    public int Delay { get; set; }

    [JsonPropertyName("duration")]
    public int Duration { get; set; }

    [JsonPropertyName("fadeOut")]
    public int FadeOut { get; set; }
}

public class BuffData
{
    [JsonPropertyName("AbsoluteValue")]
    public bool AbsoluteValue { get; set; }

    [JsonPropertyName("BuffType")]
    public string BuffType { get; set; } = string.Empty;

    [JsonPropertyName("Chance")]
    public double Chance { get; set; }

    [JsonPropertyName("Delay")]
    public int Delay { get; set; }

    [JsonPropertyName("Duration")]
    public int Duration { get; set; }

    [JsonPropertyName("SkillName")]
    public string SkillName { get; set; } = string.Empty;

    [JsonPropertyName("Value")]
    public double Value { get; set; }
}

public class LocaleData
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("shortName")]
    public string ShortName { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;
}

public class TraderData
{
    [JsonPropertyName("traderId")]
    public string TraderId { get; set; } = string.Empty;

    [JsonPropertyName("loyaltyReq")]
    public int LoyaltyReq { get; set; }

    [JsonPropertyName("price")]
    public int Price { get; set; }

    [JsonPropertyName("amountForSale")]
    public int AmountForSale { get; set; }
}
