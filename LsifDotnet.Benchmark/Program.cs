using System.Diagnostics;
using BenchmarkDotNet.Running;

namespace LsifDotnet.Benchmark;

public class Program
{
    public static void Main(string[] args)
    {
        Trace.Listeners.Add(new ConsoleTraceListener());
        var summary = BenchmarkRunner.Run(typeof(Program).Assembly);
    }
}