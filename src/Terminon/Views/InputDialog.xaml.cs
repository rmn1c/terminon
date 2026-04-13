using System.Windows;

namespace Terminon.Views;

/// <summary>Simple single-line input prompt dialog.</summary>
public class InputDialog : Window
{
    public string InputText { get; private set; } = string.Empty;

    private System.Windows.Controls.TextBox _textBox = null!;

    public InputDialog(string prompt)
    {
        Title = "Input";
        Width = 360;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = (System.Windows.Media.Brush)Application.Current.Resources["WindowBackgroundBrush"];

        var panel = new System.Windows.Controls.StackPanel { Margin = new Thickness(20) };
        var label = new System.Windows.Controls.TextBlock
        {
            Text = prompt,
            Foreground = (System.Windows.Media.Brush)Application.Current.Resources["TextBrush"],
            FontSize = 13,
            Margin = new Thickness(0, 0, 0, 8)
        };
        _textBox = new System.Windows.Controls.TextBox
        {
            FontSize = 13,
            Padding = new Thickness(6, 4, 6, 4),
            Margin = new Thickness(0, 0, 0, 12)
        };
        if (Application.Current.Resources["DarkTextBox"] is System.Windows.Style s)
            _textBox.Style = s;

        var btnPanel = new System.Windows.Controls.Grid();
        btnPanel.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        btnPanel.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = GridLength.Auto });
        btnPanel.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = new GridLength(8) });
        btnPanel.ColumnDefinitions.Add(new System.Windows.Controls.ColumnDefinition { Width = GridLength.Auto });

        var okBtn = new System.Windows.Controls.Button { Content = "OK", IsDefault = true, Padding = new Thickness(16, 6, 16, 6) };
        var cancelBtn = new System.Windows.Controls.Button { Content = "Cancel", IsCancel = true, Padding = new Thickness(16, 6, 16, 6), Margin = new Thickness(0, 0, 0, 0) };
        if (Application.Current.Resources["PrimaryButton"] is System.Windows.Style ps) okBtn.Style = ps;
        if (Application.Current.Resources["SecondaryButton"] is System.Windows.Style ss) cancelBtn.Style = ss;

        System.Windows.Controls.Grid.SetColumn(cancelBtn, 1);
        System.Windows.Controls.Grid.SetColumn(okBtn, 3);
        btnPanel.Children.Add(cancelBtn);
        btnPanel.Children.Add(okBtn);

        okBtn.Click += (_, _) => { InputText = _textBox.Text; DialogResult = true; };
        cancelBtn.Click += (_, _) => { DialogResult = false; };

        panel.Children.Add(label);
        panel.Children.Add(_textBox);
        panel.Children.Add(btnPanel);
        Content = panel;

        Loaded += (_, _) => _textBox.Focus();
    }
}
