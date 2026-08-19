using System.Runtime.CompilerServices;
using TaleWorlds.CampaignSystem;

namespace GameInterface.Services.PlayerCaptivityService;

/// <summary>
/// Tracks the last ransom release of each player hero so a hero only fetches a ransom value once per
/// in-game day. Server-authoritative: marking happens on the server when a player hero is released by
/// ransom, and <see cref="Services.Party.Patches.RansomPlayerValuePatch"/> zeroes the value on a repeat
/// ransom while the cooldown is active.
/// </summary>
internal static class PlayerRansomCooldownTracker
{
    private static ConditionalWeakTable<Hero, RansomDeadline> ransomDeadlines = new();

    /// <summary>
    /// Records that <paramref name="hero"/> was released by ransom now, so further ransoms fetch zero
    /// gold until the deadline is expired.
    /// </summary>
    public static void MarkRansomed(Hero hero)
    {
        if (hero == null || Campaign.Current == null) return;

        ransomDeadlines.Remove(hero);
        ransomDeadlines.Add(hero, new RansomDeadline(CampaignTime.DaysFromNow(1)));
    }

    /// <summary>
    /// True when <paramref name="hero"/> was ransomed within the deadline. Expired entries are
    /// pruned on check.
    /// </summary>
    public static bool IsRansomOnCooldown(Hero hero)
    {
        if (hero == null || Campaign.Current == null) return false;

        if (!ransomDeadlines.TryGetValue(hero, out var deadline)) return false;

        if (!deadline.Until.IsPast) return true;

        ransomDeadlines.Remove(hero);
        return false;
    }

    public static void Reset()
    {
        ransomDeadlines = new ConditionalWeakTable<Hero, RansomDeadline>();
    }

    private sealed class RansomDeadline
    {
        public readonly CampaignTime Until;

        public RansomDeadline(CampaignTime until)
        {
            Until = until;
        }
    }
}