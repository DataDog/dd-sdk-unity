"""
Utility code for running subprocesses and processing their output line-by-line.
"""
import subprocess
import selectors
import io
import sys
from typing import Callable, Optional, cast, Tuple, List


OutputHandlerFunc = Callable[[str, bool], None]


class OutputBuffer(object):
    buf: str

    def __init__(self):
        self.buf = ''

    def write(self, s: str):
        self.buf += s

    def __iter__(self):
        while '\n' in self.buf:
            line, self.buf = self.buf.split('\n', 1)
            yield line


def run_cmd(
    *args: str,
    raise_on_nonzero_exitcode = False,
    cwd: Optional[str] = None,
    bufsize: int = 1,
    echo: bool = False,
    output_handler: Optional[OutputHandlerFunc] = None
) -> int:    
    # Launch a child process
    process = subprocess.Popen(
        args,
        cwd=cwd,
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

    # Buffer output so we can handle it line-by-line
    stdout_buffer = OutputBuffer()
    stderr_buffer = OutputBuffer()

    # Read output from the process until it's finished
    exitcode: Optional[int] = None
    while True:
        # Block until new output is available for read
        for key, _ in sel.select(timeout=0.1):
            # Read the next chunk of data from the next available stream
            stream = cast(io.TextIOBase, key.fileobj)
            is_stderr = stream is process.stderr
            if bufsize == 1:
                data = stream.readline()
            else:
                data = stream.read(bufsize)

            # If we read EOF, close the stream and continue
            if not data:
                sel.unregister(stream)
                stream.close()
                continue

            # Buffer the data we've just received, and consume any complete lines that
            # are now held in the buffer
            lines: List[str] = []
            if bufsize == 1:
                lines = [data.rstrip('\n')]
            else:
                buffer = stderr_buffer if is_stderr else stdout_buffer
                buffer.write(data)
                lines = list(buffer)
            for line in lines:
                if echo:
                    echo_stream = sys.stderr if is_stderr else sys.stdout
                    echo_stream.write(line + '\n')
                if output_handler:
                    output_handler(line, is_stderr)

        # Once the process has exited AND stdout/stderr are closed, finish
        exitcode = process.poll()
        has_open_streams = len(sel.get_map()) > 0
        if exitcode is not None and not has_open_streams:
            break
    
    assert exitcode is not None
    if raise_on_nonzero_exitcode and exitcode != 0:
        raise subprocess.CalledProcessError(exitcode, args)

    return exitcode


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
