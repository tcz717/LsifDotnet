using System.CommandLine;
using System.CommandLine.Builder;
using System.CommandLine.Hosting;
using System.CommandLine.NamingConventionBinder;
using System.CommandLine.Parsing;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using LsifDotnet.Lsif;
using LsifDotnet.Roslyn;
using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.Extensions.Configuration;
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
        var outputOption = new Option<FileInfo>("--output", () => new FileInfo("dump.lsif"),
            "The lsif dump output file.");
        outputOption.AddAlias("-o");

        var dotOption = new Option<bool>("--dot", "Dump graphviz dot file.");
        dotOption.AddAlias("-d");

        var svgOption = new Option<bool>("--svg", "Dump graph svg file. (by quickchart.io/graphviz API)");
        svgOption.AddAlias("-s");

        var cultureOption = new Option<CultureInfo>("--culture", () => CultureInfo.CurrentUICulture,
            "The culture used to show hover info.");
        cultureOption.AddAlias("-c");

        var quietOption = new Option<bool>("--quiet", "Be more quiet");
        quietOption.AddAlias("-q");

        var rootCommand = new RootCommand("An indexer that dumps lsif information from dotnet solution.")
        {
            new Argument<FileInfo>("SolutionFile",
                    "The solution to be indexed. Default is the only solution file in the current folder.")
                { Arity = ArgumentArity.ZeroOrOne },
            outputOption,
            dotOption,
            svgOption,
            cultureOption,
            quietOption,
        };

        rootCommand.Handler = CommandHandler.Create(IndexHandler.Process);

        return new CommandLineBuilder(rootCommand);
    }

    private static void ConfigureHost(IHostBuilder host)
    {
        host.ConfigureAppConfiguration(builder => builder.AddInMemoryCollection());

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
}