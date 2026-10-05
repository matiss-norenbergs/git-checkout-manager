using GitCheckoutManager.Services;

namespace GitCheckoutManager.Tests;

public class UpdateCheckerTests
{
    private sealed class FakeUpdateService : IUpdateService
    {
        public bool IsInstalled { get; set; } = true;
        public Func<Task<string?>> OnCheck { get; set; } = () => Task.FromResult<string?>(null);
        private int _calls;
        public int Calls => Volatile.Read(ref _calls);

        /// <summary>Completed once the service has been entered (the check runs on a thread-pool thread).</summary>
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<string?> CheckAndDownloadAsync()
        {
            Interlocked.Increment(ref _calls);
            Started.TrySetResult();
            return OnCheck();
        }

        public void ApplyUpdatesAndRestart() { }
    }

    private sealed class Harness
    {
        public FakeUpdateService Service { get; } = new();
        public List<string> Errors { get; } = new();
        public List<string> Ready { get; } = new();
        public UpdateChecker Checker { get; }

        public Harness() => Checker = new UpdateChecker(Service, Errors.Add, Ready.Add);
    }

    [Fact]
    public async Task UpToDate()
    {
        var h = new Harness();
        Assert.IsType<UpdateCheckOutcome.UpToDate>(await h.Checker.RunCheckAsync());
        Assert.Empty(h.Ready);
        Assert.Null(h.Checker.ReadyVersion);
    }

    [Fact]
    public async Task UpdateReady()
    {
        var h = new Harness();
        h.Service.OnCheck = () => Task.FromResult<string?>("2.0.0");

        var outcome = await h.Checker.RunCheckAsync();

        Assert.Equal(new UpdateCheckOutcome.UpdateReady("2.0.0"), outcome);
        Assert.Equal(new[] { "2.0.0" }, h.Ready);
        Assert.Equal("2.0.0", h.Checker.ReadyVersion);
    }

    [Fact]
    public async Task AlreadyChecking_WhenSecondCheckStartsWhileFirstRuns()
    {
        var h = new Harness();
        var gate = new TaskCompletionSource<string?>();
        h.Service.OnCheck = () => gate.Task;

        var first = h.Checker.RunCheckAsync();
        await h.Service.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(h.Checker.IsChecking);
        Assert.IsType<UpdateCheckOutcome.AlreadyChecking>(await h.Checker.RunCheckAsync());
        Assert.Equal(1, h.Service.Calls);

        gate.SetResult(null);
        Assert.IsType<UpdateCheckOutcome.UpToDate>(await first);
        Assert.False(h.Checker.IsChecking);
    }

    [Fact]
    public async Task Error_Throws_AndClearsRunningFlag()
    {
        var h = new Harness();
        h.Service.OnCheck = () => throw new InvalidOperationException("boom");

        await Assert.ThrowsAsync<InvalidOperationException>(() => h.Checker.RunCheckAsync());
        Assert.False(h.Checker.IsChecking);
    }

    [Fact]
    public async Task Tick_SkippedWhileCheckRuns()
    {
        var h = new Harness();
        var gate = new TaskCompletionSource<string?>();
        h.Service.OnCheck = () => gate.Task;

        var running = h.Checker.RunCheckAsync();
        await h.Service.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await h.Checker.TickAsync();
        Assert.Equal(1, h.Service.Calls);

        gate.SetResult(null);
        await running;
    }

    [Fact]
    public async Task Tick_RunsSilentCheck()
    {
        var h = new Harness();
        await h.Checker.TickAsync();
        Assert.Equal(1, h.Service.Calls);
    }

    [Fact]
    public async Task Tick_AfterUpdateReady_DoesNotCheckAgain_AndSignalsReadyOnce()
    {
        var h = new Harness();
        h.Service.OnCheck = () => Task.FromResult<string?>("2.0.0");

        await h.Checker.TickAsync();
        await h.Checker.TickAsync();

        Assert.Equal(1, h.Service.Calls);
        Assert.Single(h.Ready);
    }

    [Fact]
    public async Task Manual_Messages()
    {
        var h = new Harness();
        Assert.Equal("You're up to date.", await h.Checker.RunManualCheckAsync());

        h.Service.OnCheck = () => throw new InvalidOperationException("boom");
        Assert.Equal("Update check failed: boom", await h.Checker.RunManualCheckAsync());

        h.Service.IsInstalled = false;
        Assert.Equal("Updates are only available in the installed version.", await h.Checker.RunManualCheckAsync());
    }

    [Fact]
    public async Task Manual_UpdateReady()
    {
        var h = new Harness();
        h.Service.OnCheck = () => Task.FromResult<string?>("2.0.0");
        Assert.Equal("Version 2.0.0 is ready. Restart to update.", await h.Checker.RunManualCheckAsync());
    }

    [Fact]
    public async Task Manual_WhileCheckRuns_SaysAlreadyRunning()
    {
        var h = new Harness();
        var gate = new TaskCompletionSource<string?>();
        h.Service.OnCheck = () => gate.Task;

        var running = h.Checker.RunCheckAsync();
        await h.Service.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("An update check is already running…", await h.Checker.RunManualCheckAsync());
        Assert.Equal(1, h.Service.Calls);

        gate.SetResult(null);
        await running;
    }

    [Fact]
    public async Task Manual_WhenUpdateAlreadyDownloaded_ReportsItWithoutChecking()
    {
        var h = new Harness();
        h.Service.OnCheck = () => Task.FromResult<string?>("2.0.0");
        await h.Checker.RunSilentCheckAsync();

        Assert.Equal("Version 2.0.0 is ready. Restart to update.", await h.Checker.RunManualCheckAsync());
        Assert.Equal(1, h.Service.Calls);
    }

    [Fact]
    public async Task SilentError_ReportedOnlyOncePerRun()
    {
        var h = new Harness();
        h.Service.OnCheck = () => throw new InvalidOperationException("offline");

        await h.Checker.RunSilentCheckAsync();
        await h.Checker.TickAsync();
        await h.Checker.TickAsync();

        Assert.Equal(3, h.Service.Calls);
        Assert.Equal(new[] { "Update check failed: offline" }, h.Errors);
    }
}
