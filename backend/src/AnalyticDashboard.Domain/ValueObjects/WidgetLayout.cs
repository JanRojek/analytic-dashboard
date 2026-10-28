namespace AnalyticDashboard.Domain.ValueObjects;

public sealed record WidgetLayout
{
    public int X { get; }

    public int Y { get; }

    public int Width { get; }

    public int Height { get; }

    public const int Columns = 24;

    public const int MinWidth = 6;

    public const int MinHeight = 14;

    public WidgetLayout(
        int x,
        int y,
        int width,
        int height)
    {
        if (x < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(x),
                x,
                "Widget coordinate 'X' cannot be negative."
            );
        }

        if (y < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(y),
                y,
                "Widget coordinate 'Y' cannot be negative."
            );
        }

        if (width < MinWidth)
        {
            throw new ArgumentOutOfRangeException(
                nameof(width),
                width,
                $"Widget width cannot be less than {MinWidth}."
            );
        }

        if (height < MinHeight)
        {
            throw new ArgumentOutOfRangeException(
                nameof(height),
                height,
                $"Widget height cannot be less than {MinHeight}."
            );
        }

        var overflow = (long)x + width - Columns;
        if (overflow > 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(width),
                width,
                $"Widget exceeds canvas width by {overflow} (x={x}, canvas width={Columns})."
            );
        }

        X = x;
        Y = y;
        Width = width;
        Height = height;
    }
}
