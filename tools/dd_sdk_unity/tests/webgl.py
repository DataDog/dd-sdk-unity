# Unless explicitly stated otherwise, all files in this repository are licensed under the
# Apache License Version 2.0. This product includes software developed at Datadog
# (https://www.datadoghq.com/). Copyright 2026-Present Datadog, Inc.

"""Run an exported Unity integration-test player in an owned Chrome instance."""
from functools import partial
from http.server import SimpleHTTPRequestHandler, ThreadingHTTPServer
from os import PathLike, fspath
from pathlib import Path
from tempfile import TemporaryDirectory
from threading import Thread

from playwright.sync_api import Dialog, sync_playwright
from playwright.sync_api import Error as PlaywrightError

from dd_sdk_unity.scripts.common.log import get_default_logger
from dd_sdk_unity.scripts.common.xslt import collect_results


class _PlayerRequestHandler(SimpleHTTPRequestHandler):
    def guess_type(self, path: str | PathLike[str]) -> str:
        path = fspath(path)
        path = path.removesuffix('.gz')
        return super().guess_type(path)

    def end_headers(self) -> None:
        if self.path.endswith('.gz'):
            self.send_header('Content-Encoding', 'gzip')
        self.send_header('Cache-Control', 'no-store')
        super().end_headers()

    def log_message(self, format: str, *args: object) -> None:
        # Browser diagnostics capture failed requests without logging every asset.
        pass


def run_webgl_tests(export_path: str | Path, nunit_path: str | Path,
                    log_path: str | Path, timeout_ms: int = 600_000) -> None:
    export_path, log_path = Path(export_path), Path(log_path)
    if not (export_path / 'index.html').is_file():
        raise RuntimeError(f'Unity did not export a WebGL player to {export_path}')

    handler = partial(_PlayerRequestHandler, directory=str(export_path))
    server = ThreadingHTTPServer(('127.0.0.1', 0), handler)
    server_thread = Thread(target=server.serve_forever, daemon=True)
    server_thread.start()
    try:
        with (
            log_path.open('w') as log,
            sync_playwright() as playwright,
            playwright.chromium.launch(channel='chrome', headless=True) as browser,
        ):
            def write(message: str) -> None:
                log.write(message + '\n')
                log.flush()

            def on_dialog(dialog: Dialog) -> None:
                write(f'[dialog] {dialog.message}')
                dialog.dismiss()

            context = browser.new_context()
            context.tracing.start(screenshots=True, snapshots=True)
            page = context.new_page()
            page.on('console', lambda message: write(f'[{message.type}] {message.text}'))
            page.on('pageerror', lambda error: write(f'[page error] {error}'))
            page.on('requestfailed', lambda request: write(f'[request failed] {request.url}: {request.failure}'))
            page.on('response', lambda response: write(
                f'[HTTP {response.status}] {response.url}') if response.status >= 400 else None)
            page.on('dialog', on_dialog)
            try:
                page.goto(f'http://127.0.0.1:{server.server_port}/', wait_until='domcontentloaded')
                versions = page.evaluate('''() => ({
                    rum: window.DD_RUM?.version ?? null,
                    logs: window.DD_LOGS?.version ?? null
                })''')
                write(f'[Browser SDK versions] {versions}')
                page.wait_for_function(
                    "typeof window.__datadogIntegrationTestResult === 'string'", timeout=timeout_ms)
                xml = page.evaluate('window.__datadogIntegrationTestResult')
                if not isinstance(xml, str):
                    raise TypeError('The WebGL player did not return NUnit XML')
                with TemporaryDirectory(prefix='datadog-webgl-results-') as directory:
                    source = Path(directory) / 'runtime-suite.xml'
                    source.write_text(xml)
                    collect_results(source, nunit_path)
                get_default_logger().info(f'Chrome test results written to {nunit_path}')
            except BaseException:
                try:
                    page.screenshot(path=str(log_path.with_suffix('.png')), timeout=5_000)
                except (PlaywrightError, OSError) as error:
                    write(f'[screenshot failed] {error}')
                try:
                    context.tracing.stop(path=str(log_path.with_suffix('.zip')))
                except (PlaywrightError, OSError) as error:
                    write(f'[trace failed] {error}')
                raise
            else:
                context.tracing.stop()
            finally:
                context.close()
    finally:
        server.shutdown()
        server.server_close()
        server_thread.join()
