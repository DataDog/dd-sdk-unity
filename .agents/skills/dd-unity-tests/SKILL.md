---
name: dd-unity-tests
description: Run and diagnose dd-sdk-unity unit tests and platform integration tests. Use when validating SDK changes, choosing player coverage, filtering tests, or investigating an incomplete local test run.
---

# Run Datadog Unity tests

Use the repository's `dd-unity-*` runners. They own test selection, player setup,
mock-server lifecycle, and result collection.

## Resolve the current setup

Run commands from the checkout being tested with its Python environment activated.

Use runner defaults unless the user requests a particular project or Unity
version. Let the runner resolve installed Editors; do not reuse a previous chat's
install path, patch version, simulator ID, IP address, or test count.

| Switch | Runner | Use |
| --- | --- | --- |
| `--project`, `-p` | Both | Override the project path when requested. |
| `--unity-version`, `-u` | Both | Select a requested Editor version; accepts a full version or prefix. |
| `--platform` | Unit | `EditMode` or `PlayMode`; repeat to select both. Omit to run both. |
| `--platform` | Integration | Required: `android`, `ios`, `webgl`, or `macos`. |
| `--target` | Integration | Mobile target: `simulator` (default) or `device` for a connected phone. |
| `-filter` | Both | Quoted, comma-separated test names or patterns, e.g. `'Fixture.Test,OtherFixture'`. Omit for the full suite. |
| `--outputDir` | Both | Override the output directory. Unit runs add `editmode/` and `playmode/`; integration runs use the directory directly. Each contains `junit.xml` and stage logs. |

Check that the target project is closed before a headless run. Coordinate closing
an open Editor without discarding unsaved work. Do not silently copy the project
or switch checkouts to avoid its lock. If the user requests an Editor run, follow
the contributor guide's Editor workflow and run integration tests on the target
player; Editor PlayMode alone does not validate native or WebGL bridges.

## Choose coverage

For C# changes, run `dd-unity-unit-test`. It runs EditMode and PlayMode. Use
`--platform EditMode` or `--platform PlayMode` only for an intentionally narrower
check, and report that scope.

For platform-specific behavior, also run the affected player. For shared bridge
or integration-test changes, cover the affected platforms; honor any explicit
matrix the user requested. A successful unit run does not replace player tests.

| Coverage | Command | Requirements and runner behavior |
| --- | --- | --- |
| Android emulator | `dd-unity-integration-test --platform android` | Android modules and SDK from the contributor guide. Let the runner start and stop its emulator. |
| iOS Simulator | `dd-unity-integration-test --platform ios --target simulator` | macOS, Xcode, iOS Build Support, and an available Simulator runtime. The runner exports, builds, and runs an owned Simulator. |
| Physical mobile device | `dd-unity-integration-test --platform android --target device` or `--platform ios --target device` | Use when the user requests a connected phone or the test needs hardware. Check debugging readiness and, for iOS, signing. |
| WebGL | `dd-unity-integration-test --platform webgl` | WebGL Build Support and installed Chrome. The runner exports and launches its own headless Chrome through Playwright. |
| macOS player | `dd-unity-integration-test --platform macos` | macOS build support and the pinned desktop native assets. Runs a standalone player. |

Stage or verify pinned native assets using `dd-unity-manage-sdk-dependencies`
as documented; do not change SDK pins to make a test run. Report unavailable
prerequisites or platforms.

For filtered runs, confirm the requested tests actually appear in the results.
Follow a focused run with the relevant full suite when the change needs broader
coverage.

## Run and collect evidence

- Run Unity suites sequentially on a shared machine. Worktrees can still share
  mock-server ports and devices; separate checkout paths do not isolate those.
- Let the integration runner manage the mock server and player. Use
  `dd-unity-integration-test-setup start|status|stop --project <project>` only when
  managing the server separately for an Editor run or diagnosis. Read its reported
  endpoint; do not start another Flask server or hard-code localhost for a phone.
- Results and logs default to `build/test-results/unit/<mode>/` or
  `build/test-results/integration/<platform>/`. Use `--outputDir` to keep distinct
  directories when comparing runs.
- Wait for the runner to finish. Follow its logs during long exports and player
  runs. An export, player launch, screenshot, or quiet console is not a passing
  suite. Check the exit status and fresh JUnit XML, including executed, failed,
  and skipped cases. Zero selected tests is incomplete validation.
- Report project/Editor, platform/target, filter if any, counts, and artifact paths.
  Distinguish test failures from compile, setup, launch, timeout, and result-delivery
  failures. State any coverage still missing.

## Diagnose without replacing the workflow

Use the log path printed for the failed stage. Unity output is in `unity.log`,
with CLI and startup diagnostics in `launcher.log`. WebGL uses `browser.log` and
may save a failure screenshot or trace. iOS Simulator uses `xcodebuild.log` for
compilation, `simulator.log` for setup, and `player.log` for the player.
Mock-server state and logs are under the selected project's
`Library/DatadogIntegrationTests/`.

If the player starts but no XML arrives, inspect player errors, mock-server
reachability, and the result transport before retrying. In particular, browser
linkage errors can prevent results entirely. For Android ADB disconnections,
check the contributor guide's **Kill external ADB instances** advice. Do not switch
to `--target device` just to work around emulator setup. Preserve logs and retry
after identifying a concrete cause; stop repeating an unchanged failure.

Allow runner cleanup to finish after cancellation. If manual cleanup is needed,
identify only the processes/devices belonging to this run. Avoid broad process
kills, deleting Library caches, hand-editing generated settings, or changing
machine preferences as speculative fixes. Inspect the final diff and remove only
incidental changes attributable to this run, preserving pre-existing edits.

Consult `--help` or the owning code only when an option is unclear, rejected, or
needed for a platform not listed above:
[unit runner](../../../tools/dd_sdk_unity/scripts/unit_test.py),
[integration runner](../../../tools/dd_sdk_unity/scripts/integration_test.py),
[server setup](../../../tools/dd_sdk_unity/scripts/integration_test_setup.py),
[WebGL driver](../../../tools/dd_sdk_unity/tests/webgl.py), or
[iOS Simulator driver](../../../tools/dd_sdk_unity/scripts/common/apple/simulator.py).
Fix a runner defect there when in scope instead of creating a one-off test harness.

## Sandbox access

Unity runners may need installed Editors, licensing, caches, local sockets,
browsers, and simulator services outside a restricted sandbox. When those needs
are known or a sandbox denial establishes them, use the harness's normal approval
mechanism for the direct `dd-unity-*` command with the same arguments and checkout.
Explain the required access. This skill grants no permissions and needs no blanket
allow rule. If approval is unavailable or denied, report the blocked validation;
do not reroute through another shell, tool, or manual player launch to evade it.
