using HorizonRadioOverlay.Models;
using HorizonRadioOverlay.ViewModels;

namespace HorizonRadioOverlay.Tests;

public sealed class FloatingSettingsRepositionTests
{
    [Fact]
    public void UpdatePositionFromDrag_updates_percents_and_text_correctly()
    {
        FloatingSettingsViewModel vm = new();

        vm.UpdatePositionFromDrag(0.35, 0.72);

        Assert.Equal(35.0, vm.HorizontalPercent);
        Assert.Equal(72.0, vm.BottomOffsetPercent);
        Assert.Equal("35%", vm.HorizontalText);
        Assert.Equal("72%", vm.BottomOffsetText);
    }

    [Fact]
    public void IsPositionAdjusting_toggles_button_text()
    {
        FloatingSettingsViewModel vm = new();

        Assert.False(vm.IsPositionAdjusting);
        Assert.Equal(UiText.StartDragReposition, vm.AdjustPositionButtonText);

        vm.IsPositionAdjusting = true;
        Assert.Equal(UiText.FinishDragReposition, vm.AdjustPositionButtonText);
    }

    [Fact]
    public void ToggleDragRepositionCommand_fires_event()
    {
        FloatingSettingsViewModel vm = new();
        bool eventFired = false;
        vm.ToggleDragRepositionRequested += () => eventFired = true;

        vm.ToggleDragRepositionCommand.Execute(null);

        Assert.True(eventFired);
    }
}
