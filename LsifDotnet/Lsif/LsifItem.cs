using System.Collections.Generic;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace LsifDotnet.Lsif;

public abstract class LsifItem
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        Converters =
        {
            new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)
        },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public const string MetaDataLabel = "metaData";
    public const string ProjectLabel = "project";
    public const string DocumentLabel = "document";
    public const string RangeLabel = "range";
    public const string ResultSetLabel = "resultSet";
    public const string HoverResultLabel = "hoverResult";
    public const string ReferenceResultLabel = "referenceResult";
    public const string HoverRequestLabel = "textDocument/hover";
    public const string ReferencesRequestLabel = "textDocument/references";
    public const string NextLabel = "next";
    public const string ItemLabel = "item";
    public const string ContainsLabel = "contains";

    public const string CSharpLanguageId = "csharp";

    protected LsifItem(int id, LsifItemType type, string label)
    {
        Id = id;
        Type = type;
        Label = label;
    }

    public int Id { get; set; }
    public LsifItemType Type { get; set; }
    public string Label { get; set; }

    public string ToJson()
    {
        return JsonSerializer.Serialize<object>(this, SerializerOptions);
    }
}

class SingleEdge : LsifItem
{
    public int InV { get; set; }
    public int OutV { get; set; }

    public SingleEdge(int id, string label, int outV, int inV) : base(id, LsifItemType.Edge, label)
    {
        OutV = outV;
        InV = inV;
    }

    public SingleEdge(int id, string label, LsifItem outV, LsifItem inV) : base(id, LsifItemType.Edge, label)
    {
        OutV = outV.Id;
        InV = inV.Id;
    }

    public static SingleEdge NextEdge(int id, LsifItem outV, LsifItem inV)
    {
        return new SingleEdge(id, NextLabel, outV, inV);
    }

    public static LsifItem NextEdge(int id, int outV, int inV)
    {
        return new SingleEdge(id, NextLabel, outV, inV);
    }

    public static SingleEdge HoverEdge(int id, LsifItem outV, LsifItem inV)
    {
        return new SingleEdge(id, HoverRequestLabel, outV, inV);
    }

    public static LsifItem ReferenceEdge(int id, int outV, int inV)
    {
        return new SingleEdge(id, ReferencesRequestLabel, outV, inV);
    }
}

internal class MultipleEdge : LsifItem
{
    protected MultipleEdge(int id, int outV, List<int> inVs, string label) : base(id, LsifItemType.Edge, label)
    {
        OutV = outV;
        InVs = inVs;
    }

    public int OutV { get; set; }
    public List<int> InVs { get; set; }

    public static MultipleEdge ContainsEdge(int id, int outV, List<int> inVs)
    {
        return new MultipleEdge(id, outV, inVs, ContainsLabel);
    }
}

class ItemEdge : MultipleEdge
{
    public const string ReferencesProperty = "references";
    public const string DefinitionsProperty = "definitions";
    public int Document { get; set; }
    public string? Property { get; set; }

    public ItemEdge(int id, int outV, List<int> inVs, int document, string? property = default)
        : base(id, outV, inVs, ItemLabel)
    {
        Document = document;
        Property = property;
    }

    public static ItemEdge ReferenceItemEdge(int id, int outV, List<int> inVs, int documentId)
    {
        return new ItemEdge(id, outV, inVs, documentId, ReferencesProperty);
    }

    public static ItemEdge DefinitionItemEdge(int id, int outV, List<int> inVs, int documentId)
    {
        return new ItemEdge(id, outV, inVs, documentId, DefinitionsProperty);
    }
}

class ResultSetVertex : LsifItem
{
    public ResultSetVertex(int id) : base(id, LsifItemType.Vertex, ResultSetLabel)
    {
    }
}

class ReferenceResultVertex : LsifItem
{
    public ReferenceResultVertex(int id) : base(id, LsifItemType.Vertex, ReferenceResultLabel)
    {
    }
}

class MetaDataVertex : LsifItem
{
    public const string LsifVersion = "0.4.0";

    public static readonly ToolInfoRecord LsifDotnetToolInfo =
        new(Assembly.GetExecutingAssembly().GetName().Name!, Assembly.GetExecutingAssembly().GetName().Version?.ToString());

    public string Version { get; set; }
    public string ProjectRoot { get; set; }
    public ToolInfoRecord ToolInfo { get; set; }

    internal readonly record struct ToolInfoRecord(string Name, string? Version = default, string[]? Args = default);

    public MetaDataVertex(int id, string projectRoot, string version = LsifVersion)
        : this(id, projectRoot, LsifDotnetToolInfo, version)
    {
    }

    public MetaDataVertex(int id, string projectRoot, ToolInfoRecord toolInfo, string version = LsifVersion)
        : base(id, LsifItemType.Vertex, MetaDataLabel)
    {
        ProjectRoot = projectRoot;
        ToolInfo = toolInfo;
        Version = version;
    }
}

class ProjectVertex : LsifItem
{
    public string Resource { get; set; }
    public string Kind { get; set; }

    public ProjectVertex(int id, string uri, string languageId = CSharpLanguageId)
        : base(id, LsifItemType.Vertex, ProjectLabel)
    {
        Resource = uri;
        Kind = languageId;
    }
}

class DocumentVertex : LsifItem
{
    public string Uri { get; set; }
    public string LanguageId { get; set; }

    public DocumentVertex(int id, string uri, string languageId = CSharpLanguageId)
        : base(id, LsifItemType.Vertex, DocumentLabel)
    {
        Uri = uri;
        LanguageId = languageId;
    }
}

class HoverResultVertex : LsifItem
{
    public record HoverResult(List<string> Contents);

    public HoverResult Result { get; set; }

    public HoverResultVertex(int id, List<string> contents) : base(id, LsifItemType.Vertex, HoverResultLabel)
    {
        Result = new HoverResult(contents);
    }
}

class RangeVertex : LsifItem
{
    public LinePosition Start { get; set; }
    public LinePosition End { get; set; }

    public RangeVertex(int id, LinePosition start, LinePosition end) : base(id, LsifItemType.Vertex, RangeLabel)
    {
        Start = start;
        End = end;
    }

    public RangeVertex(int id, FileLinePositionSpan linePositionSpan)
        : this(id, linePositionSpan.StartLinePosition, linePositionSpan.EndLinePosition)
    {
    }
}

public enum LsifItemType
{
    Vertex,
    Edge,
}