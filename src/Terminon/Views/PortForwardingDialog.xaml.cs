using System.Windows;
using Terminon.Models;
using Terminon.ViewModels;

namespace Terminon.Views;

public partial class PortForwardingDialog : Window
{
    private readonly PortForwardingViewModel _vm;

    public List<PortForwardRule> ResultRules { get; private set; } = new();

    public PortForwardingDialog(IEnumerable<PortForwardRule> existingRules)
    {
        InitializeComponent();
        _vm = new PortForwardingViewModel(existingRules);
        _vm.RequestClose += (_, _) =>
        {
            if (_vm.DialogResult == true)
                ResultRules = _vm.GetRules();
            DialogResult = _vm.DialogResult;
        };
        DataContext = _vm;
    }
}
