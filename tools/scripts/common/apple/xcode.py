import subprocess
from typing import List


def run_xcodebuild(cwd: str, args: List[str]):
    # Run xcodebuild and pipe the output into xcbeautify
    xcodebuild_args = ['xcodebuild'] + args
    xcodebuild = subprocess.Popen(xcodebuild_args, cwd=cwd, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
    xcbeautify = subprocess.Popen(['xcbeautify'], cwd=cwd, stdin=xcodebuild.stdout)

    # Close xcodebuild's stdout to handle broken pipe gracefully
    assert xcodebuild.stdout
    xcodebuild.stdout.close()

    # Wait for both processes to complete
    xcbeautify.wait()
    xcodebuild.wait()

    # Check both exit codes
    if xcodebuild.returncode != 0:
        raise subprocess.CalledProcessError(xcodebuild.returncode, xcodebuild_args)
    if xcbeautify.returncode != 0:
        raise subprocess.CalledProcessError(xcbeautify.returncode, ['xcbeautify'])
