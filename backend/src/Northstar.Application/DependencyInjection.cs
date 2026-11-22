using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Northstar.Application.Abstractions;
using Northstar.Application.Services;
using Northstar.Application.Validation;

namespace Northstar.Application;

/// <summary>Registers application services and request validators.</summary>
public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddValidatorsFromAssemblyContaining<RegisterRequestValidator>();

        services.AddScoped<CustomerService>();
        services.AddScoped<ProductService>();
        services.AddScoped<ApplicationService>();
        services.AddScoped<DocumentService>();
        services.AddScoped<NotificationService>();
        services.AddScoped<TicketService>();
        services.AddScoped<AiAssistanceService>();
        services.AddScoped<DashboardService>();
        services.AddScoped<AdminService>();
        services.AddScoped<KnowledgeService>();
        services.AddScoped<IKnowledgeRetrievalService, KnowledgeRetrievalService>();
        services.AddScoped<AccountOverviewService>();

        return services;
    }
}
