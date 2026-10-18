using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Media;

namespace PdfReader.App.Rendering;

// Opt-in UI-thread cadence probe. These callbacks are not display-present timestamps.
internal sealed class ReadingPerformanceCapture : IAsyncDisposable
{
    private readonly record struct Sample(
        long Ticks,
        double Offset,
        double Extent,
        double Viewport,
        double Zoom,
        int Page,
        bool Ready,
        long CachedBytes);

    private readonly Func<(double Offset, double Extent, double Viewport, double Zoom, int Page, bool Ready, long CachedBytes)> _snapshot;
    private readonly Channel<Sample>? _samples;
    private readonly Task _writer = Task.CompletedTask;
    private long _dropped;
    private bool _disposed;

    public ReadingPerformanceCapture(
        Func<(double Offset, double Extent, double Viewport, double Zoom, int Page, bool Ready, long CachedBytes)> snapshot)
    {
        _snapshot = snapshot;
        string? output = Environment.GetEnvironmentVariable("PDFREADER_PERF_OUTPUT");
        if (string.IsNullOrWhiteSpace(output))
        {
            return;
        }

        _samples = Channel.CreateBounded<Sample>(new BoundedChannelOptions(4096)
        {
            SingleReader = true,
            SingleWriter = true,
            FullMode = BoundedChannelFullMode.Wait,
        });
        _writer = Task.Run(() => WriteSamplesAsync(output, _samples.Reader));
        CompositionTarget.Rendering += OnRendering;
    }

    private void OnRendering(object? sender, object args)
    {
        long ticks = Stopwatch.GetTimestamp();
        var state = _snapshot();
        if (!_samples!.Writer.TryWrite(new Sample(
            ticks, state.Offset, state.Extent, state.Viewport, state.Zoom, state.Page, state.Ready, state.CachedBytes)))
        {
            _dropped++;
        }
    }

    private async Task WriteSamplesAsync(string output, ChannelReader<Sample> reader)
    {
        try
        {
            Directory.CreateDirectory(output);
            using var stream = new StreamWriter(Path.Combine(output, "ui-frames.csv"));
            await stream.WriteLineAsync($"# StopwatchFrequency={Stopwatch.Frequency}").ConfigureAwait(false);
            await stream.WriteLineAsync("Ticks,Offset,Extent,Viewport,Zoom,Page,Ready,CachedBytes").ConfigureAwait(false);
            int count = 0;
            await foreach (Sample sample in reader.ReadAllAsync().ConfigureAwait(false))
            {
                await stream.WriteLineAsync(string.Create(CultureInfo.InvariantCulture,
                    $"{sample.Ticks},{sample.Offset:R},{sample.Extent:R},{sample.Viewport:R},{sample.Zoom:R},{sample.Page},{(sample.Ready ? 1 : 0)},{sample.CachedBytes}"))
                    .ConfigureAwait(false);
                if (++count % 30 == 0)
                {
                    await stream.FlushAsync().ConfigureAwait(false);
                }
            }

            await stream.WriteLineAsync($"# DroppedSamples={_dropped}").ConfigureAwait(false);
            await stream.WriteLineAsync($"# InitialWin2DCacheBytes={PageThemeProcessor.InitialDeviceCacheBytes}").ConfigureAwait(false);
            await stream.WriteLineAsync($"# Win2DCacheBytes={PageThemeProcessor.DeviceCacheBytes}").ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            Debug.WriteLine($"Performance capture could not be saved: {ex.Message}");
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed || _samples is null)
        {
            return;
        }

        _disposed = true;
        CompositionTarget.Rendering -= OnRendering;
        _samples.Writer.TryComplete();
        await _writer.ConfigureAwait(false);
    }
}
