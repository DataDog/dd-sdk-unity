import logging


class ColoredFormatter(logging.Formatter):
    YELLOW = '\033[33m'
    RED = '\033[31m'
    RESET = '\033[0m'
    
    def format(self, record):
        message = super().format(record)
        if record.levelno == logging.WARNING:
            return f"{self.YELLOW}{message}{self.RESET}"
        elif record.levelno >= logging.ERROR:
            return f"{self.RED}{message}{self.RESET}"
        else:
            return message


def init_logger() -> logging.Logger:
    logger = logging.getLogger()
    logger.setLevel(logging.INFO)
    
    # Remove existing handlers to avoid duplicates
    for handler in logger.handlers[:]:
        logger.removeHandler(handler)
    
    # Create console handler with custom formatter
    handler = logging.StreamHandler()
    handler.setFormatter(ColoredFormatter('%(message)s'))
    logger.addHandler(handler)
    
    return get_default_logger()


def get_default_logger() -> logging.Logger:
    return logging.getLogger(__name__)
