using MockDataStudio.App.Services;
using Shouldly;
using Xunit;

namespace MockDataStudio.App.Tests;

// T-TPL-NAV-001..: guarda de navegação (TC-104) sem Shell nem janela.
public sealed class NavigationGateTests
{
    private sealed class FakePage(bool dirty, bool saveWorks = true) : INavigationGuard
    {
        public bool HasUnsavedChanges { get; private set; } = dirty;
        public string UnsavedChangesDescription => "pendente";
        public int Saves { get; private set; }
        public int Discards { get; private set; }

        public Task<bool> SaveChangesAsync()
        {
            Saves++;
            if (saveWorks)
            {
                HasUnsavedChanges = false;
            }

            return Task.FromResult(saveWorks);
        }

        public void DiscardChanges()
        {
            Discards++;
            HasUnsavedChanges = false;
        }
    }

    private sealed class FakePrompt(LeaveDecision answer) : IUnsavedChangesPrompt
    {
        public int Asked { get; private set; }

        public Task<LeaveDecision> AskAsync(string description)
        {
            Asked++;
            return Task.FromResult(answer);
        }
    }

    [Fact]
    [Trait("Id", "T-TPL-NAV-001")]
    public async Task SemPendenciaLiberaSemPerguntar()
    {
        var prompt = new FakePrompt(LeaveDecision.Stay);

        (await new NavigationGate(prompt).CanLeaveAsync(new FakePage(dirty: false))).ShouldBeTrue();
        (await new NavigationGate(prompt).CanLeaveAsync(new object())).ShouldBeTrue();
        (await new NavigationGate(prompt).CanLeaveAsync(null)).ShouldBeTrue();
        prompt.Asked.ShouldBe(0);
    }

    [Fact]
    [Trait("Id", "T-TPL-NAV-002")]
    public async Task SalvarLiberaQuandoGravaComSucesso()
    {
        var page = new FakePage(dirty: true);

        (await new NavigationGate(new FakePrompt(LeaveDecision.Save)).CanLeaveAsync(page)).ShouldBeTrue();
        page.Saves.ShouldBe(1);
        page.Discards.ShouldBe(0);
    }

    [Fact]
    [Trait("Id", "T-TPL-NAV-003")]
    public async Task SalvarComFalhaBloqueiaNavegacao()
    {
        var page = new FakePage(dirty: true, saveWorks: false);

        (await new NavigationGate(new FakePrompt(LeaveDecision.Save)).CanLeaveAsync(page)).ShouldBeFalse();
        page.HasUnsavedChanges.ShouldBeTrue();
    }

    [Fact]
    [Trait("Id", "T-TPL-NAV-004")]
    public async Task DescartarLiberaEDescarta()
    {
        var page = new FakePage(dirty: true);

        (await new NavigationGate(new FakePrompt(LeaveDecision.Discard)).CanLeaveAsync(page)).ShouldBeTrue();
        page.Discards.ShouldBe(1);
        page.Saves.ShouldBe(0);
    }

    [Fact]
    [Trait("Id", "T-TPL-NAV-005")]
    public async Task ContinuarEditandoBloqueiaSemAlterarNada()
    {
        var page = new FakePage(dirty: true);

        (await new NavigationGate(new FakePrompt(LeaveDecision.Stay)).CanLeaveAsync(page)).ShouldBeFalse();
        page.Saves.ShouldBe(0);
        page.Discards.ShouldBe(0);
        page.HasUnsavedChanges.ShouldBeTrue();
    }
}
