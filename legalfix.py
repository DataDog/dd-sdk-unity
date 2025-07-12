#!/usr/bin/env python3
"""
Iterates over every file in tools/scripts in order to ensure that a Datadog legal
notice is present at the top of the file. Invoke with --fix to automatically write
comments where they are missing.

This script has no external dependencies; it can be run with any compatible Python
interpreter.

Unless explicitly stated otherwise, all files in this repository are licensed under the
Apache License Version 2.0. This product includes software developed at Datadog
(https://www.datadoghq.com/). Copyright 2025-Present Datadog, Inc.
"""
import os
import re
import sys
import ast
import doctest
import argparse
from datetime import datetime
from dataclasses import dataclass
from typing import Optional, Literal, List

# Get the path to the directory containing our Python scripts
__repo_root__ = os.path.abspath(os.path.dirname(__file__))
__scripts_root__ = os.path.join(__repo_root__, 'tools', 'scripts')
assert os.path.isdir(__scripts_root__)

__notice_begin__ = 'Unless explicitly stated otherwise'
__notice_license__ = 'Apache License Version 2.0'
__notice_company__ = 'software developed at Datadog (https://www.datadoghq.com'
__notice_copyright_regex__ = re.compile(r'Copyright (\d{4})(?: ?- ?(\d{4}|Present))? Datadog, Inc\.')

__notice_pattern__ = """
Unless explicitly stated otherwise, all files in this repository are licensed under the
Apache License Version 2.0. This product includes software developed at Datadog
(https://www.datadoghq.com/). Copyright %d-Present Datadog, Inc.
"""[1:]


@dataclass
class LegalNoticeInfo:
    """
    Details of a legal notice found in a file docstring. 
    """
    start_year: int
    end_year: Optional[int | Literal['Present']]

    @classmethod
    def parse(cls, docstring_value: str) -> Optional['LegalNoticeInfo']:
        '''
        >>> s = """
        ... This is a docstring comment.
        ... <BLANKLINE>
        ... Unless explicitly stated otherwise all files in this
        ... repository are licensed under the Apache
        ... License Version 2.0. This product includes software
        ... developed at Datadog
        ...     (https://www.datadoghq.com/). Copyright
        ...  2025-Present Datadog, Inc.
        ... <BLANKLINE>
        ... Goodbye.
        ... """
        >>> legal_notice = LegalNoticeInfo.parse(s)
        >>> legal_notice.start_year
        2025
        >>> legal_notice.end_year
        'Present'
        >>> assert LegalNoticeInfo.parse('not a legal notice') is None
        >>> assert LegalNoticeInfo.parse(__notice_pattern__ % 2025).start_year == 2025
        '''
        # Find the phrase that begins the legal notice
        try:
            start_pos = docstring_value.index(__notice_begin__)
        except ValueError:
            return None

        # Take all the text in that paragraph and normalize it to a single line
        lines = docstring_value[start_pos:].splitlines()
        end = next((i for i, line in enumerate(lines) if line.strip() == ''), len(lines))
        notice_text = ' '.join((s.strip() for s in lines[:end]))

        # Check for remaining key phrases in the text of the notice
        if __notice_license__ not in notice_text:
            return None
        if __notice_company__ not in notice_text:
            return None
        
        # Check for a copyright statement and parse the year(s)
        match = __notice_copyright_regex__.search(notice_text)
        if not match:
            return None
        start_year = int(match.group(1))
        end_year: Optional[int | Literal['Present']]
        end_year_text = match.group(2)
        if end_year_text:
            end_year = 'Present' if end_year_text == 'Present' else int(end_year_text)

        return LegalNoticeInfo(start_year, end_year)


