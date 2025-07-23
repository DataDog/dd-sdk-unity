import subprocess
import threading
from typing import Optional

from ..log import get_default_logger
from ..shell import run_cmd, OutputHandlerFunc


class IDeviceSyslog(object):
    def run(self, udid: str, match_only: str, output_handler: Optional[OutputHandlerFunc]):
        log = get_default_logger()
        args = ['idevicesyslog', '-u', udid, '-m', match_only]

        def _log_main():
            def _read(line: str, is_stderr: bool):
                log.info(line)
                if output_handler:
                    output_handler(line, is_stderr)
            run_cmd(*args, raise_on_nonzero_exitcode=True, output_handler=_read)
        
        threading.Thread(target=_log_main, daemon=True).start()

    @classmethod
    def require(cls) -> 'IDeviceSyslog':
        try:
            subprocess.check_output(['idevicesyslog', '-v'])
        except:
            raise RuntimeError("idevicesyslog not found in PATH: install with 'brew install libimobiledevice'")
        return cls()
