using Terminon.Services;

namespace Terminon.Infrastructure;

/// <summary>Lightweight service locator — avoids a full DI container dependency.</summary>
public static class ServiceLocator
{
    private static readonly Dictionary<Type, object> _services = new();

    public static void Register<T>(T instance) where T : class
        => _services[typeof(T)] = instance;

    public static T Get<T>() where T : class
    {
        if (_services.TryGetValue(typeof(T), out var svc))
            return (T)svc;
        throw new InvalidOperationException($"Service {typeof(T).Name} not registered.");
    }

    public static void Initialize()
    {
        var settings = new SettingsService();
        var profiles = new ProfileService();
        var knownHosts = new KnownHostsService();
        var ssh = new SshConnectionService(knownHosts, settings);
        var sftp = new SftpService();
        var keys = new KeyService();

        Register<SettingsService>(settings);
        Register<ProfileService>(profiles);
        Register<KnownHostsService>(knownHosts);
        Register<SshConnectionService>(ssh);
        Register<SftpService>(sftp);
        Register<KeyService>(keys);
    }
}
