using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.SqlServer.XEvent.XELite;
using ExploreDB.Services.Profiler.Models;

namespace ExploreDB.Services.Profiler.Engine;

/// <summary>
/// Background streaming service that pulls real-time Extended Events from SQL Server
/// and pushes parsed TraceEvents into a high-performance Channel.
/// </summary>
public sealed class ProfilerStreamerService
{
    public async Task StreamEventsAsync(
        string connectionString,
        string sessionName,
        ChannelWriter<TraceEvent> writer,
        CancellationToken ct)
    {
        var streamer = new XELiveEventStreamer(connectionString, sessionName);

        try
        {
            await streamer.ReadEventStream(
                xEvent =>
                {
                    if (ct.IsCancellationRequested)
                    {
                        return Task.CompletedTask;
                    }

                    try
                    {
                        var parsed = XEventParser.Parse(xEvent);
                        writer.TryWrite(parsed);
                    }
                    catch
                    {
                        // Ignore individual event parsing issues to keep stream healthy
                    }

                    return Task.CompletedTask;
                },
                ct);
        }
        catch (OperationCanceledException)
        {
            // Expected on trace stop / cancellation
        }
    }
}
