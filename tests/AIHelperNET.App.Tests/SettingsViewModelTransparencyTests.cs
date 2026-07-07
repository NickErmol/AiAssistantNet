using AIHelperNET.App.ViewModels;
using AIHelperNET.Application.Abstractions;
using FluentAssertions;
using FluentAssertions.Events;
using Mediator;
using NSubstitute;
using Xunit;

namespace AIHelperNET.App.Tests;

/// <summary>Verifies that <see cref="SettingsViewModel.OverlayTransparency"/> is the inverse
/// of <see cref="SettingsViewModel.OverlayOpacity"/> and that changing the opacity raises
/// <see cref="System.ComponentModel.INotifyPropertyChanged.PropertyChanged"/> for the computed property.</summary>
public class SettingsViewModelTransparencyTests
{
    private static SettingsViewModel CreateVm()
        => new(Substitute.For<IMediator>(), new StubHotkeyApplier(), Substitute.For<ITranscriptionGlossaryProvider>(),
               Substitute.For<AIHelperNET.Application.Abstractions.IDocumentTextExtractor>(),
               Substitute.For<AIHelperNET.Application.Abstractions.IProfileCondenser>());

    [Fact]
    public void OverlayTransparency_is_inverse_of_opacity()
    {
        var vm = CreateVm();
        vm.OverlayOpacity = 0.75;
        vm.OverlayTransparency.Should().BeApproximately(0.25, 1e-9);
    }

    [Fact]
    public void OverlayTransparency_reflects_updated_opacity()
    {
        var vm = CreateVm();
        vm.OverlayOpacity = 0.4;
        vm.OverlayTransparency.Should().BeApproximately(0.6, 1e-9);
    }

    [Fact]
    public void Changing_opacity_notifies_transparency()
    {
        var vm = CreateVm();
        using IMonitor<SettingsViewModel> monitor = vm.Monitor();
        vm.OverlayOpacity = 0.4;
        monitor.Should().RaisePropertyChangeFor(v => v.OverlayTransparency);
    }
}
