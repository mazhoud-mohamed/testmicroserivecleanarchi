using Commande.Api;
using Commande.Application;
using Commande.Application.Commandes.Ecriture;
using Commande.Application.Commandes.Lecture;
using Commande.Infrastructure;
using Commande.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

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

var commandes = app.MapGroup("/api/commandes").WithTags("Commandes");

commandes.MapGet("/", async (ObtenirCommandesHandler handler, CancellationToken ct) =>
    Results.Ok(await handler.Handle(new ObtenirCommandes(), ct)));

commandes.MapGet("/{id:guid}", async (Guid id, ObtenirCommandeParIdHandler handler, CancellationToken ct) =>
    Results.Ok(await handler.Handle(new ObtenirCommandeParId(id), ct)));

commandes.MapPost("/", async (CreerCommande demande, CreerCommandeHandler handler, CancellationToken ct) =>
{
    var commande = await handler.Handle(demande, ct);
    return Results.Created($"/api/commandes/{commande.Id}", commande);
});

commandes.MapPost("/{id:guid}/confirmer", (Guid id, ChangerStatutCommandeHandler handler, CancellationToken ct) =>
    ChangerStatut(id, ActionCommande.Confirmer, handler, ct));

commandes.MapPost("/{id:guid}/expedier", (Guid id, ChangerStatutCommandeHandler handler, CancellationToken ct) =>
    ChangerStatut(id, ActionCommande.Expedier, handler, ct));

commandes.MapPost("/{id:guid}/annuler", (Guid id, ChangerStatutCommandeHandler handler, CancellationToken ct) =>
    ChangerStatut(id, ActionCommande.Annuler, handler, ct));

app.Run();

static async Task<IResult> ChangerStatut(
    Guid id, ActionCommande action, ChangerStatutCommandeHandler handler, CancellationToken ct) =>
    Results.Ok(await handler.Handle(new ChangerStatutCommande(id, action), ct));
