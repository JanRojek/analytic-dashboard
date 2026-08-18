namespace AnalyticDashboard.Domain.Entities;

public sealed class InvalidProjectNameException(string message) : ArgumentException(message);
