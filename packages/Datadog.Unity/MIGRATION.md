# Migration from 1.x to 2.0

## User information

`SetUserInfo` now requires a non-null, non-empty `id`. Invalid calls log a warning
and leave the current user unchanged. Use `ClearUserInfo()` to clear identity.

Empty names and emails are omitted from events.

The extra-info keys `id`, `name`, `email`, and `anonymous_id` are now ignored with a
warning. Use `SetUserInfo` parameters for identity fields and rename custom
attributes that use these reserved keys.

## Android attribute values

Enumerable collections in Android attributes, including lists, arrays, sets,
and LINQ sequences, are now sent as JSON arrays instead of their C# `ToString()`
representations. String values remain strings.

Additional dictionary types implementing `System.Collections.IDictionary`,
such as `Dictionary<string, int>`, are now sent as JSON objects instead of strings.
Dictionary keys must be strings. Null values inside attribute maps and
collections are now sent as null instead of the string `"null"`.

Review queries, facets, and processing rules that depend on the old string
values. If you need string attributes, convert values explicitly before passing
them to the SDK. Use `value?.ToString() ?? "null"` to keep the literal string for
null values.

## Distributed tracing

`TraceContext.parentSpanId` and the `parentSpanId` constructor parameter have been
removed. Remove the third argument when constructing a trace context:

```diff
-new TraceContext(traceId, spanId, parentSpanId, sampled)
+new TraceContext(traceId, spanId, sampled)
```

With `TraceContextInjection.All`, both B3 formats now include trace and span IDs
for unsampled requests, with sampling set to `0`.

## WebGL Changes

Update both Browser SDK scripts in your WebGL template. The example uses the US1
site; use the CDN site for your Datadog organization.

```diff
-<script src="https://www.datadoghq-browser-agent.com/us1/v6/datadog-logs.js"></script>
-<script src="https://www.datadoghq-browser-agent.com/us1/v6/datadog-rum-slim.js"></script>
+<script src="https://www.datadoghq-browser-agent.com/us1/v7/datadog-logs.js" crossorigin="anonymous"></script>
+<script src="https://www.datadoghq-browser-agent.com/us1/v7/datadog-rum-slim.js" crossorigin="anonymous"></script>
```

The v7 CDN bundles use ESM dynamic imports to load async chunks, so
`crossorigin="anonymous"` is required on the script tags. The Browser SDK requires
Chrome 80+, Firefox 78+, or Safari 14+.

If you allowlist cookies, add `_dd_s_v2` alongside `_dd_s`. The Browser SDK migrates
existing sessions automatically on first load; rolling back to v6 starts a new session.

If you configure `FirstPartyHosts` with tracing headers, the Browser SDK now sends
an additional `baggage` header by default. If your server restricts
`Access-Control-Allow-Headers`, add `baggage` to the allowlist.

Logs now require cookie access with the default configuration. `console.error`
is no longer forwarded by default, so Unity C# exceptions reported to RUM are not
also captured as Logs.

Trace sampling now uses the RUM session ID. Review cross-product retention
filters, since more linked traces may be retained.

See the [Browser SDK upgrade guide](https://docs.datadoghq.com/real_user_monitoring/guide/browser-sdk-upgrade/)
for the full list of changes.
