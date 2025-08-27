#nullable enable
namespace UK.Gov.Legislation.Lawmaker;

using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using System.Text.RegularExpressions;

using UK.Gov.Legislation.Judgments;
using UK.Gov.Legislation.Judgments.Parse;
using UK.Gov.NationalArchives.CaseLaw.Parse;
using System;

/* The EBNF grammar for table blocks can be represented like this (note: Table, Alphanumeric aren't specified here, hopefully they are relatively self explanatory):
Table Block = [Table Number] Table ;
Table Number = Number, [Captions] ;
Number = "Table" Num ;
Num = [Alphanumeric | "."]+ ; (* this will match nums such as 6a, a6, 6.a, ..6aa ..., etc - we're not being super strict here *)
Captions = Heading, { Subheadings } ;
*/

record LdappTableBlock(
    LdappTableNumber? TableNumber,
    WTable Table
) : IBlock/*, IBuildable */
{

    // public XElement Build()
    // {
    //     XElement tblock = new("tblock",
    //         new XAttribute("class", "table"),
    //         new XAttribute("xmlns", Builder.AknNamespace),
    //         TableNumber != null ? new XElement("num", TableNumber?.Number.NormalizedContent) : null,
    //         new XElement("foreign",
    //             BuildTable(Table)
    //         )
    //     );
    // }

    // private XElement BuildTable(ITable model)
    // {
    //     return new("table",
    //         new XAttribute("xmlns", HtmlNamespace),
    //         new XAttribute("xmlns:akn", AknNamespace),
    //         new XAttribute(HtmlNamespace + "class", "allBorders tableleft width100"),
    //         new XAttribute("cols", model.ColumnWidthsIns.Count.ToString())
    //     );
    // }
    internal static LdappTableBlock? Parse(IParser<IBlock> parser)
    {
        // We can have a table on it's own *or* a table with a table num
        {
            if (parser.Match(ParseTable) is WTable table)
            {
                return new LdappTableBlock(null, table);
            }
        }

        if (parser.Match(LdappTableNumber.Parse) is LdappTableNumber number)
        {
            if (parser.Match(ParseTable) is WTable table)
            {
                return new LdappTableBlock(number, table);
            }
        }
        return null;
    }

    private static WTable? ParseTable(IParser<IBlock> parser)
    {
        if (parser.Advance() is WTable table)
        {
            // Identify lines with leading numbers in each table cell.
            WTable extracted = WTable.Enrich(table, HardNumbers.ExtractTableCell);
            // Parse any structured content in each table cell.
            return WTable.Enrich(extracted, ParseTableCell);
        }
        return null;
    }

    // Creates BlockLists from structured content inside table cells (if any).
    private static WCell ParseTableCell(WCell cell)
    {
        IEnumerable<IBlock> enriched = BlockList.ParseBlocks(cell.Contents);
        return new WCell(cell.Row, cell.Props, enriched);
    }

}

// There can optionally be an arbitrary number of text blocks between
// the table number and the table itself.
// The first text block is the caption, the rest are arbitrary paragraphs
// Here they are all referred to as captions
partial record LdappTableNumber(
    WLine Number,
    List<WLine>? Captions
) {

    const string TABLE_NUMBER_PATTERN = @"^Table\s+\w+$";

    internal static LdappTableNumber? Parse(IParser<IBlock> parser)
    {
        IBlock? block = parser.Advance();
        if (block is not WLine line) return null;
        if (!TableNumberPattern().IsMatch(line.NormalizedContent)) return null;
        return new LdappTableNumber(line, parser.Match(LdappTableCaptions.Parse));
    }

    [GeneratedRegex(TABLE_NUMBER_PATTERN, RegexOptions.IgnoreCase, "en-AU")]
    private static partial Regex TableNumberPattern();
}

class LdappTableCaptions
{


    internal static List<WLine>? Parse(IParser<IBlock> parser)
    {
        // Everything between the table num and the table itself is considered a caption
        List<WLine> captions = parser
            .AdvanceWhile(block => block is not WTable)
            .Where(block => block is WLine)
            .Select(block => block as WLine)
            .Where(block => block is not null)
            .Select(block => block!)
            .ToList();

        return captions switch
        {
            null or [] => null,
            _ => captions,
        };
    }
}