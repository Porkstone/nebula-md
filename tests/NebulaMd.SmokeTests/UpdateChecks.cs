using System.Net;
using System.Net.Http;
using NebulaMd;

internal static class UpdateChecks
{
    internal static async Task Run(Action<bool, string> check)
    {
        using var handler = new FeedHandler();
        using var client = new HttpClient(handler);
        var clock = new TestClock();
        var checker = new UpdateChecker(client, new Version(1, 2, 0, 0), clock);
        foreach (var version in new[] { "1.2", "1.2.0", "1.2.0.0", "1.1.99", "bad", "2.0.0-beta" })
        {
            handler.Xml = Feed(Entry(version));
            check(await checker.FindUpdateAsync(default) is null, $"Update comparison ignores {version}");
        }
        handler.Xml = Feed(Entry("1.10.0"), Entry("1.3.0"), Entry("9.0.0", approved: false),
            Entry("8.0.0", prerelease: true), Entry("7.0.0", id: "another-package"), Entry("6.0.0", latest: false));
        check(await checker.FindUpdateAsync(default) == new Version(1, 10, 0), "Selects numerically newest approved stable nebula-md package");
        handler.Xml = Feed(Entry("1.2.0.1"));
        check(await checker.FindUpdateAsync(default) == new Version(1, 2, 0, 1), "Four-part package versions compare correctly");
        foreach (var xml in new[] { "not xml", Feed(""), "<!DOCTYPE feed [<!ENTITY file SYSTEM 'file:///missing'>]><feed>&file;</feed>" })
        {
            handler.Xml = xml;
            check(await checker.FindUpdateAsync(default) is null, "Invalid, empty, or DTD-bearing feeds do not interrupt the app");
        }
        handler.Status = HttpStatusCode.ServiceUnavailable;
        check(await checker.FindUpdateAsync(default) is null, "HTTP errors are silent");
        handler.Status = HttpStatusCode.OK;
        handler.Error = new HttpRequestException("Offline");
        check(await checker.FindUpdateAsync(default) is null, "Offline failures are silent");
        handler.Error = new TaskCanceledException("Request timeout");
        check(await checker.FindUpdateAsync(default) is null, "Request timeouts are silent");
        handler.Error = null;

        handler.Xml = Feed(Entry("1.3.0"));
        using var cancellation = new CancellationTokenSource();
        var notifications = new List<Version>();
        var before = handler.Requests;
        var run = checker.RunAsync(notifications.Add, cancellation.Token);
        check(handler.Requests == before + 1 && notifications.Count == 1, "Startup checks immediately and announces an update");
        clock.Advance(TimeSpan.FromHours(24) - TimeSpan.FromTicks(1));
        check(handler.Requests == before + 1, "No repeat request before 24 hours");
        clock.Advance(TimeSpan.FromTicks(1));
        check(handler.Requests == before + 2 && notifications.Count == 1, "Checks at 24 hours without repeating the same alert");
        handler.Error = new HttpRequestException("Offline next day");
        clock.Advance(TimeSpan.FromHours(24));
        check(handler.Requests == before + 3 && notifications.Count == 1, "Daily schedule survives a network failure");
        handler.Error = null;
        handler.Xml = Feed(Entry("1.4.0"));
        clock.Advance(TimeSpan.FromHours(24));
        check(handler.Requests == before + 4 && notifications.Last() == new Version(1, 4, 0), "Later releases produce a fresh alert");
        cancellation.Cancel();
        await run;
        clock.Advance(TimeSpan.FromDays(2));
        check(handler.Requests == before + 4 && clock.Disposed, "Closing cancels the schedule and disposes its timer");

        using var blockedHandler = new BlockingHandler();
        using var blockedClient = new HttpClient(blockedHandler);
        using var pendingCancellation = new CancellationTokenSource();
        var pendingClock = new TestClock();
        var pendingRun = new UpdateChecker(blockedClient, new Version(1, 0), pendingClock)
            .RunAsync(_ => throw new Exception("Notification after cancellation"), pendingCancellation.Token);
        pendingClock.Advance(TimeSpan.FromDays(3));
        check(blockedHandler.Requests == 1, "A slow request never overlaps another update check");
        pendingCancellation.Cancel();
        await pendingRun;
        check(blockedHandler.Cancelled && pendingClock.Disposed, "Closing cancels an in-flight HTTP request without a notification");
    }

    private static string Feed(params string[] entries) =>
        "<feed xmlns='http://www.w3.org/2005/Atom' xmlns:m='http://schemas.microsoft.com/ado/2007/08/dataservices/metadata' xmlns:d='http://schemas.microsoft.com/ado/2007/08/dataservices'>" + string.Concat(entries) + "</feed>";

    private static string Entry(string version, bool approved = true, bool prerelease = false, string id = "nebula-md", bool latest = true) =>
        $"<entry><title>{id}</title><m:properties><d:Version>{version}</d:Version><d:IsApproved>{approved.ToString().ToLowerInvariant()}</d:IsApproved><d:IsPrerelease>{prerelease.ToString().ToLowerInvariant()}</d:IsPrerelease><d:IsLatestVersion>{latest.ToString().ToLowerInvariant()}</d:IsLatestVersion></m:properties></entry>";

    private sealed class FeedHandler : HttpMessageHandler
    {
        internal string Xml = Feed();
        internal HttpStatusCode Status = HttpStatusCode.OK;
        internal Exception? Error;
        internal int Requests;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsoluteUri != UpdateChecker.FeedUrl) throw new Exception("Unexpected feed URL");
            Requests++;
            return Error is not null ? Task.FromException<HttpResponseMessage>(Error) :
                Task.FromResult(new HttpResponseMessage(Status) { Content = new StringContent(Xml) });
        }
    }

    private sealed class BlockingHandler : HttpMessageHandler
    {
        internal int Requests;
        internal bool Cancelled;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); }
            catch (OperationCanceledException) { Cancelled = true; throw; }
            throw new Exception("Unreachable");
        }
    }

    // Drive the real PeriodicTimer without waiting a day or adding test packages.
    private sealed class TestClock : TimeProvider
    {
        private TestTimer? timer;
        private TimeSpan elapsed;
        internal bool Disposed => timer?.Disposed == true;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            timer = new TestTimer(callback, state) { Due = elapsed + dueTime, Period = period };
            return timer;
        }
        internal void Advance(TimeSpan duration)
        {
            elapsed += duration;
            while (timer is { Disposed: false } && elapsed >= timer.Due)
            {
                timer.Due += timer.Period;
                timer.Fire();
            }
        }
        private sealed class TestTimer(TimerCallback callback, object? state) : ITimer
        {
            internal TimeSpan Due;
            internal TimeSpan Period;
            internal bool Disposed;
            internal void Fire() => callback(state);
            public bool Change(TimeSpan dueTime, TimeSpan period) => throw new NotSupportedException();
            public void Dispose() => Disposed = true;
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }
}
