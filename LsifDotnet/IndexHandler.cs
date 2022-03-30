using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using LsifDotnet.Lsif;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LsifDotnet;

public class IndexHandler
{
    public static async Task Process(IHost host, FileInfo? solutionFile, FileInfo output, CultureInfo culture, bool dot,
        bool svg, bool quiet, uint parallelism, uint index)
    {
        var logger = host.Services.GetRequiredService<ILogger<IndexHandler>>();
        ConfigLoggingLevel(host, quiet);
        PrintDescription(logger);

        var solutionFilePath = solutionFile?.FullName ?? FindSolutionFile();

        await host.Services.GetRequiredService<MSBuildWorkspace>().OpenSolutionAsync(solutionFilePath);

        var defaultCulture = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = culture;

        if (parallelism is  0)
            await LegacyLsifIndex(host, logger, output, dot, svg);
        else
            await DataFlowLsifIndex(host, logger, output, dot, svg, parallelism, index);

        CultureInfo.CurrentUICulture = defaultCulture;
    }

    private static async Task DataFlowLsifIndex(IHost host, ILogger logger, FileInfo output, bool dot,
        bool svg, uint parallelism, uint initId)
    {
        logger.LogInformation($"Using {nameof(DataFlowLsifIndexer)}");
        var indexer = host.Services.GetRequiredService<DataFlowLsifIndexer>();
        var stopwatch = Stopwatch.StartNew();

        var items = indexer.BuildLsifEmitGraph((int)parallelism, (int)initId);

        var writer = output.Create();
        var count = 0;
        var saveLsifBlock = new ActionBlock<LsifItem>(async item =>
        {
            await item.ToJsonAsync(writer);
            writer.WriteByte((byte)'\n');
            Interlocked.Increment(ref count);
        });
        var completion = saveLsifBlock.Completion;

        var graphBuilder = new GraphBuilder();
        if (dot || svg)
        {
            var graphBuildBlock = graphBuilder.BuildDataFlowBlock();
            items.LinkTo(graphBuildBlock);
            completion = graphBuildBlock.Completion;
        }

        items.LinkTo(saveLsifBlock, new DataflowLinkOptions { PropagateCompletion = true });

        var solution = host.Services.GetRequiredService<MSBuildWorkspace>().CurrentSolution;
        await items.SendAsync(solution);
        items.Complete();

        await completion;
        await writer.DisposeAsync();

        stopwatch.Stop();
        logger.LogInformation("Totally emitted {count} items in {time}", indexer.EmittedItem, stopwatch.Elapsed);

        if (dot) await graphBuilder.SaveDotAsync();
        if (svg) await graphBuilder.SaveSvgAsync();
    }

    private static async Task LegacyLsifIndex(IHost host, ILogger logger, FileInfo output, bool dot,
        bool svg)
    {
        logger.LogInformation($"Using {nameof(LegacyLsifIndexer)}");
        var indexer = host.Services.GetRequiredService<LegacyLsifIndexer>();

        var stopwatch = Stopwatch.StartNew();

        var items = indexer.EmitLsif();
        var graphBuilder = new GraphBuilder();
        if (dot || svg) items = graphBuilder.RecordLsifItem(items);
        await SaveLsifDump(items, output.FullName);

        stopwatch.Stop();
        logger.LogInformation("Totally emitted {count} items in {time}", indexer.EmittedItem, stopwatch.Elapsed);

        if (dot) await graphBuilder.SaveDotAsync();
        if (svg) await graphBuilder.SaveSvgAsync();
    }

    private static void ConfigLoggingLevel(IHost host, bool quiet)
    {
        if (!quiet) return;

        var root = host.Services.GetRequiredService<IConfiguration>();
        root["Logging:LogLevel:Default"] = nameof(LogLevel.Error);
        ((IConfigurationRoot)root).Reload();
    }

    private static void PrintDescription(ILogger logger)
    {
        var isDebug = false;
#if DEBUG
        isDebug = true;
#endif

        logger.LogInformation(
            "LsifDotnet - a language Server Indexing Format (LSIF) generator for dotnet. Version {version} {profile}",
            Assembly.GetExecutingAssembly().GetName().Version,
            isDebug ? "(Debug)" : string.Empty);
    }

    private static string FindSolutionFile()
    {
        var files = Directory.GetFiles(Directory.GetCurrentDirectory()).Where(file =>
            string.Equals(Path.GetExtension(file), ".sln", StringComparison.OrdinalIgnoreCase)).ToList();

        if (files.Count != 1)
        {
            throw new FileNotFoundException("Solution file not found or found more than one.");
        }

        return files.First();
    }

    private static async Task SaveLsifDump(IAsyncEnumerable<LsifItem> items, string dumpPath)
    {
        await using var writer = new StreamWriter(dumpPath);
        await foreach (var item in items)
        {
            var json = item.ToJson();
            await writer.WriteLineAsync(json);
        }
    }
}