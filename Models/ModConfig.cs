using System.Text.Json.Serialization;

namespace ConsumablesGalore.Models;

public class ModConfig
{
    [JsonPropertyName("debug")]
    public bool Debug { get; set; } = false;

    [JsonPropertyName("realDebug")]
    public bool RealDebug { get; set; } = false;
}
