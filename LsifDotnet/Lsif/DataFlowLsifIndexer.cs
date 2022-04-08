using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using LsifDotnet.Roslyn;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.QuickInfo;
using Microsoft.Extensions.Logging;

namespace LsifDotnet.Lsif;

public class DataFlowLsifIndexer
{
    private int _emittedItem;
    private int _initId;

    public DataFlowLsifIndexer(Workspace workspace, IdentifierCollectorFactory identifierCollectorFactory,
        ILogger<DataFlowLsifIndexer> logger)
    {
        Workspace = workspace;
        IdentifierCollectorFactory = identifierCollectorFactory;
        Logger = logger;
    }

    public Workspace Workspace { get; }
    public IdentifierCollectorFactory IdentifierCollectorFactory { get; }
    protected ILogger<DataFlowLsifIndexer> Logger { get; }

    public int EmittedItem => _emittedItem - _initId;

    [System.Diagnostics.CodeAnalysis.SuppressMessage("MicrosoftCodeAnalysisCorrectness", "RS1024")]
    protected Dictionary<ISymbol, CachedSymbolResult> VisitedSymbols { get; } =
        new(SymbolEqualityComparer.Default);

    /// <summary>
    ///     The symbol cache representing a ResultSet shared with related <see cref="RangeVertex" />
    /// </summary>
    /// <param name="ResultSetId">The ResultSet's Id</param>
    /// <param name="DefinitionResultId">The DefinitionResult's Id</param>
    /// <param name="ReferenceResultId">The ReferenceResult's Id</param>
    /// <param name="ReferenceVs">All <see cref="RangeVertex" />s referring the same symbol</param>
    protected record CachedSymbolResult(int ResultSetId, int? DefinitionResultId, int? ReferenceResultId,
        List<SymbolRef>? ReferenceVs)
    {
        /// <summary>
        ///     The ReferenceResult's Id
        /// </summary>
        public int? ReferenceResultId { get; set; } = ReferenceResultId;

        /// <summary>
        ///     The DefinitionResult's Id
        /// </summary>
        public int? DefinitionResultId { get; set; } = DefinitionResultId;
    }

    /// <summary>
    ///     A <see cref="RangeVertex" />s referring some symbol
    /// </summary>
    /// <param name="RangeId">The Id of the <see cref="RangeVertex" /></param>
    /// <param name="IsDefinition">true if this ref also the definition</param>
    protected readonly record struct SymbolRef(int RangeId, bool IsDefinition);

    protected readonly record struct IndexedProject(int Id, Project Project);

    protected readonly record struct IndexedDocument(int Id, Document Document);

    protected readonly record struct HoverContentRequest(int ResultSetId, Document Document, int Position);


    public IPropagatorBlock<Solution, LsifItem> BuildLsifEmitGraph(int maxDegreeOfParallelism = 4, int initId = 0)
    {
        _emittedItem = initId;
        _initId = initId;

        var solutionSource = new BufferBlock<Solution>();

        var lsifItemOutput = ReorderLsifItems();

        var toProjects = new TransformManyBlock<Solution, IndexedProject>(async solution =>
        {
            Logger.LogInformation("Emitting solution {solution}", solution.FilePath);
            await lsifItemOutput.SendAsync(
                new MetaDataVertex(NextId(), ToAbsoluteUri(Path.GetDirectoryName(solution.FilePath))));

            return solution.Projects.Select(project => new IndexedProject(NextId(), project));
        });

        var toDocuments = new TransformManyBlock<IndexedProject, IndexedDocument>(async indexedProject =>
        {
            var (projectId, project) = indexedProject;
            Logger.LogInformation("Emitting {language} project {project}", project.Language, project.FilePath);

            await lsifItemOutput.SendAsync(new ProjectVertex(projectId, ToAbsoluteUri(project.FilePath), project.Name));

            if (project.Language != "C#")
            {
                Logger.LogWarning($"Currently {project.Language} not supported");
                return ImmutableArray<IndexedDocument>.Empty;
            }

            var documents = new List<IndexedDocument>();
            foreach (var document in project.Documents)
            {
                var documentId = NextId();
                documents.Add(new IndexedDocument(documentId, document));
                Trace.Assert(
                    await lsifItemOutput.SendAsync(new DocumentVertex(documentId, ToAbsoluteUri(document.FilePath))));
            }

            Trace.Assert(await lsifItemOutput.SendAsync(MultipleEdge.ContainsEdge(NextId(), projectId,
                documents.Select(doc => doc.Id).ToList())));

            return documents;
        });

        var hoverHandleBlock =
            TransformAsyncEnumerable<HoverContentRequest, LsifItem>(GenerateHoverContent,
                new ExecutionDataflowBlockOptions { MaxDegreeOfParallelism = maxDegreeOfParallelism });

        var toLsifItem = TransformAsyncEnumerable(EmitDocument(hoverHandleBlock));

        var linkOptions = new DataflowLinkOptions { PropagateCompletion = true };
        solutionSource.LinkTo(toProjects, linkOptions);
        toProjects.LinkTo(toDocuments, linkOptions);
        toDocuments.LinkTo(toLsifItem, linkOptions);
        toLsifItem.LinkTo(lsifItemOutput);
        hoverHandleBlock.LinkTo(lsifItemOutput);

        toLsifItem.Completion.ContinueWith(_ => hoverHandleBlock.Complete());

        Task.WhenAll(toLsifItem.Completion, hoverHandleBlock.Completion).ContinueWith(_ => lsifItemOutput.Complete());

        return DataflowBlock.Encapsulate(solutionSource, lsifItemOutput);
    }

