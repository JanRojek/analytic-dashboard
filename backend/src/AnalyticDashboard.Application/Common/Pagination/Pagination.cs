namespace AnalyticDashboard.Application.Common.Pagination;

public static class Pagination
{
    private static int NormalizePageSize(int pageSize)
    {
        return pageSize is > 0 and <= PaginationRequest.MaxPageSize
            ? pageSize
            : PaginationRequest.DefaultPageSize;
    }

    private static int NormalizePage(int page, int totalPages)
    {
        return page > 0 && page <= totalPages
            ? page
            : PaginationRequest.DefaultPage;
    }

    public static ResolvedPagination Calculate(
        PaginationRequest request,
        int totalCount)
    {
        var pageSize = NormalizePageSize(request.PageSize);
        var totalPages = Math.Max(
            1,
            (int)Math.Ceiling(totalCount / (double)pageSize)
        );
        var page = NormalizePage(request.Page, totalPages);
        var skip = (page - 1) * pageSize;

        return new ResolvedPagination(
            page,
            pageSize,
            skip,
            totalPages
        );
    }
}

public sealed record ResolvedPagination(
    int Page,
    int PageSize,
    int Skip,
    int TotalPages
);
