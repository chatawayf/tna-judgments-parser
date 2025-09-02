#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml.Wordprocessing;
using UK.Gov.Legislation.Judgments;
using UK.Gov.Legislation.Judgments.Parse;
using Microsoft.Extensions.Logging;
using System.Collections;

namespace UK.Gov.Legislation.Lawmaker;

/// <summary>
///
/// </summary>
/// <typeparam name="R"></typeparam>
/// <param name="Content"></param>
/// <param name="QuoteDifference"></param>
/// <param name="Depth"></param>
internal record QuoteInfo<R>(
    R Content,
    // positive for more left quotes, negative for more right quotes
    // This is positive for blocks that the start a quoted structure and
    // negative for blocks ending a quoted structur
    int QuoteDifference,
    // number of quoted structures deep, if Content is not in a quoted structure then this is 0
    // if Content starts or ends a quoted structure then this is the depth of the number quoted structures beginning/ending in that block
    // if Content starts or ends multiple quoted structures, this is the depth of the deepest quoted structure
    int Depth
    // QuoteLocation
)
{
    internal static Func<List<QuoteInfo<T>> ,T, List<QuoteInfo<T>>> From<T>(
        Func<T, (int, int)> quoteCounter,
        int initialDifference = 0,
        int initialDepth = 0
    ) => (List<QuoteInfo<T>> acc, T t) =>
    {
        (int left, int right) = quoteCounter(t);
        int difference = left - right;
        var last = acc.LastOrDefault();
        int lastDiff = last?.QuoteDifference ?? 0;
        int depth = last?.Depth ?? 0;
        if (lastDiff < 0)
        {
            depth += lastDiff;
        } else
        {
            depth += difference;
        }
        acc.Add(new QuoteInfo<T>(t, difference + initialDifference, depth + initialDepth));
        return acc;
    };

    internal int NextDepth => Depth + QuoteDifference;
    /// <summary>
    /// Returns a predicate that checks if the T is "unmatched" based on previous context
    /// </summary>
    /// <param name="context">The T's proceeding the T to be checked.</param>
    /// <returns>A predicate taking a T and returns true if there is no matching end T, respecting depth</returns>
    internal bool IsUnmatched<T>(IEnumerable<QuoteInfo<T>> context)
        => context
            .TakeWhile(i => i.Depth >= Depth)
            .Select(i => i.QuoteDifference)
            .Sum() == this.QuoteDifference + 1;

}

/// <summary>
///
/// </summary>
/// <param name="Quote"></param>
/// <param name="InlineIndex"></param>
/// <param name="ContentsIndex"></param>
internal record QuoteLocation(
    // the quote mark we're looking for, e.g. '\u201C'. We may want to support multiple characters in the future
    string Quote,
    // The index of the IInline in the IBlock
    int InlineIndex,
    // The index of the quote marker in the Contents
    int ContentsIndex
)
{

    // Case where we have a string marked up with quote depth and difference info. e.g.
    /*
    diff:       0 0 0 0  0 0  0  1 0 0 0 0 0 0  1 0 0 0 0-1  0 0 0 0 0 0 0-1  1 0 0 0 0 0 0 0   total: 1 = unmatched opening quotes
    depth:      0 0 0 0  0 0  0  0 1 1 1 1 1 1  1 2 2 2 2 2  1 1 1 1 1 1 1 0  1 1 1 1 1 1 1 1
    content:    t h i s  i s  a  “ s t r i n g  “ w i t h ”  o p e n i n g ”  “ q u o t e s —
                                                    take while depth >= 1 --> ^_____________^
                                                                              sum of diff is 1 which equals the depth of the quote => it is an unmatched quote
                      take while depth >= 2 --> ^_________^
                                                sum of diff is 0
                                 ^_________________________________________^
                                 sum of diff is 0
    */
    // internal static List<int> FindUnmatched(List<QuoteInfo<char>> text, Predicate<char> pred)
    // {
    //     List<QuoteInfo<IInline>> split = text.AsEnumerable()
    //         .Select((inline, index) => (inline, index)) // zip with index
    // }
}

enum BlockPositionType
{
    NUMBER,
    CONTENT
}
record struct BlockPosition(
    int InlineIndex,
    int CharacterIndex,
    BlockPositionType Type = BlockPositionType.CONTENT
);

record BlockQuotePositions(
    List<(char, BlockPosition)> UnmatchedStartQuotes,
    List<(char, BlockPosition)> UnmatchedEndQuotes
);

