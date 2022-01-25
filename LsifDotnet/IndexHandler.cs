using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices.ComTypes;
using System.Threading.Tasks;
using LsifDotnet.Lsif;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LsifDotnet;

internal class IndexHandler
{
    public static async Task Process(IHost host, FileInfo solutionFile, FileInfo output, CultureInfo culture, bool dot, bool svg)
    {
        ConfigLoggingLevel(host);
        PrintDescription(host);

        var solutionFilePath = solutionFile?.FullName ?? FindSolutionFile();

        await host.Services.GetRequiredService<MSBuildWorkspace>().OpenSolutionAsync(solutionFilePath);
        var indexer = host.Services.GetRequiredService<LsifIndexer>();

        var defaultCulture = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = culture;

        var items = indexer.EmitLsif();
        var graphBuilder = new GraphBuilder();
        if (dot || svg) items = graphBuilder.RecordLsifItem(items);
        await SaveLsifDump(items, output.FullName);

        CultureInfo.CurrentUICulture = defaultCulture;

        if (dot) await graphBuilder.SaveDotAsync();

        if (svg) await graphBuilder.SaveSvgAsync();
    }

    private static void ConfigLoggingLevel(IHost host)
    {
        throw new NotImplementedException();
    }

    private static void PrintDescription(IHost host)
    {
        var logger = host.Services.GetRequiredService<ILogger<IndexHandler>>();

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