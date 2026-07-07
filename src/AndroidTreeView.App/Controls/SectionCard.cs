using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Layout;
using Avalonia.Media;

namespace AndroidTreeView.App.Controls;

/// <summary>
/// A "liquid glass" card with an optional <see cref="Header"/> and injected content. The template is
/// built in code so the control renders without depending on an app-level control theme; the glass
/// brushes come from Styles/Glass.axaml via dynamic resources (degrading to transparent if absent).
/// </summary>
public class SectionCard : ContentControl
{
    public static readonly StyledProperty<string?> HeaderProperty =
        AvaloniaProperty.Register<SectionCard, string?>(nameof(Header));

    public SectionCard()
    {
        Template = BuildTemplate();
        Padding = new Thickness(16);
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;
    }

    /// <summary>Optional card header; the header row is hidden when null or whitespace.</summary>
    public string? Header
    {
        get => GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    private static FuncControlTemplate BuildTemplate() => new((templatedParent, scope) =>
    {
        var card = (SectionCard)templatedParent;

        var header = new TextBlock
        {
            FontSize = 15,
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(0, 0, 0, 10),
            TextWrapping = TextWrapping.Wrap
        };
        header.Bind(TextBlock.TextProperty, card.GetObservable(HeaderProperty));
        header.Bind(
            Visual.IsVisibleProperty,
            new Binding
            {
                Source = card,
                Path = nameof(Header),
                Converter = new FuncValueConverter<string?, bool>(static h => !string.IsNullOrWhiteSpace(h))
            });

        var presenter = new ContentPresenter
        {
            Name = "PART_ContentPresenter"
        };
        presenter.Bind(ContentPresenter.ContentProperty, card.GetObservable(ContentProperty));
        presenter.Bind(ContentPresenter.ContentTemplateProperty, card.GetObservable(ContentTemplateProperty));
        presenter.Bind(ContentPresenter.HorizontalContentAlignmentProperty, card.GetObservable(HorizontalContentAlignmentProperty));
        presenter.Bind(ContentPresenter.VerticalContentAlignmentProperty, card.GetObservable(VerticalContentAlignmentProperty));
        presenter.RegisterInNameScope(scope);

        var layout = new StackPanel();
        layout.Children.Add(header);
        layout.Children.Add(presenter);

        var border = new Border
        {
            Child = layout,
            CornerRadius = new CornerRadius(12),
            BorderThickness = new Thickness(1)
        };
        border.Bind(Border.BackgroundProperty, card.GetResourceObservable("Glass.Card.Background"));
        border.Bind(Border.BorderBrushProperty, card.GetResourceObservable("Glass.Card.Border"));
        border.Bind(Decorator.PaddingProperty, card.GetObservable(PaddingProperty));

        return border;
    });
}
