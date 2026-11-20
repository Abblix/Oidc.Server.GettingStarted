// Starts a provider, the API (ApiSample) and the web client (TestClientApp) together.
//
//   dotnet run-loop.cs           starts the three and keeps them running until Ctrl+C
//   dotnet run-loop.cs --check   also signs in as the demo user, calls the API through the client and signs
//                                out, then stops them; the exit code is non-zero if any step did not answer
//
//   --provider mvc               the provider is OpenIDProviderApp (the default)
//   --provider minimal           the provider is OpenIDProviderApp.MinimalApi; the client and the API are
//                                pointed at it through environment variables, their files stay as they are
//
// The addresses come from each project's launch profile, the same ones `dotnet run --project` uses.

#:package AngleSharp@1.9.1

using System.Diagnostics;
using System.Net;
using System.Security.Authentication;
using System.Text.Json;
using System.Web;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;

var root = AppContext.GetData("EntryPointFileDirectoryPath") as string
           ?? throw new InvalidOperationException("Run this file with `dotnet run-loop.cs` from the SDK");

const string usage = "Use no arguments, --check, --provider mvc|minimal, or both in any order.";
var check = false;
string? choice = null;
for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--check" when !check:
            check = true;
            break;

        case "--provider" when choice == null && i + 1 < args.Length:
            choice = args[++i];
            break;

        default:
            Console.Error.WriteLine($"==> Unknown arguments: {string.Join(' ', args)}. {usage}");
            return 2;
    }
}

// The client and the API are configured for OpenIDProviderApp, so only another provider needs them pointed at it.
// Leaving them alone for that one keeps a wrong address in their own settings visible.
var (providerName, pointAtProvider) = (choice ?? "mvc") switch
{
    "mvc" => ("OpenIDProviderApp", false),
    "minimal" => ("OpenIDProviderApp.MinimalApi", true),
    _ => (null, false),
};
if (providerName == null)
{
    Console.Error.WriteLine($"==> Unknown provider: {choice}. {usage}");
    return 2;
}

// The provider first: the API and the client read its discovery document
var provider = new Sample(providerName, root);
var api = new Sample("ApiSample", root);
var client = new Sample("TestClientApp", root);
Sample[] samples = [provider, api, client];

// The demo user both providers register in their Program.cs
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
    // Environment variables outrank the appsettings files, so they point the two at the provider without editing
    // them. The API validates tokens against the discovery document it reads from the authority, whose issuer is
    // accepted alongside the configured ValidIssuer, so that one needs no override.
    if (pointAtProvider)
    {
        var authority = provider.Address.GetLeftPart(UriPartial.Authority);
        client.Point(["OpenIdConnect", "Authority"], authority);
        api.Point(["JwtBearerAuthentication", "Authority"], authority);
    }

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
    Console.WriteLine($"==> The loop works with {provider.Name}: signed in, called the API with the access token, signed out");
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

    // Signing in resumes the authorization request, and the client redeems the code it gets back. The form is
    // posted with every field the page put in it, as a browser does: the request_uri and the antiforgery token.
    var login = await FormOf(loginPage, "/Auth/Login");
    if (login.Fields.GetValueOrDefault("requestUri") != requestUri)
        throw new StepFailed("The provider's login form does not carry the request_uri of its address");

    using var signedIn = await browser.PostAsync(login.Action, new FormUrlEncodedContent(
        new Dictionary<string, string>(login.Fields) { ["email"] = email, ["password"] = password }));
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

    // Signing out ends the provider's session too, so the home page asks for a sign-in again. It is the form on
    // the client's pages, posted with its antiforgery token.
    var logout = await FormOf(signedInAgain, "/Home/EndSession");
    using var signedOut = await browser.PostAsync(logout.Action, new FormUrlEncodedContent(logout.Fields));
    Expect(signedOut, provider, "/Auth/Login", "Signing out did not end at the provider's login page");
    Console.WriteLine("==> Signed out");
}

// The form on the page that posts to the path, with the address it posts to and the value of every named input
static async Task<(Uri Action, Dictionary<string, string> Fields)> FormOf(HttpResponseMessage page, string path)
{
    var address = page.RequestMessage!.RequestUri!;
    var document = await new HtmlParser().ParseDocumentAsync(await page.Content.ReadAsStringAsync());
    foreach (var form in document.Forms)
    {
        var action = new Uri(address, form.GetAttribute("action") ?? "");
        if (!string.Equals(action.AbsolutePath, path, StringComparison.OrdinalIgnoreCase))
            continue;

        var fields = form.Elements.OfType<IHtmlInputElement>()
            .Where(input => !string.IsNullOrEmpty(input.Name))
            .ToDictionary(input => input.Name!, input => input.Value);
        return (action, fields);
    }

    throw new StepFailed($"The page at {address} has no form that posts to {path}");
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
    private readonly Dictionary<string, string> _environment = [];
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
    public string CookieName() => Setting(["Cookies", "Cookie", "Name"]);

    /// <summary>
    /// Overrides one of the sample's settings for this run through the environment variable configuration
    /// reads it from. The setting must already be in its appsettings files: a renamed one would otherwise
    /// leave the sample pointed elsewhere with nothing saying why.
    /// </summary>
    public void Point(string[] path, string value)
    {
        Setting(path);
        _environment[string.Join("__", path)] = value;
    }

    private string Setting(string[] path)
    {
        // The Development file layers over the base one, so a setting may live in either
        foreach (var file in new[] { "appsettings.Development.json", "appsettings.json" })
        {
            var appSettings = Path.Combine(Project, file);
            if (!File.Exists(appSettings))
                continue;

            using var settings = JsonDocument.Parse(File.ReadAllText(appSettings),
                new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            // A key under a value that is not an object is not there, the same as a missing key
            var element = settings.RootElement;
            if (!path.All(key => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(key, out element)))
                continue;

            if (element.ValueKind != JsonValueKind.String)
                throw new StepFailed($"{Name} has {string.Join(':', path)} in {file} as {element.ValueKind}, not a string");

            return element.GetString()!;
        }

        throw new StepFailed($"{Name} has no {string.Join(':', path)} in its appsettings files");
    }

    public void Start()
    {
        // The launch profile also sets the Development environment, where the client and the API find the provider
        var start = new ProcessStartInfo("dotnet", ["run", "--project", Project, "--no-build", "--launch-profile", Profile])
            { EnvironmentVariables = { ["DOTNET_NOLOGO"] = "1" } };
        foreach (var (name, value) in _environment)
            start.EnvironmentVariables[name] = value;

        _process = Process.Start(start) ?? throw new StepFailed($"{Name} did not start");
    }

    public void Stop()
    {
        if (_process is { HasExited: false })
            _process.Kill(entireProcessTree: true);
        _process?.Dispose();
    }
}

sealed class StepFailed(string message) : Exception(message);
