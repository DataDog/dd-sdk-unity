"""
Utility code for finding and invoking the standalone Unity CLI (the `unity` binary).

Unless explicitly stated otherwise, all files in this repository are licensed under the
Apache License Version 2.0. This product includes software developed at Datadog
(https://www.datadoghq.com/). Copyright 2026-Present Datadog, Inc.
"""
import json
import shutil
from typing import List, Optional, Union

from common.log import get_default_logger
from common.shell import run_cmd, run_cmd_streaming

from .install import UnityInstall, UnityVersion, resolve_unity_install


# https://docs.unity.com/en-us/unity-cli/unity-cli-reference#exit-codes
_EXIT_CODE_DESCRIPTIONS = {
    1: 'general error',
    2: 'invalid command arguments',
    3: 'authentication or authorization failed',
    4: 'required configuration is missing',
    6: 'operation failed',
    7: 'Unity service or Editor could not be reached',
    8: 'one or more tests failed',
    130: 'cancelled by Ctrl+C (SIGINT)',
    143: 'terminated (SIGTERM)',
}


class UnityCli(object):
    path: str

    def __init__(self, path: str):
        self.path = path

    def list_installs(self) -> List[UnityInstall]:
        """Lists installed Unity Editors."""
        log = get_default_logger()
        log.debug('Finding Unity installations...')
        installs = [
            UnityInstall(
                version=UnityVersion.parse(editor['version']),
                architecture=editor['architecture'],
                path=editor['location'],
            )
            for editor in self._list_items('editors', '--installed')
        ]
        for install in installs:
            log.info(f'Found: {install.version} at {install.path}')
        return installs

    def install_version(self, version: Union[UnityVersion, str], modules: List[str]) -> UnityInstall:
        """
        Installs an exact version or numeric version prefix with its modules,
        accepting module license agreements. Unity CLI resolves partial versions.
        """
        log = get_default_logger()
        log.info(f'Installing Unity {version}...')
        exitcode = run_cmd_streaming(
            *self._command('human'), 'install', str(version),
            *self._module_args(modules),
            '--accept-eula',
        )
        self._raise_for_exitcode(exitcode, f'install {version}')
        install = resolve_unity_install(self.list_installs(), str(version))
        if install:
            return install
        raise RuntimeError('Failed to resolve Unity install after successful completion of install command')

    def install_modules(self, version: UnityVersion, modules: List[str]):
        """Adds modules and their children, accepting module license agreements."""
        if not modules:
            return
        log = get_default_logger()
        log.info(f'Installing modules for Unity {version}...')
        command = [
            *self._command('human'), 'install-modules', '--editor-version', str(version),
            *self._module_args(modules),
            '--accept-eula',
        ]
        # Check for an empty selection before giving the live install the terminal.
        output: List[str] = []
        exitcode = run_cmd(
            *command, '--dry-run',
            merge_stderr=True,
            output_handler=lambda line, is_stderr: output.append(line),
        )
        # An empty selection also occurs for invalid IDs, so confirm installation.
        if exitcode == 6 and 'Error: No modules found to install.' in output:
            inventory = self._list_items('install-modules', '--editor-version', str(version), '--list')
            installed = {module['id'] for module in inventory if module['status'] == 'Installed'}
            if all(module in installed for module in modules):
                log.info(f'Requested modules are already installed for Unity {version}.')
                return
        self._raise_for_exitcode(
            exitcode, f'install-modules --editor-version {version} --dry-run', '\n'.join(output),
        )
        exitcode = run_cmd_streaming(*command)
        self._raise_for_exitcode(exitcode, f'install-modules --editor-version {version}')

    def run_build(
        self,
        version: UnityVersion,
        project_path: str,
        target: str,
        execute_method: str,
        output_path: str,
        log_path: Optional[str] = None,
    ) -> int:
        return run_cmd_streaming(
            *self._command('human'), 'build', project_path,
            '--editor-version', str(version),
            '--target', target,
            '--execute-method', execute_method,
            '--output-path', output_path,
            *(['--log-file', log_path] if log_path else []),
            # Build scripts temporarily modify project settings before invoking Unity.
            '--allow-dirty-build',
        )

    def run_tests(
        self,
        version: UnityVersion,
        project_path: str,
        platform: str,
        results_path: str,
        log_path: str,
        *editor_args: str,
    ) -> int:
        return run_cmd_streaming(
            *self._command('human'), 'test', project_path,
            '--editor-version', str(version),
            '--mode', platform,
            '--output', results_path,
            '--', '-logFile', log_path, *editor_args,
        )

    def _command(self, output_format: str) -> List[str]:
        return [self.path, '--no-banner', '--no-pager', '--non-interactive', '--format', output_format]

    def _list_items(self, *args: str) -> List[dict]:
        command = ' '.join(args)
        stdout: List[str] = []
        stderr: List[str] = []

        def _read(line: str, is_stderr: bool):
            (stderr if is_stderr else stdout).append(line)

        exitcode = run_cmd(*self._command('json'), *args, output_handler=_read)
        output = '\n'.join(stdout)
        if exitcode != 0:
            details = '\n'.join(stderr + stdout)
            self._raise_for_exitcode(exitcode, command, details)
        try:
            result = json.loads(output)
        except json.JSONDecodeError as error:
            raise RuntimeError(f'Invalid JSON from Unity CLI {command}: {output}') from error
        if not isinstance(result, dict) or result.get('success') is not True:
            raise RuntimeError(f'Unity CLI {command} failed: {output}')
        if not isinstance(result.get('data'), list):
            raise RuntimeError(f'Unexpected list from Unity CLI {command}: {output}')
        return result['data']

    @staticmethod
    def _raise_for_exitcode(exitcode: int, command: str, details: str = ''):
        if exitcode == 0:
            return
        reason = _EXIT_CODE_DESCRIPTIONS.get(exitcode, 'unrecognized exit code')
        message = f'Unity CLI {command} failed (exit {exitcode}: {reason})'
        if details:
            message += f':\n{details}'
        raise RuntimeError(message)

    @staticmethod
    def _module_args(modules: List[str]) -> List[str]:
        return ['--module', *modules, '--child-modules'] if modules else []

    @classmethod
    def require(cls) -> 'UnityCli':
        """Finds the standalone Unity CLI on PATH, raising if it is missing."""
        path = shutil.which('unity')
        if not path:
            raise RuntimeError('Unity CLI binary not found on PATH')
        get_default_logger().info(f'Found Unity CLI binary at: {path}')
        return cls(path)
