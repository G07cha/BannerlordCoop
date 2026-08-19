using GameInterface.Services.Heroes.Extensions;
using GameInterface.Services.PlayerCaptivityService;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameComponents;

namespace GameInterface.Services.Party.Patches;

[HarmonyPatch(typeof(DefaultRansomValueCalculationModel))]
internal class RansomPlayerValuePatch
{
    [HarmonyPatch(nameof(DefaultRansomValueCalculationModel.PrisonerRansomValue))]
    [HarmonyPrefix]
    public static bool PrisonerRansomValuePrefix(ref int __result, CharacterObject prisoner, Hero sellerHero = null)
    {
        if (prisoner.IsHero &&
            prisoner.HeroObject.IsPlayerHero() &&
            PlayerRansomCooldownTracker.IsRansomOnCooldown(prisoner.HeroObject))
        {
            __result = 0;
            return false;
        }

        return true;
    }
}
