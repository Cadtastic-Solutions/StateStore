using StateStore.Exceptions;
using StateStore.Serialization;

namespace StateStore.Tests;

public sealed class JsonStateSerializerTests
{
    private readonly JsonStateSerializer _serializer = new();

    [Fact]
    public void Serialize_And_Deserialize_RoundTrips_String()
    {
        var bytes = _serializer.Serialize("hello");
        var result = _serializer.Deserialize<string>(bytes);
        Assert.Equal("hello", result);
    }

    [Fact]
    public void Serialize_And_Deserialize_RoundTrips_Int()
    {
        var bytes = _serializer.Serialize(42);
        var result = _serializer.Deserialize<int>(bytes);
        Assert.Equal(42, result);
    }

    [Fact]
    public void Serialize_And_Deserialize_RoundTrips_ComplexObject()
    {
        var original = new TestPayload { Id = 1, Name = "test", Tags = ["a", "b"] };
        var bytes = _serializer.Serialize(original);
        var result = _serializer.Deserialize<TestPayload>(bytes);
        Assert.NotNull(result);
        Assert.Equal(1, result.Id);
        Assert.Equal("test", result.Name);
        Assert.Equal(["a", "b"], result.Tags);
    }

    [Fact]
    public void Deserialize_ThrowsStateSerializationException_ForInvalidData()
    {
        var badData = "not json"u8.ToArray();
        Assert.Throws<StateSerializationException>(() =>
            _serializer.Deserialize<TestPayload>(badData));
    }

    [Fact]
    public void Deserialize_ThrowsStateSerializationException_WithTargetType()
    {
        var badData = "not json"u8.ToArray();
        var ex = Assert.Throws<StateSerializationException>(() =>
            _serializer.Deserialize<TestPayload>(badData));
        Assert.Equal(typeof(TestPayload), ex.TargetType);
    }

    [Fact]
    public void Serialize_WithCustomOptions_UsesIndented()
    {
        var serializer = new JsonStateSerializer(new JsonStateSerializerOptions { WriteIndented = true });
        var bytes = serializer.Serialize(new { Name = "test" });
        var json = System.Text.Encoding.UTF8.GetString(bytes);
        Assert.Contains("\n", json);
    }

    [Fact]
    public void Serialize_DefaultOptions_UsesCamelCase()
    {
        var bytes = _serializer.Serialize(new TestPayload { Id = 1, Name = "test" });
        var json = System.Text.Encoding.UTF8.GetString(bytes);
        Assert.Contains("\"id\"", json);
        Assert.Contains("\"name\"", json);
    }

    public sealed class TestPayload
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public List<string>? Tags { get; set; }
    }
}
