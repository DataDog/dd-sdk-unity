# Demo Data Project

The purpose of this project is to exercise E2E capabilities of the Datadog SDK, and produce
Demoable data with some amount of variance.

## Running

The Datadog configuration is already setup, but is lacking the Client Token and Application ID. These
can be populated by running `./add-keys.sh`, which will use the `DATADOG_CLIENT_TOKEN` and
`DATADOG_APPLICATION_ID` environment variables to populate the necessary keys in the settings file.

To run the application and send demo data to the environment, run `./run-demo.sh`.
