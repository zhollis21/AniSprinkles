using System.Diagnostics;

namespace AniSprinkles.UITests;

/// <summary>
/// The journeys' handle on the running app: a DevFlow <see cref="AgentClient"/> plus the waiting
/// every assertion needs, because the UI settles on its own schedule after a tap or a tab switch.
/// </summary>
internal sealed class AppDriver : IDisposable
{
    /// <summary>How long a wait gives the UI to reach the expected state before it fails.</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);

    /// <summary>How often a wait re-reads the UI.</summary>
    public static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);

    private const int DefaultAgentPort = 9223;

    /// <summary>
    /// One mutation lease for the whole run. The agent lets a single holder tap and fill at a time
    /// and keeps the lease for about 10 s after its last use; each <see cref="AgentClient"/> mints
    /// its own id, so a second client — the next fixture's — is refused as "another session" until
    /// the first one's lease expires. Sharing the id makes every client in this process one holder.
    /// </summary>
    private static readonly string RunLeaseId = Guid.NewGuid().ToString("N");

    private AppDriver(AgentClient client) => Client = client;

    public AgentClient Client { get; }

    public AndroidDevice Device { get; } = AndroidDevice.FromParameters();

    /// <summary>
    /// Connects to the agent on <c>--test-parameter AgentPort=&lt;port&gt;</c> (default 9223,
    /// DevFlow's own default, which is what CI forwards to). Locally, pass the port
    /// <c>driver.ps1 launch</c> printed.
    /// </summary>
    public static AppDriver Connect()
    {
        var port = TestContext.Parameters.Get("AgentPort", DefaultAgentPort);
        var host = TestContext.Parameters.Get("AgentHost", "localhost");
        return new AppDriver(new AgentClient(host, port)
        {
            MutationLeaseId = RunLeaseId,
            MutationLeaseLabel = "AniSprinkles.UITests",
        });
    }

    /// <summary>
    /// Waits until a query returns a visible element that satisfies <paramref name="predicate"/>,
    /// and returns it. Fails the test with <paramref name="what"/> and the last value seen.
    /// </summary>
    public async Task<ElementInfo> WaitForAsync(
        string what,
        string? automationId = null,
        string? text = null,
        string? type = null,
        Func<ElementInfo, bool>? predicate = null,
        TimeSpan? timeout = null)
    {
        var deadline = Stopwatch.StartNew();
        var limit = timeout ?? DefaultTimeout;
        string lastSeen = "nothing matched the query";
        while (true)
        {
            var matches = await Client.QueryAsync(type, automationId, text);
            var visible = matches.Where(e => e.IsVisible).ToList();
            var hit = visible.FirstOrDefault(e => predicate?.Invoke(e) ?? true);
            if (hit is not null)
            {
                return hit;
            }

            if (visible.Count > 0)
            {
                lastSeen = string.Join(", ", visible.Select(e => $"'{e.Text}'"));
            }

            if (deadline.Elapsed >= limit)
            {
                Assert.Fail($"Timed out after {limit.TotalSeconds:0} s waiting for {what}. Last seen: {lastSeen}.");
            }

            await Task.Delay(PollInterval);
        }
    }

    /// <summary>Waits for an element and taps it.</summary>
    public async Task TapAsync(string what, string? automationId = null, string? text = null, string? type = null)
    {
        var element = await WaitForAsync(what, automationId, text, type);
        Assert.That(await Client.TapAsync(element.Id), Is.True, $"The agent refused to tap {what}.");
    }

    /// <summary>
    /// A real touch at the centre of <paramref name="element"/>, injected by the device's input
    /// system (<c>adb shell input tap</c>). Unlike <see cref="AgentClient.TapAsync(string)"/>, which
    /// invokes the element's tap action directly, this reaches the app the way a finger does.
    /// <para>
    /// The window's bounds, not the element's own: those are relative to its parent. Window
    /// coordinates times the density are screen pixels because the app draws edge to edge, its window
    /// starting at the screen's top-left (checked against uiautomator's bounds for the tab bar).
    /// </para>
    /// </summary>
    public async Task TouchAsync(ElementInfo element, string what)
    {
        if (element.WindowBounds is not { } bounds)
        {
            Assert.Fail($"{what} has no window bounds to touch.");
            return;
        }

        var density = await Device.GetDensityAsync();
        var x = (int)Math.Round((bounds.X + (bounds.Width / 2)) * density);
        var y = (int)Math.Round((bounds.Y + (bounds.Height / 2)) * density);
        await Device.RunAsync($"shell input tap {x} {y}");
    }

    public void Dispose() => Client.Dispose();
}
