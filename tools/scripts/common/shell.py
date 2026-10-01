"""
Utility code for running subprocesses and processing their output line-by-line.

Unless explicitly stated otherwise, all files in this repository are licensed under the
Apache License Version 2.0. This product includes software developed at Datadog
(https://www.datadoghq.com/). Copyright 2025-Present Datadog, Inc.
"""
import subprocess
import selectors
import io
from typing import Callable, Optional, cast, Tuple


OutputHandlerFunc = Callable[[str, bool], None]


def run_cmd(
    *args: str,
    raise_on_nonzero_exitcode = False,
    merge_stderr: bool = False,
    inherit_output: bool = False,
    output_handler: Optional[OutputHandlerFunc] = None
) -> int:
    if inherit_output:
        if output_handler is not None:
            raise ValueError('Cannot capture output when inheriting the terminal')
        # Keep terminal detection and cursor control intact for progress displays.
        return subprocess.run(
            args,
            stderr=subprocess.STDOUT if merge_stderr else None,
            check=raise_on_nonzero_exitcode,
        ).returncode

    # Launch a child process
    process = subprocess.Popen(
        args,
        # Merge human output when requested; keep streams separate for data parsing.
        stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT if merge_stderr else subprocess.PIPE,
        # Ensure line-buffered text output
        bufsize=1,
        text=True,
    )
    assert process.stdout

    # Select on stdout and stderr so we can process output in real time
    sel = selectors.DefaultSelector()
    sel.register(process.stdout, selectors.EVENT_READ)
    if process.stderr is not None:
        sel.register(process.stderr, selectors.EVENT_READ)

    # Read output from the process until it's finished
    exitcode: Optional[int] = None
    while True:
        # Block until new output is available for read
        for key, _ in sel.select(timeout=0.1):
            # Read the next chunk of data from the next available stream
            stream = cast(io.TextIOBase, key.fileobj)
            is_stderr = stream is process.stderr
            data = stream.readline()

            # If we read EOF, close the stream and continue
            if not data:
                sel.unregister(stream)
                stream.close()
                continue

            if output_handler:
                output_handler(data.rstrip('\n'), is_stderr)

        # Once the process has exited AND stdout/stderr are closed, finish
        exitcode = process.poll()
        has_open_streams = len(sel.get_map()) > 0
        if exitcode is not None and not has_open_streams:
            break
    
    assert exitcode is not None
    if raise_on_nonzero_exitcode and exitcode != 0:
        raise subprocess.CalledProcessError(exitcode, args)

    return exitcode


def run_cmd_streaming(*args: str) -> int:
    """Stream combined output directly to the terminal, preserving progress displays."""
    return run_cmd(*args, merge_stderr=True, inherit_output=True)


def capture_output(*args: str) -> Tuple[str, str]:
    stdout = io.StringIO()
    stderr = io.StringIO()
    def _read(line: str, is_stderr: bool):
        if is_stderr:
            stderr.write(line + '\n')
        else:
            stdout.write(line + '\n')

    run_cmd(*args, raise_on_nonzero_exitcode=True, output_handler=_read)

    stdout.seek(0)
    stderr.seek(0)
    return stdout.read(), stderr.read()
