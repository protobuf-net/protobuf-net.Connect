# protobuf-net.Connect.AspNetCore

**Experimental.** Serves the [Connect protocol](https://connectrpc.com) from ASP.NET Core.

**Documentation: [connect.protobuf-net.dev](https://connect.protobuf-net.dev)**

```csharp
builder.Services.AddMyServices();   // generated from [ProtoConnect]
var app = builder.Build();
app.BindMyServices();               // one endpoint per method
```

All four method shapes: unary, client-streaming, server-streaming and bidirectional. Binds to
`HttpContext` and endpoint routing rather than to Kestrel, so Kestrel, HTTP.sys, IIS and `TestServer`
all work from one implementation.

Each method becomes **its own endpoint**, so `[Authorize]`, CORS policies, rate limiting and output
caching attach per RPC — ASP.NET Core resolves those from the matched endpoint, in middleware that
runs before any handler.

**Only full-duplex bidirectional streaming needs HTTP/2.** Everything else — including *half*-duplex
bidi — works over plaintext HTTP/1.1, because Connect carries trailing metadata in the body rather
than in HTTP trailers. That is the operational argument for Connect over gRPC.

A contract-first service needs no generator and no attribute:

```csharp
builder.Services.AddConnect(o => o.Codecs.Add(MarshallerConnectCodec.Instance));
app.MapConnectService<GreeterImpl>(Greeter.BindService);   // protoc's own method group
```

⚠️ On that path `[Authorize]` is **not** inferred — this deliberately does not reflect. Chain
`.RequireAuthorization(...)` or pass `metadata`; the analyzer **PBN5007** warns if you do neither.
