using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;

namespace DorkNet.AdminMobile.Design;

public static class AppDesign
{
    public static readonly Color Canvas = Color.FromArgb("#0b0f14");
    public static readonly Color Surface = Color.FromArgb("#1d222c");
    public static readonly Color SurfaceGlass = Color.FromArgb("#bb222833");
    public static readonly Color SurfaceLifted = Color.FromArgb("#293141");
    public static readonly Color Text = Color.FromArgb("#f6f8fb");
    public static readonly Color Muted = Color.FromArgb("#aab4c4");
    public static readonly Color Subtle = Color.FromArgb("#748094");
    public static readonly Color Accent = Color.FromArgb("#78c7ff");
    public static readonly Color AndroidAccent = Color.FromArgb("#9ee37d");
    public static readonly Color Danger = Color.FromArgb("#f26d7d");

    public static Brush PageBackground => new LinearGradientBrush
    {
        StartPoint = new Point(0, 0),
        EndPoint = new Point(1, 1),
        GradientStops =
        {
            new GradientStop(Color.FromArgb("#0b0f14"), 0),
            new GradientStop(Color.FromArgb("#121927"), 0.52f),
            new GradientStop(Color.FromArgb("#10151c"), 1),
        },
    };

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
        FontSize = 28,
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
        Stroke = Color.FromArgb("#33ffffff"),
        StrokeThickness = 1,
        Background = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 1),
            GradientStops =
            {
                new GradientStop(Color.FromArgb("#cc27303d"), 0),
                new GradientStop(Color.FromArgb("#99202733"), 1),
            },
        },
        StrokeShape = new RoundRectangle { CornerRadius = 24 },
        Shadow = new Shadow
        {
            Brush = Brush.Black,
            Offset = new Point(0, 12),
            Radius = 22,
            Opacity = 0.25f,
        },
        Content = content,
    };

    public static Button PrimaryButton(string text) => new()
    {
        Text = text,
        BackgroundColor = PlatformAccent,
        TextColor = Colors.Black,
        CornerRadius = 22,
        HeightRequest = 48,
        FontAttributes = FontAttributes.Bold,
        Padding = new Thickness(18, 0),
    };

    public static Button SecondaryButton(string text) => new()
    {
        Text = text,
        BackgroundColor = SurfaceLifted,
        TextColor = Text,
        CornerRadius = 22,
        HeightRequest = 46,
        Padding = new Thickness(16, 0),
    };

    public static Button DangerButton(string text) => new()
    {
        Text = text,
        BackgroundColor = Color.FromArgb("#4c1d28"),
        TextColor = Color.FromArgb("#ffd5dc"),
        CornerRadius = 22,
        HeightRequest = 44,
        Padding = new Thickness(14, 0),
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
        HeightRequest = 48,
    };

    public static Label StatusLabel() => new()
    {
        TextColor = Muted,
        FontSize = 13,
    };
}
