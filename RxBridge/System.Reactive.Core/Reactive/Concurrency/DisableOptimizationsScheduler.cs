﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the Apache 2.0 License.
// See the LICENSE file in the project root for more information. 

using System;
using System.Linq;

#if !NO_WEAKTABLE
using System.Runtime.CompilerServices;
#endif

namespace System.Reactive.Concurrency
{
    class DisableOptimizationsScheduler : SchedulerWrapper
    {
        private readonly Type[] _optimizationInterfaces;

        public DisableOptimizationsScheduler(IScheduler scheduler)
            : base(scheduler)
        {
            _optimizationInterfaces = Scheduler.OPTIMIZATIONS;
        }

        public DisableOptimizationsScheduler(IScheduler scheduler, Type[] optimizationInterfaces)
            : base(scheduler)
        {
            _optimizationInterfaces = optimizationInterfaces;
        }

#if !NO_WEAKTABLE
        public DisableOptimizationsScheduler(IScheduler scheduler, Type[] optimizationInterfaces, ConditionalWeakTable<IScheduler, IScheduler> cache)
            : base(scheduler, cache)
        {
            _optimizationInterfaces = optimizationInterfaces;
        }

        protected override SchedulerWrapper Clone(IScheduler scheduler, ConditionalWeakTable<IScheduler, IScheduler> cache)
        {
            return new DisableOptimizationsScheduler(scheduler, _optimizationInterfaces, cache);
        }
#else
        protected override SchedulerWrapper Clone(IScheduler scheduler)
        {
            return new DisableOptimizationsScheduler(scheduler, _optimizationInterfaces);
        }
#endif

        protected override bool TryGetService(IServiceProvider provider, Type serviceType, out object service)
        {
            service = null;
            return _optimizationInterfaces.Contains(serviceType);
        }
    }
}
