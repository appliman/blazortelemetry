using BlazorTelemetry.Core;
using BlazorTelemetry.Host.Contracts.Models.Incidents;
using BlazorTelemetry.Host.Contracts.Results;
using ChannelMediator;

namespace BlazorTelemetry.Host.ModelExtensions;

public static class IncidentExtensions
{
    public static async Task<IReadOnlyList<Incident>> GetIncidents(this IMediator mediator, bool includeResolved, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new GetIncidentsRequest(includeResolved), cancellationToken);
        if (result.HasError)
        {
            throw new InvalidOperationException(result.BrokenRules[0].Message);
        }
        return result.Data!;
    }

    public static async Task AcknowledgeIncident(this IMediator mediator, long id, CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new AcknowledgeIncidentRequest(id), cancellationToken);
        if (result.HasError)
        {
            throw new InvalidOperationException(result.BrokenRules[0].Message);
        }
    }

}
