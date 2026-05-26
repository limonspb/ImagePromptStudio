using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ImagePromptStudio;

public sealed class OpenAiApiKeyDialog : Window
{
    private readonly PasswordBox _keyBox = new();

    public string ApiKey => _keyBox.Password.Trim();

    public OpenAiApiKeyDialog(Window owner)
    {
        Title = "OpenAI API Key";
        Owner = owner;
        Width = 520;
        Height = 245;
        MinWidth = 460;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(16, 18, 22));

        var panel = new StackPanel
        {
            Margin = new Thickness(18),
        };

        panel.Children.Add(new TextBlock
        {
            Text = "OPENAI_API_KEY is missing. Paste your key to save it to your Windows user environment and start the app.",
            Foreground = new SolidColorBrush(Color.FromRgb(232, 237, 244)),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 14),
        });

        panel.Children.Add(new TextBlock
        {
            Text = "API key",
            Foreground = new SolidColorBrush(Color.FromRgb(184, 193, 204)),
            Margin = new Thickness(0, 0, 0, 5),
        });

        _keyBox.MinHeight = 32;
        _keyBox.Padding = new Thickness(8, 5, 8, 5);
        _keyBox.Background = new SolidColorBrush(Color.FromRgb(22, 26, 32));
        _keyBox.Foreground = Brushes.White;
        _keyBox.BorderBrush = new SolidColorBrush(Color.FromRgb(48, 56, 68));
        _keyBox.BorderThickness = new Thickness(1);
        panel.Children.Add(_keyBox);

        var note = new TextBlock
        {
            Text = "The key is not written to project files, history, or logs.",
            Foreground = new SolidColorBrush(Color.FromRgb(143, 155, 170)),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 10, 0, 0),
        };
        panel.Children.Add(note);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 18, 0, 0),
        };
        var cancel = new Button
        {
            Content = "Cancel",
            Width = 88,
            MinHeight = 30,
            Margin = new Thickness(0, 0, 8, 0),
            IsCancel = true,
        };
        var save = new Button
        {
            Content = "Save",
            Width = 88,
            MinHeight = 30,
            IsDefault = true,
        };
        cancel.Click += (_, _) => DialogResult = false;
        save.Click += (_, _) => Save();
        buttons.Children.Add(cancel);
        buttons.Children.Add(save);
        panel.Children.Add(buttons);

        Content = panel;
        Loaded += (_, _) => _keyBox.Focus();
    }

    private void Save()
    {
        if (string.IsNullOrWhiteSpace(ApiKey))
        {
            MessageBox.Show(this, "Paste an OpenAI API key first.", "Missing API key", MessageBoxButton.OK, MessageBoxImage.Warning);
            _keyBox.Focus();
            return;
        }

        DialogResult = true;
    }
}