public static class BlockQuotedStructureExtensions
{
    internal static IEnumerable<(char, BlockPosition)> IterateCharacters(this WOldNumberedParagraph wp) =>
        wp.Number.Text
            .Select((c, i) => (c, new BlockPosition(0, i, BlockPositionType.NUMBER)))
            .Concat((wp as WLine).IterateCharacters());
    internal static IEnumerable<(char, BlockPosition)> IterateCharacters(this WLine line) => line.Contents
            .OfType<IFormattedText>()
            .Select((inline, i) => inline.IterateCharacters()
                .Select(c => (c.Item1, new BlockPosition(i, c.Item2, BlockPositionType.CONTENT))))
            .SelectMany(i => i);
    static IEnumerable<(char, int)> IterateCharacters(this IFormattedText inline)
        => inline.Text.Select((c, i) => (c, i));


    internal static BlockQuotePositions FindUnmatchedQuotes(this WLine block)
    {
        // This can possibly be done with one stack
        Stack<(char, BlockPosition)> startQuotes = new();
        Stack<(char, BlockPosition)> endQuotes = new();

        IEnumerator<(char, BlockPosition)> characters = block.IterateCharacters().GetEnumerator();

        while (characters.MoveNext())
        {
            (char c, BlockPosition position) = characters.Current;
            // for now only consider double quotes
            // QuotedStructureCode? code = null;
            // if (c == '{')
            // {

            //     while (characters.MoveNext() && characters.Current.Item1 != '}')
            //     {

            //     }
            // }

            // start new quote depth
            if (c == '\u201C') startQuotes.Push((c, position));
            // we have a matching pair of quotes
            if (c == '\u201D' && startQuotes.Count > 0 && startQuotes.Peek().Item1 == '\u201C') startQuotes.Pop();
            // we have an unmatched ending quote
            else if (c == '\u201D') endQuotes.Push((c, position));
        }
        return new BlockQuotePositions(
            [.. startQuotes],
            [.. endQuotes]);

    }

    internal static IEnumerable<WLine> Split(this WLine line, IEnumerable<BlockPosition> positions)
    {
        HashSet<int> inlineIndexesInContent = positions
            .Where(p => p.Type == BlockPositionType.CONTENT)
            .Select(p => p.InlineIndex)
            .ToHashSet();
        int inlineIndex = 0;

        List<WLine> newLines = [];
        List<IInline> newContents = [];

        // we have to reverse inlines here, otherwise the indexes are off after splitting
        foreach (IInline inline in line.Contents.Reverse())
        {
            if (!inlineIndexesInContent.Contains(inlineIndex) || inline is not ISplittable<IInline> splittable)
            {
                newContents.Add(inline);
                continue;
            } else
            {
                List<int> indexesToSplit = positions
                    .Where(p => p.InlineIndex == inlineIndex)
                    .OrderByDescending(p => p.CharacterIndex)
                    .Select(p => p.CharacterIndex)
                    .ToList();

                IEnumerable<IInline> splitContents = splittable.Split([.. indexesToSplit]);
                // first split appended to previous contents
                if (splitContents.FirstOrDefault() is IInline preInline)
                {
                    newContents.Add(preInline);
                    newLines.Add(WLine.Make(line, newContents));
                    newContents = [];
                }
                // rest are their own contents
                foreach (IInline middleInline in splitContents.Skip(1).SkipLast(1))
                {
                    newLines.Add(WLine.Make(line, [middleInline]));
                }
                if (splitContents.Skip(1).LastOrDefault() is IInline last)
                {
                    newContents = [last];
                }
            }
            inlineIndex++;
        }
        newLines.Add(WLine.Make(line, newContents));
        return newLines;
    }
}
/// <summary>
/// Splits out quoted structures from a list of blocks.
/// </summary>
/// <remarks>
/// TODO: explain the algorithm
/// </remarks>
public class QuotedStructureSplitter
{
    // QuotedStructureSplitter(IEnumerable<IBlock> contents): base(contents) { }
    private static readonly ILogger Logger = Logging.Factory.CreateLogger<QuotedStructureSplitter>();

