using System.Diagnostics;
using System.Globalization;
using System.Threading.Tasks.Dataflow;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using LsifDotnet.Lsif;
using LsifDotnet.Roslyn;
using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using IHost = Microsoft.Extensions.Hosting.IHost;

namespace LsifDotnet.Benchmark;

[MemoryDiagnoser]
[ThreadingDiagnoser]
[SimpleJob(RunStrategy.Monitoring)]
public class SelfEmit
{
    private IHost? _host;

    public FileInfo SolutionFileInfo => new FileInfo(@"F:\Code\Science\LsifDotnet\LsifDotnet.sln");

    [GlobalSetup]
    public void SetupMSBuild()
    {
        MSBuildLocator.RegisterDefaults();
    }

    [IterationSetup]
    public void BuildHost()
    {
        _host = Host.CreateDefaultBuilder()
            .ConfigureLogging((_, builder) => builder.ClearProviders())
            .ConfigureServices((_, collection) => collection.AddSingleton(_ => CreateWorkspace())
                .AddSingleton<IdentifierCollectorFactory>()
                .AddTransient<LegacyLsifIndexer>()
                .AddTransient<DataFlowLsifIndexer>()
                .AddTransient(services => (Workspace)services.GetRequiredService<MSBuildWorkspace>()))
            .Build();
    }


    private static MSBuildWorkspace CreateWorkspace()
    {
        var workspace = MSBuildWorkspace.Create();
        return workspace;
    }

    [Benchmark]
    public async Task AsyncEnumerable()
    {
        const string fileName =
            $@"F:\Code\Science\LsifDotnet\LsifDotnet.Benchmark\bin\Release\test\{nameof(AsyncEnumerable)}.lsif";
        await IndexHandler.Process(_host!, SolutionFileInfo, new FileInfo(fileName),
            CultureInfo.CurrentUICulture, false, false, true, true, 4, 0);
    }

    [Benchmark]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(4)]
    [Arguments(8)]
    public async Task DataFlow(int maxDegreeOfParallelism)
    {
        var indexer = _host!.Services.GetRequiredService<DataFlowLsifIndexer>();
        var logger = _host.Services.GetRequiredService<ILogger<SelfEmit>>();

        var stopwatch = Stopwatch.StartNew();
        var defaultCulture = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.CurrentUICulture;

        var items = indexer.BuildLsifEmitGraph(maxDegreeOfParallelism);

        const string path =
            $@"F:\Code\Science\LsifDotnet\LsifDotnet.Benchmark\bin\Release\test\{nameof(DataFlow)}.lsif";
        var writer = new StreamWriter(path);
        var count = 0;
        var saveLsifBlock = new ActionBlock<LsifItem>(async item =>
        {
            var json = item.ToJson();
            await writer.WriteLineAsync(json);
            Interlocked.Increment(ref count);
        });

        items.LinkTo(saveLsifBlock, new DataflowLinkOptions { PropagateCompletion = true });

        var solution = await _host.Services.GetRequiredService<MSBuildWorkspace>()
            .OpenSolutionAsync(SolutionFileInfo.FullName);
        await items.SendAsync(solution);
        items.Complete();

        await saveLsifBlock.Completion;
        await writer.DisposeAsync();

        stopwatch.Stop();
        logger.LogInformation("Totally emitted {count} items in {time}", indexer.EmittedItem, stopwatch.Elapsed);
        CultureInfo.CurrentUICulture = defaultCulture;

        Trace.Assert(count == indexer.EmittedItem);
    }


    [Benchmark]
    [Arguments(1)]
    [Arguments(2)]
    [Arguments(4)]
    [Arguments(8)]
    public async Task DataFlowWithAsyncIO(int maxDegreeOfParallelism)
    {
        var indexer = _host!.Services.GetRequiredService<DataFlowLsifIndexer>();
        var logger = _host.Services.GetRequiredService<ILogger<SelfEmit>>();

        var stopwatch = Stopwatch.StartNew();
        var defaultCulture = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.CurrentUICulture;

        var items = indexer.BuildLsifEmitGraph(maxDegreeOfParallelism);

        const string path =
            $@"F:\Code\Science\LsifDotnet\LsifDotnet.Benchmark\bin\Release\test\{nameof(DataFlowWithAsyncIO)}.lsif";
        var writer = File.Create(path);
        var count = 0;
        var saveLsifBlock = new ActionBlock<LsifItem>(async item =>
        {
            await item.ToJsonAsync(writer);
            writer.WriteByte((byte)'\n');
            Interlocked.Increment(ref count);
        });

        items.LinkTo(saveLsifBlock, new DataflowLinkOptions { PropagateCompletion = true });

        var solution = await _host.Services.GetRequiredService<MSBuildWorkspace>()
            .OpenSolutionAsync(SolutionFileInfo.FullName);
        await items.SendAsync(solution);
        items.Complete();

        await saveLsifBlock.Completion;
        await writer.DisposeAsync();

        stopwatch.Stop();
        logger.LogInformation("Totally emitted {count} items in {time}", indexer.EmittedItem, stopwatch.Elapsed);
        CultureInfo.CurrentUICulture = defaultCulture;

        Trace.Assert(count == indexer.EmittedItem);
    }
}