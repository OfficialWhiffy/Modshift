using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Navigation;
using TruckModsListUpdater.App.Services;
using TruckModsListUpdater.Core.Mods;

namespace TruckModsListUpdater.App.Views;

/// <summary>Attached property that fills a TextBlock with the styled spans of one description line.</summary>
public static class DescriptionText
{
    public static readonly DependencyProperty SpansProperty = DependencyProperty.RegisterAttached(
        "Spans", typeof(IReadOnlyList<DescriptionSpan>), typeof(DescriptionText), new PropertyMetadata(null, OnSpansChanged));

    public static IReadOnlyList<DescriptionSpan>? GetSpans(DependencyObject d) => (IReadOnlyList<DescriptionSpan>?)d.GetValue(SpansProperty);
    public static void SetSpans(DependencyObject d, IReadOnlyList<DescriptionSpan>? value) => d.SetValue(SpansProperty, value);

    static void OnSpansChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not TextBlock block) return;
        block.Inlines.Clear();
        var spans = e.NewValue as IReadOnlyList<DescriptionSpan>;
        if (spans == null || spans.Count == 0)
        {
            // A blank line keeps a little height so paragraphs stay separated.
            block.Inlines.Add(new Run(" ") { FontSize = 6 });
            return;
        }
        foreach (var span in spans) block.Inlines.Add(Build(span));
    }

    static Inline Build(DescriptionSpan span)
    {
        Inline inline = span.Link is { } url ? Link(span, url) : new Run(span.Text);
        if (span.Bold) inline.FontWeight = FontWeights.SemiBold;
        if (span.Italic) inline.FontStyle = FontStyles.Italic;
        if (span.Underline && span.Link == null) inline.TextDecorations = TextDecorations.Underline;
        if (span.Link == null && ColorBrush(span) is { } brush) inline.Foreground = brush;
        return inline;
    }

    static Hyperlink Link(DescriptionSpan span, string url)
    {
        var link = new Hyperlink(new Run(span.Text)) { ToolTip = url };
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri)) link.NavigateUri = uri;
        link.RequestNavigate += OnNavigate;
        return link;
    }

    static void OnNavigate(object sender, RequestNavigateEventArgs e)
    {
        ExternalLinks.TryOpen(e.Uri.AbsoluteUri);
        e.Handled = true;
    }

    /// <summary>The colour brushes live in App.xaml (DescOrangeBrush, …); Normal keeps the inherited text colour.</summary>
    static Brush? ColorBrush(DescriptionSpan span)
    {
        if (span.Hex != null)
        {
            try { return (Brush?)new BrushConverter().ConvertFromString(span.Hex); }
            catch (FormatException) { return null; }
        }
        return span.Color == DescriptionColor.Normal ? null : Application.Current?.TryFindResource($"Desc{span.Color}Brush") as Brush;
    }
}

/// <summary>Indent level of a description list item to a left margin.</summary>
public sealed class IndentMarginConverter : System.Windows.Data.IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
        new Thickness((value is int level ? Math.Min(level, 6) : 0) * 16, 0, 0, 0);

    public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
        throw new NotSupportedException();
}
