using System.Diagnostics;
using Flamoris.Logging;

namespace Flamoris.StudioClient.Infrastructure;

public sealed class StudioDiagnostics(string root)
{
    private readonly FlamorisLogger logger = FlamorisLogger.Create(new LoggingOptions
    {
        Level = "error", Outputs = [new() { Type = "file", Path = "logs/studioclient.log",
            Rotation = new() { Enabled = true, MaxFileSizeMb = 2, MaxFiles = 3 } }]
    }, root);
    public void Write(Exception exception, string operation)
    {
        // Raw messages/Exception.ToString can contain lyrics, filenames and paths.
        // Retain exception types, codes and method frames only; no local variable data.
        try
        {
            var chain = new List<object>();
            for (var current = exception; current is not null && chain.Count < 5; current = current.InnerException)
                chain.Add(new { type = current.GetType().FullName, code = current.HResult,
                    frames = new StackTrace(current, false).GetFrames().Take(24)
                        .Select(f => f.GetMethod()).Select(m => m?.DeclaringType?.FullName + "." + m?.Name).ToArray() });
            logger.Error("app.failure", "StudioClient operation failed", properties: new Dictionary<string, object?>
            { ["operation"] = operation, ["exceptions"] = chain, ["version"] = typeof(StudioDiagnostics).Assembly.GetName().Version?.ToString() });
        }
        catch (Exception) { /* Diagnostics must not replace the original failure. */ }
    }
}
