using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ProtoBuf.Connect.AspNetCore;
using ProtoBuf.ConnectCorsChecks;
using System.Net;

// Answers the CORS question that notes/findings.md §12 carried as UNVERIFIED, and that the docs'
// "callable from connect-es in a browser" claim rests on.
//
// It cannot drive a real browser, and does not pretend to: what it drives is ASP.NET Core's own CORS
// middleware with the exact preflight a browser sends for a Connect call. That is the half we control
// and the half that goes wrong - the browser's side of CORS is not in question, the server's policy is.
//
// Every check has a negative twin, because a CORS test that only asserts "allowed" passes just as
// happily against a policy that allows everything.

var failures = new List<string>();

const string Origin = "https://app.example.test";
const string OtherOrigin = "https://evil.example.test";

var port = FreePort();
var builder = WebApplication.CreateSlimBuilder(args);
builder.Logging.ClearProviders();
builder.WebHost.UseUrls($"http://127.0.0.1:{port}");

builder.Services.AddCors(options =>
{
    // what the helper gives you, and nothing else
    options.AddPolicy("connect", policy => policy.WithOrigins(Origin).WithConnect());

    // ...plus the two things the helper documents that it cannot do for you
    options.AddPolicy("connect-extras", policy => policy
        .WithOrigins(Origin)
        .WithConnect()
        .WithHeaders("Authorization")
        .WithExposedHeaders("trailer-x-audit"));
});

builder.Services.AddCorsServices();

var app = builder.Build();
app.UseCors();
app.BindCorsServices("plain").RequireCors("connect");
app.BindCorsServices("extras").RequireCors("connect-extras");

// A plain GET endpoint carrying the same policy, purely so the GET half of the policy can be checked
// against a route that actually accepts GET. It is NOT a Connect endpoint, and that is the point: the
// code-first generator never marks a method idempotent (it always emits ConnectMethod(..., idempotent:
// false)), so no code-first method is bound for GET and Connect GET cannot be exercised from here at
// all. See notes/findings.md - that is a gap in the generator, not in this policy.
app.MapGet("/probe", () => "ok").RequireCors("connect");

await app.StartAsync();

var address = $"http://127.0.0.1:{port}";
try
{
    using var http = new HttpClient();

    await Check("preflight allows the Connect protocol headers", async () =>
    {
        var response = await Preflight(http, "plain", "POST",
            "content-type,connect-protocol-version,connect-timeout-ms");
        Require(Allowed(response), "the preflight is allowed");
        var allowed = Header(response, "Access-Control-Allow-Headers");
        foreach (var header in ConnectCors.AllowedHeaders)
        {
            Require(allowed.Contains(header, StringComparison.OrdinalIgnoreCase),
                $"'{header}' is allowed; got '{allowed}'");
        }
        return allowed;
    });

    await Check("the policy allows GET as well as POST, which Connect GET needs", async () =>
    {
        // against /probe rather than a Connect route: a preflight only gets a policy applied if some
        // endpoint matches the requested method, and no code-first Connect method is bound for GET
        using var request = new HttpRequestMessage(HttpMethod.Options, $"{address}/probe");
        request.Headers.Add("Origin", Origin);
        request.Headers.Add("Access-Control-Request-Method", "GET");
        using var response = await http.SendAsync(request);

        Require(Allowed(response), "the preflight is allowed");
        var methods = Header(response, "Access-Control-Allow-Methods");
        Require(methods.Contains("GET", StringComparison.OrdinalIgnoreCase), $"GET is allowed; got '{methods}'");
        Require(methods.Contains("POST", StringComparison.OrdinalIgnoreCase), $"POST is allowed; got '{methods}'");
        return methods;
    });

    await Check("NEGATIVE: an application header the policy does not list is NOT echoed back", async () =>
    {
        // The first of the two caveats on ConnectCors: your own headers are yours to add. This is also
        // the control for every check above - a policy that allowed everything would fail here.
        //
        // Note WHAT is asserted. ASP.NET Core answers the preflight 204 WITH Access-Control-Allow-Origin
        // either way, and simply omits the header it will not allow; nothing on the server errors, logs
        // or 4xxs. It is the BROWSER that then refuses to send the real request. So a misconfigured
        // policy is invisible from the server side - which is the whole reason this harness exists, and
        // why the first version of this check asserted the wrong thing and failed.
        var response = await Preflight(http, "plain", "POST", "content-type,authorization");
        var allowed = Header(response, "Access-Control-Allow-Headers");
        Require(!allowed.Contains("Authorization", StringComparison.OrdinalIgnoreCase),
            $"'Authorization' is NOT echoed; got '{allowed}'");
        Require(Allowed(response), "...while the preflight still answers with Allow-Origin, which is the trap");
        return $"HTTP {(int)response.StatusCode}, Authorization absent from '{allowed}'";
    });

    await Check("...and is allowed once the policy names it", async () =>
    {
        var response = await Preflight(http, "extras", "POST", "content-type,authorization");
        Require(Allowed(response), "the preflight is allowed");
        return Header(response, "Access-Control-Allow-Headers");
    });

    await Check("NEGATIVE: another origin is refused", async () =>
    {
        var response = await Preflight(http, "plain", "POST", "content-type", origin: OtherOrigin);
        Require(!Allowed(response), "the preflight is NOT allowed");
        return $"HTTP {(int)response.StatusCode}, no Allow-Origin";
    });

    await Check("an actual call carries Allow-Origin and exposes the gRPC status headers", async () =>
    {
        var response = await Call(http, "plain", "SayHello");
        Require(response.IsSuccessStatusCode, $"the call succeeds, was {(int)response.StatusCode}");
        Require(Allowed(response), "Access-Control-Allow-Origin is present");
        var exposed = Header(response, "Access-Control-Expose-Headers");
        foreach (var header in ConnectCors.ExposedHeaders)
        {
            Require(exposed.Contains(header, StringComparison.OrdinalIgnoreCase),
                $"'{header}' is exposed; got '{exposed}'");
        }
        return exposed;
    });

    await Check("THE TRAP: unary trailing metadata is a trailer- header, and is NOT exposed by default", async () =>
    {
        // CORS has no prefix wildcard, so `trailer-x-audit` has to be named. A browser client sees the
        // call succeed and the metadata silently missing - which is why this is documented rather than
        // left to be discovered
        var response = await Call(http, "plain", "WithTrailer");
        Require(response.IsSuccessStatusCode, $"the call succeeds, was {(int)response.StatusCode}");
        Require(response.Headers.Contains("trailer-x-audit"), "the server really did send it");
        var exposed = Header(response, "Access-Control-Expose-Headers");
        Require(!exposed.Contains("trailer-x-audit", StringComparison.OrdinalIgnoreCase),
            $"it is NOT exposed; got '{exposed}'");
        return "sent, not exposed";
    });

    await Check("...and is exposed once the policy names it", async () =>
    {
        var response = await Call(http, "extras", "WithTrailer");
        var exposed = Header(response, "Access-Control-Expose-Headers");
        Require(exposed.Contains("trailer-x-audit", StringComparison.OrdinalIgnoreCase),
            $"it is exposed; got '{exposed}'");
        return exposed;
    });
}
finally
{
    await app.StopAsync();
}

