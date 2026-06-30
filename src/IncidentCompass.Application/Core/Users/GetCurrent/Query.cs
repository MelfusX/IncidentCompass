using IncidentCompass.Application.Core.Dispatching;

namespace IncidentCompass.Application.Core.Users;

public sealed record GetCurrentUserQuery : IRequest<CurrentUserDto>;
