using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Hosting;
using System.CommandLine.NamingConventionBinder;
using System.CommandLine.Parsing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using LsifDotnet.Lsif;
using LsifDotnet.Roslyn;
using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LsifDotnet;

class Program
{
    static async Task Main(string[] args)
    {
        var builder = BuildCommandLine();
        await builder.UseHost(_ => Host.CreateDefaultBuilder(), ConfigureHost)
            .UseDefaults()
            .Build()
            .InvokeAsync(args);
    }

    private static CommandLineBuilder BuildCommandLine()
    {
        var rootCommand = new RootCommand("An indexer that dumps lsif information from dotnet solution.")
        {
            new Argument<FileInfo>("SolutionFile",
                    "The solution to be indexed. Default is the only solution file in the current folder.")
                { Arity = ArgumentArity.ZeroOrOne },
            new Option<FileInfo>("--output", () => new FileInfo("dump.lsif"),
                "The lsif dump output file."),
            new Option<bool>("--dot", "Dump graphviz dot file."),
            new Option<bool>("--svg", "Dump graph svg file. (by quickchart.io/graphviz API)"),
            new Option<CultureInfo>("--culture", () => CultureInfo.CurrentUICulture,
                "The culture used to show hover info."),
        };

        rootCommand.Handler = CommandHandler.Create(
            async (IHost host, FileInfo? solutionFile, FileInfo output, CultureInfo culture, bool dot, bool svg) =>
            {
                var solutionFilePath = solutionFile?.FullName ?? FindSolutionFile();

                await host.Services.GetRequiredService<MSBuildWorkspace>().OpenSolutionAsync(solutionFilePath);
                var indexer = host.Services.GetRequiredService<LsifIndexer>();

                CultureInfo.CurrentUICulture = culture;

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

        return new CommandLineBuilder(rootCommand);
    }

    private static void ConfigureHost(IHostBuilder host)
    {
        host.ConfigureLogging(builder =>
            builder.AddFilter("Microsoft.Hosting.Lifetime", LogLevel.None));

        host.ConfigureServices((context, collection) => collection.AddSingleton(_ => CreateWorkspace())
            .AddSingleton<IdentifierCollectorFactory>()
            .AddTransient<LsifIndexer>()
            .AddTransient(services => (Workspace)services.GetRequiredService<MSBuildWorkspace>()));
    }

    private static MSBuildWorkspace CreateWorkspace()
    {
        MSBuildLocator.RegisterDefaults();

        var workspace = MSBuildWorkspace.Create();
        return workspace;
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