
using System.Collections.Generic;
using System.Linq;

using UK.Gov.Legislation.Judgments;
using UK.Gov.Legislation.Judgments.Parse;

namespace UK.Gov.Legislation.Lawmaker
{

    public partial class LegislationParser
    {

        private Schedules ParseSchedules(WLine line)
        {
            if (!PeekSchedules(line))
                return null;

            IFormattedText headingText = new WText("Schedules", null);
            ILine heading = WLine.Make(line, new List<IInline>(1) { headingText });

            var save1 = i;
            i += 1;

            List<IDivision> children = ParseSchedulesChildren();
            if (children.Count == 0)
            {
                i = save1;
                return null;
            }
            return new Schedules { Number = null, Heading = heading, Children = children };
        }

        private bool PeekSchedules(WLine line)
        {
            if (line is WOldNumberedParagraph np)
                return false;
            if (!IsCenterAligned(line))
                return false;
            if (i > Contents.Count - 3)
                return false;
            string heading = IgnoreQuotedStructureStart(line.NormalizedContent, quoteDepth);
            if (!Schedules.IsValidHeading(heading))
                return false;

            // Schedules container must be followed by Schedule
            if (Contents[i + 1] is not WLine line2)
                return false;
            if (!IsCenterAligned(line2))
                return false;
            return true;
        }

        internal List<IDivision> ParseSchedulesChildren()
        {
            frames.PushScheduleContext();
            List<IDivision> children = [];
            while (i < Contents.Count)
            {
                HContainer peek = PeekGroupingProvision();
                if (peek != null && !Schedules.IsValidChild(peek))
                    break;

                int save = i;
                IDivision next = ParseNextBodyDivision();
                if (!Schedules.IsValidChild(next))
                {
                    i = save;
                    break;
                }
                children.Add(next);
            }
            frames.Pop();
            return children;
        }

    }

}
