using System.Diagnostics;
using System.Net.Http;
using System.Xml;
using System.Xml.Linq;

namespace NebulaMd;

internal sealed class UpdateChecker
{
    internal const string PackageUrl = "https://community.chocolatey.org/packages/nebula-md";
    internal const string FeedUrl = "https://community.chocolatey.org/api/v2/Packages()?$filter=(tolower(Id)%20eq%20%27nebula-md%27)%20and%20IsLatestVersion";
    internal static readonly TimeSpan CheckInterval = TimeSpan.FromHours(24);
    private static readonly HttpClient SharedClient = CreateClient();
    private readonly HttpClient client;
    private readonly Version currentVersion;
    private readonly TimeProvider timeProvider;

    internal UpdateChecker(HttpClient? client = null, Version? currentVersion = null, TimeProvider? timeProvider = null)
    {
        this.client = client ?? SharedClient;
        this.currentVersion = Normalize(currentVersion ?? typeof(UpdateChecker).Assembly.GetName().Version!);
        this.timeProvider = timeProvider ?? TimeProvider.System;
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15), MaxResponseContentBufferSize = 1024 * 1024 };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("nebula-md/" + typeof(UpdateChecker).Assembly.GetName().Version);
        return client;
    }

    internal async Task RunAsync(Action<Version> notify, CancellationToken cancellationToken)
    {
        // Start the clock before the first request so network latency does not
        // move the daily schedule. PeriodicTimer coalesces missed ticks.
        using var timer = new PeriodicTimer(CheckInterval, timeProvider);
        var lastNotified = currentVersion;
        try
        {
            do
            {
                var latest = await FindUpdateAsync(cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                if (latest is not null && Normalize(latest) > lastNotified)
                {
                    lastNotified = Normalize(latest);
                    notify(latest);
                }
            } while (await timer.WaitForNextTickAsync(cancellationToken));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    internal async Task<Version?> FindUpdateAsync(CancellationToken cancellationToken)
    {
        try
        {
            var xml = await client.GetStringAsync(FeedUrl, cancellationToken);
            using var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = 1024 * 1024
            });
            var feed = XDocument.Load(reader);
            XNamespace atom = "http://www.w3.org/2005/Atom";
            XNamespace metadata = "http://schemas.microsoft.com/ado/2007/08/dataservices/metadata";
            XNamespace data = "http://schemas.microsoft.com/ado/2007/08/dataservices";
            Version? latest = null;
            foreach (var entry in feed.Root?.Elements(atom + "entry") ?? [])
            {
                var properties = entry.Element(metadata + "properties");
                if ((string?)entry.Element(atom + "title") != "nebula-md" ||
                    (string?)properties?.Element(data + "IsApproved") != "true" ||
                    (string?)properties?.Element(data + "IsPrerelease") != "false" ||
                    (string?)properties?.Element(data + "IsLatestVersion") != "true" ||
                    !Version.TryParse((string?)properties?.Element(data + "Version"), out var version)) continue;

                if (Normalize(version) > currentVersion &&
                    (latest is null || Normalize(version) > Normalize(latest))) latest = version;
            }
            return latest;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or XmlException or IOException)
        {
            // Updates are optional. An offline feed must never interrupt editing.
            Debug.WriteLine($"Update check unavailable: {ex.Message}");
            return null;
        }
    }

    private static Version Normalize(Version version) =>
        new(version.Major, version.Minor, Math.Max(0, version.Build), Math.Max(0, version.Revision));
}
