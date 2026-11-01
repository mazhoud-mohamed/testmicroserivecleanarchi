using FluentValidation;

namespace Commande.Application.Commandes.Ecriture;

public class CreerCommandeValidator : AbstractValidator<CreerCommande>
{
    public CreerCommandeValidator()
    {
        RuleFor(c => c.ClientId)
            .NotEmpty().WithMessage("Le client est obligatoire.");

        RuleFor(c => c.Lignes)
            .NotEmpty().WithMessage("La commande doit contenir au moins une ligne.");

        RuleForEach(c => c.Lignes).ChildRules(ligne =>
        {
            ligne.RuleFor(l => l.ProduitId)
                .NotEmpty().WithMessage("Le produit est obligatoire.");
            ligne.RuleFor(l => l.NomProduit)
                .NotEmpty().WithMessage("Le nom du produit est obligatoire.")
                .MaximumLength(200).WithMessage("Le nom du produit ne peut pas dépasser 200 caractères.");
            ligne.RuleFor(l => l.PrixUnitaire)
                .GreaterThanOrEqualTo(0).WithMessage("Le prix unitaire ne peut pas être négatif.");
            ligne.RuleFor(l => l.Quantite)
                .GreaterThan(0).WithMessage("La quantité doit être supérieure à zéro.");
        });
    }
}
