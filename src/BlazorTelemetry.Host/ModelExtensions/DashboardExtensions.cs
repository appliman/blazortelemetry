using BlazorTelemetry.Core;
using BlazorTelemetry.Host.Contracts.Models.Dashboards;
using BlazorTelemetry.Host.Contracts.Results;
using ChannelMediator;

namespace BlazorTelemetry.Host.ModelExtensions;

public static class DashboardExtensions
{
    public static async Task<IReadOnlyList<DashboardDefinition>> GetDashboards(this IMediator mediator, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new GetDashboardsRequest(), cancellationToken);
        if (result.HasError)
        {
            throw new InvalidOperationException(result.BrokenRules[0].Message);
        }
        return result.Data!;
    }

    public static async Task SaveDashboard(this IMediator mediator, DashboardDefinition dashboard, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new SaveDashboardRequest(dashboard), cancellationToken);
        if (result.HasError)
        {
            throw new InvalidOperationException(result.BrokenRules[0].Message);
        }
    }

}
