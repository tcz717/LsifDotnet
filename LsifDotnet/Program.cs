using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.NamingConventionBinder;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using LsifDotnet.Lsif;
using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis.MSBuild;
using QuikGraph;
using QuikGraph.Graphviz;

namespace LsifDotnet;

/// <summary>
/// Test
/// </summary>
class Program
{
    /// <summary>
    /// Test
    /// </summary>
    /// <param name="args"></param>
    /// <returns></returns>
    static async Task Main(string[] args)
    {
        var rootCommand = new RootCommand("An indexer that dumps lsif information from dotnet solution.")
        {
            new Argument<FileInfo>("SolutionFile",
                    "The solution to be indexed. Default is the only solution file in the current folder.")
                { Arity = ArgumentArity.ZeroOrOne },
            new Option<FileInfo>("--output", () => new FileInfo("dump.lsif"),
                "The lsif dump output file."),
            new Option<bool>("--dot", "Dump graphviz dot file."),
            new Option<bool>("--svg", "Dump graph svg file."),
            new Option<CultureInfo>("--culture", () => CultureInfo.CurrentUICulture,
                "The culture used to show hover info."),
        };

        rootCommand.Handler = CommandHandler.Create(
            async (FileInfo? solutionFile, FileInfo output, CultureInfo culture, bool dot, bool svg) =>
            {
                MSBuildLocator.RegisterDefaults();

                var workspace = MSBuildWorkspace.Create();

                var solutionFilePath = solutionFile?.FullName ?? FindSolutionFile();

                await workspace.OpenSolutionAsync(solutionFilePath);

                CultureInfo.CurrentUICulture = culture;
                var indexer = new LsifIndexer(workspace);

                var items = indexer.EmitLsif();
                var graphBuilder = new GraphBuilder();
                if (dot || svg)
                    items = graphBuilder.RecordLsifItem(items);
                await SaveLsifDump(items, output.FullName);
                CultureInfo.CurrentUICulture = CultureInfo.DefaultThreadCurrentUICulture ?? culture;

                if (dot)
                    await graphBuilder.SaveDotAsync();

                if (svg)
                    await graphBuilder.SaveSvgAsync();
            });
        await rootCommand.InvokeAsync(args);
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