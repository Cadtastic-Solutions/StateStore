using BenchmarkDotNet.Running;

namespace StateStore.Benchmarks;

internal class Program
{
    static void Main(string[] args)
    {
        var arg = args;
        var _ = BenchmarkRunner.Run(typeof(Program).Assembly);
    }
}
