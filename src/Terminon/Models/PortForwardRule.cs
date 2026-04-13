namespace Terminon.Models;

public enum ForwardDirection { Local, Remote }

public class PortForwardRule
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public ForwardDirection Direction { get; set; } = ForwardDirection.Local;
    public string LocalHost { get; set; } = "127.0.0.1";
    public int LocalPort { get; set; }
    public string RemoteHost { get; set; } = string.Empty;
    public int RemotePort { get; set; }
    public bool AutoStart { get; set; } = true;
    public string? Description { get; set; }
}
