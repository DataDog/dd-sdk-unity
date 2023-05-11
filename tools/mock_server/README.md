# Debugging Mock Server for Datadog RUM SDKs

This is (still under construction) a helper HTTP server that logs requests to Datadog RUM endpoints. Depending on the endpoint, requests can then be parsed into properties that are more easily inspected in the built in web interface at `/inspect`. The server can also be used to retrieve a JSON representation of all requests using the `/inspect_requests` endpoint. This makes it usable as both a debug tool and a logging mock server for integration tests.

# Using

Create a Python virtual environment and get all required libraries with the following command:

```bash
python -m venv ./venv
./venv/bin/pip install -r requirements.txt
```

You can then run:

```bash
./venv/bin/python app.py
```

**Note**: the server will bind to your private IP address on either the 10.x.x.x subnet or 192.168.x.x subnet by default, so it will be reachable by any device on your local network. If you only need to use machine local communication, run the server with the `--prefer-localhost` flag, which will bind only to 127.0.0.1
