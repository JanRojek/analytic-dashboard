using AnalyticDashboard.Application.Common.Pagination;

namespace AnalyticDashboard.Application.Projects.GetProjects;

public sealed record GetProjectsQuery(
    Guid OwnerId,
    PaginationRequest Pagination
);
