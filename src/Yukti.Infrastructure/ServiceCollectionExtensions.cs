using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Yukti.Application.Abstractions;
using Yukti.Application.ApiTesting;
using Yukti.Application.UiVocabulary;
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
        services.AddScoped<IRequestTemplateRepository, EfRequestTemplateRepository>();
        services.AddScoped<IRequestTemplateUsageQuery, EfRequestTemplateUsageQuery>();
        services.AddScoped<IRequestTemplateSummaryQuery, EfRequestTemplateSummaryQuery>();
        services.AddScoped<IRequestTemplateExistsChecker, RequestTemplateExistsChecker>();
        services.AddScoped<IObjectRepositoryRepository, EfObjectRepositoryRepository>();
        services.AddScoped<IObjectRepositoryElementQuery, EfObjectRepositoryElementQuery>();
        services.AddScoped<IRepositoryEntryExistsChecker, RepositoryEntryExistsChecker>();
        services.AddScoped<IProjectRepository, EfProjectRepository>();
        services.AddScoped<IProjectSummaryQuery, EfProjectSummaryQuery>();
        services.AddScoped<ITestEnvironmentRepository, EfTestEnvironmentRepository>();
        services.AddScoped<ITestEnvironmentSummaryQuery, EfTestEnvironmentSummaryQuery>();
        services.AddScoped<ITriggerRepository, EfTriggerRepository>();
        services.AddScoped<ITriggerSummaryQuery, EfTriggerSummaryQuery>();
        services.AddScoped<IAuditSummaryQuery, EfAuditSummaryQuery>();
        services.AddScoped<IFlowReportSummaryQuery, EfFlowReportSummaryQuery>();
        services.AddScoped<ITenantSessionInitializer, EfTenantSessionInitializer>();
        // D5-T9: ai_cost_records — registered here regardless of Ai:Enabled, since the write/read
        // side is just persistence; Program.cs's own Ai:Enabled check decides whether anything
        // ever calls into the AI pipeline that would produce a record at all.
        services.AddScoped<Application.Abstractions.IAiCostRepository, EfAiCostRepository>();
        services.AddScoped<Application.Ai.IAiUsageQuery, EfAiUsageQuery>();

        return services;
    }
}
