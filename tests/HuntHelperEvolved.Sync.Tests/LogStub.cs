namespace Dalamud.Plugin.Services;
// Only the transport's diagnostic sink is replaced; no game assemblies are loaded.
public interface IPluginLog { void Debug(System.Exception ex, string message); }
public sealed class TestLog : IPluginLog { public void Debug(System.Exception ex, string message) { } }
