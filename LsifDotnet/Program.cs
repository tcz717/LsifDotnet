using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
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
        MSBuildLocator.RegisterDefaults();

        var workspace = MSBuildWorkspace.Create();
        // await workspace.OpenSolutionAsync(@"F:\Code\Science\LsifDotnet\LsifDotnet.sln");
        await workspace.OpenSolutionAsync(@"F:\Code\Desktop\ConsoleApplication8\ConsoleApplication8.sln");

        var indexer = new LsifIndexer(workspace);

        var graph = await BuildGraph(indexer);

        await SaveResult(graph);

        Console.ReadLine();
    }

    private static async Task SaveResult(BidirectionalGraph<LsifItem, EquatableTaggedEdge<LsifItem, string>> graph)
    {
        var dot = graph.ToGraphviz(algorithm =>
        {
            algorithm.FormatVertex +=
                (_, eventArgs) => eventArgs.VertexFormat.Label = eventArgs.Vertex.ToJson();
            algorithm.FormatEdge += (_, eventArgs) =>
                eventArgs.EdgeFormat.Label.Value = eventArgs.Edge.Tag;
        });
        await File.WriteAllTextAsync("lsif.dot", dot);
        var client = new HttpClient();
        var responseMessage = await client.PostAsJsonAsync("https://quickchart.io/graphviz", new { graph = dot });
        if (responseMessage.IsSuccessStatusCode)
        {
            await File.WriteAllTextAsync("lsif.svg", await responseMessage.Content.ReadAsStringAsync());
            Process.Start(new ProcessStartInfo("lsif.svg") { UseShellExecute = true });
        }
        else
        {
            Console.WriteLine("Bad svg response");
        }
    }

    private static async Task<BidirectionalGraph<LsifItem, EquatableTaggedEdge<LsifItem, string>>> BuildGraph(
        LsifIndexer indexer)
    {
        var graph = new BidirectionalGraph<LsifItem, EquatableTaggedEdge<LsifItem, string>>();
        var vertexDict = new Dictionary<int, LsifItem>();
        await foreach (var item in indexer.EmitLsif())
        {
            Console.WriteLine(item.ToJson());

            switch (item)
            {
                case { Type: LsifItemType.Vertex }:
                    graph.AddVertex(item);
                    vertexDict.Add(item.Id, item);
                    break;
                case SingleEdge singleEdge:
                    graph.AddEdge(new EquatableTaggedEdge<LsifItem, string>(
                        vertexDict[singleEdge.OutV],
                        vertexDict[singleEdge.InV],
                        singleEdge.Label));
                    break;
                case ItemEdge itemEdge:
                    graph.AddEdgeRange(itemEdge.InVs.Select(inV => new EquatableTaggedEdge<LsifItem, string>(
                        vertexDict[itemEdge.OutV],
                        vertexDict[inV],
                        $"Item({itemEdge.Document}) {itemEdge.Property}")));
                    break;
                case MultipleEdge multipleEdge:
                    graph.AddEdgeRange(multipleEdge.InVs.Select(inV => new EquatableTaggedEdge<LsifItem, string>(
                        vertexDict[multipleEdge.OutV],
                        vertexDict[inV],
                        multipleEdge.Label)));
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        return graph;
    }
}