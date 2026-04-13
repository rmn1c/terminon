namespace Terminon.Models;

public class ConnectionFolder
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    public string ParentId { get; set; } = string.Empty;
    public bool IsExpanded { get; set; } = true;
}
