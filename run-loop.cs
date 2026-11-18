// Starts the provider (OpenIDProviderApp), the API (ApiSample) and the web client (TestClientApp) together.
//
//   dotnet run-loop.cs           starts the three and keeps them running until Ctrl+C
//   dotnet run-loop.cs --check   also signs in as the demo user, calls the API through the client and signs
//                                out, then stops them; the exit code is non-zero if any step did not answer
//
// The addresses come from each project's launch profile, the same ones `dotnet run --project` uses.

using System.Diagnostics;
using System.Net;
using System.Security.Authentication;
using System.Text.Json;
using System.Web;

var root = AppContext.GetData("EntryPointFileDirectoryPath") as string
           ?? throw new InvalidOperationException("Run this file with `dotnet run-loop.cs` from the SDK");

var check = args switch
{
    [] => false,
    ["--check"] => true,
    _ => throw new ArgumentException($"Unknown arguments: {string.Join(' ', args)}. Use none or --check."),
};

// The provider first: the API and the client read its discovery document
var provider = new Sample("OpenIDProviderApp", root);
var api = new Sample("ApiSample", root);
var client = new Sample("TestClientApp", root);
Sample[] samples = [provider, api, client];

// The demo user OpenIDProviderApp registers in its Program.cs
const string email = "john.doe@example.com";
const string password = "Jd!2024$3cur3";

using var stop = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    stop.Cancel();
};

using var probe = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(5) };

try
{
    // A sample already listening would answer every step below with binaries this run did not build
    foreach (var sample in samples)
    {
        if (await Answers(probe, sample.Address))
            throw new StepFailed($"{sample.Address} already answers: stop whatever runs {sample.Name} there first");
    }

    foreach (var sample in samples)
        Run("dotnet", "build", sample.Project, "--nologo", "--verbosity", "quiet");

    foreach (var sample in samples)
    {
        sample.Start();
        await WaitUntilAnswers(probe, sample, stop.Token);
        Console.WriteLine($"==> {sample.Name} answers at {sample.Address}");
    }

    if (!check)
    {
        Console.WriteLine($"==> Open {client.Address} and sign in as {email} / {password}. Ctrl+C stops all three.");
        await Task.WhenAny(Task.Delay(Timeout.Infinite, stop.Token).ContinueWith(_ => { }),
            Task.WhenAny(samples.Select(sample => sample.Exited)));
        if (stop.IsCancellationRequested)
            return 0;

        throw new StepFailed($"{samples.First(sample => sample.HasExited).Name} exited, so the others are stopped too");
    }

    await DriveTheLoop();
    Console.WriteLine("==> The loop works: signed in, called the API with the access token, signed out");
    return 0;
}
// Ctrl+C reaches the build and the samples too, so whatever failed after it is the stop, not a defect
catch (Exception) when (stop.IsCancellationRequested)
{
    Console.Error.WriteLine("==> Stopped by Ctrl+C");
    return 130;
}
catch (StepFailed failure)
{
    Console.Error.WriteLine($"==> FAILED: {failure.Message}");
    return 1;
}
finally
{
    // One sample that cannot be stopped must not leave the others running
    foreach (var sample in samples)
    {
        try
        {
            sample.Stop();
        }
        catch (Exception failure)
        {
            Console.Error.WriteLine($"==> Could not stop {sample.Name}: {failure.Message}");
        }
    }
}

async Task DriveTheLoop()
{
    // One browser: the cookies of all three hosts in one jar, redirects followed
    var cookies = new CookieContainer();
    using var browser = new HttpClient(new HttpClientHandler { CookieContainer = cookies })
        { Timeout = TimeSpan.FromSeconds(30) };

    // The client's home page needs a signed-in user, so it sends the browser to the provider's login page
    using var loginPage = await browser.GetAsync(client.Address);
    Expect(loginPage, provider, "/Auth/Login", "Opening the client did not land on the provider's login page");
    var requestUri = HttpUtility.ParseQueryString(loginPage.RequestMessage!.RequestUri!.Query)["request_uri"]
                     ?? throw new StepFailed("The provider's login page was opened without a request_uri");

    // Signing in resumes the authorization request, and the client redeems the code it gets back
    using var signedIn = await browser.PostAsync(new Uri(provider.Address, "/Auth/Login"), new FormUrlEncodedContent(
        new Dictionary<string, string> { ["email"] = email, ["password"] = password, ["requestUri"] = requestUri }));
    Expect(signedIn, client, "/", "Signing in did not return to the client's home page");
    Console.WriteLine("==> Signed in");

    // The client calls the API with its access token and passes the API's answer on
    using var weather = await browser.GetAsync(new Uri(client.Address, "/Home/Weather"));
    Expect(weather, client, "/Home/Weather", "The API refused the call the client made with the access token");
    using var forecast = JsonDocument.Parse(await weather.Content.ReadAsStringAsync());
    if (forecast.RootElement is not { ValueKind: JsonValueKind.Array } days || days.GetArrayLength() == 0)
        throw new StepFailed("The API answered without a forecast");
    Console.WriteLine($"==> The API answered with {days.GetArrayLength()} days of forecast");

    // Cookies do not separate by port, so the provider's session cookie and the client's share one jar. Dropping
    // only the client's shows the provider session is alive: the client gets a new sign-in without the login page.
    // Without this, a sign-out that kept the provider session would end at the login page all the same.
    var clientCookie = client.CookieName();
    foreach (Cookie cookie in cookies.GetCookies(client.Address))
    {
        if (cookie.Name.StartsWith(clientCookie, StringComparison.Ordinal))
            cookie.Expired = true;
    }

    if (cookies.GetCookies(client.Address).Any(cookie => cookie.Name.StartsWith(clientCookie, StringComparison.Ordinal)))
        throw new StepFailed($"The client's {clientCookie} cookie could not be dropped");

    using var signedInAgain = await browser.GetAsync(client.Address);
    Expect(signedInAgain, client, "/", "Without its own cookie the client did not get a new sign-in from the provider's session");
    Console.WriteLine("==> The provider's session signed the client in again without the login page");

    // Signing out ends the provider's session too, so the home page asks for a sign-in again
    using var signedOut = await browser.GetAsync(new Uri(client.Address, "/Home/EndSession"));
    Expect(signedOut, provider, "/Auth/Login", "Signing out did not end at the provider's login page");
    Console.WriteLine("==> Signed out");
}

