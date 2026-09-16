CORS
-

# CORS: calling from a browser

Browser reach is a large part of why Connect exists. A unary call is an ordinary HTTP POST, so
`connect-es` can call this server directly where gRPC cannot — but only if the preflight allows the
protocol's own headers.

```csharp
using ProtoBuf.Connect.AspNetCore;

builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
    .WithOrigins("https://app.example.com")
    .WithConnect()));

var app = builder.Build();
app.UseCors();
app.BindMyServices();
```

`WithConnect()` applies the methods, allowed request headers and exposed response headers a Connect
client needs. The lists mirror [`connectrpc/cors`](https://github.com/connectrpc/cors-go), the
reference answer every other implementation's documentation points at, so a policy copied from any
Connect guide works here unchanged:

| | |
| --- | --- |
| methods | `POST`, `GET` |
| allowed headers | `Content-Type`, `Connect-Protocol-Version`, `Connect-Timeout-Ms`, `Grpc-Timeout`, `X-Grpc-Web`, `X-User-Agent` |
| exposed headers | `Grpc-Status`, `Grpc-Message`, `Grpc-Status-Details-Bin` |

They are also available as `ConnectCors.Methods`, `ConnectCors.AllowedHeaders` and
`ConnectCors.ExposedHeaders` if you would rather compose a policy yourself.

The `Grpc-*` entries are gRPC-web's, not Connect's — a Connect error carries its code and details in
the *body*. They cost nothing and are what a consumer who later adds gRPC-web would need.

## Two things it cannot do for you

Both are ordinary CORS rather than anything Connect-specific, and both fail **silently**.

### Your own headers

`Authorization`, an API key, a tenant header — none are protocol headers, so none are in the list:

```csharp
p.WithOrigins("https://app.example.com").WithConnect().WithHeaders("Authorization");
```

### Trailing metadata

On a unary call this server writes trailing metadata as **`trailer-` prefixed response headers** —
that is how Connect avoids HTTP trailers, and so avoids requiring HTTP/2. CORS has no prefix wildcard,
so each one has to be exposed *by name*:

```csharp
p.WithOrigins("https://app.example.com").WithConnect().WithExposedHeaders("trailer-x-audit");
```

Miss it and the call succeeds, the server really does send the header, and the client sees nothing.

## Why this is hard to diagnose

**A misconfigured policy is invisible from the server side.** ASP.NET Core answers the preflight
`204` *with* `Access-Control-Allow-Origin` whether or not it allows the headers the browser asked
about — it simply omits the ones it will not allow. Nothing errors, logs or returns 4xx. The browser
then refuses to send the real request, and reports it as an opaque network error.

So the server's logs will tell you nothing, and neither will a `curl` that does not send `Origin`.
Check `Access-Control-Allow-Headers` on the preflight response itself:

```bash
curl -i -X OPTIONS http://localhost:5000/mypackage.v1.Greeter/SayHello \
  -H 'Origin: https://app.example.com' \
  -H 'Access-Control-Request-Method: POST' \
  -H 'Access-Control-Request-Headers: content-type,connect-protocol-version'
```

`tests/ConnectCors` in the repository asserts all of the above against a real host, including the
negative cases.

## Connect GET

A side-effect-free method can be served over `GET`, which is why `GET` is in the allowed methods. Note
that a Connect GET **bypasses preflight entirely** — it sets no request headers — so it needs only
`Access-Control-Allow-Origin` on the response.

> ⚠️ Connect GET is currently reachable on the **contract-first** path only; see
> [contract-first](contract-first#idempotency-and-get). The code-first generator does not yet mark a
> method side-effect-free, so `useGet` has nothing to act on there.
