﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information. 

#if !NO_PERF
using System;
using System.Collections.Generic;

namespace System.Reactive.Linq.ObservableImpl
{
    class OnErrorResumeNext<TSource> : Producer<TSource>
    {
        private readonly IEnumerable<IObservable<TSource>> _sources;

        public OnErrorResumeNext(IEnumerable<IObservable<TSource>> sources)
        {
            _sources = sources;
        }

        protected override IDisposable Run(IObserver<TSource> observer, IDisposable cancel, Action<IDisposable> setSink)
        {
            var sink = new _(observer, cancel);
            setSink(sink);
            return sink.Run(_sources);
        }

        class _ : TailRecursiveSink<TSource>
        {
            public _(IObserver<TSource> observer, IDisposable cancel)
                : base(observer, cancel)
            {
            }

            protected override IEnumerable<IObservable<TSource>> Extract(IObservable<TSource> source)
            {
                var oern = source as OnErrorResumeNext<TSource>;
                if (oern != null)
                    return oern._sources;

                return null;
            }

            public override void OnNext(TSource value)
            {
                base._observer.OnNext(value);
            }

            public override void OnError(Exception error)
            {
                _recurse();
            }

            public override void OnCompleted()
            {
                _recurse();
            }

            protected override bool Fail(Exception error)
            {
                //
                // Note that the invocation of _recurse in OnError will
                // cause the next MoveNext operation to be enqueued, so
                // we will still return to the caller immediately.
                //
                OnError(error);
                return true;
            }
        }
    }
}
#endif