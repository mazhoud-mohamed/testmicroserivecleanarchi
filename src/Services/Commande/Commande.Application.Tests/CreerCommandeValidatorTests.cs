using Commande.Application.Commandes.Ecriture;
using FluentValidation.TestHelper;

namespace Commande.Application.Tests;

public class CreerCommandeValidatorTests
{
    private readonly CreerCommandeValidator _validateur = new();

    private static LigneACreer LigneValide() => new(Guid.NewGuid(), "Clavier", 49.9m, 2);

    private static CreerCommande CommandeValide() => new(Guid.NewGuid(), [LigneValide()]);

    [Fact]
    public void Une_commande_valide_est_acceptee()
    {
        var resultat = _validateur.TestValidate(CommandeValide());

        resultat.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Client_vide_est_refuse()
    {
        var resultat = _validateur.TestValidate(CommandeValide() with { ClientId = Guid.Empty });

        resultat.ShouldHaveValidationErrorFor(c => c.ClientId).WithErrorMessage("Le client est obligatoire.");
    }

    [Fact]
    public void Liste_de_lignes_vide_est_refusee()
    {
        var resultat = _validateur.TestValidate(CommandeValide() with { Lignes = [] });

        resultat.ShouldHaveValidationErrorFor(c => c.Lignes)
            .WithErrorMessage("La commande doit contenir au moins une ligne.");
    }

    [Fact]
    public void Produit_vide_est_refuse()
    {
        var commande = CommandeValide() with { Lignes = [LigneValide() with { ProduitId = Guid.Empty }] };

        var resultat = _validateur.TestValidate(commande);

        resultat.ShouldHaveValidationErrorFor("Lignes[0].ProduitId").WithErrorMessage("Le produit est obligatoire.");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Nom_de_produit_vide_est_refuse(string nom)
    {
        var commande = CommandeValide() with { Lignes = [LigneValide() with { NomProduit = nom }] };

        var resultat = _validateur.TestValidate(commande);

        resultat.ShouldHaveValidationErrorFor("Lignes[0].NomProduit");
    }

    [Fact]
    public void Nom_de_produit_de_200_caracteres_est_accepte_mais_201_est_refuse()
    {
        var limite = CommandeValide() with { Lignes = [LigneValide() with { NomProduit = new string('a', 200) }] };
        var depassement = CommandeValide() with { Lignes = [LigneValide() with { NomProduit = new string('a', 201) }] };

        _validateur.TestValidate(limite).ShouldNotHaveValidationErrorFor("Lignes[0].NomProduit");
        _validateur.TestValidate(depassement).ShouldHaveValidationErrorFor("Lignes[0].NomProduit");
    }

    [Fact]
    public void Prix_negatif_est_refuse_mais_prix_zero_est_accepte()
    {
        var negatif = CommandeValide() with { Lignes = [LigneValide() with { PrixUnitaire = -0.01m }] };
        var zero = CommandeValide() with { Lignes = [LigneValide() with { PrixUnitaire = 0m }] };

        _validateur.TestValidate(negatif).ShouldHaveValidationErrorFor("Lignes[0].PrixUnitaire");
        _validateur.TestValidate(zero).ShouldNotHaveValidationErrorFor("Lignes[0].PrixUnitaire");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Quantite_nulle_ou_negative_est_refusee(int quantite)
    {
        var commande = CommandeValide() with { Lignes = [LigneValide() with { Quantite = quantite }] };

        var resultat = _validateur.TestValidate(commande);

        resultat.ShouldHaveValidationErrorFor("Lignes[0].Quantite");
    }

    [Fact]
    public void L_erreur_indique_la_ligne_concernee()
    {
        var commande = CommandeValide() with { Lignes = [LigneValide(), LigneValide() with { Quantite = 0 }] };

        var resultat = _validateur.TestValidate(commande);

        resultat.ShouldNotHaveValidationErrorFor("Lignes[0].Quantite");
        resultat.ShouldHaveValidationErrorFor("Lignes[1].Quantite");
    }
}