    public static IBlock MarkQuotes(IBlock )
    public static List<IBlock> Split(List<IBlock> contents)
    {

        contents.Aggregate(new List<IBlock>(),
        (acc, block) =>
        {
            //if (block is not ISplittable splittable)
            if (block is not WLine line)
            {
                acc.Add(block);
            }
            QuotedStructure.CountStartAndEndQuotes(block);
        })
        // need an exception for tables?
        // also exception for single block quoted structures
        IEnumerable<QuoteInfo<IBlock>> blocksWithInfo = contents
            .Aggregate(new List<QuoteInfo<IBlock>>(contents.Count),
                QuoteInfo<IBlock>.From<IBlock>(QuotedStructure.CountStartAndEndQuotes));
        // IEnumerable<IEnumerable<QuoteInfo<IBlock>>> split = blocksWithInfo
        //     .Aggregate(new List<List<QuoteInfo<IBlock>>>(), )

        var chars = contents
            .OfType<WLine>()
            .Select(b => b.IterateCharacters())
            .SelectMany(i => i);

        var quotePositions = contents.OfType<WLine>()
            .Select(line => (line, line.FindUnmatchedQuotes()));
        var qsStartSplits = quotePositions
            .Select(lineWithPositions => lineWithPositions.Item1.Split(lineWithPositions.Item2.UnmatchedStartQuotes.Select(p => p.Item2))).ToList();

        var qsEndSplits = quotePositions
            .Select(lineWithPositions => lineWithPositions.line.Split(lineWithPositions.Item2.UnmatchedEndQuotes.Select(p => p.Item2))).ToList();
        return contents;
    }

    private enum QuotedStructureMarkerType
    {
        START,
        END
    }


    // static IEnumerable<(IInline, int)> BlockContents(this IBlock block)
    // private static IEnumerable<char> BlockContentsIterator(IEnumerable<IBlock> blocks) => blocks.Select((block, i) => block.BlockContents()).SelectMany(i=>i)

    // private static List<List<QuoteInfo<IBlock>>> SplitBlock(List<List<QuoteInfo<IBlock>>> acc, QuoteInfo<IBlock> block)
    // {

    // }

    // An opening quote for a quoted structure is the first unmatched opening quote
    // private QuoteLocation? FindUnmatchedOpeningQuote(IBlock block)
    /// <summary>
    ///
    /// </summary>
    /// <param name="line"></param>
    /// <returns>the indexes of unmatched opening quotes</returns>
    // private static List<int>? FindUnmatchedOpeningQuote(QuoteInfo<WLine> line)
    // {
    //     List<QuoteInfo<IInline>> inlinesWithInfo = line.Contents
    //         .Aggregate(new List<QuoteInfo<IInline>>(line.Contents.Count()),
    //             QuoteInfo<IInline>(QuotedStructure.CountStartAndEndQuotes));
    //     // Unmatched opening quote if sum of difference from point to end is one more than difference at point
    //     // Unmatched closing quote if sum of difference from start to point is negative

    //     // an opening quote is unmatched if the sum of the difference from the quote to end is one more than the initial difference
    //     List<QuoteInfo<IInline>> splitByOpeningQuotes = inlinesWithInfo.AsEnumerable()
    //         .Select((inline, index) => (inline, index)) // zip with index
    //         .Reverse()
    //         .Aggregate(new List<List<(QuoteInfo<IInline>, int)>>(),
    //             )


    //     return inlinesWithInfo;
    //     // an inline with a positive difference

    // }

    // private static List<int> FindUnmatchedQuoteLocations(QuoteInfo<WLine> line, )
    // {
    //     List<QuoteInfo<IInline>> inlinesWithInfo = line.Content.Contents

    // }

    // private static List<List<QuoteInfo<IInline>>> SplitByFirstUnmatchedQuote(List<List<QuoteInfo<IInline>>> acc, QuoteInfo<IInline> inline)
    //     => inline.Content switch
    //     {
    //         WText text => text
    //     }

    // private (WLine, OpeningQuote, WLine) SplitOn
    // private List<IBlock> SplitOnFirstUnmatchedOpeningQuote(IBlock block)
    // {

        // }

        // private static bool IsQuotedStructureStart(BlockQuoteInfo block) => block.QuoteDifference > 0;

        // private bool IsSingleQuotedStructure(IBlock block) =>

}
internal record QuotedStructureStart(
    Frame Frame,
    OpeningQuote OpeningQuote,
    // QuotedStructureCode Code,
    // text between the start of the line and the opening quote of the quoted structure
    // empty if the quoted structure starts on a new line
    IEnumerable<IInline>? PrecedingText,
    // text between the opening quote
    IEnumerable<IInline?> QuotedText
) : IBlock/*, Lawmaker.IParsable<IBlock> */
{
    // public static QuotedStructureStart? Parse(IParser<IBlock> parser)
    // {
    //     if (parser.Advance())

    // }
}

internal record QuotedStructureEnd(
    string ClosingQuote,
    IInline AppendText
): IBlock;

internal enum OpeningQuote
{
    DoubleOpeningQuote = '\u201C',
    // SingleOpeningQuote
}


