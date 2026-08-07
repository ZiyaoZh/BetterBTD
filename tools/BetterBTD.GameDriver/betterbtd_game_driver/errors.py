class GameDriverError(Exception):
    def __init__(self, code: str, message: str, exit_code: int = 1) -> None:
        super().__init__(message)
        self.code = code
        self.message = message
        self.exit_code = exit_code


class UsageError(GameDriverError):
    def __init__(self, message: str) -> None:
        super().__init__("invalidArguments", message, 2)
