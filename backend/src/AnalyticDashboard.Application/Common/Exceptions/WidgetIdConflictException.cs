namespace AnalyticDashboard.Application.Common.Exceptions;

public sealed class WidgetIdConflictException : Exception
{
    public WidgetIdConflictException(Exception innerException)
        : base("The widget set could not be saved.", innerException)
    {
    }
}