@dataclass
class FileDocstringInfo:
    """
    Details of a file-level docstring comment found at the top of a Python file.
    """
    value: str
    lineno: int
    end_lineno: int
    legal_notice: Optional[LegalNoticeInfo]

    @classmethod
    def parse(cls, module: ast.Module) -> Optional['FileDocstringInfo']:
        '''
        >>> docstring = FileDocstringInfo.parse(ast.parse("""
        ... #!/usr/bin/env python
        ... ```
        ... Hello, this is a file with a multi-line docstring.
        ... <BLANKLINE>
        ... Unless explicitly stated otherwise, Apache License Version 2.0, software developed at Datadog (https://www.datadoghq.com/) Copyright 2010-2025 Datadog, Inc.
        ... ```
        ... print('hello')
        ... """[1:].replace('`', '"')))
        >>> docstring.value
        '\\nHello, this is a file with a multi-line docstring.\\n<BLANKLINE>\\nUnless explicitly stated otherwise, Apache License Version 2.0, software developed at Datadog (https://www.datadoghq.com/) Copyright 2010-2025 Datadog, Inc.\\n'
        >>> docstring.lineno, docstring.end_lineno
        (2, 6)
        >>> docstring.legal_notice.start_year, docstring.legal_notice.end_year
        (2010, 2025)
        >>> docstring = FileDocstringInfo.parse(ast.parse("""
        ... ```
        ... This is a multi-line docstring.
        ... ```
        ... """[1:].replace('`', '"')))
        >>> docstring.value, docstring.lineno, docstring.end_lineno, docstring.legal_notice
        ('\\nThis is a multi-line docstring.\\n', 1, 3, None)
        >>> docstring = FileDocstringInfo.parse(ast.parse("""
        ... ```This is a single-line docstring.```
        ... """[1:].replace('`', '"')))
        >>> docstring.value, docstring.lineno, docstring.end_lineno, docstring.legal_notice
        ('This is a single-line docstring.', 1, 1, None)
        >>> assert FileDocstringInfo.parse(ast.parse("")) is None
        '''
        if not module.body:
            return None
        top_expr = module.body[0]

        if not isinstance(top_expr, ast.Expr) or not isinstance(top_expr.value, ast.Constant):
            return None
        top_constant = top_expr.value
        
        if not isinstance(top_constant.value, str):
            return None
        docstring_value = top_constant.value

        return FileDocstringInfo(
            docstring_value,
            top_expr.lineno,
            top_expr.end_lineno or top_expr.lineno, 
            LegalNoticeInfo.parse(docstring_value),
        )


@dataclass
class FileInfo:
    """
    Details parsed from a Python file.
    """
    abspath: str
    docstring: Optional[FileDocstringInfo]


def fix_file(file: FileInfo, notice: str) -> None:
    # Verify that we're not attempting to modify files that already have a legal notice
    assert not file.docstring or not file.docstring.legal_notice

    # Read the Python source file as plain text
    with open(file.abspath) as fp:
        lines = fp.readlines()

    # Manipulate lines as needed to ensure we have a docstring with a legal notice
    lines = fix_file_lines(lines, file.docstring, notice)

    # Write our updated lines back to the file
    with open(file.abspath, 'w') as fp:
        fp.writelines(lines)


