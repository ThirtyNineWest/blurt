using System.Windows;
using System.Windows.Controls;
using Blurt.Engine.Update;

namespace Blurt.App.Update;

/// <summary>A modal alert presenting an <see cref="UpdateAlertContent"/> with its buttons in order, the first the default.</summary>
public sealed class UpdateAlertWindow : Window
{
    private UpdateAlertWindow(UpdateAlertContent content)
    {
        Title = content.Title;
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        MaxWidth = 460;

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        for (var index = 0; index < content.Buttons.Count; index++)
        {
            var chosen = index;
            var button = new Button
            {
                Content = content.Buttons[index],
                MinWidth = 80,
                Margin = new Thickness(8, 0, 0, 0),
                Padding = new Thickness(12, 4, 12, 4),
                IsDefault = index == 0,
                IsCancel = index == content.Buttons.Count - 1,
            };
            button.Click += (_, _) =>
            {
                ChosenIndex = chosen;
                DialogResult = true;
            };
            buttons.Children.Add(button);
        }

        var body = new StackPanel { Margin = new Thickness(20) };
        body.Children.Add(new TextBlock
        {
            Text = content.Title,
            FontWeight = FontWeights.SemiBold,
            FontSize = 14,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8),
        });
        body.Children.Add(new TextBlock { Text = content.Message, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 16) });
        body.Children.Add(buttons);
        Content = body;
    }

    /// <summary>The index of the button the user chose, or -1 when the window closed another way.</summary>
    public int ChosenIndex { get; private set; } = -1;

    /// <summary>Shows <paramref name="content"/> modally over <paramref name="owner"/> and returns the chosen button index, or -1.</summary>
    public static int Present(UpdateAlertContent content, Window? owner)
    {
        var window = new UpdateAlertWindow(content);
        if (owner is { IsVisible: true })
        {
            window.Owner = owner;
        }
        else
        {
            window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        window.ShowDialog();
        return window.ChosenIndex;
    }
}
