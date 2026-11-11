# Migration from 1.x to 2.0

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
