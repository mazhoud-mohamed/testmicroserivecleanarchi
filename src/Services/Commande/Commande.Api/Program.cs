using Commande.Api;
using Commande.Application;
using Commande.Infrastructure;
using Commande.Infrastructure.Persistence;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Serilog remplace le fournisseur de logs par défaut ; sa config est lue dans la section "Serilog".
builder.Host.UseSerilog((context, configuration) =>
    configuration.ReadFrom.Configuration(context.Configuration));

builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GestionnaireExceptions>();

var app = builder.Build();

// Une ligne de log par requête HTTP (méthode, route, statut, durée).
app.UseSerilogRequestLogging();

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwaggerUI(options => options.SwaggerEndpoint("/openapi/v1.json", "Commande API v1"));

    // Crée la base et les tables si elles n'existent pas (pas de migrations pour l'instant).
    using var scope = app.Services.CreateScope();
    scope.ServiceProvider.GetRequiredService<CommandeDbContext>().Database.EnsureCreated();
}

app.MapControllers();

app.Run();
