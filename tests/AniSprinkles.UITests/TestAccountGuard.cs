namespace AniSprinkles.UITests;

/// <summary>
/// Refuses to run any journey unless the app is signed in as the test account. The journeys change
/// settings, which the app saves to AniList; against a real account that is a real change, so this
/// fails the whole run before the first tap rather than trusting whoever started it.
/// </summary>
[SetUpFixture]
public sealed class TestAccountGuard
{
    [OneTimeSetUp]
    public async Task RequireTestAccountAsync()
    {
        using var driver = AppDriver.Connect();
        await WaitForAgentAsync(driver);

        var name = await new App(driver).ReadProfileNameAsync();
        Assert.That(name, Is.EqualTo(App.TestAccountName),
            "UI journeys change settings and the app saves them to the signed-in account, so they only run as the test account. "
            + "Use a -p:CiBuild=true build, which is signed in to it with fixture replay.");
    }

    /// <summary>
    /// Hands the run's mutation lease back. Otherwise the agent keeps it for about 10 s after the
    /// last tap, and a second run started inside that window — or a <c>driver.ps1 devflow</c> call —
    /// is refused as "another DevFlow session is driving this app".
    /// </summary>
    [OneTimeTearDown]
    public async Task ReleaseLeaseAsync()
    {
        using var driver = AppDriver.Connect();
        var status = await driver.Client.ControlMutationLeaseAsync("release", leaseId: driver.Client.MutationLeaseId);
        if (!status.Ok)
        {
            TestContext.Progress.WriteLine($"Could not release the DevFlow mutation lease: {status.Error}");
        }
    }

    /// <summary>
    /// The agent starts with the app, so a run that begins straight after a launch (CI's does) can
    /// reach the forward before anything listens behind it. Same patience as any other wait.
    /// </summary>
    private static async Task WaitForAgentAsync(AppDriver driver)
    {
        var deadline = DateTime.UtcNow + AppDriver.DefaultTimeout;
        Exception? last = null;
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                if (await driver.Client.GetStatusAsync() is not null)
                {
                    return;
                }
            }
            catch (HttpRequestException ex)
            {
                last = ex;
            }

            await Task.Delay(AppDriver.PollInterval);
        }

        Assert.Fail("No DevFlow agent answered. Launch a Debug build (driver.ps1 up) and pass its port with "
            + $"--test-parameter AgentPort=<port>. Last error: {last?.Message ?? "none"}");
    }
}
