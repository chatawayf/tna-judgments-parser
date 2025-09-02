using System.Collections.Generic;

namespace UK.Gov.Legislation.Judgments;

internal interface ISplittable
{
    IEnumerable<T> Split<T>(params int[] indexes);
}