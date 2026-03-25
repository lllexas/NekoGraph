namespace NekoGraph.Cli;

internal static class TriggerEventCatalog
{
    private static readonly string[] EventNames =
    [
        "GameStarted",
        "GameTickUpdated",
        "UnitSpawned",
        "UnitKilled",
        "UnitDamaged",
        "MoneyChanged",
        "ResourceChanged",
        "BuildingConstructed",
        "MissionCompleted",
        "ResearchCompleted",
        "GroundClicked",
        "UnitSelected",
        "SocialOption1",
        "SocialOption2",
        "SocialOption3",
        "SocialOption4",
        "BaseUnderAttack"
    ];

    public static string GetName(int enumIndex)
    {
        return enumIndex >= 0 && enumIndex < EventNames.Length
            ? EventNames[enumIndex]
            : $"UnknownEvent({enumIndex})";
    }
}
