using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;

namespace DorkNet.AdminMobile.Design;

public static class AppDesign
{
    public static readonly Color Canvas = Color.FromArgb("#0f1115");
    public static readonly Color Surface = Color.FromArgb("#171a20");
    public static readonly Color SurfaceLifted = Color.FromArgb("#20252d");
    public static readonly Color RowSurface = Color.FromArgb("#14171d");
    public static readonly Color Stroke = Color.FromArgb("#2a3039");
    public static readonly Color Text = Color.FromArgb("#f4f6fa");
    public static readonly Color Muted = Color.FromArgb("#aeb7c6");
    public static readonly Color Subtle = Color.FromArgb("#778294");
    public static readonly Color Accent = Color.FromArgb("#64b5ff");
    public static readonly Color AndroidAccent = Color.FromArgb("#b7dd72");
    public static readonly Color Danger = Color.FromArgb("#ff6f82");

    public static Brush PageBackground => new SolidColorBrush(Canvas);

    public static Color PlatformAccent =>
#if ANDROID
        AndroidAccent;
#else
        Accent;
#endif

    public static Label Title(string text) => new()
    {
        Text = text,
        TextColor = Text,
        FontSize = 22,
        FontAttributes = FontAttributes.Bold,
        LineBreakMode = LineBreakMode.TailTruncation,
    };

    public static Label Section(string text) => new()
    {
        Text = text.ToUpperInvariant(),
        TextColor = Subtle,
        FontSize = 12,
        FontAttributes = FontAttributes.Bold,
        Margin = new Thickness(0, 12, 0, 0),
    };

    public static Border GlassCard(View content, Thickness? padding = null) => new()
    {
        Padding = padding ?? new Thickness(14),
        Stroke = Stroke,
        StrokeThickness = 1,
        BackgroundColor = Surface,
        StrokeShape = new RoundRectangle { CornerRadius = 10 },
        Content = content,
    };

    public static Button PrimaryButton(string text) => new()
    {
        Text = text,
        BackgroundColor = PlatformAccent,
        TextColor = Colors.Black,
        CornerRadius = 10,
        HeightRequest = 44,
        FontAttributes = FontAttributes.Bold,
        Padding = new Thickness(14, 0),
    };

    public static Button SecondaryButton(string text) => new()
    {
        Text = text,
        BackgroundColor = SurfaceLifted,
        TextColor = Text,
        CornerRadius = 10,
        HeightRequest = 42,
        Padding = new Thickness(14, 0),
    };

    public static Button DangerButton(string text) => new()
    {
        Text = text,
        BackgroundColor = Color.FromArgb("#4c1d28"),
        TextColor = Color.FromArgb("#ffd5dc"),
        CornerRadius = 10,
        HeightRequest = 42,
        Padding = new Thickness(12, 0),
    };

    public static Entry Entry(string placeholder, Keyboard? keyboard = null, bool secret = false) => new()
    {
        Placeholder = placeholder,
        IsPassword = secret,
        TextColor = Text,
        PlaceholderColor = Subtle,
        BackgroundColor = Color.FromArgb("#19202b"),
        ClearButtonVisibility = ClearButtonVisibility.WhileEditing,
        Keyboard = keyboard ?? Keyboard.Text,
        HeightRequest = 44,
    };

    public static Label Caption(string text) => new()
    {
        Text = text,
        TextColor = Muted,
        FontSize = 13,
        LineBreakMode = LineBreakMode.WordWrap,
    };

    public static BoxView Divider() => new()
    {
        HeightRequest = 1,
        Color = Stroke,
        Opacity = 0.7,
    };

    public static Label StatusLabel() => new()
    {
        TextColor = Muted,
        FontSize = 13,
    };
}
