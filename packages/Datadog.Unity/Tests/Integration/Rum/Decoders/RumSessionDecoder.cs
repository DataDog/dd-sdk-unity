// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

using System.Collections.Generic;
using System.Linq;

namespace Datadog.Unity.Tests.Integration.Rum.Decoders
{
    public class RumSessionDecoder
    {
        public readonly List<RumViewVisit> Visits;

        public RumSessionDecoder(List<RumEventDecoder> events, bool shouldDiscardApplciationLaunch = true)
        {
            var orderedEvents = events.OrderBy(e => e.Date);

            var viewVisitsById = new Dictionary<string, RumViewVisit>();
            foreach (var rumEvent in orderedEvents)
            {
                if (rumEvent is RumViewEventDecoder viewEvent)
                {
                    var viewId = viewEvent.View.Id;
                    if (!viewVisitsById.ContainsKey(viewEvent.View.Id))
                    {
                        viewVisitsById.Add(viewId, new RumViewVisit(
                            id: viewId,
                            name: viewEvent.View.Name,
                            path: viewEvent.View.Path));
                    }

                    viewVisitsById[viewEvent.View.Id].ViewEvents.Add(viewEvent);
                }
            }

            var visits = viewVisitsById.Values.ToList();
            if (shouldDiscardApplciationLaunch)
            {
                visits = visits.Where(x => x.Name != "ApplicationLaunch").ToList();
            }

            Visits = visits;
        }
    }
}
