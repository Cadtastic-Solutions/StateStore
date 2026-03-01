using BenchmarkDotNet.Attributes;
using Microsoft.VSDiagnostics;
using StateStore.Serialization;

namespace StateStore.Benchmarks;

[CPUUsageDiagnoser]
public class JsonStateSerializerBenchmarks
{
    private readonly JsonStateSerializer _serializer = new();
    private readonly string _stringValue = "hello world";
    private readonly int _intValue = 123456;
    private readonly TestPayload _complexValue = new()
    {
        Id = 1,
        Name = "test",
        Tags = ["a", "b"]
    };
    [Benchmark]
    public byte[] Serialize_String() => _serializer.Serialize(_stringValue);
    [Benchmark]
    public byte[] Serialize_Int() => _serializer.Serialize(_intValue);
    [Benchmark]
    public byte[] Serialize_ComplexObject() => _serializer.Serialize(_complexValue);
    public sealed class TestPayload
    {
        public int Id { get; set; }
        public string? Name { get; set; }
        public List<string>? Tags { get; set; }
    }
}