    private Func<IndexedDocument, IAsyncEnumerable<LsifItem>> EmitDocument(
        ITargetBlock<HoverContentRequest> hoverHandleBlock)
    {
        return EmitDocumentInternal;

        async IAsyncEnumerable<LsifItem> EmitDocumentInternal(IndexedDocument indexedDocument)
        {
            var previousEmittedItem = EmittedItem;
            var (documentId, document) = indexedDocument;
            Logger.LogInformation("Emitting {language} document {project}", document.SourceCodeKind, document.FilePath);
            var ranges = new List<int>();

            var identifierCollector = IdentifierCollectorFactory.CreateInstance();
            identifierCollector.Visit(await document.GetSyntaxRootAsync());

            foreach (var token in identifierCollector.IdentifierList)
            {
                var symbol = await SymbolFinder.FindSymbolAtPositionAsync(document, token.SpanStart);
                var location = token.GetLocation();
                var linePositionSpan = location.GetMappedLineSpan();

                if (!location.IsInSource)
                {
                    Logger.LogWarning($"Skipped not-in-source token {token.Value}");
                    continue;
                }

                if (SkipSymbol(symbol, token)) continue;

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

                // Request hover info
                await hoverHandleBlock.SendAsync(new HoverContentRequest(resultSetVertex.Id, document,
                    token.SpanStart));

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

                var referenceVs = new List<SymbolRef> { new(rangeVertex.Id, isDefinition) };
                VisitedSymbols.Add(symbol, new CachedSymbolResult(resultSetVertex.Id, null, null, referenceVs));
            }

            yield return MultipleEdge.ContainsEdge(NextId(), documentId, ranges);

            foreach (var lsifItem in EmitReferences(documentId)) yield return lsifItem;

            Logger.LogInformation("Emitted {count} Lsif item(s) in {document}", EmittedItem - previousEmittedItem,
                document.FilePath);
        }
    }

    private IPropagatorBlock<LsifItem, LsifItem> ReorderLsifItems()
    {
        int? lastId = null;
        var queue = new PriorityQueue<LsifItem, int>();
        var source = new BufferBlock<LsifItem>();
        var target = new ActionBlock<LsifItem>(item =>
            {
                if (!lastId.HasValue || item.Id == lastId + 1)
                {
                    lastId = item.Id;
                    source.Post(item);
                    return;
                }

                queue.Enqueue(item, item.Id);

                while (queue.TryPeek(out _, out var id) && id == lastId + 1)
                {
                    lastId = id;
                    source.Post(queue.Dequeue());
                }
            });
        target.Completion.ContinueWith(_ =>
            {
                while (queue.TryDequeue(out var item, out var id))
                {
                    if (id != lastId + 1)
                    {
                        Logger.LogError("Lsif item {id} is out of order when complete, expect {expect}", id, lastId + 1);
                    }
                    lastId = id;
                    source.Post(item);
                }
                source.Complete();
            });
        return DataflowBlock.Encapsulate(target, source);
    }

    /// <summary>
    /// Build a block that emits a single Lsif item for async enumerable of <see cref="T2"/>
    /// </summary>
    /// <typeparam name="T1">The transform parameter type</typeparam>
    /// <typeparam name="T2">The async output Type</typeparam>
    /// <param name="transform">The async transform</param>
    /// <param name="executionDataflowBlockOptions">The options</param>
    /// <returns></returns>
    public static IPropagatorBlock<T1, T2> TransformAsyncEnumerable<T1, T2>(Func<T1, IAsyncEnumerable<T2>> transform,
        ExecutionDataflowBlockOptions? executionDataflowBlockOptions = null)
    {
        var source = new BufferBlock<T2>();

        var target = new ActionBlock<T1>(async item =>
        {
            await foreach (var result in transform(item))
            {
                Trace.Assert(await source.SendAsync(result));
            }
        }, executionDataflowBlockOptions ?? new ExecutionDataflowBlockOptions());

        target.Completion.ContinueWith(_ => source.Complete());
        return DataflowBlock.Encapsulate(target, source);
    }

