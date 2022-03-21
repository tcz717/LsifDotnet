using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using LsifDotnet.Lsif;
using QuikGraph;
using QuikGraph.Graphviz;

namespace LsifDotnet;

public class GraphBuilder
{
    public BidirectionalGraph<LsifItem, EquatableTaggedEdge<LsifItem, string>> Graph { get; } = new();
    private readonly Dictionary<int, LsifItem> _vertexDict = new();

    public void AddLsifItem(LsifItem item)
    {
        switch (item)
        {
            case { Type: LsifItemType.Vertex }:
                Graph.AddVertex(item);
                _vertexDict.Add(item.Id, item);
                break;
            case SingleEdge singleEdge:
                Graph.AddEdge(new EquatableTaggedEdge<LsifItem, string>(
                    _vertexDict[singleEdge.OutV],
                    _vertexDict[singleEdge.InV],
                    singleEdge.Label));
                break;
            case ItemEdge itemEdge:
                Graph.AddEdgeRange(itemEdge.InVs.Select(inV => new EquatableTaggedEdge<LsifItem, string>(
                    _vertexDict[itemEdge.OutV],
                    _vertexDict[inV],
                    $"Item({itemEdge.Document}) {itemEdge.Property}")));
                break;
            case MultipleEdge multipleEdge:
                Graph.AddEdgeRange(multipleEdge.InVs.Select(inV => new EquatableTaggedEdge<LsifItem, string>(
                    _vertexDict[multipleEdge.OutV],
                    _vertexDict[inV],
                    multipleEdge.Label)));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(item));
        }
    }

    public ITargetBlock<LsifItem> BuildDataFlowBlock()
    {
        return new ActionBlock<LsifItem>(AddLsifItem);
    }

    public async Task SaveDotAsync(string dotFile = "lsif.dot")
    {
        await File.WriteAllTextAsync(dotFile, ToGraphviz());
    }

    public async Task SaveSvgAsync(string svgFile = "lsif.svg")
    {
        var client = new HttpClient();
        var responseMessage = await client.PostAsJsonAsync("https://quickchart.io/graphviz", new { graph = ToGraphviz() });
        if (responseMessage.IsSuccessStatusCode)
        {
            await File.WriteAllTextAsync(svgFile, await responseMessage.Content.ReadAsStringAsync());
            Process.Start(new ProcessStartInfo(svgFile) { UseShellExecute = true });
        }
        else
        {
            Console.WriteLine("Bad svg response");
        }
    }

    private string ToGraphviz()
    {
        return Graph.ToGraphviz(algorithm =>
        {
            algorithm.FormatVertex +=
                (_, eventArgs) => eventArgs.VertexFormat.Label = eventArgs.Vertex.ToJson();
            algorithm.FormatEdge += (_, eventArgs) =>
                eventArgs.EdgeFormat.Label.Value = eventArgs.Edge.Tag;
        });
    }

    public async IAsyncEnumerable<LsifItem> RecordLsifItem(IAsyncEnumerable<LsifItem> source)
    {
        await foreach (var item in source)
        {
            AddLsifItem(item);
            yield return item;
        }
    }
}