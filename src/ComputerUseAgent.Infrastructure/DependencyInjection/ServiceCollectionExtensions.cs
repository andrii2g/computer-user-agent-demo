using ComputerUseAgent.Core.Configuration;
using ComputerUseAgent.Core.Interfaces;
using ComputerUseAgent.Core.Orchestration;
using ComputerUseAgent.Core.Policies;
using ComputerUseAgent.Infrastructure.OpenAI;
using ComputerUseAgent.Infrastructure.Persistence;
using ComputerUseAgent.Infrastructure.Sandboxing;
using ComputerUseAgent.Infrastructure.Support;
using ComputerUseAgent.Infrastructure.Workspace;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ComputerUseAgent.Infrastructure.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddComputerUseAgent(this IServiceCollection services)
    {
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IIdGenerator, GuidIdGenerator>();
        services.AddSingleton<ICommandPolicy, DefaultCommandPolicy>();
        services.AddSingleton<IPathPolicy, DefaultPathPolicy>();

        services.AddScoped<ISessionRepository, SqliteSessionRepository>();
        services.AddScoped<IWorkspaceService, LocalWorkspaceService>();
        services.AddScoped<ISandboxService, DockerSandboxService>();

        services.AddHttpClient<IResponsesAgentClient, OpenAiResponsesAgentClient>();

        services.AddScoped<AgentRunOrchestrator>(provider =>
        {
            var sandboxOptions = provider.GetRequiredService<IOptions<SandboxOptions>>().Value;
            return new AgentRunOrchestrator(
                provider.GetRequiredService<ISessionRepository>(),
                provider.GetRequiredService<ISandboxService>(),
                provider.GetRequiredService<IResponsesAgentClient>(),
                provider.GetRequiredService<IWorkspaceService>(),
                provider.GetRequiredService<IClock>(),
                provider.GetRequiredService<IIdGenerator>(),
                provider.GetRequiredService<ICommandPolicy>(),
                sandboxOptions);
        });

        return services;
    }
}