    private async IAsyncEnumerable<LsifItem> GenerateHoverContent(HoverContentRequest hoverContentRequest)
    {
        var (resultSetId, document, position) = hoverContentRequest;
        var quickInfoService = QuickInfoService.GetService(document);
        Debug.Assert(quickInfoService != null, nameof(quickInfoService) + " != null");

        var contents = await GenerateHoverContent(quickInfoService, document, position);
        var hoverResultVertex = new HoverResultVertex(NextId(), contents);
        yield return hoverResultVertex;
        yield return SingleEdge.HoverEdge(NextId(), resultSetId, hoverResultVertex.Id);
    }

    private bool SkipSymbol(ISymbol symbol, SyntaxToken token)
    {
        var linePositionSpan = token.GetLocation().GetMappedLineSpan();
        switch (symbol)
        {
            // ReSharper disable once ConditionIsAlwaysTrueOrFalse
            case null:
            {
                if (NotKnownIdentifier(token))
                {
                    Logger.LogWarning("Symbol not found {token.Value} at {linePositionSpan}", token.Value,
                        linePositionSpan);
                }

                return true;
            }
            // Bug: AliasSymbol.Equals throw NullReferenceException when comparing two global symbols https://github.com/tcz717/LsifDotnet/issues/8
            // Remove this case when it is fixed
            case IAliasSymbol { Name: "global" }:
                Logger.LogTrace("Skipped one global symbol at {linePositionSpan}", linePositionSpan);
                return true;
        }

        return false;
    }

    private static string ToAbsoluteUri(string? path)
    {
        return path == null ? string.Empty : new Uri(path).AbsoluteUri;
    }

    private static bool NotKnownIdentifier(SyntaxToken token)
    {
        return token.Text != "nameof";
    }


    /// <summary>
    ///     Section kind for nullability analysis.
    ///     <para>
    ///         Based on
    ///         https://github.com/dotnet/roslyn/blob/7dc32a952e77c96c31cae6a2ba6d253a558fc7ff/src/Features/LanguageServer/Protocol/Handler/Hover/HoverHandler.cs
    ///         These are internal tag values taken from
    ///         https://github.com/dotnet/roslyn/blob/master/src/Features/Core/Portable/Common/TextTags.cs
    ///     </para>
    ///     <para>
    ///         They're copied here so that we can ensure we render blocks correctly in the markdown
    ///         https://github.com/dotnet/roslyn/issues/46254 tracks making these public
    ///     </para>
    /// </summary>
    internal const string NullabilityAnalysis = nameof(NullabilityAnalysis);

    private async Task<List<string>> GenerateHoverContent(QuickInfoService quickInfoService, Document document,
        int position)
    {
        var quickInfo = await quickInfoService.GetQuickInfoAsync(document, position);
        if (quickInfo == null)
        {
            Logger.LogWarning("No quick info found at {position}", position);
            return new List<string>();
        }

        var contents = new List<string>();

        var description = quickInfo.Sections.FirstOrDefault(s => s.Kind == QuickInfoSectionKinds.Description);
        if (description != null)
        {
            contents.Add(MarkdownHelper.TaggedTextToMarkdown(description.TaggedParts, MarkdownFormat.AllTextAsCSharp));
        }

        var summary = quickInfo.Sections.FirstOrDefault(s => s.Kind == QuickInfoSectionKinds.DocumentationComments);
        if (summary != null)
        {
            contents.Add(MarkdownHelper.TaggedTextToMarkdown(summary.TaggedParts, MarkdownFormat.Default));
        }

        foreach (var section in quickInfo.Sections)
        {
            switch (section.Kind)
            {
                case QuickInfoSectionKinds.Description:
                case QuickInfoSectionKinds.DocumentationComments:
                    continue;

                case QuickInfoSectionKinds.TypeParameters:
                    contents.Add(MarkdownHelper.TaggedTextToMarkdown(section.TaggedParts,
                        MarkdownFormat.AllTextAsCSharp));
                    break;

                case QuickInfoSectionKinds.AnonymousTypes:
                    // The first line is "Anonymous Types:"
                    // Then we want all anonymous types to be C# highlighted
                    contents.Add(MarkdownHelper.TaggedTextToMarkdown(section.TaggedParts,
                        MarkdownFormat.FirstLineDefaultRestCSharp));
                    break;

                case NullabilityAnalysis:
                    // Italicize the nullable analysis for emphasis.
                    contents.Add(MarkdownHelper.TaggedTextToMarkdown(section.TaggedParts, MarkdownFormat.Italicize));
                    break;

                default:
                    contents.Add(MarkdownHelper.TaggedTextToMarkdown(section.TaggedParts, MarkdownFormat.Default));
                    break;
            }
        }

        return contents;
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