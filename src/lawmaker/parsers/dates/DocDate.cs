#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using static  UK.Gov.Legislation.Lawmaker.XmlNamespaces;

namespace UK.Gov.Legislation.Lawmaker.Date;

partial class DocDateFactory(LanguageService languageService)
{


    // Only dates of the format "d MMMM yyyy" with or without an ordinal suffix will parse successfully
    // or just a year "yyyy"
    // e.g. "17th June 2025", "9 October 2021", "2025"
    // Any other format will result in the date attribute being set to "9999-01-01"
    private static readonly Dictionary<string, string> formats = new() {
            { "d MMMM yyyy", "d'th' MMMM yyyy" },
            { "yyyy", "yyyy"},
    };

    internal DocDate Create(string? text)
    {
        if (string.IsNullOrEmpty(text)) return new NoDate();
        // Remove ordinal suffix from date if there is one
        Match match = OrdinalPostfix().Match(text);
        if (match.Success)
        {
            // Extract the numeric day and remove the suffix from the original string
            text = text.Replace(match.Value, match.Groups[1].Value);
        }

        if (PlaceholderRegex().IsMatch(text))
        {
            return new PlaceholderDate();
        }

        foreach (CultureInfo culture in languageService.Cultures)
        {
            foreach (string format in formats.Keys)
            {
                if (DateTime.TryParseExact(
                    text,
                    format,
                    culture,
                    DateTimeStyles.None,
                    out DateTime dateTime))
                {
                    return new ValidDate(dateTime, text, formats[format]);
                }
            }
        }
        if (string.IsNullOrEmpty(text?.Trim()))
        {
            return new NoDate();
        } else
        {
            return new UnknownDate(text);
        }
    }

    [GeneratedRegex(@"(\d+)(st|nd|rd|th)")]
    private static partial Regex OrdinalPostfix();

    // "***" is a placeholder for dates
    // We check for 2 or more for safety
    [GeneratedRegex( @"\*\*\**")]
    private static partial Regex PlaceholderRegex();
}

interface DocDate : IBuildable<XNode> {};

record PlaceholderDate() : DocDate
{
    public XNode? Build(Document _) =>
        new XElement(akn + "docDate",
            new XAttribute("date", "9999-01-01")
        );
};

record ValidDate(DateTime Date, string DateText, string Format) : DocDate
{
    public XNode Build(Document document)
    {
        return new XElement(akn + "docDate",
            new XAttribute("date", Date.ToString("yyyy-MM-dd")),
            new XText(DateText));
    }

};

record NoDate() : DocDate
{
    public XNode? Build(Document _) =>
        new XElement(akn + "docDate",
            new XAttribute("date", "9999-01-01")
        );
};

record UnknownDate(string Text) : DocDate
{
    public XNode Build(Document _) =>
        new XElement(akn + "docDate",
            new XAttribute("date", "9999-01-01"),
            new XText(Text)
        );
}
