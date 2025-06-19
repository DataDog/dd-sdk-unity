import os

__tools_scripts_common__ = os.path.dirname(__file__)
__repo_root__ = os.path.abspath(os.path.join(__tools_scripts_common__, '..', '..', '..'))


def repo_path(*args: str) -> str:
    return os.path.normpath(os.path.join(__repo_root__, *args))
