namespace AnalyticDashboard.Application.Common.Exceptions;

public sealed class OptimisticConcurrencyException(string message, Exception innerException)
    : Exception(message, innerException);
