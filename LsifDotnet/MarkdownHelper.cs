using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;

namespace LsifDotnet;

public static class MarkdownHelper
{
    private static readonly Regex EscapeRegex = new(@"([\\`\*_\{\}\[\]\(\)#+\-\.!])", RegexOptions.Compiled);

    public static string? Escape(string? markdown)
    {
        return markdown == null ? null : EscapeRegex.Replace(markdown, @"\$1");
    }

    /// <summary>
    /// Indicates the start of a text container. The elements after <see cref="ContainerStart"/> through (but not
    /// including) the matching <see cref="ContainerEnd"/> are rendered in a rectangular block which is positioned
    /// as an inline element relative to surrounding elements. The text of the <see cref="ContainerStart"/> element
    /// itself precedes the content of the container, and is typically a bullet or number header for an item in a
    /// list.
    /// </summary>
    private const string ContainerStart = nameof(ContainerStart);

    /// <summary>
    /// Indicates the end of a text container. See <see cref="ContainerStart"/>.
    /// </summary>
    private const string ContainerEnd = nameof(ContainerEnd);

    public static string TaggedTextToMarkdown(
        ImmutableArray<TaggedText> taggedParts,
        MarkdownFormat markdownFormat)
    {
        var isInCodeBlock = false;
        var brokeLine = true;
        var afterFirstLine = false;
        var stringBuilder = new StringBuilder();

        if (markdownFormat == MarkdownFormat.Italicize)
        {
            stringBuilder.Append("_");
        }

        for (var i = 0; i < taggedParts.Length; i++)
        {
            var current = taggedParts[i];

            if (brokeLine && markdownFormat != MarkdownFormat.Italicize)
            {
                Debug.Assert(!isInCodeBlock);
                brokeLine = false;
                var canFormatAsBlock = (afterFirstLine, markdownFormat) switch
                {
                    (false, MarkdownFormat.FirstLineAsCSharp) => true,
                    (true, MarkdownFormat.FirstLineDefaultRestCSharp) => true,
                    (_, MarkdownFormat.AllTextAsCSharp) => true,
                    _ => false
                };

                if (!canFormatAsBlock)
                {
                    // If we're on a new line and there are no text parts in the upcoming line, then we
                    // can format the whole line as C# code instead of plaintext. Otherwise, we need to
                    // intermix, and can only use simple ` code fences
                    for (var j = i; j < taggedParts.Length; j++)
                    {
                        switch (taggedParts[j].Tag)
                        {
                            case TextTags.Text:
                                canFormatAsBlock = false;
                                goto endOfLineOrTextFound;

                            case ContainerStart:
                            case ContainerEnd:
                            case TextTags.LineBreak:
                                goto endOfLineOrTextFound;

                            default:
                                // If the block is just newlines, then we don't want to format that as
                                // C# code. So, we default to false, set it to true if there's actually
                                // content on the line, then set to false again if Text content is
                                // encountered.
                                canFormatAsBlock = true;
                                continue;
                        }
                    }
                }
                else
                {
                    // If it's just a newline, we're going to default to standard handling which will
                    // skip the newline.
                    canFormatAsBlock = !IndexIsTag(i, ContainerStart, ContainerEnd, TextTags.LineBreak);
                }

                endOfLineOrTextFound:
                if (canFormatAsBlock)
                {
                    afterFirstLine = true;
                    stringBuilder.Append("```csharp");
                    stringBuilder.AppendLine();
                    for (; i < taggedParts.Length; i++)
                    {
                        current = taggedParts[i];
                        if (current.Tag is ContainerStart or ContainerEnd or TextTags.LineBreak)
                        {
                            stringBuilder.AppendLine();

                            if (markdownFormat != MarkdownFormat.AllTextAsCSharp
                                && markdownFormat != MarkdownFormat.FirstLineDefaultRestCSharp)
                            {
                                // End the code block
                                stringBuilder.Append("```");

                                // We know we're at a line break of some kind, but it could be
                                // a container start, so let the standard handling take care of it.
                                goto standardHandling;
                            }
                        }
                        else
                        {
                            stringBuilder.Append(current.Text);
                        }
                    }

                    // If we're here, that means that the last part has been reached, so just
                    // return.
                    Debug.Assert(i == taggedParts.Length);
                    stringBuilder.AppendLine();
                    stringBuilder.Append("```");
                    return stringBuilder.ToString();
                }
            }

            standardHandling:
            switch (current.Tag)
            {
                case TextTags.Text when !isInCodeBlock:
                    AddText(current.Text);
                    break;

                case TextTags.Text:
                    EndBlock();
                    AddText(current.Text);
                    break;

                case TextTags.Space when isInCodeBlock:
                    if (IndexIsTag(i + 1, TextTags.Text))
                    {
                        EndBlock();
                    }

                    AddText(current.Text);
                    break;

                case TextTags.Space:
                case TextTags.Punctuation:
                    AddText(current.Text);
                    break;

                case ContainerStart:
                    AddNewline();
                    AddText(current.Text);
                    break;

                case ContainerEnd:
                    AddNewline();
                    break;

                case TextTags.LineBreak:
                    if (stringBuilder.Length != 0 && !IndexIsTag(i + 1, ContainerStart, ContainerEnd) &&
                        i + 1 != taggedParts.Length)
                    {
                        AddNewline();
                    }

                    break;

                default:
                    if (!isInCodeBlock)
                    {
                        isInCodeBlock = true;
                        stringBuilder.Append('`');
                    }

                    stringBuilder.Append(current.Text);
                    brokeLine = false;
                    break;
            }
        }

        if (isInCodeBlock)
        {
            EndBlock();
        }

        if (!brokeLine && markdownFormat == MarkdownFormat.Italicize)
        {
            stringBuilder.Append("_");
        }

        return stringBuilder.ToString();

        void AddText(string? text)
        {
            brokeLine = false;
            afterFirstLine = true;
            if (!isInCodeBlock)
            {
                text = Escape(text);
            }

            stringBuilder.Append(text);
        }

        void AddNewline()
        {
            if (isInCodeBlock)
            {
                EndBlock();
            }

            if (markdownFormat == MarkdownFormat.Italicize)
            {
                stringBuilder.Append("_");
            }

            // Markdown needs 2 linebreaks to make a new paragraph
            stringBuilder.AppendLine();
            stringBuilder.AppendLine();
            brokeLine = true;

            if (markdownFormat == MarkdownFormat.Italicize)
            {
                stringBuilder.Append("_");
            }
        }

        void EndBlock()
        {
            stringBuilder.Append('`');
            isInCodeBlock = false;
        }

        bool IndexIsTag(int i, params string[] tags)
            => i < taggedParts.Length && tags.Contains(taggedParts[i].Tag);
    }
}

public enum MarkdownFormat
{
    /// <summary>
    /// Only format entire lines as C# code if there is no standard text on the line
    /// </summary>
    Default,

    /// <summary>
    /// Italicize the section
    /// </summary>
    Italicize,

    /// <summary>
    /// Format the first line as C#, unconditionally
    /// </summary>
    FirstLineAsCSharp,

    /// <summary>
    /// Format the first line as default text, and format the rest of the lines as C#, unconditionally
    /// </summary>
    FirstLineDefaultRestCSharp,

    /// <summary>
    /// Format the entire set of text as C#, unconditionally
    /// </summary>
    AllTextAsCSharp
}