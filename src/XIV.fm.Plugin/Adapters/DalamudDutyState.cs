using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;

namespace XIV.fm.Plugin.Adapters;

public static class DalamudDutyState
{
    public static bool IsInDuty(ICondition condition) => condition.Any(
        ConditionFlag.BoundByDuty,
        ConditionFlag.BoundByDuty56,
        ConditionFlag.BoundByDuty95);
}
