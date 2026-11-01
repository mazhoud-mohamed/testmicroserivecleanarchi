using Commande.Application.Commandes;
using Commande.Application.Commandes.Ecriture;
using Commande.Application.Commandes.Lecture;
using Microsoft.AspNetCore.Mvc;

namespace Commande.Api.Controllers;

[ApiController]
[Route("api/commandes")]
public class CommandesController(
    CreerCommandeHandler creer,
    ChangerStatutCommandeHandler changerStatut,
    ObtenirCommandeParIdHandler obtenirParId,
    ObtenirCommandesHandler obtenirToutes) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<CommandeDto>>> ObtenirToutes(CancellationToken ct) =>
        Ok(await obtenirToutes.Handle(new ObtenirCommandes(), ct));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<CommandeDto>> ObtenirParId(Guid id, CancellationToken ct) =>
        Ok(await obtenirParId.Handle(new ObtenirCommandeParId(id), ct));

    [HttpPost]
    public async Task<ActionResult<CommandeDto>> Creer(CreerCommande demande, CancellationToken ct)
    {
        var commande = await creer.Handle(demande, ct);
        return CreatedAtAction(nameof(ObtenirParId), new { id = commande.Id }, commande);
    }

    [HttpPost("{id:guid}/confirmer")]
    public async Task<ActionResult<CommandeDto>> Confirmer(Guid id, CancellationToken ct) =>
        Ok(await changerStatut.Handle(new ChangerStatutCommande(id, ActionCommande.Confirmer), ct));

    [HttpPost("{id:guid}/expedier")]
    public async Task<ActionResult<CommandeDto>> Expedier(Guid id, CancellationToken ct) =>
        Ok(await changerStatut.Handle(new ChangerStatutCommande(id, ActionCommande.Expedier), ct));

    [HttpPost("{id:guid}/annuler")]
    public async Task<ActionResult<CommandeDto>> Annuler(Guid id, CancellationToken ct) =>
        Ok(await changerStatut.Handle(new ChangerStatutCommande(id, ActionCommande.Annuler), ct));
}
