import subprocess
import selectors
import io
import sys
from typing import Callable, Optional, cast


OutputHandlerFunc = Callable[[str, bool], None]


def run_cmd(
    *args: str,
    raise_on_nonzero_exitcode=False,
    echo: bool = False,
    output_handler: Optional[OutputHandlerFunc] = None
) -> int:    
    # Launch a child process
    process = subprocess.Popen(
        args,
        # Pipe both stdout and stderr so we can read them
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        # Ensure line-buffered text output
        bufsize=1,
        text=True,
        universal_newlines=True,
    )
    assert process.stdout
    assert process.stderr

    # Select on stdout and stderr so we can process output in real time
    sel = selectors.DefaultSelector()
    sel.register(process.stdout, selectors.EVENT_READ)
    sel.register(process.stderr, selectors.EVENT_READ)

    # Read output from the process until it's finished
    exitcode: Optional[int] = None
    while True:
        for key, _ in sel.select():
            # Read the next line from the next available stream
            stream = cast(io.TextIOBase, key.fileobj)
            line = stream.readline()

            # If we read EOF, close the stream and continue
            if not line:
                sel.unregister(stream)
                stream.close()
                continue

            # Handle the line of output
            is_stderr = stream is process.stderr
            if echo:
                echo_stream = sys.stderr if is_stderr else sys.stdout
                echo_stream.write(line)
            if output_handler:
                output_handler(line.rstrip('\n'), is_stderr)

        # Once the process has exited AND stdout/stderr are closed, finish
        exitcode = process.poll()
        has_open_streams = len(sel.get_map()) > 0
        if exitcode is not None and not has_open_streams:
            break
    
    assert exitcode is not None
    if raise_on_nonzero_exitcode and exitcode != 0:
        raise RuntimeError(f'{args[0]} exited with status code {exitcode}')

    return exitcode
