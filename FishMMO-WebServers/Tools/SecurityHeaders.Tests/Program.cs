using System.Security.Cryptography;
using System.Text;
using FishMMO.WebShared;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.FileProviders;

int checks = 0;
void Check(bool condition, string message) { if (!condition) throw new Exception(message); checks++; }
var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
builder.WebHost.UseUrls("http://127.0.0.1:0");
builder.Configuration["GameHost"] = "127.0.0.1";
builder.Configuration["ContentSecurityPolicy:ConnectOrigins:0"] = "http://localhost:8088";
string root = Path.Combine(Path.GetTempPath(), "fishmmo-csp-" + Guid.NewGuid());
Directory.CreateDirectory(root);
try
{
    const string script = "\nwindow.unityReady = true;\n";
    string hash = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(script)));
    File.WriteAllText(Path.Combine(root, "index.html"), "<script>" + script.Replace("\n", "\r\n") + "</script><script src='loader.js'></script>");
    using var files = new PhysicalFileProvider(root);
    await using var app = builder.Build();
    app.UseFishMMOSecurityHeaders(app.Environment, configuration: app.Configuration,
        profile: ContentSecurityPolicyProfile.WebGl, webRoot: files);
    app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = files });
    app.UseStaticFiles(new StaticFileOptions { FileProvider = files });
    await app.StartAsync();
    string address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
    using var client = new HttpClient { BaseAddress = new Uri(address) };
    using var response = await client.GetAsync("/");
    response.EnsureSuccessStatusCode();
    string header = response.Headers.GetValues("Content-Security-Policy").Single();
    Check(header.Contains("'sha256-" + hash + "'"), "Real static-file response must hash normalized inline script contents.");
    Check(header.Split("'sha256-").Length == 2, "External scripts must not create inline hashes.");
    Check(header.Contains("script-src 'self' 'wasm-unsafe-eval' blob:"), "Unity needs wasm and blob script permissions.");
    Check(!header.Contains("'unsafe-eval'") && !header.Contains("script-src 'self' 'unsafe-inline'"), "Do not grant arbitrary script evaluation or inline execution.");
    Check(header.Contains("http://localhost:8088") && header.Contains("https://127.0.0.1:*"), "Allow API and WebTransport origins.");
    Check(header.Contains("worker-src 'self' blob:"), "Allow decompression and Unity workers.");
    using var conditionalRequest = new HttpRequestMessage(HttpMethod.Get, "/");
    conditionalRequest.Headers.IfNoneMatch.Add(response.Headers.ETag!);
    using var conditionalResponse = await client.SendAsync(conditionalRequest);
    Check(conditionalResponse.StatusCode == System.Net.HttpStatusCode.NotModified, "Static-file conditional caching must work.");
    Check(conditionalResponse.Headers.GetValues("Content-Security-Policy").Single().Contains(hash), "304 responses must preserve the startup script hash.");
    File.WriteAllText(Path.Combine(root,"index.html"), "<script>window.changed = true;</script>");
    using var rebuilt = await client.GetAsync("/index.html");
    Check(!rebuilt.Headers.GetValues("Content-Security-Policy").Single().Contains(hash), "Rebuilt HTML must get fresh hashes without restarting.");
    using var missing = await client.GetAsync("/missing.wasm");
    Check(missing.Headers.Contains("Content-Security-Policy"), "Error responses also receive CSP.");
    var context = new DefaultHttpContext();
    Check(new ContentSecurityPolicy(ContentSecurityPolicyProfile.Api, app.Configuration, app.Environment).GetHeader(context).Contains("default-src 'none'"), "APIs use a restrictive policy.");
    string panel = new ContentSecurityPolicy(ContentSecurityPolicyProfile.ControlPanel, app.Configuration, app.Environment).GetHeader(context);
    Check(panel.Contains("script-src 'self';") && !panel.Contains("blob:") && !panel.Contains("wasm-unsafe-eval"), "Panel must not inherit Unity permissions.");
    foreach (string invalid in new[] { "https://api.example/path", "https://api.example;script-src", "http://remote.example", "https://user@api.example", "https://api.example/#x" })
    {
        app.Configuration["ContentSecurityPolicy:ConnectOrigins:0"] = invalid;
        bool rejected = false;
        try { _ = new ContentSecurityPolicy(ContentSecurityPolicyProfile.WebGl, app.Configuration, app.Environment); }
        catch (InvalidOperationException) { rejected = true; }
        Check(rejected, "Reject malformed or insecure origin " + invalid);
    }
    app.Configuration["ContentSecurityPolicy:ConnectOrigins:0"] = "https://api.fishmmo.com";
    app.Configuration["GameHost"] = "127.0.0.1; default-src *";
    bool badHost = false;
    try { _ = new ContentSecurityPolicy(ContentSecurityPolicyProfile.WebGl, app.Configuration, app.Environment); }
    catch (InvalidOperationException) { badHost = true; }
    Check(badHost, "Reject GameHost policy injection.");
    app.Configuration["GameHost"] = "game.fishmmo.com";
    app.Environment.EnvironmentName = "Production";
    string production = new ContentSecurityPolicy(ContentSecurityPolicyProfile.WebGl, app.Configuration, app.Environment).GetHeader(context);
    Check(production.Contains("https://api.fishmmo.com") && !production.Contains("http://"), "Production has HTTPS origins only.");
    app.Configuration["ContentSecurityPolicy:ConnectOrigins:0"] = "http://localhost:8088";
    bool insecureProduction = false;
    try { _ = new ContentSecurityPolicy(ContentSecurityPolicyProfile.WebGl, app.Configuration, app.Environment); }
    catch (InvalidOperationException) { insecureProduction = true; }
    Check(insecureProduction, "Production rejects HTTP loopback origins.");
    await app.StopAsync();
    Console.WriteLine($"PASS: {checks} CSP checks, including real HTTP/static-file responses.");
}
finally { Directory.Delete(root, true); }