foreach (var failure in failures) Console.Error.WriteLine("FAILED: " + failure);
Console.WriteLine(failures.Count == 0
    ? "ConnectCors: the browser preflight a Connect client sends is answered - all checks pass"
    : $"ConnectCors: {failures.Count} FAILURES");
return failures.Count == 0 ? 0 : 1;

async Task<HttpResponseMessage> Preflight(HttpClient http, string prefix, string method,
    string requestHeaders, string origin = Origin)
{
    using var request = new HttpRequestMessage(HttpMethod.Options,
        $"{address}/{prefix}/cors.v1.Greeter/SayHello");
    request.Headers.Add("Origin", origin);
    request.Headers.Add("Access-Control-Request-Method", method);
    request.Headers.Add("Access-Control-Request-Headers", requestHeaders);
    return await http.SendAsync(request);
}

async Task<HttpResponseMessage> Call(HttpClient http, string prefix, string method)
{
    using var request = new HttpRequestMessage(HttpMethod.Post,
        $"{address}/{prefix}/cors.v1.Greeter/{method}")
    {
        // HelloRequest { Name = "x" }
        Content = new ByteArrayContent([0x0a, 0x01, 0x78])
        {
            Headers = { ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/proto") },
        },
    };
    request.Headers.Add("Origin", Origin);
    return await http.SendAsync(request);
}

static bool Allowed(HttpResponseMessage response)
    => response.Headers.Contains("Access-Control-Allow-Origin");

static string Header(HttpResponseMessage response, string name)
    => response.Headers.TryGetValues(name, out var values) ? string.Join(",", values) : "";

async Task Check(string name, Func<Task<string>> check)
{
    try
    {
        var detail = await check();
        Console.WriteLine($"  pass  {name}  -> {detail}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"  FAIL  {name}  -> {ex.GetType().Name}: {ex.Message}");
        failures.Add(name);
    }
}

static void Require(bool condition, string what)
{
    if (!condition) throw new InvalidOperationException(what);
}

static int FreePort()
{
    using var probe = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
    probe.Start();
    var port = ((IPEndPoint)probe.LocalEndpoint).Port;
    probe.Stop();
    return port;
}
