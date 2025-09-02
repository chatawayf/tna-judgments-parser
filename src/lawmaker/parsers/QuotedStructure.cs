#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml.Wordprocessing;
using UK.Gov.Legislation.Judgments;
using UK.Gov.Legislation.Judgments.Parse;
using UK.Gov.NationalArchives.Enrichment;

namespace UK.Gov.Legislation.Lawmaker;



// TODO Un-indent the below block
    public partial class LegislationParser
    {

        private int quoteDepth = 0;

        /*
         * Extracts the 'frame' info from the braces at the start of the quoted structure (if present),
         * and adds the frame to the stack. i.e. Given the quoted structure: {UKPGA-SCH}�quoted content�
         * The DocName is set to 'UKPGA', and the Context is set to 'SCH'.
         * If 'frame' info cannot be determined (i.e. is absent or malformed), the Context and DocName
         * of the quoted structure default to those of the overall document.
         * Returns false specifically when the 'frame' info is present, but malformed.
         */
        private bool AddQuotedStructureFrame(IBlock block)
        {
            if (block is not WLine line)
            {
                this.frames.PushDefault();
                return true;
            }
            string pattern = $"{QuotedStructure.quotedStructureInfoPattern}{QuotedStructure.StartQuotePattern()}";
            string text = Regex.Replace(line.NormalizedContent, @"\s+", string.Empty);
            MatchCollection matches = Regex.Matches(text, pattern);
            if (matches.Count == 0)
            {
                // Quoted structure start pattern could not be matched.
                // This should not be possible in practice.
                this.frames.PushDefault();
                return true;
            }
            GroupCollection groups = matches.First().Groups;
            if (!groups["info"].Success || !groups["docName"].Success)
            {
                // No frame info present - valid scenario.
                frames.PushDefault();
                return true;
            }
            DocName docName;
            if (!Enum.TryParse(groups["docName"].Value.ToUpper(), out docName))
            {
                // Frame info present but DocName is malformed - invalid scenario.
                frames.PushDefault();
                return false;
            }
            Context? context = Contexts.ToEnum(groups["context"].Value);
            Context defaultContext = Frames.IsSecondaryDocName(docName) ? Context.REGULATIONS : Context.SECTIONS;
            if (!groups["context"].Success)
            {
                // Frame info has DocName but no Context - valid scenario.
                // Resort to default Context.
                frames.Push(docName, defaultContext);
                return true;
            }
            if (context == null)
            {
                // Frame info has a Context, but it is malformed - invalid scenario.
                // Resort to default Context.
                frames.Push(docName, defaultContext);
                return false;
            }
            // Frame info has valid DocName and Context
            frames.Push(docName, (Context) context);
            return true;
        }

        private static bool IsEndOfQuotedStructure(IDivision division)
        {
            string lastParagraphText = LastLine.GetLastParagraphText(division);
            return IsEndOfQuotedStructure(lastParagraphText);
        }

        private static bool IsEndOfQuotedStructure(IList<IBlock> contents, ILine heading = null, IFormattedText number = null, bool headingPrecedesNumber = false)
        {
            // Squash text content into single string
            List<IInline> inlines = [];
            if (headingPrecedesNumber)
            {
                if (heading != null)
                    inlines.AddRange(heading.Contents);
                if (number != null)
                    inlines.Add(number);
            }
            else
            {
                if (number != null)
                    inlines.Add(number);
                if (heading != null)
                    inlines.AddRange(heading.Contents);
            }
            foreach (IBlock block in contents)
            {
                if (block is ILine line)
                    inlines.AddRange(line.Contents);
            }
            return IsEndOfQuotedStructure(IInline.ToString(inlines));
        }

        private static bool IsEndOfQuotedStructure(string text)
        {
            if (text == null)
                return false;
            bool isEndQuoteAtEnd = Regex.IsMatch(text, QuotedStructure.QuotedStructureEndPattern());
            if (!isEndQuoteAtEnd)
                return false;

            bool isStartQuoteAtStart = Regex.IsMatch(text, QuotedStructure.QuotedStructureStartPattern());
            (int left, int right) = QuotedStructure.CountStartAndEndQuotes(text);
            // if (right > left && (right - left) <= distance)
            //     return false;

            bool isSingleLine = (isStartQuoteAtStart && isEndQuoteAtEnd);
            bool isEndOfMultiLine = (!isStartQuoteAtStart && isEndQuoteAtEnd && right > left);
            return isSingleLine || isEndOfMultiLine;
        }


        /*
         * Strips the quoted structure start pattern (if present) from the beginning of the given string.
         */
        private static string IgnoreQuotedStructureStart(string text, int quoteDepth)
        {
            if (quoteDepth == 0)
                return text;
            return Regex.Replace(text, QuotedStructure.QuotedStructureStartPattern(), "");
        }

        /*
         * Determines if a given line is followed by any quoted structures, and if so,
         * parses each quoted structure and returns them in a list.
         */
        private List<IQuotedStructure> HandleQuotedStructuresAfter(WLine line)
        {
            List<IQuotedStructure> quotedStructures = [];
            if (i == Contents.Count)
                return [];
            int save = i;

            // Handle the case where the start quote of the first quoted structure is NOT at the
            // start of a new line, but rather is at the end of the previous line.
            (int left, int right) = QuotedStructure.CountStartAndEndQuotes(line);
            bool isAtStartOfLine = (left == right + 1) && Regex.IsMatch(line.NormalizedContent, QuotedStructure.QuotedStructureStartPattern());
            if (left > right && !isAtStartOfLine)
            {
                bool isValidFrame = AddQuotedStructureFrame(line);
                BlockQuotedStructure qs = ParseQuotedStructure();
                qs.HasInvalidCode = !isValidFrame;
                if (qs != null)
                    quotedStructures.Add(qs);
                else
                    i = save;
                frames.Pop();
            }
            // Handle regular quoted structures
            while (i < Contents.Count && QuotedStructure.IsStartOfQuotedStructure(Current()))
            {
                save = i;
                bool isValidFrame = AddQuotedStructureFrame(Current());
                BlockQuotedStructure qs = ParseQuotedStructure();
                qs.HasInvalidCode = !isValidFrame;
                if (qs == null)
                {
                    i = save;
                    break;
                }
                quotedStructures.Add(qs);
                frames.Pop();
            }
            return quotedStructures;
        }

        private BlockQuotedStructure ParseQuotedStructure()
        {
            if (i == Contents.Count)
                return null;
            IBlock block = Contents[i];
            if (block is not WLine line)
                return null;
            return ParseAndMemoize(line, "QuotedStructure", ParseQuotedStructure);
        }

        private BlockQuotedStructure ParseQuotedStructure(WLine line)
        {
            List<IDivision> contents = [];
            quoteDepth += 1;
            while (i < Contents.Count)
            {
                int save = i;
                var child = ParseLine();
                if (child is null)
                {
                    i = save;
                    break;
                }
                contents.Add(child);
                if (IsEndOfQuotedStructure(child))
                    break;
            }
            quoteDepth -= 1;
            if (contents.Count == 0)
                return null;
            return new BlockQuotedStructure { Contents = contents, DocName = frames.CurrentDocName, Context = frames.CurrentContext };
        }

        // extract start and end quote marks and appended text

        internal static void ExtractAllQuotesAndAppendTexts(IList<IDivision> body)
        {
            Util.WithEachBlock.Do(body, ExtractQuotesAndAppendTexts);
        }

        private static void ExtractQuotesAndAppendTexts(IBlock block)
        {
            if (block is not BlockQuotedStructure qs)
                return;

            ExtractStartQuote(qs);

            var f = new ExtractAndReplace(QuotedStructure.QuotedStructureEndPattern());
            LastLine.Replace(qs.Contents, f.Invoke);
            qs.EndQuote = f.EndQuote;
            qs.AppendText = f.AppendText;
        }

        private static void ExtractStartQuote(BlockQuotedStructure qs)
        {
            if (qs.StartQuote != null)
                return;
            if (qs.Contents.FirstOrDefault() is not HContainer hContainer)
                return;

            // First text item can be in the num, heading, intro, OR content.

            if (hContainer.HeadingPrecedesNumber && hContainer.Heading is WLine heading)
            {
                WLine enriched = EnrichFromBeginning.Enrich(heading, QuotedStructure.QuotedStructureStartPattern(), ExtractStartQuoteConstructor);
                WBookmark bookmark = enriched.Contents.First(i => i is WBookmark) as WBookmark;
                qs.StartQuote = bookmark.Name;
                heading.Contents = enriched.Contents.Where(i => i is not WBookmark);
            }
            else if (hContainer.Number is not null)
            {
                Match match = Regex.Match(hContainer.Number.Text, QuotedStructure.QuotedStructureStartPattern());
                if (!match.Success)
                    return;
                int patternEndIndex = match.Index + match.Length;
                RunProperties runProperties = hContainer.Number is WText wText ? wText.properties : null;
                hContainer.Number = new WText(hContainer.Number.Text[patternEndIndex..], runProperties);
                qs.StartQuote = match.Groups["startQuote"].Value;
            }
            else
            {
                IList<IBlock> container;
                if (hContainer is Leaf leaf)
                    container = leaf.Contents;
                else if (hContainer is Branch branch)
                    container = branch.Intro;
                else
                    return;

                // This currently ONLY removes the start quote when the first block is a WLine
                if (container is null || container.Count == 0 || container.First() is null)
                    return;
                if (container.First() is not WLine line)
                    return;

                WLine enriched = EnrichFromBeginning.Enrich(line, QuotedStructure.QuotedStructureStartPattern(), ExtractStartQuoteConstructor);
                WBookmark bookmark = enriched.Contents.First(i => i is WBookmark) as WBookmark;
                qs.StartQuote = bookmark.Name;
                line.Contents = enriched.Contents.Where(i => i is not WBookmark);
                container.RemoveAt(0);
                container.Insert(0, line);
            }
        }

        // Removes any braced quoted structure info, and wraps the start quote in a WBookmark so it
        // can later be extracted and added to the startQuote attribute of the quoted structure.
        static IInline ExtractStartQuoteConstructor(IEnumerable<IInline> inlines)
        {
            if (inlines == null || !inlines.Any())
                return null;
            string text = "";
            foreach (IInline inline in inlines)
                text += IInline.GetText(inline);
            string startQuote = text.Split("}").LastOrDefault();
            // Todo: should probably use something other than WBookmark
            return new WBookmark { Name = startQuote };
        }

        /// <summary>
        /// This function removes the end quote and appended text from a line and returns a new line.
        /// It retains the extracted bits for insertion into the QuotedStructure model.
        /// </summary>
        private class ExtractAndReplace
        {
            internal string EndQuote { get; private set; } = null;
            internal AppendText AppendText { get; private set; } = null;

            internal string EndPattern { private get; set; }

            public ExtractAndReplace(string endPattern)
            {
                EndPattern = endPattern;
            }

            /*
              Note that the end quote and appended text may span across multiple of the line's inline children.
              For example, when portions of the line have different styling.
            */
            public WLine Invoke(WLine line)
            {
                Match match = null;
                string endText = "";
                int inlineCount = 0;
                foreach (IInline inline in line.Contents.Reverse())
                {
                    if (inline is not WText wText)
                        return null;
                    endText = wText.Text + endText;
                    inlineCount += 1;
                    match = Regex.Match(endText, EndPattern);
                    if (match.Success)
                        break;
                }
                if (match is null || !match.Success)
                    return null;

                EndQuote = match.Groups["endQuote"].Value;
                WText lastWText = line.Contents.Last() as WText;
                AppendText = new AppendText(match.Groups["followingText"].Value, lastWText.properties);
                WText replacement = new(endText[..match.Index], lastWText.properties);
                return WLine.Make(line, line.Contents.SkipLast(inlineCount).Append(replacement));
            }

        }

    }
