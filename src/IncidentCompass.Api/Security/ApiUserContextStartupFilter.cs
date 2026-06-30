using IncidentCompass.Application.Core.Security;

namespace IncidentCompass.Api.Security;

internal sealed class ApiUserContextStartupFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        return applicationBuilder =>
        {
            using var scope = applicationBuilder.ApplicationServices.CreateScope();
            var userContext = scope.ServiceProvider.GetService<IUserContext>();
            if (userContext is null or IBackgroundUserContext)
            {
                throw ApiUserContextSetup.CreateMissingUserContextException();
            }

            next(applicationBuilder);
        };
    }
}
