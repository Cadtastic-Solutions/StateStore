# Serialization

Serialization converts typed objects to raw bytes for storage and back again during retrieval. StateStore provides a pluggable serialization layer with a JSON default, allowing you to use any format that suits your needs.

## The IStateSerializer Interface

```csharp
public interface IStateSerializer
{
    byte[] Serialize<T>(T value);
    T? Deserialize<T>(ReadOnlySpan<byte> data);
}
```

This interface is intentionally simple: two methods, no configuration surface. All configuration lives in the concrete implementation.

### Contract

- `Serialize<T>` converts a value to bytes. Throws `StateSerializationException` on failure.
- `Deserialize<T>` converts bytes back to a typed value. Throws `StateSerializationException` on failure. Returns `default` if deserialization produces `null`.

## The StoredState Envelope

Before your value reaches the serializer, StateStore wraps it in an internal `StoredState<T>` envelope:

```csharp
// When you call:
await store.SetAsync("key", myValue);

// StateStore internally serializes:
StoredState<T>
{
    Value = myValue,
    CreatedAt = DateTimeOffset.UtcNow,
    UpdatedAt = DateTimeOffset.UtcNow,
    Version = 1
}
```

This means the serializer always operates on `StoredState<T>`, not directly on your type `T`. The envelope is transparent to consumers, but it's important to understand when inspecting persisted data or implementing a custom serializer.

A file on disk looks like:

```json
{
  "value": {
    "theme": "dark",
    "fontSize": 16
  },
  "createdAt": "2026-01-15T10:30:00+00:00",
  "updatedAt": "2026-01-15T14:22:00+00:00",
  "version": 1
}
```

## JsonStateSerializer

The built-in serializer uses `System.Text.Json`. It is registered by default when you call `AddStateStore()` or `StateStoreBuilder.UseJsonSerializer()`.

### Default Settings

| Setting | Default Value | Effect |
|---------|--------------|--------|
| `PropertyNamingPolicy` | `JsonNamingPolicy.CamelCase` | JSON properties use camelCase |
| `WriteIndented` | `false` | Compact output, smaller files |
| `DefaultIgnoreCondition` | `JsonIgnoreCondition.WhenWritingNull` | Null properties are omitted from output |

### Customizing the Serializer

#### Via DI

```csharp
services.AddStateStore(options =>
{
    options.UseJsonSerializer(json =>
    {
        json.WriteIndented = true;                    // Pretty-print for debugging
        json.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower; // snake_case
        json.DefaultIgnoreCondition = JsonIgnoreCondition.Never;     // Include nulls
    });
});
```

#### Via Builder

```csharp
var store = new StateStoreBuilder()
    .UseInMemory()
    .UseJsonSerializer(json =>
    {
        json.WriteIndented = true;
    })
    .Build();
```

### JsonStateSerializerOptions

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `WriteIndented` | `bool` | `false` | Whether to pretty-print JSON output |
| `PropertyNamingPolicy` | `JsonNamingPolicy` | `CamelCase` | Naming policy for JSON properties |
| `DefaultIgnoreCondition` | `JsonIgnoreCondition` | `WhenWritingNull` | When to ignore properties during serialization |
| `CustomSerializerOptions` | `JsonSerializerOptions?` | `null` | Complete override; when set, all other properties are ignored |

### Using Custom JsonSerializerOptions

When you need full control over `System.Text.Json` configuration, provide a complete `JsonSerializerOptions` instance:

```csharp
var customOptions = new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    WriteIndented = false,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    Converters = { new JsonStringEnumConverter() },
    NumberHandling = JsonNumberHandling.AllowReadingFromString
};

services.AddStateStore(options =>
{
    options.UseJsonSerializer(json =>
    {
        json.CustomSerializerOptions = customOptions;
    });
});
```

When `CustomSerializerOptions` is set, the `WriteIndented`, `PropertyNamingPolicy`, and `DefaultIgnoreCondition` properties on `JsonStateSerializerOptions` are ignored.

## Implementing a Custom Serializer

To use a different serialization format, implement `IStateSerializer`:

```csharp
public sealed class MessagePackSerializer : IStateSerializer
{
    public byte[] Serialize<T>(T value)
    {
        try
        {
            return MessagePack.MessagePackSerializer.Serialize(value);
        }
        catch (Exception ex)
        {
            throw new StateSerializationException(
                $"Failed to serialize {typeof(T).FullName}",
                typeof(T),
                ex);
        }
    }

    public T? Deserialize<T>(ReadOnlySpan<byte> data)
    {
        try
        {
            return MessagePack.MessagePackSerializer.Deserialize<T>(data.ToArray());
        }
        catch (Exception ex)
        {
            throw new StateSerializationException(
                $"Failed to deserialize {typeof(T).FullName}",
                typeof(T),
                ex);
        }
    }
}
```

### Registration

```csharp
// DI: Register before AddStateStore
services.AddSingleton<IStateSerializer, MessagePackSerializer>();
services.AddStateStore(); // TryAddSingleton won't override your registration

// Builder
var store = new StateStoreBuilder()
    .UseSerializer(new MessagePackSerializer())
    .UseInMemory()
    .Build();
```

### Important: Wrap Exceptions

Custom serializers should catch all non-StateStore exceptions and wrap them in `StateSerializationException`. This ensures the exception hierarchy is consistent for consumers and that the exception carries the `TargetType` context property.

## AOT Considerations

`JsonStateSerializer` uses reflection-based `System.Text.Json`, which is annotated with `[RequiresUnreferencedCode]` and `[RequiresDynamicCode]`. In AOT (Ahead-of-Time) compiled scenarios, you should provide a custom serializer using source-generated `JsonSerializerContext`:

```csharp
[JsonSerializable(typeof(StoredState<AppSettings>))]
[JsonSerializable(typeof(StoredState<UserPreferences>))]
public partial class AppJsonContext : JsonSerializerContext { }

public sealed class AotJsonSerializer : IStateSerializer
{
    private readonly JsonSerializerOptions _options;

    public AotJsonSerializer()
    {
        _options = new JsonSerializerOptions
        {
            TypeInfoResolver = AppJsonContext.Default
        };
    }

    public byte[] Serialize<T>(T value)
    {
        return JsonSerializer.SerializeToUtf8Bytes(value, _options);
    }

    public T? Deserialize<T>(ReadOnlySpan<byte> data)
    {
        return JsonSerializer.Deserialize<T>(data, _options);
    }
}
```

Note: You must include `StoredState<T>` for each `T` you plan to store, because the internal envelope is what actually gets serialized.

## Related Guides

- [Core Concepts](02-Core-Concepts.md) - Where serialization fits in the pipeline
- [Error Handling](12-Error-Handling.md) - `StateSerializationException` details
- [Extensibility](14-Extensibility.md) - Building custom serializers
- [Storage Providers](05-Storage-Providers.md) - What happens after serialization
