using System.Collections.ObjectModel;
using Terminon.Infrastructure;
using Terminon.Models;

namespace Terminon.ViewModels;

public class PortForwardRuleViewModel : ViewModelBase
{
    public PortForwardRule Rule { get; }

    private bool _isActive;
    public bool IsActive { get => _isActive; set => SetProperty(ref _isActive, value); }

    public PortForwardRuleViewModel(PortForwardRule rule) => Rule = rule;

    public string DisplayText => Rule.Direction == ForwardDirection.Local
        ? $"L  {Rule.LocalHost}:{Rule.LocalPort}  →  {Rule.RemoteHost}:{Rule.RemotePort}"
        : $"R  {Rule.RemoteHost}:{Rule.RemotePort}  →  {Rule.LocalHost}:{Rule.LocalPort}";
}

public class PortForwardingViewModel : ViewModelBase
{
    private string _localHost = "127.0.0.1";
    private int _localPort;
    private string _remoteHost = string.Empty;
    private int _remotePort;
    private ForwardDirection _direction = ForwardDirection.Local;
    private string? _description;
    private PortForwardRuleViewModel? _selected;

    public string LocalHost { get => _localHost; set => SetProperty(ref _localHost, value); }
    public int LocalPort { get => _localPort; set => SetProperty(ref _localPort, value); }
    public string RemoteHost { get => _remoteHost; set => SetProperty(ref _remoteHost, value); }
    public int RemotePort { get => _remotePort; set => SetProperty(ref _remotePort, value); }
    public ForwardDirection Direction { get => _direction; set => SetProperty(ref _direction, value); }
    public string? Description { get => _description; set => SetProperty(ref _description, value); }

    public PortForwardRuleViewModel? Selected { get => _selected; set { if (SetProperty(ref _selected, value)) RemoveCommand.RaiseCanExecuteChanged(); } }

    public ObservableCollection<PortForwardRuleViewModel> Rules { get; } = new();
    public List<ForwardDirection> Directions { get; } = Enum.GetValues<ForwardDirection>().ToList();

    public RelayCommand AddCommand { get; }
    public RelayCommand RemoveCommand { get; }
    public RelayCommand OkCommand { get; }
    public RelayCommand CancelCommand { get; }

    public bool? DialogResult { get; private set; }
    public event EventHandler? RequestClose;

    public PortForwardingViewModel(IEnumerable<PortForwardRule> existing)
    {
        foreach (var r in existing)
            Rules.Add(new PortForwardRuleViewModel(r));

        AddCommand = new RelayCommand(AddRule, () => !string.IsNullOrWhiteSpace(RemoteHost) && RemotePort > 0 && LocalPort > 0);
        RemoveCommand = new RelayCommand(RemoveSelected, () => Selected is not null);
        OkCommand = new RelayCommand(() => { DialogResult = true; RequestClose?.Invoke(this, EventArgs.Empty); });
        CancelCommand = new RelayCommand(() => { DialogResult = false; RequestClose?.Invoke(this, EventArgs.Empty); });
    }

    private void AddRule()
    {
        var rule = new PortForwardRule
        {
            Direction = Direction,
            LocalHost = LocalHost,
            LocalPort = LocalPort,
            RemoteHost = RemoteHost,
            RemotePort = RemotePort,
            Description = Description
        };
        Rules.Add(new PortForwardRuleViewModel(rule));
        LocalPort = 0;
        RemoteHost = string.Empty;
        RemotePort = 0;
        Description = null;
    }

    private void RemoveSelected()
    {
        if (Selected is not null) Rules.Remove(Selected);
    }

    public List<PortForwardRule> GetRules() => Rules.Select(r => r.Rule).ToList();
}
