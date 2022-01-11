using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Linq;
using LsifDotnet.Roslyn;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.QuickInfo;

namespace LsifDotnet.Lsif;

public class LsifIndexer
{
    private int _emittedItem;

    public LsifIndexer(Workspace workspace)
    {
        Workspace = workspace;
    }

    public Workspace Workspace { get; }

    public int EmittedItem => _emittedItem;

    [System.Diagnostics.CodeAnalysis.SuppressMessage("MicrosoftCodeAnalysisCorrectness", "RS1024:正确比较符号")]
    protected Dictionary<ISymbol, CachedSymbolResult> VisitedSymbols { get; } =
        new(SymbolEqualityComparer.Default);

    protected record CachedSymbolResult(int ResultSetId, int? DefinitionResultId, int? ReferenceResultId,
        List<SymbolRef>? ReferenceVs)
    {
        public int? ReferenceResultId { get; set; } = ReferenceResultId;
        public int? DefinitionResultId { get; set; } = DefinitionResultId;
    }

    protected readonly record struct SymbolRef(int RangeId, bool IsDefinition);

    public async IAsyncEnumerable<LsifItem> EmitLsif()
    {
        var solution = Workspace.CurrentSolution;
        yield return new MetaDataVertex(NextId(), new Uri(Path.GetDirectoryName(solution.FilePath) ?? "").AbsoluteUri);

        foreach (var project in solution.Projects)
        {
            var projectId = NextId();
            var documents = new List<int>();
            yield return new ProjectVertex(projectId, new Uri(project.FilePath!).AbsoluteUri, project.Name);

            if (project.Language != "C#")
            {
                Console.WriteLine($"Currently {project.Language} not supported");
                continue;
            }

            foreach (var document in project.Documents)
            {
                var documentId = NextId();
                var ranges = new List<int>();
                yield return new DocumentVertex(documentId, new Uri(document.FilePath!).AbsoluteUri);
                documents.Add(documentId);

                var quickInfoService = QuickInfoService.GetService(document);
                Debug.Assert(quickInfoService != null, nameof(quickInfoService) + " != null");
                Console.WriteLine("Document {0}", document.Name);

                var identifierVisitor = new IdentifierVisitor();
                identifierVisitor.Visit(await document.GetSyntaxRootAsync());

                foreach (var token in identifierVisitor.IdentifierList)
                {
                    var symbol = await SymbolFinder.FindSymbolAtPositionAsync(document, token.SpanStart);
                    var location = token.GetLocation();

                    if (!location.IsInSource)
                    {
                        Console.WriteLine($"Skipped non in source token {token.Value}");
                        continue;
                    }

                    // ReSharper disable once ConditionIsAlwaysTrueOrFalse
                    if (symbol is null)
                    {
                        Console.WriteLine($"Symbol not found {token.Value}");
                        continue;
                    }

                    var linePositionSpan = location.GetMappedLineSpan();
                    var rangeVertex = new RangeVertex(NextId(), linePositionSpan);
                    ranges.Add(rangeVertex.Id);
                    yield return rangeVertex;

                    var isDefinition = symbol.Locations.Any(defLocation => defLocation.Equals(location));
                    if (VisitedSymbols.TryGetValue(symbol, out var cachedSymbolResult))
                    {
                        // Connect existing result set
                        cachedSymbolResult.ReferenceVs?.Add(new SymbolRef(rangeVertex.Id, isDefinition));
                        yield return SingleEdge.NextEdge(NextId(), rangeVertex.Id, cachedSymbolResult.ResultSetId);
                        continue;
                    }

                    var resultSetVertex = SimpleVertex.ResultSet(NextId());
                    yield return resultSetVertex;
                    yield return SingleEdge.NextEdge(NextId(), rangeVertex, resultSetVertex);

                    // Get hover info
                    var quickInfo = await quickInfoService.GetQuickInfoAsync(document,
                        token.SpanStart);
                    Debug.Assert(quickInfo != null, nameof(quickInfo) + " != null");

                    var hoverResultVertex = new HoverResultVertex(NextId(),
                        quickInfo.Sections.Select(section => section.Text).ToList());
                    yield return hoverResultVertex;
                    yield return SingleEdge.HoverEdge(NextId(), resultSetVertex, hoverResultVertex);

                    var shouldImport = ShouldImport(symbol);
                    if (shouldImport)
                    {
                        // Emit import info
                        foreach (var item in EmitImportSymbol(symbol, resultSetVertex)) yield return item;
                    }
                    else if (ShouldExport(symbol))
                    {
                        // Emit export info
                        foreach (var item in EmitExportSymbol(symbol, resultSetVertex)) yield return item;
                    }

                    List<SymbolRef>? referenceVs = null;
                    // Skip reference for namespace and extern metadata symbol 
                    if (symbol.Kind != SymbolKind.Namespace && !shouldImport)
                    {
                        referenceVs = new List<SymbolRef> { new(rangeVertex.Id, isDefinition) };
                    }

                    VisitedSymbols.Add(symbol, new CachedSymbolResult(
                        resultSetVertex.Id,
                        null,
                        null,
                        referenceVs));
                }

                yield return MultipleEdge.ContainsEdge(NextId(), documentId, ranges);

                foreach (var lsifItem in EmitReferences(documentId)) yield return lsifItem;
            }

            yield return MultipleEdge.ContainsEdge(NextId(), projectId, documents);
        }
    }

