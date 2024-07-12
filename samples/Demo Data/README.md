# Demo Data Project

The purpose of this project is to exercise E2E capabilities of the Datadog SDK, and produce
Demoable data with some amount of variance.

## Running

The Datadog configuration is already setup, but is lacking the Client Token and Application ID. These
can be populated by running `./add-keys.py`, which will use the `DATADOG_CLIENT_TOKEN` and
`DATADOG_APPLICATION_ID` environment variables to populate the necessary keys in the settings file.

To run the application and send demo data to the environment, run `./run-demo.py`.

## Notes

To prevent committing changes to the `DatadogSettings.asset` file that might include a client token or
RUM Application Id, consider running the following git command to ignore changes to the file:

```bash
git update-index --skip-worktree samples/Demo\ Data/Assets/Resources/DatadogSettings.asset
```
