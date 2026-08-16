using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Yukti.Application.Abstractions;
using Yukti.Domain.FlowAuthoring;

namespace Yukti.Infrastructure;

/// <summary>
/// One call replaces every InMemory repository/UnitOfWork registration
/// with its real, CockroachDB-backed counterpart — no other file in
/// Yukti.Api changes to make the swap (Volume 1 README's own stated plan).
/// Everything registered here is Scoped, not Singleton: a DbContext is not
/// thread-safe and must not outlive one request, unlike the in-memory
/// repositories it replaces, which relied on Singleton for their
/// process-lifetime storage.
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddYuktiInfrastructure(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<YuktiDbContext>(options => options.UseNpgsql(connectionString));

        services.AddScoped<IFlowRepository, EfFlowRepository>();
        services.AddScoped<IFlowRunRepository, EfFlowRunRepository>();
        services.AddScoped<IModuleRegistrationRepository, EfModuleRegistrationRepository>();
        services.AddScoped<IUserRepository, EfUserRepository>();
        services.AddScoped<IRoleRepository, EfRoleRepository>();
        services.AddScoped<IModuleActionResolver, EfModuleActionResolver>();
        services.AddScoped<IUnitOfWorkFactory, EfUnitOfWorkFactory>();
        services.AddScoped<IIdempotencyStore, EfIdempotencyStore>();
        services.AddScoped<IAuditRepository, EfAuditRepository>();
        services.AddScoped<IFlowSummaryQuery, EfFlowSummaryQuery>();
        services.AddScoped<IApiCollectionRepository, EfApiCollectionRepository>();
        services.AddScoped<IApiCollectionSummaryQuery, EfApiCollectionSummaryQuery>();
        services.AddScoped<IProjectRepository, EfProjectRepository>();
        services.AddScoped<IProjectSummaryQuery, EfProjectSummaryQuery>();
        services.AddScoped<ITestEnvironmentRepository, EfTestEnvironmentRepository>();
        services.AddScoped<ITestEnvironmentSummaryQuery, EfTestEnvironmentSummaryQuery>();
        services.AddScoped<ITriggerRepository, EfTriggerRepository>();
        services.AddScoped<ITriggerSummaryQuery, EfTriggerSummaryQuery>();
        services.AddScoped<IAuditSummaryQuery, EfAuditSummaryQuery>();
        services.AddScoped<ITenantSessionInitializer, EfTenantSessionInitializer>();

        return services;
    }
}
