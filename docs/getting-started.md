Getting started, code-first
-

# Getting started: code-first

The contract is an ordinary [protobuf-net.Grpc](https://protobuf-net.github.io/protobuf-net.Grpc/)
service contract. Nothing about it is Connect-specific, and **the same interface still serves gRPC** —
simultaneously, from the same host, if you want.

## 1. The contract

```csharp
using ProtoBuf;
using ProtoBuf.Grpc;
using ProtoBuf.Grpc.Configuration;

// The wire name is pinned rather than derived. [Service] with no name gives
// "{namespace}.{name-without-I}", which is fine inside .NET but is not a name another language's
// schema would have chosen - so pin it if anything else will ever call this.
[Service("mypackage.v1.Greeter")]
public interface IGreeter
{
    Task<HelloReply> SayHelloAsync(HelloRequest request, CallContext context = default);

    IAsyncEnumerable<HelloReply> Subscribe(HelloRequest request, CallContext context = default);

    Task<HelloReply> CollectAsync(IAsyncEnumerable<HelloRequest> requests, CallContext context = default);

    IAsyncEnumerable<HelloReply> Chat(IAsyncEnumerable<HelloRequest> requests, CallContext context = default);
}

[ProtoContract]
public class HelloRequest
{
    [ProtoMember(1)] public string? Name { get; set; }
}

[ProtoContract]
public class HelloReply
{
    [ProtoMember(1)] public string? Message { get; set; }
}
```

All four method shapes are supported. Only `Chat` — full-duplex bidirectional — requires HTTP/2 at
call time; the other three work over HTTP/1.1.

## 2. The implementation

An ordinary class. `CallContext` gives you deadlines, cancellation and metadata, exactly as under
gRPC.

```csharp
public class GreeterService : IGreeter
{
    public Task<HelloReply> SayHelloAsync(HelloRequest request, CallContext context)
        => Task.FromResult(new HelloReply { Message = $"hello {request.Name}" });

    public async IAsyncEnumerable<HelloReply> Subscribe(HelloRequest request, CallContext context)
    {
        for (var i = 1; i <= 3; i++)
        {
            yield return new HelloReply { Message = $"hello {request.Name} #{i}" };
            await Task.Delay(100, context.CancellationToken);
        }
    }

    // ...
}
```

## 3. Two declarations

One for the serializers, one for the transport:

```csharp
using ProtoBuf;
using ProtoBuf.Connect;
using ProtoBuf.Meta;

// compile-time serializers; everything reachable from the services below is pulled in automatically
[ProtoModel]
public partial class MyModel : TypeModel { }

[ProtoConnect(Model = typeof(MyModel))]
[ProtoService(typeof(IGreeter), typeof(GreeterService))]
public static partial class MyServices { }
```

`[ProtoModel]` and `[ProtoSerializable]` are `[Experimental("PBN9001")]`, so add
`<NoWarn>$(NoWarn);PBN9001</NoWarn>` to opt in.

`static` on the container is optional and **changes what is generated**: declared `static`, the
registration and binding methods arrive as *extension methods* (as used below); declared non-static
they are plain statics plus a private constructor. The accessibility you write governs the generated
surface too.

## 4. The server

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMyServices();   // generated: registers the codecs and the implementations

var app = builder.Build();
app.BindMyServices();               // generated: one endpoint per method
app.Run();
```

Each method becomes **its own endpoint**, so `[Authorize]`, CORS policies, rate limiting and output
caching attach per RPC — ASP.NET Core resolves all of those from the matched endpoint, in middleware
that runs before any handler. Write them on your implementation, as you would under gRPC:

```csharp
public class GreeterService : IGreeter
{
    [Authorize(Policy = "admins")]
    public Task<HelloReply> SayHelloAsync(HelloRequest request, CallContext context) => ...

    [AllowAnonymous]
    public IAsyncEnumerable<HelloReply> Subscribe(HelloRequest request, CallContext context) => ...
}
```

**Nothing reflects to find those.** `Grpc.AspNetCore.Server` discovers them by reflecting over your
implementation at startup; here they are reconstructed at *build* time, and the generator emits the
constructor calls into the binding. The list and its order match what gRPC computes, so a contract
served over both transports gets the same answer from each — attributes on the contract interface
count too, most-specific last, which is what lets a per-method `[AllowAnonymous]` beat a class-level
`[Authorize]`.

Your own attributes are carried as well as the framework's, so `endpoint.Metadata.GetMetadata<T>()`
finds them.

> **Version note.** This comes from protobuf-net's build-time tooling rather than from this package,
> so it needs a protobuf-net **newer than 3.4.28** — the release that first carried the Connect
> generator. On 3.4.28 itself the attributes are not carried at all; until you upgrade, put the policy
> on the binding with `.RequireAuthorization(...)` as below.

### When an attribute cannot be reconstructed

An attribute the generated file cannot *name* — one `internal` to another assembly, say — cannot be
constructed either, and that is **PBN5008**. There is no fallback, because nothing on this path
reflects: that operation is bound with **no metadata at all**, so an `[Authorize]` on it would not be
honoured. It is reported per operation, and the rest of the service is unaffected.

The failure is silent by nature — the build succeeds, the service answers, and the only difference is
that anybody may call it — so this is the rule here worth escalating:

```xml
<WarningsAsErrors>$(WarningsAsErrors);PBN5008</WarningsAsErrors>
```

Either make the attribute constructible from your assembly, or attach the policy to the binding
instead — `app.BindMyServices().RequireAuthorization("admins")` for every endpoint in the container,
or `app.BindGreeter().RequireAuthorization("admins")` for one service (a `Bind{Contract}` extension is
generated per contract alongside the container-wide one).

`BindMyServices` takes an optional routing prefix (`app.BindMyServices("connect")`), which is what
lets Connect share a host with gRPC: the two use identical paths otherwise, and ASP.NET Core will not
route two endpoints to one pattern.

## 5. The client

```csharp
using var http = new HttpClient();
var channel = new ConnectChannel(http, new ProtoConnectCodec(MyModel.Instance), new Uri(address));

IGreeter client = MyServices.CreateClient<IGreeter>(channel);

var reply = await client.SayHelloAsync(new HelloRequest { Name = "Marc" });

await foreach (var item in client.Subscribe(new HelloRequest { Name = "Marc" }))
{
    Console.WriteLine(item.Message);
}
```

Both halves are generated at build time; the serializers come from the `[ProtoModel]`. Nothing is
emitted at runtime and nothing is reflected over, which is what makes the whole thing publish cleanly
under `PublishAot`.

## Options

`ConnectChannel` takes a few optional arguments:

```csharp
new ConnectChannel(http, codec, baseAddress,
    httpVersion: HttpVersion.Version20,          // default: whatever HttpClient negotiates
    compression: ConnectCompression.Gzip,        // gzip, br, deflate, or identity
    useGet: true);                               // GET for side-effect-free unary methods
```

`useGet` only applies to methods declared side-effect-free; anything else silently uses POST, because
quietly not caching is a far better failure than quietly exposing a side-effecting method to
prefetchers.

## Errors

Throw `RpcException` and the code, message and details arrive at the caller as a Connect error:

```csharp
throw new RpcException(new Status(StatusCode.PermissionDenied, "'mallory' may not greet."));
```

```csharp
try
{
    await client.SayHelloAsync(request);
}
catch (ConnectException ex) when (ex.Code == ConnectCode.PermissionDenied)
{
    // ex.RawMessage, ex.HttpStatus, ex.Headers, ex.Trailers, ex.Details
}
```

Rich errors travel the way a gRPC service already sends them — a `google.rpc.Status` in a
`grpc-status-details-bin` trailer — so a service that already uses them keeps them.
