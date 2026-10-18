using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.Text.Json;
using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Diagnostics.Tracing;

int target = int.Parse(args[0]);
string output = Path.GetFullPath(args[1]);
using var process = Process.GetProcessById(target);
using var samples = new StreamWriter(Path.Combine(output, "process.jsonl"));
using var runtime = new StreamWriter(Path.Combine(output, "runtime.jsonl"));
using var events = new StreamWriter(Path.Combine(output, "gc-events.jsonl"));
EventPipeSession? session = null;
Task reader = Task.CompletedTask;
try
{
    session = new DiagnosticsClient(target).StartEventPipeSession(new[] {
        new EventPipeProvider("System.Runtime", EventLevel.Informational, 0,
            new Dictionary<string,string>{{"EventCounterIntervalSec","1"}}),
        new EventPipeProvider("Microsoft-Windows-DotNETRuntime", EventLevel.Verbose, 1)
    }, false, 32);
    var source = new EventPipeEventSource(session.EventStream);
    source.Dynamic.All += data =>
    {
        if (data.ProviderName == "System.Runtime")
        {
            var payload = data.PayloadValue(0);
            runtime.WriteLine(JsonSerializer.Serialize(new { Utc = data.TimeStamp.ToUniversalTime(), Payload = payload }));
            runtime.Flush();
        }
    };
    source.Clr.All += data =>
    {
        if (data.EventName.Contains("AllocationTick") || data.EventName is "GC/Start" or "GC/Stop" || data.EventName.Contains("HeapStats"))
        {
            var payload = new Dictionary<string, object?>();
            foreach (var name in data.PayloadNames) payload[name] = data.PayloadByName(name);
            events.WriteLine(JsonSerializer.Serialize(new { Utc = data.TimeStamp.ToUniversalTime(), data.EventName, Payload = payload }));
            events.Flush();
        }
    };
    reader = Task.Run(() => { try { source.Process(); } catch (Exception ex) { File.WriteAllText(Path.Combine(output, "eventpipe-read-error.txt"), ex.ToString()); } });
    File.WriteAllText(Path.Combine(output, "attached.txt"), DateTime.UtcNow.ToString("O"));
}
catch (Exception ex) { File.WriteAllText(Path.Combine(output, "eventpipe-attach-error.txt"), ex.ToString()); }
DateTime previous = DateTime.UtcNow;
double previousCpu = process.TotalProcessorTime.TotalSeconds;
while (!File.Exists(Path.Combine(output, "stop")))
{
    process.Refresh();
    if (process.HasExited) break;
    DateTime now = DateTime.UtcNow;
    double cpu = process.TotalProcessorTime.TotalSeconds;
    double elapsed = (now - previous).TotalSeconds;
    samples.WriteLine(JsonSerializer.Serialize(new
    {
        Utc = now,
        PrivateBytes = process.PrivateMemorySize64,
        WorkingSet = process.WorkingSet64,
        PeakWorkingSet = process.PeakWorkingSet64,
        CpuSeconds = cpu,
        CpuOneCorePercent = elapsed > 0 ? (cpu - previousCpu) / elapsed * 100 : 0,
        Threads = process.Threads.Count,
        Handles = process.HandleCount,
        process.Responding
    }));
    samples.Flush();
    previous = now; previousCpu = cpu;
    await Task.Delay(250);
}
if (session is not null) { session.Stop(); await reader.WaitAsync(TimeSpan.FromSeconds(15)); session.Dispose(); }
