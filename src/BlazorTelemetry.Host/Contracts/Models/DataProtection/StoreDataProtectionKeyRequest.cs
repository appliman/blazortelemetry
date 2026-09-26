using BlazorTelemetry.Host.Contracts.Results;
using ChannelMediator;

namespace BlazorTelemetry.Host.Contracts.Models.DataProtection;

public sealed record StoreDataProtectionKeyRequest(string FriendlyName, string Xml) : IRequest<CommandResult>;
