using Microsoft.EntityFrameworkCore;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Stock.Api;
using Stock.Application;
using Stock.Infrastructure;
using Stock.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, configuration) =>
    configuration.ReadFrom.Configuration(context.Configuration));

// OBSERVABILITÉ : OpenTelemetry produit des TRACES. Une trace = l'histoire d'une opération, sous forme d'un arbre
// d'étapes (spans) qui partagent un même TraceId : la requête HTTP, puis chaque commande SQL qu'elle a déclenchée...
// Le nom du service permet de distinguer Commande de Stock quand les traces seront rassemblées.
builder.Services.AddOpenTelemetry()
    .ConfigureResource(ressource => ressource.AddService("stock-api"))
    .WithTracing(traces =>
    {
        traces
            // Nos propres spans (publication depuis l'Outbox, réception d'un message) : sans cette ligne, ils sont ignorés.
            .AddSource(Telemetrie.NomSource)
            .AddAspNetCoreInstrumentation(o =>
            {
                // Pas de trace pour la documentation de l'API : ce n'est pas du métier, ça noie le reste.
                o.Filter = ctx => !ctx.Request.Path.StartsWithSegments("/openapi")
                                  && !ctx.Request.Path.StartsWithSegments("/swagger");
                o.RecordException = true;
            })
            // EF Core passe par SqlClient : on voit donc chaque requête SQL, avec sa durée, comme un span enfant.
            .AddSqlClientInstrumentation(o =>
            {
                o.RecordException = true;
                // Le publicateur de l'Outbox interroge la base toutes les 2 secondes, même quand il n'y a rien à envoyer :
                // une trace par sondage noierait les vraies opérations.
                o.Filter = commande => commande is not System.Data.IDbCommand c || !c.CommandText.Contains("[TraiteLe] IS NULL");
            });

        // Envoi des traces à un collecteur (Jaeger) via OTLP, le protocole standard d'OpenTelemetry.
        // Sans adresse dans la configuration, rien n'est envoyé : l'application ne dépend pas du collecteur.
        var adresseOtlp = builder.Configuration["OpenTelemetry:Otlp:Endpoint"];
        if (!string.IsNullOrWhiteSpace(adresseOtlp))
            traces.AddOtlpExporter(o => o.Endpoint = new Uri(adresseOtlp));

        // Affichage des spans dans la console : utile pour déboguer, mais très verbeux, donc désactivé par défaut.
        if (builder.Configuration.GetValue<bool>("OpenTelemetry:Console"))
            traces.AddConsoleExporter();
    })
    .WithMetrics(mesures =>
    {
        // Nos mesures (Outbox, messages reçus) et celles d'ASP.NET (nombre et durée des requêtes HTTP).
        mesures.AddMeter(Telemetrie.NomSource).AddAspNetCoreInstrumentation();

        // Affichage console : un gros bloc toutes les 10 secondes, donc désactivé par défaut (même réglage que les traces).
        if (builder.Configuration.GetValue<bool>("OpenTelemetry:Console"))
            mesures.AddConsoleExporter();
    });

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
