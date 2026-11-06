using Microsoft.EntityFrameworkCore;
using Serilog;
using Stock.Api;
using Stock.Application;
using Stock.Infrastructure;
using Stock.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, configuration) =>
    configuration.ReadFrom.Configuration(context.Configuration));

builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GestionnaireExceptions>();

var app = builder.Build();

app.UseSerilogRequestLogging();
app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwaggerUI(options => options.SwaggerEndpoint("/openapi/v1.json", "Stock API v1"));

    // Applique les migrations en attente : crée la base si besoin, puis fait évoluer le schéma pas à pas.
    using var scope = app.Services.CreateScope();
    scope.ServiceProvider.GetRequiredService<StockDbContext>().Database.Migrate();
}

app.MapControllers();

app.Run();
