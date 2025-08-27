#nullable enable

namespace UK.Gov.Legislation.Lawmaker;

using System;
using System.Collections.Generic;
using System.Linq;
using UK.Gov.Legislation.Judgments;
using UK.Gov.Legislation.Judgments.Parse;


public class BlockParser : IParser<IBlock>
{
    private int i = 0;
    internal readonly List<IBlock> Contents;

    public BlockParser(IEnumerable<IBlock> contents)
    {
        Contents = contents.ToList();
    }

    public int Save() => i;

    public void Restore(int save) => i = save;

    // Get the current block the parser is at
    public IBlock? Current() => IsInRange(i) ? Contents[i] : null;

    public bool IsAtEnd() => i >= Contents.Count;


    // Get the block that is `num` positions away.
    // `Peek(0)` will show the current block without advancing (same as `Current()`).
    // At the moment num can be negative to look behind. There are currently no safeguards
    // for checking within the bounds of the Document Body.
    public IBlock Peek(int num = 1) => Contents[i + num];

    /// <summary>
    ///
    /// </summary>
    /// <typeparam name="R"></typeparam>
    /// <param name="strategy"></param>
    /// <returns></returns>
    public R? Peek<R>(IParser<IBlock>.ParseStrategy<R> strategy)
    {
        int save = Save();
        R? result = strategy(this);
        Restore(save);
        return result;

    }

    public IBlock? Advance()
    {
        IBlock? current = Current();
        i++;
        return current;
    }
    // Advance the parser forward by `num` and returns to blocks passed.
    public IEnumerable<IBlock> Advance(int num)
    {
        if (num <= 0) return [];
        var slice = Contents[i..(i + num)];
        i += slice.Count;
        return slice;
    }

    // Move the parser forward while `condition` is true and return everything advanced over
    public List<IBlock> AdvanceWhile(Predicate<IBlock> condition)
    {
        IEnumerable<IBlock> list = Contents[i..]
            .TakeWhile(block => condition(block) && !IsAtEnd());
        Advance(list.Count());
        return list.ToList();
    }

    public R? Match<R>(IParser<IBlock>.ParseStrategy<R> strategy)
    {
        // TODO: memoize here if needed
        int save = this.Save();
        R? block = strategy(this);
        if (block == null) this.Restore(save);
        return block;
    }

    public R? Match<R>(params IParser<IBlock>.ParseStrategy<R>[] strategies)
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

    public List<R> MatchWhile<R>(Predicate<IBlock> condition, params IParser<IBlock>.ParseStrategy<R>[] strategies)
    {
        List<R> matches = [];
        while (Current() is IBlock r
            && condition(r)
            && Match(strategies) is R match
            && !IsAtEnd())
        {
            matches.Add(match);
        }
        return matches;
    }

    public static IBlock? Identity(IParser<IBlock> parser)
    {
        return parser.Advance() as IBlock;
    }
    private bool IsInRange(int i) => i >= 0 && i < Contents.Count;

    public List<R> MatchWhile<R>(Predicate<R> condition, params IParser<IBlock>.ParseStrategy<R>[] strategies)
    {
        List<R> matches = [];
        while (Current() is R r
            && condition(r)
            && Match(strategies) is R match
            && !IsAtEnd())
        {
            matches.Add(match);
        }
        return matches;
    }
}
