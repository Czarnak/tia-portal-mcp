using Microsoft.Extensions.DependencyInjection;
using TiaMcpServer.Safety.Pipeline;

namespace TiaMcpServer.Cli;

public static class UserConfirmationRegistration
{
    public static IServiceCollection AddUserConfirmationOptions(
        this IServiceCollection services, UserConfirmationParseResult configuration)
    {
        if (!configuration.IsValid)
        {
            throw new ArgumentException(configuration.Error, nameof(configuration));
        }

        return services.AddSingleton(new UserConfirmationOptions(configuration.ConfirmWithUser));
    }
}
