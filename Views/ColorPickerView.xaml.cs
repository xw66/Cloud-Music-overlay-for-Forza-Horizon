using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using HorizonRadioOverlay.Services;

namespace HorizonRadioOverlay.Views;

public partial class ColorPickerView : UserControl
{
    public static readonly DependencyProperty SelectedHexColorProperty =
        DependencyProperty.Register(
            nameof(SelectedHexColor),
            typeof(string),
            typeof(ColorPickerView),
            new FrameworkPropertyMetadata("#FFFFFF", FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnSelectedHexColorChanged));

    public static readonly DependencyProperty TitleTextProperty =
        DependencyProperty.Register(
            nameof(TitleText),
            typeof(string),
            typeof(ColorPickerView),
            new PropertyMetadata("颜色调节", OnTitleTextChanged));

    public string SelectedHexColor
    {
        get => (string)GetValue(SelectedHexColorProperty);
        set => SetValue(SelectedHexColorProperty, value);
    }

    public string TitleText
    {
        get => (string)GetValue(TitleTextProperty);
        set => SetValue(TitleTextProperty, value);
    }

    public event Action<string>? ColorChanged;

    private double _hue = 0.0;
    private double _saturation = 0.0;
    private double _value = 1.0;
    private bool _isUpdatingInternally;
    private bool _isDraggingSatVal;
    private bool _isDraggingHue;

    public ColorPickerView()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            UpdateFromHex(SelectedHexColor);
        };
        SizeChanged += (_, _) =>
        {
            UpdateSatValCursorPosition();
            UpdateHueCursorPosition();
        };
    }

    private static void OnSelectedHexColorChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ColorPickerView picker && !picker._isUpdatingInternally && e.NewValue is string hex)
        {
            picker.UpdateFromHex(hex);
        }
    }

    private static void OnTitleTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ColorPickerView picker && e.NewValue is string title)
        {
            picker.ColorTargetLabel.Text = title;
        }
    }

    public void SetColor(string hex)
    {
        UpdateFromHex(hex);
    }

    private void UpdateFromHex(string? hex)
    {
        if (!ColorUtils.TryParseHex(hex, out Color color))
        {
            color = Colors.White;
        }

        var (h, s, v) = ColorUtils.ColorToHsv(color);
        _hue = h;
        _saturation = s;
        _value = v;

        UpdateUiFromHsv(triggerEvent: false);
    }

    private void UpdateUiFromHsv(bool triggerEvent = true)
    {
        _isUpdatingInternally = true;
        try
        {
            Color pureHueColor = ColorUtils.HsvToColor(_hue, 1.0, 1.0);
            HueBackgroundRect.Fill = new SolidColorBrush(pureHueColor);

            Color finalColor = ColorUtils.HsvToColor(_hue, _saturation, _value);
            ColorPreviewSwatch.Color = finalColor;
            string hex = ColorUtils.ColorToHex(finalColor);
            HexInputBox.Text = hex;
            SelectedHexColor = hex;

            UpdateSatValCursorPosition();
            UpdateHueCursorPosition();

            if (triggerEvent)
            {
                ColorChanged?.Invoke(hex);
            }
        }
        finally
        {
            _isUpdatingInternally = false;
        }
    }

    private void UpdateSatValCursorPosition()
    {
        double width = SatValBox.ActualWidth > 0 ? SatValBox.ActualWidth : 286;
        double height = SatValBox.ActualHeight > 0 ? SatValBox.ActualHeight : 160;

        double x = _saturation * width;
        double y = (1.0 - _value) * height;

        Canvas.SetLeft(SatValCursor, Math.Clamp(x - 7, -7, width - 7));
        Canvas.SetTop(SatValCursor, Math.Clamp(y - 7, -7, height - 7));
    }

    private void UpdateHueCursorPosition()
    {
        double width = HueBar.ActualWidth > 0 ? HueBar.ActualWidth : 286;
        double x = (_hue / 360.0) * width;
        Canvas.SetLeft(HueCursor, Math.Clamp(x - 8, -8, width - 8));
        Canvas.SetTop(HueCursor, 1);
    }

    private void SatValBox_MouseDown(object sender, MouseButtonEventArgs e)
    {
        _isDraggingSatVal = true;
        SatValBox.CaptureMouse();
        UpdateSatValFromMouse(e.GetPosition(SatValBox));
    }

    private void SatValBox_MouseMove(object sender, MouseEventArgs e)
    {
        if (_isDraggingSatVal)
        {
            UpdateSatValFromMouse(e.GetPosition(SatValBox));
        }
    }

    private void SatValBox_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_isDraggingSatVal)
        {
            _isDraggingSatVal = false;
            SatValBox.ReleaseMouseCapture();
        }
    }

    private void UpdateSatValFromMouse(Point pos)
    {
        double width = SatValBox.ActualWidth;
        double height = SatValBox.ActualHeight;
        if (width <= 0 || height <= 0) return;

        double s = Math.Clamp(pos.X / width, 0.0, 1.0);
        double v = Math.Clamp(1.0 - (pos.Y / height), 0.0, 1.0);

        _saturation = s;
        _value = v;

        UpdateUiFromHsv(triggerEvent: true);
    }

    private void HueBar_MouseDown(object sender, MouseButtonEventArgs e)
    {
        _isDraggingHue = true;
        HueBar.CaptureMouse();
        UpdateHueFromMouse(e.GetPosition(HueBar));
    }

    private void HueBar_MouseMove(object sender, MouseEventArgs e)
    {
        if (_isDraggingHue)
        {
            UpdateHueFromMouse(e.GetPosition(HueBar));
        }
    }

    private void HueBar_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_isDraggingHue)
        {
            _isDraggingHue = false;
            HueBar.ReleaseMouseCapture();
        }
    }

    private void UpdateHueFromMouse(Point pos)
    {
        double width = HueBar.ActualWidth;
        if (width <= 0) return;

        double ratio = Math.Clamp(pos.X / width, 0.0, 1.0);
        _hue = ratio * 360.0;

        UpdateUiFromHsv(triggerEvent: true);
    }

    private void HexInputBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CommitHexInput();
            Keyboard.ClearFocus();
        }
    }

    private void HexInputBox_LostFocus(object sender, RoutedEventArgs e)
    {
        CommitHexInput();
    }

    private void CommitHexInput()
    {
        string text = HexInputBox.Text.Trim();
        if (!text.StartsWith('#'))
        {
            text = "#" + text;
        }

        if (ColorUtils.TryParseHex(text, out _))
        {
            UpdateFromHex(text);
            ColorChanged?.Invoke(SelectedHexColor);
        }
        else
        {
            HexInputBox.Text = SelectedHexColor;
        }
    }

    private void QuickColor_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement elem && elem.Tag is string hex)
        {
            UpdateFromHex(hex);
            ColorChanged?.Invoke(SelectedHexColor);
        }
    }
}