internal enum ClosingQuote
{
    DoubleClosingQuote = '\u201D',
}
internal record QuotedStructure(
    QuotedStructureStart Start,
    IEnumerable<IBlock> Contents,
    QuotedStructureEnd End
): IBlock
{


    /// <summary>
    /// Groups together blocks in a list belonging to Quoted Structures.
    /// </summary>
    /// <param name="body"></param>
    /// <returns>The same collection of blocks where blocks belonging to quoted structures have been grouped into QuotedStructure objects</returns>
    // internal static IEnumerable<IBlock> GroupQuotedStructures(IEnumerable<IBlock> body)
    // {
    // }
    /*
        * Determines if a given block begins with a quoted structure.
        */
    public static bool IsStartOfQuotedStructure(IBlock block)
    {
        if (block is not WLine line)
            return false;
        string text = Regex.Replace(line.NormalizedContent, @"\s+", string.Empty);
        if (!Regex.IsMatch(text, QuotedStructureStartPattern()))
            return false;

        var (left, right) = QuotedStructure.CountStartAndEndQuotes(text);
        // Handle start of multi-line quoted structures.
        // These feature more left quotes than right quotes, as the end quote will be on a later line.
        if (left > right)
            return true;
        // Handle single-paragraph quoted structures
        // Number of left and right quotes must match.
        else if (left == right && Regex.IsMatch(line.NormalizedContent, QuotedStructureEndPattern()))
            return true;
        return false;
    }

        internal static string startQuotePattern;
        internal static string endQuotePattern;
        internal static string quotedStructureEndPattern;
        internal static string quotedStructureStartPattern;
        internal static string quotedStructureInfoPattern = @"(?'info'{?(?'docName'.*?)(?:-(?'context'.*?))?}?\s*)?";
        /*
         * A quoted structure must begin with a start quote. Optionally, there may be 'info'
         * before the start quote surrounded in braces i.e. {ukpga-sch} which dictates the
         * 'doctype' and 'context' with which to parse the contents of the quoted structure.
         */
        internal static string QuotedStructureStartPattern()
        {
            if (quotedStructureStartPattern is not null)
                return quotedStructureStartPattern;

            quotedStructureStartPattern = @$"^\s*({quotedStructureInfoPattern}{StartQuotePattern()})";
            return quotedStructureStartPattern;
        }

        /*
         * Returns a regular expression representing a single possible start quote.
         */
        internal static string StartQuotePattern()
        {
            if (startQuotePattern is not null)
                return startQuotePattern;

            string[] possibleStartQuotes =
            {
                "\u201C"
            };
            startQuotePattern = $"(?'startQuote'{string.Join("|", possibleStartQuotes)})";
            return startQuotePattern;
        }

        /*
         * A quoted structure must terminate with an end quote. Optionally, there may be
         * 'following text' after the end quote, which has a small number of possible patterns.
         */
        public static string QuotedStructureEndPattern()
        {
            if (quotedStructureEndPattern is not null)
                return quotedStructureEndPattern;

            string[] possibleFollowingTexts = {
                ".",
                ";",
                ",",
                ", or",
                ", and",
                "; or",
                "; and"
            };
            string followingTextRegex = $"(?'followingText'{string.Join("|", possibleFollowingTexts)})";
            followingTextRegex = followingTextRegex.Replace(".", "\\.");
            quotedStructureEndPattern = $"{EndQuotePattern()}{followingTextRegex}?$";
            return quotedStructureEndPattern;
        }

        /*
         * Returns a regular expression representing a single possible end quote.
         */
        private static string EndQuotePattern()
        {
            if (endQuotePattern is not null)
                return endQuotePattern;

            string[] possibleEndQuotes =
            {
                "\u201D"
            };
            endQuotePattern = $"(?'endQuote'{string.Join("|", possibleEndQuotes)})";
            return endQuotePattern;
        }


        internal static (int, int) CountStartAndEndQuotes(string text)
        {
            int start = Regex.Matches(text, StartQuotePattern()).Count;
            int end = Regex.Matches(text, EndQuotePattern()).Count;
            return (start, end);
        }

        internal static (int, int) CountStartAndEndQuotes(IInline inline) => QuotedStructure.CountStartAndEndQuotes(IInline.GetText(inline));

        internal static (int, int) CountStartAndEndQuotes(IBlock block)
        {
            if (block is not WLine line)
                return (0, 0);
            return CountStartAndEndQuotes(line.TextContent);
        }

        private static (int, int) CountStartAndEndQuotes(IEnumerable<IBlock> blocks)
        {
            int start = 0;
            int end = 0;
            foreach (IBlock block in blocks)
            {
                (int addToStart, int addToEnd) = CountStartAndEndQuotes(block);
                start += addToStart;
                end += addToEnd;
            }
            return (start, end);
        }


}