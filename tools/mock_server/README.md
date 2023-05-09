# Debugging Mock Server for Datadog RUM SDKs

This is (still under construciton) a helper http server that logs requests and that would be made to Datadog RUM endpoints. Depending on the endpoint, requests can then be parsed into properties that are more easily inspected in the built in web interface at `/inspect`. The server can also be used to retrieve a JSON representation of all requests using the `/insepct_requests` endpoint. This makes it usable as both a debug tool, and a logging mock server for integration tests.

# Using

Create a python virtual environment and get all required libraries with

```bash
python -m venv ./venv
./venv/bin/pip install -r requirements.txt
```

Run with

```bash
./venv/bin/python app.py
```

Note, the sever will bind to your private IP address on either the 10.x.x.x subnet or 192.168.x.x subnet by default, so it will be reachable by any device on your local network. If you only need to use machine local communication, prefer to run the server with the `--prefer-localhost` flag, which will bind only to 127.0.0.1
