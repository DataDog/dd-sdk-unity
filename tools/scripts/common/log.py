import logging


def init_logger() -> logging.Logger:
    logging.basicConfig(level=logging.INFO, format='%(message)s')
    return get_default_logger()


def get_default_logger() -> logging.Logger:
    return logging.getLogger(__name__)
