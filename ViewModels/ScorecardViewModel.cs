using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using SI360.GateRunner.Models;
using SI360.GateRunner.Services;

namespace SI360.GateRunner.ViewModels;

public partial class ScorecardViewModel : ObservableObject
{
    public ScorecardViewModel()
    {
        ApplyGateResults(null);
    }

    [ObservableProperty] private double scenarioScore;
    [ObservableProperty] private double probabilisticScore;
    [ObservableProperty] private double overallScore;
    [ObservableProperty] private string grade = "-";
    [ObservableProperty] private DeployDecision decision = DeployDecision.NoGo;
    [ObservableProperty] private string decisionText = "NO-GO";

    public ObservableCollection<GateScorecardItem> GateItems { get; } = new();

    public void Apply(Scorecard card, DeployDecision decision)
    {
        Apply(card, decision, null);
    }

    public void Apply(Scorecard card, DeployDecision decision, IEnumerable<GateResult>? gateResults)
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

        ApplyGateResults(gateResults);
    }

    public void ApplyGateResults(IEnumerable<GateResult>? gateResults)
    {
        GateItems.Clear();
        foreach (var item in GateScorecardBuilder.Build(gateResults))
            GateItems.Add(item);
    }
}
