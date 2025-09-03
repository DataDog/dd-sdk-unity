// Unless explicitly stated otherwise all files in this repository are licensed under the Apache License Version 2.0.
// This product includes software developed at Datadog (https://www.datadoghq.com/).
// Copyright 2023-Present Datadog, Inc.

using System.Collections.Generic;
using System.Linq;
using Datadog.Unity.Rum;
using UnityEngine;

namespace Datadog.Unity.Tests.Integration.Rum.Decoders
{
    public class RumSessionDecoder
    {
        public readonly List<RumViewVisit> Visits;

        public RumSessionDecoder(List<RumEventDecoder> events, bool shouldDiscardApplicationLaunch = true)
        {
            var orderedEvents = events.OrderBy(e => e.Date);

            var viewVisitsById = new Dictionary<string, RumViewVisit>();
            foreach (var rumEvent in orderedEvents.Where(e => e is RumViewEventDecoder))
            {
                var viewEvent = (RumViewEventDecoder)rumEvent;

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

            // Add other events to their view visits
            foreach (var rumEvent in orderedEvents.Where(e => e is not RumViewEventDecoder))
            {
                string viewId = rumEvent switch
                {
                    RumActionEventDecoder actionEvent => actionEvent.ViewInfo.Id,
                    RumErrorEventDecoder errorEvent => errorEvent.ViewInfo.Id,
                    RumResourceEventDecoder resourceEvent => resourceEvent.ViewInfo.Id,
                    _ => null
                };

                if (viewId != null && viewVisitsById.TryGetValue(viewId, out var visit))
                {
                    switch (rumEvent)
                    {
                        case RumActionEventDecoder actionEvent:
                            visit.ActionEvents.Add(actionEvent);
                            break;
                        case RumErrorEventDecoder errorEvent:
                            visit.ErrorEvents.Add(errorEvent);
                            break;
                        case RumResourceEventDecoder resourceEvent:
                            visit.ResourceEvents.Add(resourceEvent);
                            break;
                    }
                }
                else
                {
                    Debug.Log($"Could not find view for event {rumEvent} with viewId {viewId}! Skipping!");
                }
            }

            var visits = viewVisitsById.Values.ToList();
            if (shouldDiscardApplicationLaunch)
            {
                visits = visits.Where(x => x.Name != "ApplicationLaunch").ToList();
            }

            Visits = visits;
        }
    }
}
