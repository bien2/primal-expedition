public class PassiveAggressionBehavior : DinoBehaviorRuleTemplate
{
    public void HandleIdle(DinoAI ai)
    {
        ai.ChangeState(DinoAI.DinoState.Roam);
        ai.ExecuteRoamCycle(false, true);
    }

    public void HandleRoam(DinoAI ai)
    {
        ai.ExecuteRoamCycle(false, true);
    }

    public void HandleInvestigate(DinoAI ai)
    {
        ai.ClearInvestigate();
        ai.ChangeState(DinoAI.DinoState.Roam);
    }

    public void HandleChase(DinoAI ai)
    {
        ai.StopChase();
    }
}