    private IEnumerable<LsifItem> EmitImportSymbol(ISymbol symbol, SimpleVertex resultSetVertex)
    {
        var monikerVertex = new MonikerVertex(NextId(), MonikerKind.Import, "csharp",
            symbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat));
        yield return monikerVertex;
        yield return SingleEdge.MonikerEdge(NextId(), resultSetVertex.Id, monikerVertex.Id);
    }

    private IEnumerable<LsifItem> EmitExportSymbol(ISymbol symbol, SimpleVertex resultSetVertex)
    {
        // TODO: decide scheme name and identity format
        var monikerVertex = new MonikerVertex(NextId(), MonikerKind.Export, "csharp",
            symbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat));
        yield return monikerVertex;
        yield return SingleEdge.MonikerEdge(NextId(), resultSetVertex.Id, monikerVertex.Id);
    }

    private static bool ShouldImport(ISymbol symbol)
    {
        return symbol.Locations.Any(loc => loc.IsInMetadata);
    }

    private static bool ShouldExport(ISymbol symbol)
    {
        var isInSource = symbol.Locations.Any(loc => loc.IsInSource);
        return symbol.DeclaredAccessibility == Accessibility.Public && symbol.Kind != SymbolKind.Local && isInSource;
    }

    private IEnumerable<LsifItem> EmitReferences(int documentId)
    {
        foreach (var cachedSymbolResult in VisitedSymbols.Values.Where(cachedSymbolResult =>
                     cachedSymbolResult.ReferenceVs?.Any() == true))
        {
            Debug.Assert(cachedSymbolResult.ReferenceVs != null, "cachedSymbolResult.ReferenceVs != null");

            if (cachedSymbolResult.ReferenceResultId == null)
            {
                var referenceResultVertex = SimpleVertex.ReferenceResult(NextId());
                yield return referenceResultVertex;
                yield return SingleEdge.ReferenceEdge(NextId(), cachedSymbolResult.ResultSetId,
                    referenceResultVertex.Id);
                cachedSymbolResult.ReferenceResultId = referenceResultVertex.Id;
            }

            var defVs = cachedSymbolResult.ReferenceVs.Where(refV => refV.IsDefinition)
                .Select(refV => refV.RangeId)
                .ToList();
            if (defVs.Any())
            {
                if (cachedSymbolResult.DefinitionResultId == null)
                {
                    var definitionResultVertex = SimpleVertex.DefinitionResult(NextId());
                    yield return definitionResultVertex;
                    yield return SingleEdge.DefinitionEdge(NextId(), cachedSymbolResult.ResultSetId,
                        definitionResultVertex.Id);
                    cachedSymbolResult.DefinitionResultId = definitionResultVertex.Id;
                }

                yield return new ItemEdge(NextId(), cachedSymbolResult.DefinitionResultId.Value, defVs, documentId);
                yield return ItemEdge.DefinitionItemEdge(
                    NextId(), cachedSymbolResult.ReferenceResultId.Value, defVs, documentId);
            }

            var refVs = cachedSymbolResult.ReferenceVs.Where(refV => !refV.IsDefinition)
                .Select(refV => refV.RangeId)
                .ToList();
            if (refVs.Any())
            {
                yield return ItemEdge.ReferenceItemEdge(
                    NextId(), cachedSymbolResult.ReferenceResultId.Value, refVs, documentId);
            }

            cachedSymbolResult.ReferenceVs.Clear();
        }
    }

    private int NextId()
    {
        return Interlocked.Increment(ref _emittedItem);
    }
}