static void Expect(HttpResponseMessage response, Sample host, string path, string failure)
{
    var landed = response.RequestMessage!.RequestUri!;
    if (response.StatusCode != HttpStatusCode.OK
        || landed.GetLeftPart(UriPartial.Authority) != host.Address.GetLeftPart(UriPartial.Authority)
        || !string.Equals(landed.AbsolutePath, path, StringComparison.OrdinalIgnoreCase))
    {
        throw new StepFailed($"{failure}: {(int)response.StatusCode} at {landed}");
    }
}

static async Task<bool> Answers(HttpClient probe, Uri address)
{
    try
    {
        using var response = await probe.GetAsync(address);
        return true;
    }
    catch (HttpRequestException failure) when (failure.InnerException is AuthenticationException)
    {
        // Something listens there, and the samples cannot call each other through a certificate nobody trusts
        throw new StepFailed($"{address} answers with a certificate this machine does not trust: " +
                             "run `dotnet dev-certs https --trust` (on Linux, also add its folder to SSL_CERT_DIR)");
    }
    catch (HttpRequestException)
    {
        return false;
    }
    catch (TaskCanceledException)
    {
        return false;
    }
}

static async Task WaitUntilAnswers(HttpClient probe, Sample sample, CancellationToken stop)
{
    var deadline = DateTimeOffset.UtcNow + TimeSpan.FromMinutes(1);
    while (!await Answers(probe, sample.Address))
    {
        if (sample.HasExited)
            throw new StepFailed($"{sample.Name} exited before it answered at {sample.Address}");
        if (DateTimeOffset.UtcNow > deadline)
            throw new StepFailed($"{sample.Name} did not answer at {sample.Address} within a minute");

        await Task.Delay(TimeSpan.FromMilliseconds(500), stop);
    }
}

static void Run(string fileName, params string[] arguments)
{
    using var process = Process.Start(new ProcessStartInfo(fileName, arguments))
                        ?? throw new StepFailed($"{fileName} did not start");
    process.WaitForExit();
    if (process.ExitCode != 0)
        throw new StepFailed($"{fileName} {string.Join(' ', arguments)} exited with {process.ExitCode}");
}

/// <summary>
/// One sample project, started with `dotnet run` on the address of its launch profile.
/// </summary>
sealed class Sample
{
    private const string Profile = "https";
    private Process? _process;

    public Sample(string name, string root)
    {
        Name = name;
        Project = Path.Combine(root, name);

        var launchSettings = Path.Combine(Project, "Properties", "launchSettings.json");
        using var settings = JsonDocument.Parse(File.ReadAllText(launchSettings));
        var url = settings.RootElement.GetProperty("profiles").GetProperty(Profile).GetProperty("applicationUrl").GetString()
                  ?? throw new InvalidOperationException($"{launchSettings} has no applicationUrl in the {Profile} profile");
        Address = new Uri(url, UriKind.Absolute);
    }

    public string Name { get; }
    public string Project { get; }
    public Uri Address { get; }
    public bool HasExited => _process?.HasExited ?? false;
    public Task Exited => _process?.WaitForExitAsync() ?? Task.CompletedTask;

    /// <summary>
    /// The name of the sample's session cookie, from the Cookies section its cookie handler binds.
    /// </summary>
    public string CookieName()
    {
        var appSettings = Path.Combine(Project, "appsettings.json");
        using var settings = JsonDocument.Parse(File.ReadAllText(appSettings),
            new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        return settings.RootElement.GetProperty("Cookies").GetProperty("Cookie").GetProperty("Name").GetString()
               ?? throw new InvalidOperationException($"{appSettings} has no Cookies:Cookie:Name");
    }

    public void Start()
    {
        // The launch profile also sets the Development environment, where the client and the API find the provider
        _process = Process.Start(new ProcessStartInfo("dotnet",
                       ["run", "--project", Project, "--no-build", "--launch-profile", Profile]) { EnvironmentVariables = { ["DOTNET_NOLOGO"] = "1" } })
                   ?? throw new StepFailed($"{Name} did not start");
    }

    public void Stop()
    {
        if (_process is { HasExited: false })
            _process.Kill(entireProcessTree: true);
        _process?.Dispose();
    }
}

sealed class StepFailed(string message) : Exception(message);
