﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information. 

#if !NO_PERF
using System.Threading;

namespace System.Reactive
{
   /// <summary>
   /// Base class for implementation of query operators, providing a lightweight sink that can be disposed to mute the outgoing observer.
   /// </summary>
   /// <typeparam name="TSource">Type of the resulting sequence's elements.</typeparam>
   /// <remarks>Implementations of sinks are responsible to enforce the message grammar on the associated observer. Upon sending a terminal message, a pairing Dispose call should be made to trigger cancellation of related resources and to mute the outgoing observer.</remarks>
    internal abstract class Sink<TSource> : IDisposable
    {
#if !NO_VOLATILE
      protected internal volatile IObserver<TSource> _observer;
#else
      private IDisposable _cancel;
      protected internal IObserver<TSource> _observer;
#endif

      public Sink(IObserver<TSource> observer, IDisposable cancel)
        {
            _observer = observer;
            _cancel = cancel;
        }

        public virtual void Dispose()
        {
            _observer = NopObserver<TSource>.Instance;

            var cancel = Interlocked.Exchange(ref _cancel, null);
            if (cancel != null)
            {
                cancel.Dispose();
            }
        }

        public IObserver<TSource> GetForwarder()
        {
            return new Impl(this);
        }

        //#RX_BRIDGE This class used to be called "_" which is both crazy and incompatible with the bridge build....
        class Impl : IObserver<TSource>
        {
            private readonly Sink<TSource> _forward;

            public Impl(Sink<TSource> forward)
            {
                _forward = forward;
            }

            public void OnNext(TSource value)
            {
                _forward._observer.OnNext(value);
            }

            public void OnError(Exception error)
            {
                _forward._observer.OnError(error);
                _forward.Dispose();
            }

            public void OnCompleted()
            {
                _forward._observer.OnCompleted();
                _forward.Dispose();
            }
        }
    }
}
#endif