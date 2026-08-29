namespace Satli.Core;

public class SatliException(string message, int exitCode, Exception? innerException = null)
    : Exception(message, innerException)
{
    public int ExitCode { get; } = exitCode;
}

public sealed class UsageException(string message, Exception? innerException = null)
    : SatliException(message, 2, innerException);

public sealed class PreflightException(string message, Exception? innerException = null)
    : SatliException(message, 3, innerException);

public sealed class CatalogException(string message, Exception? innerException = null)
    : SatliException(message, 4, innerException);

public sealed class IntegrityException(string message, Exception? innerException = null)
    : SatliException(message, 5, innerException);

public sealed class TransactionException(string message, Exception? innerException = null)
    : SatliException(message, 6, innerException);
