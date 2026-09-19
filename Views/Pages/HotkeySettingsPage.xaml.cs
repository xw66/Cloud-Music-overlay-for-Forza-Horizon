using System.Windows.Controls;

namespace HorizonRadioOverlay.Views.Pages;

public partial class HotkeySettingsPage : UserControl
{
    public HotkeySettingsPage()
    {
        InitializeComponent();
    }

    public void SetupCaptures(Action<TextBox> configureKeyboard, Action<TextBox> configureGamepad)
    {
        configureKeyboard(AppPrevHotkeyBox);
        configureKeyboard(AppNextHotkeyBox);
        configureKeyboard(AppToggleHotkeyBox);
        configureKeyboard(AppToggleOverlayHotkeyBox);
        configureKeyboard(NeteasePrevHotkeyBox);
        configureKeyboard(NeteaseNextHotkeyBox);
        configureKeyboard(NeteaseToggleHotkeyBox);

        configureGamepad(GamepadPrevHotkeyBox);
        configureGamepad(GamepadNextHotkeyBox);
        configureGamepad(GamepadToggleHotkeyBox);
        configureGamepad(GamepadToggleOverlayHotkeyBox);
    }
}
