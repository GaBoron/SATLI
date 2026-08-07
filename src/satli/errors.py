class SatliError(Exception):
    """Base exception carrying the public CLI exit code."""

    exit_code = 1


class UsageError(SatliError):
    exit_code = 2


class PreflightError(SatliError):
    exit_code = 3


class CatalogError(SatliError):
    exit_code = 4


class IntegrityError(SatliError):
    exit_code = 5


class TransactionError(SatliError):
    exit_code = 6
