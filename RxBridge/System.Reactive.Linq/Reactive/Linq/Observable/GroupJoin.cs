﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information. 

#if !NO_PERF
using System;
using System.Collections.Generic;
using System.Reactive.Disposables;
using System.Reactive.Subjects;

namespace System.Reactive.Linq.ObservableImpl
{
    class GroupJoin<TLeft, TRight, TLeftDuration, TRightDuration, TResult> : Producer<TResult>
    {
        private readonly IObservable<TLeft> _left;
        private readonly IObservable<TRight> _right;
        private readonly Func<TLeft, IObservable<TLeftDuration>> _leftDurationSelector;
        private readonly Func<TRight, IObservable<TRightDuration>> _rightDurationSelector;
        private readonly Func<TLeft, IObservable<TRight>, TResult> _resultSelector;

        public GroupJoin(IObservable<TLeft> left, IObservable<TRight> right, Func<TLeft, IObservable<TLeftDuration>> leftDurationSelector, Func<TRight, IObservable<TRightDuration>> rightDurationSelector, Func<TLeft, IObservable<TRight>, TResult> resultSelector)
        {
            _left = left;
            _right = right;
            _leftDurationSelector = leftDurationSelector;
            _rightDurationSelector = rightDurationSelector;
            _resultSelector = resultSelector;
        }

        protected override IDisposable Run(IObserver<TResult> observer, IDisposable cancel, Action<IDisposable> setSink)
        {
            var sink = new _(this, observer, cancel);
            setSink(sink);
            return sink.Run();
        }

        class _ : Sink<TResult>
        {
            private readonly GroupJoin<TLeft, TRight, TLeftDuration, TRightDuration, TResult> _parent;

            public _(GroupJoin<TLeft, TRight, TLeftDuration, TRightDuration, TResult> parent, IObserver<TResult> observer, IDisposable cancel)
                : base(observer, cancel)
            {
                _parent = parent;
            }

            private object _gate;
            private CompositeDisposable _group;
            private RefCountDisposable _refCount;

            private int _leftID;
            private SortedDictionary<int, IObserver<TRight>> _leftMap;

            private int _rightID;
            private SortedDictionary<int, TRight> _rightMap;

            public IDisposable Run()
            {
                _gate = new object();
                _group = new CompositeDisposable();
                _refCount = new RefCountDisposable(_group);

                var leftSubscription = new SingleAssignmentDisposable();
                _group.Add(leftSubscription);
                _leftID = 0;
                _leftMap = new SortedDictionary<int, IObserver<TRight>>();

                var rightSubscription = new SingleAssignmentDisposable();
                _group.Add(rightSubscription);
                _rightID = 0;
                _rightMap = new SortedDictionary<int, TRight>();

                leftSubscription.Disposable = _parent._left.SubscribeSafe(new LeftObserver(this, leftSubscription));
                rightSubscription.Disposable = _parent._right.SubscribeSafe(new RightObserver(this, rightSubscription));

                return _refCount;
            }

            class LeftObserver : IObserver<TLeft>
            {
                private readonly _ _parent;
                private readonly IDisposable _self;

                public LeftObserver(_ parent, IDisposable self)
                {
                    _parent = parent;
                    _self = self;
                }

                private void Expire(int id, IObserver<TRight> group, IDisposable resource)
                {
                    lock (_parent._gate)
                    {
                        if (_parent._leftMap.Remove(id))
                        {
                            group.OnCompleted();
                        }
                    }

                    _parent._group.Remove(resource);
                }

                public void OnNext(TLeft value)
                {
                    var s = new Subject<TRight>();
                    var id = 0;
                    var rightID = 0;
                    lock (_parent._gate)
                    {
                        id = _parent._leftID++;
                        rightID = _parent._rightID;
                        _parent._leftMap.Add(id, s);
                    }

                    var window = new WindowObservable<TRight>(s, _parent._refCount);

                    // BREAKING CHANGE v2 > v1.x - Order of evaluation or the _leftDurationSelector and _resultSelector now consistent with Join.
                    var md = new SingleAssignmentDisposable();
                    _parent._group.Add(md);

                    var duration = default(IObservable<TLeftDuration>);
                    try
                    {
                        duration = _parent._parent._leftDurationSelector(value);
                    }
                    catch (Exception exception)
                    {
                        OnError(exception);
                        return;
                    }

                    // BREAKING CHANGE v2 > v1.x - The duration sequence is subscribed to before the result sequence is evaluated.
                    md.Disposable = duration.SubscribeSafe(new Delta(this, id, s, md));

                    var result = default(TResult);
                    try
                    {
                        result = _parent._parent._resultSelector(value, window);
                    }
                    catch (Exception exception)
                    {
                        OnError(exception);
                        return;
                    }

                    lock (_parent._gate)
                    {
                        _parent._observer.OnNext(result);

                        foreach (var rightValue in _parent._rightMap)
                        {
                            if (rightValue.Key < rightID)
                            {
                                s.OnNext(rightValue.Value);
                            }
                        }
                    }
                }

