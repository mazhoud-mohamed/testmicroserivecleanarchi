using Commande.Api;
using Commande.Application;
using Commande.Infrastructure;
using Commande.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GestionnaireExceptions>();

var app = builder.Build();

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();

    // Crée la base et les tables si elles n'existent pas (pas de migrations pour l'instant).
    using var scope = app.Services.CreateScope();
    scope.ServiceProvider.GetRequiredService<CommandeDbContext>().Database.EnsureCreated();
}

app.MapControllers();

app.Run();
