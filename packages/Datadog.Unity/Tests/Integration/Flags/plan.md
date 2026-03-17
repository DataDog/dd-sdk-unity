# Flags Module — Integration Test Plan

## Context

Existing tests in `Tests/Flags/` are all pure NUnit (no network I/O):
`ExposureTrackerTests`, `EvaluationAggregatorTests`, `FlagEvaluationTests`,
`PrecomputeParserTests`, `JsonSerializationTests`.

Nothing tests the actual HTTP layer end-to-end. This plan covers that gap.

---

## Files to create / modify

```
tools/mock_server/app.py                                      MODIFY
packages/Datadog.Unity/Tests/Integration/
  MockServerHelper.cs                                         MODIFY
  Flags/
    FlagsIntegrationTests.cs                                  CREATE
    Decoders/
      ExposureEventDecoder.cs                                 CREATE
      EvaluationEventDecoder.cs                               CREATE
```

---

## Mock server change (`app.py`)

The mock server always returns `202 "OK - request recorded"` for every POST.
That's fine for exposure/evaluation intake (fire-and-forget), but the precompute
fetcher POSTs and then parses the response body as JSON. A `202 "OK"` body
parses to an empty flags dict, so `SetEvaluationContext` would always call
`onComplete(false)`.

**Fix**: add a `/configure_response` endpoint that stores a per-path response,
and check that store in `generic_post` before falling back to the default `202`.
Also clear configured responses when `/reset` is called.

```python
# module scope
configured_responses = {}   # path -> { status, body, content_type }

@app.route('/configure_response', methods=['POST'])
def configure_response():
    data = request.get_json()
    configured_responses[data['path']] = data
    return flask.Response('OK', status=200)

# in generic_post, before the default response block:
if gr.path in configured_responses:
    cfg = configured_responses[gr.path]
    resp = flask.Response(cfg['body'], status=cfg.get('status', 200))
    resp.headers['Content-Type'] = cfg.get('content_type', 'application/json')
    add_cors_headers(resp)
    return resp

# in reset():
configured_responses.clear()
```

---

## MockServerHelper.cs change

Add one coroutine alongside `Clear()` and `PollRequests()`:

```csharp
public IEnumerator ConfigureResponse(string path, int status, string body, string contentType = "application/json")
{
    var payload = JsonConvert.SerializeObject(new { path, status, body, content_type = contentType });
    var request = new UnityWebRequest($"{_endpoint}/configure_response", "POST");
    request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(payload));
    request.downloadHandler = new DownloadHandlerBuffer();
    request.SetRequestHeader("Content-Type", "application/json");
    yield return request.SendWebRequest();
}
```

---

## Precompute response payload

Configure the mock to return this body for `POST /precompute-assignments`:

```json
{
  "data": {
    "id": "test_subject",
    "type": "precomputed-assignments",
    "attributes": {
      "createdAt": 1731939805123,
      "environment": { "name": "prod" },
      "flags": {
        "string-flag": {
          "allocationKey": "allocation-123",
          "variationKey": "variation-123",
          "variationType": "STRING",
          "variationValue": "red",
          "doLog": true,
          "reason": "TARGETING_MATCH"
        },
        "boolean-flag": {
          "allocationKey": "allocation-124",
          "variationKey": "variation-124",
          "variationType": "BOOLEAN",
          "variationValue": true,
          "doLog": true,
          "reason": "TARGETING_MATCH"
        },
        "integer-flag": {
          "allocationKey": "allocation-125",
          "variationKey": "variation-125",
          "variationType": "NUMBER",
          "variationValue": 42,
          "doLog": true,
          "reason": "TARGETING_MATCH"
        },
        "numeric-flag": {
          "allocationKey": "allocation-126",
          "variationKey": "variation-126",
          "variationType": "NUMBER",
          "variationValue": 3.14,
          "doLog": true,
          "reason": "TARGETING_MATCH"
        },
        "json-flag": {
          "allocationKey": "allocation-127",
          "variationKey": "variation-127",
          "variationType": "OBJECT",
          "variationValue": { "key": "value", "prop": 123 },
          "doLog": true,
          "reason": "TARGETING_MATCH"
        }
      }
    }
  }
}
```