                class Delta : IObserver<TLeftDuration>
                {
                    private readonly LeftObserver _parent;
                    private readonly int _id;
                    private readonly IObserver<TRight> _group;
                    private readonly IDisposable _self;

                    public Delta(LeftObserver parent, int id, IObserver<TRight> group, IDisposable self)
                    {
                        _parent = parent;
                        _id = id;
                        _group = group;
                        _self = self;
                    }

                    public void OnNext(TLeftDuration value)
                    {
                        _parent.Expire(_id, _group, _self);
                    }

                    public void OnError(Exception error)
                    {
                        _parent.OnError(error);
                    }

                    public void OnCompleted()
                    {
                        _parent.Expire(_id, _group, _self);
                    }
                }

                public void OnError(Exception error)
                {
                    lock (_parent._gate)
                    {
                        foreach (var o in _parent._leftMap)
                        {
                            o.Value.OnError(error);
                        }

                        _parent._observer.OnError(error);
                        _parent.Dispose();
                    }
                }

                public void OnCompleted()
                {
                    lock (_parent._gate)
                    {
                        _parent._observer.OnCompleted();
                        _parent.Dispose();
                    }

                    _self.Dispose();
                }
            }

            class RightObserver : IObserver<TRight>
            {
                private readonly _ _parent;
                private readonly IDisposable _self;

                public RightObserver(_ parent, IDisposable self)
                {
                    _parent = parent;
                    _self = self;
                }

                private void Expire(int id, IDisposable resource)
                {
                    lock (_parent._gate)
                    {
                        _parent._rightMap.Remove(id);
                    }

                    _parent._group.Remove(resource);
                }

                public void OnNext(TRight value)
                {
                    var id = 0;
                    var leftID = 0;
                    lock (_parent._gate)
                    {
                        id = _parent._rightID++;
                        leftID = _parent._leftID;
                        _parent._rightMap.Add(id, value);
                    }

                    var md = new SingleAssignmentDisposable();
                    _parent._group.Add(md);

                    var duration = default(IObservable<TRightDuration>);
                    try
                    {
                        duration = _parent._parent._rightDurationSelector(value);
                    }
                    catch (Exception exception)
                    {
                        OnError(exception);
                        return;
                    }

                    md.Disposable = duration.SubscribeSafe(new Delta(this, id, md));

                    lock (_parent._gate)
                    {
                        foreach (var o in _parent._leftMap)
                        {
                            if (o.Key < leftID)
                            {
                                o.Value.OnNext(value);
                            }
                        }
                    }
                }

                class Delta : IObserver<TRightDuration>
                {
                    private readonly RightObserver _parent;
                    private readonly int _id;
                    private readonly IDisposable _self;

                    public Delta(RightObserver parent, int id, IDisposable self)
                    {
                        _parent = parent;
                        _id = id;
                        _self = self;
                    }

                    public void OnNext(TRightDuration value)
                    {
                        _parent.Expire(_id, _self);
                    }

                    public void OnError(Exception error)
                    {
                        _parent.OnError(error);
                    }

                    public void OnCompleted()
                    {
                        _parent.Expire(_id, _self);
                    }
                }

                public void OnError(Exception error)
                {
                    lock (_parent._gate)
                    {
                        foreach (var o in _parent._leftMap)
                        {
                            o.Value.OnError(error);
                        }

                        _parent._observer.OnError(error);
                        _parent.Dispose();
                    }
                }

                public void OnCompleted()
                {
                    _self.Dispose();
                }
            }
        }
    }
}
#endif