def fix_file_lines(lines: List[str], docstring: Optional[FileDocstringInfo], notice: str) -> List[str]:
    '''
    >>> notice = 'This is a legal notice. Please pay attention to it.\\nReally, please do.\\n'
    >>> fix = lambda text: ''.join(fix_file_lines(text.splitlines(keepends=True), FileDocstringInfo.parse(ast.parse(text)), notice))
    >>> print(fix("""
    ... ```
    ... Hello, this is my script.
    ... ```
    ... print('hello')
    ... """[1:].replace('`', '"')))
    """
    Hello, this is my script.
    <BLANKLINE>
    This is a legal notice. Please pay attention to it.
    Really, please do.
    """
    print('hello')
    <BLANKLINE>
    >>> print(fix("""
    ... ```Hello, this is my script.```
    ... print('hello')
    ... """[1:].replace('`', '"')))
    """
    Hello, this is my script.
    <BLANKLINE>
    This is a legal notice. Please pay attention to it.
    Really, please do.
    """
    print('hello')
    <BLANKLINE>
    >>> print(fix("""
    ... #!/usr/bin/env python
    ... # This is a script that does something.
    ... print('hello')
    ... """[1:].replace('`', '"')))
    #!/usr/bin/env python
    # This is a script that does something.
    """
    This is a legal notice. Please pay attention to it.
    Really, please do.
    """
    print('hello')
    <BLANKLINE>
    
    '''
    if not docstring:
        # If the file does not have a top-level docstring, add one, skipping any
        # initial lines beginning with '#' so we don't preempt shebang lines
        insert_index = next((i for i, line in enumerate(lines) if not line.startswith('#')), 0)
        head_lines = lines[:insert_index] + ['"""\n']
        tail_lines = ['"""\n'] + lines[insert_index:]
        return head_lines + notice.splitlines(keepends=True) + tail_lines
    else:
        # The file has an existing docstring: add the notice to it, below the existing
        # comment text
        line_index = docstring.lineno - 1
        end_line_index = docstring.end_lineno - 1
        assert line_index >= 0 and line_index < len(lines)
        assert end_line_index >= 0 and end_line_index < len(lines)
        assert end_line_index >= line_index

        # Single line docstrings (e.g. """foo""") need to be expanded to multi-line
        if line_index == end_line_index:
            head_lines = lines[:line_index] + ['"""\n']
            value_lines = [docstring.value + '\n']
            tail_lines = ['"""\n'] + lines[end_line_index+1:]
        else:
            head_lines = lines[:line_index+1]
            value_lines = lines[line_index+1:end_line_index]
            tail_lines = lines[end_line_index:]
        assert head_lines[-1] == '"""\n'
        assert tail_lines[0] == '"""\n'

        # Append our new legal notice lines to the existing docstring value, separated
        # by a blank line
        value_lines.append('\n')
        value_lines += notice.splitlines(keepends=True)

        # Rebuild the lines array with our updated docstring value
        return head_lines + value_lines + tail_lines


def main(src_dir: str, fix: bool) -> None:
    # Get the working directory so we can print compact file paths
    cwd = os.getcwd()

    # Get the current year so we can write it in new docstring comments
    current_year = datetime.now().year
    notice = __notice_pattern__ % current_year

    # Iterate over every .py file in the target directory, recursively
    all_files_are_ok = True
    for root, dirs, filenames in os.walk(src_dir):
        dirs[:] = [d for d in dirs if d not in ['venv', '__pycache__'] and not d.startswith('.')]
        for filename in [f for f in filenames if f.endswith('.py')]:
            filepath = os.path.join(root, filename)
            relpath = os.path.relpath(filepath, cwd)

            # Read the text of the current Python file and parse it to an AST
            with open(filepath, 'r', encoding='utf-8') as fp:
                python_source = fp.read()
                module = ast.parse(python_source, filename=filepath)

            # Examine the AST to see if it has a top-level docstring, and if so,
            # whether there's a conformat legal notice in that docstring
            file = FileInfo(filepath, FileDocstringInfo.parse(module))
            if file.docstring and file.docstring.legal_notice:
                print(f'✅ [ {file.docstring.legal_notice.start_year} ] {relpath}')
            elif fix:
                fix_file(file, notice)
                print(f'⚠️ [FIXED!] {relpath}')
            else:
                print(f'❌ [      ] {relpath}')
                all_files_are_ok = False
    
    if not all_files_are_ok:
        sys.exit(1)


if __name__ == '__main__':
    doctest.testmod(raise_on_error=True)
    parser = argparse.ArgumentParser(description='Checks Python source files to ensure that they contain a legal notice in a header comment')
    parser.add_argument('--src-dir', '-s', default=__scripts_root__, help='Directory containing .py files to check recursively')
    parser.add_argument('--fix', '-f', action='store_true', help='Inject header comments into files that are missing a legal notice')
    args = parser.parse_args()
    main(args.src_dir, args.fix)
