using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Terminon.ViewModels;

namespace Terminon.Controls;

public partial class SearchBar : UserControl
{
    public event EventHandler? CloseRequested;
    public event EventHandler? NextRequested;
    public event EventHandler? PrevRequested;

    public SearchBar()
    {
        InitializeComponent();
    }

    public void FocusSearchBox()
    {
        SearchTextBox.Focus();
        SearchTextBox.SelectAll();
    }

    private void SearchTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) CloseRequested?.Invoke(this, EventArgs.Empty);
        else if (e.Key == Key.Enter)
        {
            if ((e.KeyboardDevice.Modifiers & ModifierKeys.Shift) != 0)
                PrevRequested?.Invoke(this, EventArgs.Empty);
            else
                NextRequested?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        }
    }

    private void NextHit_Click(object sender, RoutedEventArgs e) => NextRequested?.Invoke(this, EventArgs.Empty);
    private void PrevHit_Click(object sender, RoutedEventArgs e) => PrevRequested?.Invoke(this, EventArgs.Empty);
    private void Close_Click(object sender, RoutedEventArgs e) => CloseRequested?.Invoke(this, EventArgs.Empty);

    public void UpdateHitCount(int current, int total)
    {
        HitCount.Text = total == 0 ? "No results" : $"{current + 1}/{total}";
    }
}
