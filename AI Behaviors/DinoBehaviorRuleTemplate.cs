public interface DinoBehaviorRuleTemplate
{
    void HandleIdle(DinoAI ai);
    void HandleRoam(DinoAI ai);
    void HandleInvestigate(DinoAI ai);
    void HandleChase(DinoAI ai);
}
