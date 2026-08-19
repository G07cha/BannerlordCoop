using Common.Messaging;
using GameInterface.Services.PlayerCaptivityService.Messages;
using GameInterface.Services.Players;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;

namespace GameInterface.Services.Party;

internal interface IPrisonerSaleProcessor
{
    void Sell(PartyBase sellingParty, TroopRoster requestedPrisoners);
}

internal readonly struct PrisonerSalePlan
{
    public readonly TroopRoster PrisonersForVanillaSale;
    public readonly IReadOnlyList<PlayerCaptivityEndedByServer> PlayerReleases;

    public PrisonerSalePlan(
        TroopRoster prisonersForVanillaSale,
        IReadOnlyList<PlayerCaptivityEndedByServer> playerReleases)
    {
        PrisonersForVanillaSale = prisonersForVanillaSale;
        PlayerReleases = playerReleases;
    }
}

/// <summary>
/// Applies an authoritative prisoner sale while releasing co-op player heroes through the full
/// player-captivity path that restores their parked parties.
/// </summary>
internal class PrisonerSaleProcessor : IPrisonerSaleProcessor
{
    private readonly IMessageBroker messageBroker;
    private readonly IPlayerManager playerManager;
    private readonly IPrisonerSaleValidator prisonerSaleValidator;
    private readonly IPlayerRansomReleaseSettlementProvider releaseSettlementProvider;

    public PrisonerSaleProcessor(
        IMessageBroker messageBroker,
        IPlayerManager playerManager,
        IPrisonerSaleValidator prisonerSaleValidator,
        IPlayerRansomReleaseSettlementProvider releaseSettlementProvider)
    {
        this.messageBroker = messageBroker;
        this.playerManager = playerManager;
        this.prisonerSaleValidator = prisonerSaleValidator;
        this.releaseSettlementProvider = releaseSettlementProvider;
    }

    public void Sell(PartyBase sellingParty, TroopRoster requestedPrisoners)
    {
        if (sellingParty == null) throw new System.ArgumentNullException(nameof(sellingParty));
        if (requestedPrisoners == null) throw new System.ArgumentNullException(nameof(requestedPrisoners));

        var validatedPrisoners = prisonerSaleValidator.Validate(
            requestedPrisoners,
            sellingParty.PrisonRoster);
        var plan = CreateSalePlan(validatedPrisoners, sellingParty);

        if (plan.PrisonersForVanillaSale.Count > 0)
        {
            SellPrisonersAction.ApplyForSelectedPrisoners(
                sellingParty,
                null,
                plan.PrisonersForVanillaSale);
        }

        foreach (var release in plan.PlayerReleases)
        {
            GiveRansomGold(sellingParty, release.PrisonerHero);
            messageBroker.Publish(this, release);
        }
    }

    /// <summary>
    /// Pays the seller the ransomed player hero's value, mirroring the gold vanilla
    /// <see cref="SellPrisonersAction"/> grants for ordinary prisoners. The ransom value is zero when the
    /// hero was already recently ransomed, so a repeat sale fetches no gold.
    /// </summary>
    private static void GiveRansomGold(PartyBase sellingParty, Hero playerHero)
    {
        if (Campaign.Current == null) return;

        int ransomValue = Campaign.Current.Models.RansomValueCalculationModel.PrisonerRansomValue(
            playerHero.CharacterObject,
            sellingParty.LeaderHero);
        if (ransomValue <= 0) return;

        if (sellingParty.IsMobile)
        {
            Hero recipientHero = GetSellerRecipient(sellingParty);
            if (recipientHero != null)
            {
                GiveGoldAction.ApplyBetweenCharacters(null, recipientHero, ransomValue, false);
            }
        }
        else
        {
            GiveGoldAction.ApplyForPartyToSettlement(null, sellingParty.Settlement, ransomValue, false);
        }
    }

    private static Hero GetSellerRecipient(PartyBase sellingParty)
    {
        if (sellingParty.LeaderHero != null && sellingParty.LeaderHero.HeroState == Hero.CharacterStates.Active)
            return sellingParty.LeaderHero;
        if (sellingParty.Owner != null && sellingParty.Owner.HeroState == Hero.CharacterStates.Active)
            return sellingParty.Owner;
        return sellingParty.MobileParty?.ActualClan?.Leader;
    }

    internal PrisonerSalePlan CreateSalePlan(
        TroopRoster validatedPrisoners,
        PartyBase sellingParty)
    {
        var prisonersForVanillaSale = new TroopRoster();
        var playerReleases = new List<PlayerCaptivityEndedByServer>();

        foreach (var prisoner in validatedPrisoners.GetTroopRoster())
        {
            var hero = prisoner.Character?.HeroObject;
            if (hero != null && playerManager.Contains(hero))
            {
                var releaseSettlement = releaseSettlementProvider.GetReleaseSettlement(sellingParty, hero);
                playerReleases.Add(new PlayerCaptivityEndedByServer(
                    hero,
                    EndCaptivityDetail.Ransom,
                    null,
                    releaseSettlement.GatePosition));
                continue;
            }

            prisonersForVanillaSale.AddToCounts(
                prisoner.Character,
                prisoner.Number,
                false,
                prisoner.WoundedNumber,
                prisoner.Xp,
                true);
        }

        return new PrisonerSalePlan(prisonersForVanillaSale, playerReleases);
    }
}
