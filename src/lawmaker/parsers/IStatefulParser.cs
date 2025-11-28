#nullable enable

namespace UK.Gov.Legislation.Lawmaker;

using System;
using System.Collections.Generic;

public interface IStatefulParser<T, S> : IParser<T>
{

    public struct UnifiedStrategy<R>
    {
        private readonly Func<IStatefulParser<T, S>, (R?, StateUpdate?)> _invoke;

        internal UnifiedStrategy(StatefulParseStrategy<R> stateful)
            => _invoke = stateful.Invoke;

        public UnifiedStrategy(ParseStrategy<R> stateless)
            => _invoke = parser => (stateless.Invoke(parser), default);

        public readonly (R?, StateUpdate?) Invoke(IStatefulParser<T, S> parser) => _invoke(parser);

        public static implicit operator UnifiedStrategy<R>(StatefulParseStrategy<R> s)
            => new(s);

        public static implicit operator UnifiedStrategy<R>(ParseStrategy<R> s)
            => new(s);

    }

    public S State { get;}

    public delegate (R?, StateUpdate?) StatefulParseStrategy<R>(IStatefulParser<T, S> statefulParser);

    public delegate S StateUpdate(S state);

    public R? Match<R>(params UnifiedStrategy<R>[] strategy);

    public List<R>? MatchWhile<R>(params UnifiedStrategy<R>[] strategies)
        => MatchWhile(_ => true, strategies);

    public List<R>? MatchWhile<R>(Predicate<(T, S)> condition, params UnifiedStrategy<R>[] strategies);
}