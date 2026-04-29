using CommunityToolkit.Mvvm.ComponentModel;
using SI360.GateRunner.Models;

namespace SI360.GateRunner.ViewModels;

public partial class ScorecardViewModel : ObservableObject
{
    [ObservableProperty] private double scenarioScore;
    [ObservableProperty] private double probabilisticScore;
    [ObservableProperty] private double overallScore;
    [ObservableProperty] private string grade = "-";
    [ObservableProperty] private DeployDecision decision = DeployDecision.NoGo;
    [ObservableProperty] private string decisionText = "NO-GO";

    public void Apply(Scorecard card, DeployDecision decision)
    {
        ScenarioScore = card.ScenarioScore;
        ProbabilisticScore = card.ProbabilisticScore;
        OverallScore = card.OverallScore;
        Grade = card.Grade;
        Decision = decision;
        DecisionText = decision switch
        {
            DeployDecision.Go => "GO",
            DeployDecision.Hold => "HOLD",
            _ => "NO-GO"
        };
    }
}
