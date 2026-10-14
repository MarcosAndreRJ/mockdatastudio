using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MockDataStudio.App.Resources;
using MockDataStudio.Application;

namespace MockDataStudio.App.ViewModels.Templates;

// Linha da lista de regras aprendidas (FR-064).
public sealed record LearnedRuleRow(long Id, string Column, string Target, string TargetKind);

/// <summary>Regras aprendidas por "Lembrar para outros projetos" no Mapeamento: listar e esquecer (FR-064, TC-101).</summary>
public sealed partial class LearnedRulesViewModel : ObservableObject
{
    private readonly MappingLearningService _learning;

    public LearnedRulesViewModel(MappingLearningService learning)
    {
        _learning = learning;
        ForgetRuleCommand = new AsyncRelayCommand<LearnedRuleRow?>(ForgetRuleAsync);
        RefreshCommand = new AsyncRelayCommand(RefreshAsync);
        _ = RefreshAsync();
    }

    public ObservableCollection<LearnedRuleRow> Rules { get; } = [];

    public IAsyncRelayCommand<LearnedRuleRow?> ForgetRuleCommand { get; }

    public IAsyncRelayCommand RefreshCommand { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsReady))]
    private ViewState _state = ViewState.Loading;

    [ObservableProperty]
    private string _statusMessage = TemplatesText.StateLoading;

    public bool IsReady => State == ViewState.Ready;

    public async Task RefreshAsync()
    {
        State = ViewState.Loading;
        StatusMessage = TemplatesText.StateLoading;
        try
        {
            var rules = await _learning.LearnedRulesAsync(CancellationToken.None);
            Rules.Clear();
            foreach (var rule in rules)
            {
                Rules.Add(new LearnedRuleRow(rule.Id, rule.ColumnPattern, rule.TargetRef, rule.TargetKind.ToString()));
            }

            State = Rules.Count == 0 ? ViewState.Empty : ViewState.Ready;
            StatusMessage = Rules.Count == 0 ? TemplatesText.RulesEmpty : TemplatesText.RulesCount(Rules.Count);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or System.Data.Common.DbException)
        {
            State = ViewState.Error;
            StatusMessage = TemplatesText.StateError(ex.Message);
        }
    }

    private async Task ForgetRuleAsync(LearnedRuleRow? rule)
    {
        if (rule is null)
        {
            return;
        }

        await _learning.ForgetAsync(rule.Id, CancellationToken.None);
        await RefreshAsync();
        StatusMessage = TemplatesText.RuleForgotten(rule.Column);
    }
}
