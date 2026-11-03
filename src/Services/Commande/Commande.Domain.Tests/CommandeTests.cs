using Commande.Domain.Commandes;
using Commande.Domain.Commandes.Evenements;
using Commande.Domain.Common;
using CommandeAgregat = Commande.Domain.Commandes.Commande;

namespace Commande.Domain.Tests;

public class CommandeTests
{
    private static readonly Guid ClientId = Guid.NewGuid();
    private static readonly Guid ProduitId = Guid.NewGuid();

    // Une commande en attente avec une ligne : le point de départ de la plupart des tests.
    private static CommandeAgregat CommandeAvecUneLigne()
    {
        var commande = new CommandeAgregat(ClientId);
        commande.AjouterLigne(ProduitId, "Clavier", 50m, 2);
        return commande;
    }

    [Fact]
    public void Creer_sans_client_est_refuse()
    {
        Assert.Throws<DomainException>(() => new CommandeAgregat(Guid.Empty));
    }

    [Fact]
    public void Nouvelle_commande_est_en_attente_et_sans_evenement()
    {
        var commande = new CommandeAgregat(ClientId);

        Assert.Equal(StatutCommande.EnAttente, commande.Statut);
        Assert.Empty(commande.Evenements);
    }

    [Fact]
    public void Montant_total_est_la_somme_des_lignes()
    {
        var commande = CommandeAvecUneLigne();           // 2 x 50 = 100
        commande.AjouterLigne(Guid.NewGuid(), "Souris", 15.5m, 2); // 2 x 15,5 = 31

        Assert.Equal(131m, commande.MontantTotal);
    }

    [Theory]
    [InlineData("", 10, 1)]     // nom vide
    [InlineData("Clavier", -1, 1)] // prix négatif
    [InlineData("Clavier", 10, 0)] // quantité nulle
    [InlineData("Clavier", 10, -3)] // quantité négative
    public void Ajouter_une_ligne_invalide_est_refuse(string nom, decimal prix, int quantite)
    {
        var commande = new CommandeAgregat(ClientId);

        Assert.Throws<DomainException>(() => commande.AjouterLigne(ProduitId, nom, prix, quantite));
    }

    [Fact]
    public void Confirmer_sans_ligne_est_refuse()
    {
        var commande = new CommandeAgregat(ClientId);

        Assert.Throws<DomainException>(commande.Confirmer);
    }

    [Fact]
    public void Confirmer_change_le_statut_et_leve_un_seul_evenement()
    {
        var commande = CommandeAvecUneLigne();

        commande.Confirmer();

        Assert.Equal(StatutCommande.Confirmee, commande.Statut);
        var evenement = Assert.IsType<CommandeConfirmee>(Assert.Single(commande.Evenements));
        Assert.Equal(commande.Id, evenement.CommandeId);
        Assert.Equal(ClientId, evenement.ClientId);
        Assert.Equal(100m, evenement.MontantTotal);
        var ligne = Assert.Single(evenement.Lignes);
        Assert.Equal(ProduitId, ligne.ProduitId);
        Assert.Equal(2, ligne.Quantite);
    }

    [Fact]
    public void Confirmer_deux_fois_est_refuse_et_ne_leve_pas_un_second_evenement()
    {
        var commande = CommandeAvecUneLigne();
        commande.Confirmer();

        Assert.Throws<DomainException>(commande.Confirmer);
        Assert.Single(commande.Evenements);
    }

    [Fact]
    public void Ajouter_une_ligne_apres_confirmation_est_refuse()
    {
        var commande = CommandeAvecUneLigne();
        commande.Confirmer();

        Assert.Throws<DomainException>(() => commande.AjouterLigne(ProduitId, "Souris", 10m, 1));
    }

    [Fact]
    public void Expedier_avant_confirmation_est_refuse()
    {
        var commande = CommandeAvecUneLigne();

        Assert.Throws<DomainException>(commande.Expedier);
        Assert.Equal(StatutCommande.EnAttente, commande.Statut);
    }

    [Fact]
    public void Expedier_une_commande_confirmee_leve_CommandeExpediee()
    {
        var commande = CommandeAvecUneLigne();
        commande.Confirmer();

        commande.Expedier();

        Assert.Equal(StatutCommande.Expediee, commande.Statut);
        Assert.Contains(commande.Evenements, e => e is CommandeExpediee);
    }

    [Fact]
    public void Annuler_une_commande_en_attente_leve_CommandeAnnulee()
    {
        var commande = CommandeAvecUneLigne();

        commande.Annuler();

        Assert.Equal(StatutCommande.Annulee, commande.Statut);
        Assert.IsType<CommandeAnnulee>(Assert.Single(commande.Evenements));
    }

    [Fact]
    public void Annuler_une_commande_expediee_est_refuse()
    {
        var commande = CommandeAvecUneLigne();
        commande.Confirmer();
        commande.Expedier();

        Assert.Throws<DomainException>(commande.Annuler);
        Assert.Equal(StatutCommande.Expediee, commande.Statut);
    }

    [Fact]
    public void Annuler_deux_fois_est_refuse()
    {
        var commande = CommandeAvecUneLigne();
        commande.Annuler();

        Assert.Throws<DomainException>(commande.Annuler);
    }

    [Fact]
    public void ViderEvenements_vide_la_liste()
    {
        var commande = CommandeAvecUneLigne();
        commande.Confirmer();

        commande.ViderEvenements();

        Assert.Empty(commande.Evenements);
    }
}
