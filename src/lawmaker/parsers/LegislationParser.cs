#nullable enable
using System.Linq;
using System.Collections.Generic;
using DocumentFormat.OpenXml.Packaging;
using Microsoft.Extensions.Logging;

using UK.Gov.Legislation.Judgments;
using AkN = UK.Gov.Legislation.Judgments.AkomaNtoso;
using CaseLaw = UK.Gov.NationalArchives.CaseLaw.Parse;
using DOCX = UK.Gov.Legislation.Judgments.DOCX;
using System;


namespace UK.Gov.Legislation.Lawmaker;

using static UK.Gov.Legislation.Lawmaker.IStatefulParser<IBlock, Document.State>;

using DocumentStyle = Dictionary<string, Dictionary<string, string>>;
public partial class LegislationParser : BlockParser, IStatefulParser<IBlock, Document.State>
{

    // We may need to hold this information in the Frames, but that may be tricky. For now we store them
    // at the root.
    private readonly DocName docName;

    public Document.State State { get; private set; }

    /*
        This class takes a list of "pre-parsed" blocks, and arranges them into a bill structure.
        The pre-parsed list contains blocks of only four types:
        - WLine (a line of text, corresponding to a Word "paragraph") (won't have a number unless subclassed)
        - WOldNumberedParagraph (a subclass of WLine, with a number)
        = WTable
        - WTableOfContents
    */
    public static Document Parse(byte[] docx, LegislationClassifier classifier, LanguageService languageService)
    {
        WordprocessingDocument doc = AkN.Parser.Read(docx);
        CaseLaw.WordDocument simple = new CaseLaw.PreParser().Parse(doc);
        return new LegislationParser(simple, classifier, languageService).Parse();
    }

    private LegislationParser(CaseLaw.WordDocument doc, LegislationClassifier classifier, LanguageService languageService) : this(
            doc.Body.Select(b => b.Block).ToList(),
            DOCX.CSS.Extract(doc.Docx.MainDocumentPart, "#bill"),
            classifier,
            languageService
        )
    { }

    private LegislationParser(IEnumerable<IBlock> contents, DocumentStyle? style, LegislationClassifier classifier, LanguageService languageService) : base(languageService, contents)
    {
        // We can safely discard the `BlockWithBreak` added boolean here, we don't need it
        Styles = style;
        docName = classifier.DocName;
        frames = new Frames(classifier.DocName, classifier.GetContext());
        provisionRecords = new ProvisionRecords();
        State = new(languageService, new());
        // LanguageService = languageService;
    }

    private readonly ILogger Logger = Logging.Factory.CreateLogger<LegislationParser>();
    private ProvisionRecords provisionRecords;
    private readonly Frames frames;
    private Dictionary<string, Dictionary<string, string>>? Styles { get;  init; }


    int parseDepth = 0;
    int parseDepthMax = 0;
    int parseAndMemoizeDepth = 0;
    int parseAndMemoizeDepthMax = 0;

    private Lawmaker.Document Parse()
    {
        ParseAndEnrichHeader();
        ParseBody();
        ParseConclusions();

        if (i != Body.Count)
            Logger.LogWarning("parsing did not complete: {}", i);

        Logger.LogInformation($"Maximum ParseAndMemoize depth reached: {parseAndMemoizeDepthMax}");
        Logger.LogInformation($"Maximum Parse depth reached: {parseDepthMax}");

        // Handle start and end quotes after parsing is complete, because it alters the
        // contents of parsed results which does not work well with memoization
        ExtractAllQuotesAndAppendTexts(body);
        QuotationEnricher quotationEnricher = new(this.State.LanguageService, $"(?:{{.*?}})?{StartQuotePattern()}", EndQuotePattern());
        quotationEnricher.EnrichDivisions(body);

        FootnoteEnricher footnoteEnricher = new FootnoteEnricher();
        footnoteEnricher.EnrichBlocks(preamble);
        footnoteEnricher.EnrichDivisions(body);


        return new Lawmaker.Document
        {
            Type = docName,
            Styles = Styles,
            Metadata = new(),
            CoverPage = coverPage,
            Preface = preface,
            Preamble = preamble,
            Body = body,
            Schedules = [],
            Conclusions = conclusions
        };
    }

    private R? Match<R>(UnifiedStrategy<R> strategy)
    {
        int save = this.Save();
        (R? returned, StateUpdate? stateUpdate) = strategy.Invoke(this);
        if (stateUpdate is not null)
        {
            this.State = stateUpdate.Invoke(this.State);
        }
        if (returned is not null) {
            return returned;
        }
        this.Restore(save);
        return default;
    }

    public R? Match<R>(params UnifiedStrategy<R>[] strategies)
    {
        foreach (var strategy in strategies)
        {
            if (Match(strategy) is R matched)
            {
                return matched;
            }

        }
        return default;
    }

    public List<R>? MatchWhile<R>(Predicate<(IBlock, Document.State)> condition, params UnifiedStrategy<R>[] strategies)
    {

        List<R> matches = [];
        while (Current() is IBlock r
            && condition((r, State))
            && Match(strategies) is R match
            && !IsAtEnd())
        {
            matches.Add(match);
        }
        return matches.Count != 0 ? matches : null;
    }
}
