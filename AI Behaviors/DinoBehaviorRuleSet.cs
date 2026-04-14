public static class DinoBehaviorRuleSet
{
    private static readonly DinoBehaviorRuleTemplate passive = new PassiveAggressionBehavior();
    private static readonly DinoBehaviorRuleTemplate neutral = new NeutralAggressionBehavior();
    private static readonly DinoBehaviorRuleTemplate plunderer = new PlundererAggressionBehavior();
    private static readonly DinoBehaviorRuleTemplate hunter = new HunterAggressionBehavior();
    private static readonly DinoBehaviorRuleTemplate roamer = new RoamerAggressionBehavior();
    private static readonly DinoBehaviorRuleTemplate apex = new ApexAggressionBehavior();

    public static DinoBehaviorRuleTemplate Create(DinoAI.AggressionType aggressionType)
    {
        switch (aggressionType)
        {
            case DinoAI.AggressionType.Passive:
                return passive;
            case DinoAI.AggressionType.Neutral:
                return neutral;
            case DinoAI.AggressionType.Plunderer:
                return plunderer;
            case DinoAI.AggressionType.Hunter:
                return hunter;
            case DinoAI.AggressionType.Roamer:
                return roamer;
            case DinoAI.AggressionType.Apex:
                return apex;
            default:
                return apex;
        }
    }

    public static void CleanupState(DinoAI ai)
    {
        if (ai == null)
        {
            return;
        }

        int key = ai.GetInstanceID();
        switch (ai.aggressionType)
        {
            case DinoAI.AggressionType.Roamer:
                ((RoamerAggressionBehavior)roamer).CleanupState(key);
                break;
            case DinoAI.AggressionType.Plunderer:
                ((PlundererAggressionBehavior)plunderer).CleanupState(key);
                break;
        }
    }
}