---

## Runtime configuration

The `integration_test.py` script injects these values into `DatadogSettings.asset`:

| Field            | Value                    |
|------------------|--------------------------|
| `ClientToken`    | `"fake-client-token"`    |
| `RumApplicationId` | `"fake-rum-application-id"` |
| `Env`            | `"integration-test"`     |
| `CustomEndpoint` | `http://<host>:<port>`   |

In each test, construct endpoint URLs from `DatadogConfigurationOptions.CustomEndpoint`:

```csharp
var mockBase = DatadogConfigurationOptions.Load().CustomEndpoint;
var flagsConfig = new FlagsConfiguration
{
    CustomFlagsEndpoint      = $"{mockBase}/precompute-assignments",
    CustomExposureEndpoint   = $"{mockBase}/api/v2/exposures",
    CustomEvaluationEndpoint = $"{mockBase}/api/v2/flagevaluation",
    TrackExposures           = true,
    TrackEvaluations         = true,
    EvaluationFlushIntervalSeconds = 60f, // disable timer; use explicit Flush()
};
```

---

## Flush strategy

`EvaluationAggregator` clamps the flush interval to `[1.0f, 60.0f]` seconds.
For most tests, skip the timer entirely and call `DdFlags.GetClient().Flush()`
directly — the test assembly has access via `InternalsVisibleTo("com.datadoghq.unity.tests")`.

For the one test that validates timer-based flushing, set
`EvaluationFlushIntervalSeconds = 1.0f` and `yield return new WaitForSeconds(1.5f)`.

---

## Test cases

### Setup pattern (shared across all tests)

```csharp
yield return mockServerHelper.Clear();
yield return mockServerHelper.ConfigureResponse(
    "/precompute-assignments", 200, precomputePayloadJson);
DdFlags.Enable(flagsConfig);
DdFlags.CreateClient();
var fetchSuccess = false;
DdFlags.SetEvaluationContext(
    new FlagsEvaluationContext("user-123"),
    onComplete: ok => fetchSuccess = ok);
yield return new WaitUntil(() => fetchSuccess || timedOut);
```

---

### Group 1 — Precompute request shape

**`PrecomputeRequest_HasCorrectHeaders`**
Poll `/inspect_requests/`, find request to `/precompute-assignments`.
Assert: `Content-Type: application/vnd.api+json`, `dd-client-token: fake-client-token`,
`dd-application-id: fake-rum-application-id`.

**`PrecomputeRequest_BodyHasCorrectJsonApiShape`**
Assert body: `data.type == "precompute-assignments-request"`,
`data.attributes.env.name == "integration-test"`,
`data.attributes.subject.targeting_key == "user-123"`.

**`PrecomputeRequest_IncludesContextAttributes`**
Call `SetEvaluationContext` with `attributes: { "plan": "premium" }`.
Assert: `data.attributes.subject.targeting_attributes.plan == "premium"`.

---

### Group 2 — Flags evaluable after fetch

**`SetEvaluationContext_Success_FlagsAvailableViaOpenFeature`**
After `onComplete(true)`, assert via `Api.Instance.GetClient()`:
- `GetBooleanValueAsync("boolean-flag", false)` → `true`
- `GetStringValueAsync("string-flag", "x")` → `"red"`
- `GetIntegerValueAsync("integer-flag", 0)` → `42`
- `GetDoubleValueAsync("numeric-flag", 0.0)` → `3.14` (±0.001)
- `GetObjectDetailsAsync("json-flag", null).Variant` → `"variation-127"`

**`SetEvaluationContext_ServerError_StateIsError`**
Configure mock to return 500. Assert: `onComplete(false)`,
`DdFlags.GetClient().State == Error`.

**`SetEvaluationContext_ServerErrorAfterCache_StateIsStale`**
Successful fetch first. Then configure 500. Call `SetEvaluationContext` again.
Assert: `State == Stale`. Old flag values still evaluable.

