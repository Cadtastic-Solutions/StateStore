using System.Text.Json;
using System.Text.Json.Serialization;

namespace StateStore.Serialization;

/// <summary>
/// Configuration options for <see cref="JsonStateSerializer"/>.
/// </summary>
public sealed class JsonStateSerializerOptions
{
    /// <summary>
    /// Gets or sets whether JSON output should be indented. Defaults to <c>false</c>.
    /// </summary>
    public bool WriteIndented { get; set; }

    /// <summary>
    /// Gets or sets the JSON property naming policy. Defaults to <see cref="JsonNamingPolicy.CamelCase"/>.
    /// </summary>
    public JsonNamingPolicy PropertyNamingPolicy { get; set; } = JsonNamingPolicy.CamelCase;

    /// <summary>
    /// Gets or sets the default ignore condition for null properties.
    /// Defaults to <see cref="JsonIgnoreCondition.WhenWritingNull"/>.
    /// </summary>
    public JsonIgnoreCondition DefaultIgnoreCondition { get; set; } = JsonIgnoreCondition.WhenWritingNull;

    /// <summary>
    /// Gets or sets custom <see cref="JsonSerializerOptions"/> to use instead of the built-in defaults.
    /// When set, all other properties on this class are ignored.
    /// </summary>
    public JsonSerializerOptions? CustomSerializerOptions { get; set; }

    /// <summary>
    /// Builds the effective <see cref="JsonSerializerOptions"/> based on this configuration.
    /// </summary>
    /// <returns>The configured serializer options.</returns>
    internal JsonSerializerOptions BuildSerializerOptions()
    {
        if (CustomSerializerOptions is not null)
        {
            return CustomSerializerOptions;
        }

        return new JsonSerializerOptions
        {
            WriteIndented = WriteIndented,
            PropertyNamingPolicy = PropertyNamingPolicy,
            DefaultIgnoreCondition = DefaultIgnoreCondition
        };
    }
}