---

### Group 3 — Exposure telemetry

**`BooleanFlagEvaluation_SendsExposureEvent`**
Evaluate `boolean-flag`. Poll `/api/v2/exposures`.
Parse body with `ExposureEventDecoder` (NDJSON line).
Assert: `flag.key == "boolean-flag"`, `allocation.key == "allocation-124"`,
`variant.key == "variation-124"`, `subject.id == "user-123"`.
Assert headers: `dd-api-key == "fake-client-token"`,
`dd-evp-origin == "unity"`, `Content-Type: text/plain; charset=utf-8`.

**`SameFlag_EvaluatedMultipleTimes_SendsOnlyOneExposure`**
Evaluate `boolean-flag` 5 times. Poll.
Assert: exactly 1 exposure record across all requests.

**`ContextChange_SendsFreshExposure`**
Evaluate `boolean-flag` for `user-A`. `SetEvaluationContext("user-B")`.
Evaluate `boolean-flag` again. Assert: 2 exposure records,
`subject.id` values are `"user-A"` and `"user-B"`.

---

### Group 4 — Evaluation telemetry

**`FlagEvaluation_AfterExplicitFlush_SendsEvaluationBatch`**
Evaluate `string-flag` 3 times. Call `DdFlags.GetClient().Flush()`.
Poll `/api/v2/flagevaluation`. Parse with `EvaluationEventDecoder`.
Assert: `context` block has `env`, `service`, `device`, `os` fields.
Assert: `flagEvaluations` has one entry for `string-flag` with
`evaluation_count == 3`, `variant.key == "variation-123"`,
`targeting_key == "user-123"`, `runtime_default_used` absent.

**`MultipleFlags_ProduceSeparateEvaluationRecords`**
Evaluate `boolean-flag` ×2 and `string-flag` ×1. Flush. Poll.
Assert: 2 records in `flagEvaluations`, counts correct.

**`MissingFlag_EvaluationRecord_HasRuntimeDefaultUsedTrue`**
Evaluate `"nonexistent-flag"`. Flush. Poll.
Assert: `runtime_default_used: true`, `error.message == "FLAG_NOT_FOUND"`,
no `variant` or `allocation` fields.

**`Shutdown_FlushesEvaluationsPendingOnTimer`**
Set flush interval to 60s. Evaluate a flag. Call `DdFlags.Shutdown()`.
Assert: evaluation batch arrives without waiting for timer.

**`TimerFlush_SendsEvaluationBatchAfterInterval`**
Set `EvaluationFlushIntervalSeconds = 1.0f`. Evaluate a flag.
`yield return new WaitForSeconds(1.5f)`. Assert: batch arrived
without explicit `Flush()` call.

---

### Group 5 — Evaluation EVP headers

**`EvaluationBatch_HasCorrectEvpHeaders`**
Find request to `/api/v2/flagevaluation`.
Assert: `dd-api-key == "fake-client-token"`, `dd-evp-origin == "unity"`,
`dd-evp-origin-version` matches `DatadogSdk.SdkVersion`,
`Content-Type: application/json`.

---

## Decoders

### `ExposureEventDecoder.cs`

Parses one NDJSON line from `MockServerSchema.Data`:

```csharp
string FlagKey        // flag.key
string AllocationKey  // allocation.key
string VariantKey     // variant.key
string SubjectId      // subject.id
Dictionary<string,object> SubjectAttributes  // subject.attributes
```

### `EvaluationEventDecoder.cs`

Parses the top-level batch JSON body from `MockServerSchema.Data`:

```csharp
class BatchedEvaluations {
    BatchContext Context;                   // device/os/service/version/env
    List<EvaluationRecord> FlagEvaluations;
}
class EvaluationRecord {
    string   FlagKey, VariantKey, AllocationKey, TargetingKey, ErrorMessage;
    long     FirstEvaluation, LastEvaluation;
    int      EvaluationCount;
    bool?    RuntimeDefaultUsed;
}
```

A helper on `MockServerHelper` collects all records across multiple
requests to the same endpoint path.
