Contract-first
-

# Contract-first: your existing `.proto`, unchanged

If you already have a `.proto` and `Grpc.Tools`, there is **no generator to add, no attribute to
write, and no change to your contracts**. `protoc` already emits everything needed.

This is the path to take if your schema is the source of truth, or if your messages are
`Google.Protobuf` types rather than protobuf-net contracts.

## The server

The whole opt-in is two lines:

```csharp
using ProtoBuf.Connect;
using ProtoBuf.Connect.AspNetCore;

builder.Services.AddConnect(o => o.Codecs.Add(MarshallerConnectCodec.Instance));
builder.Services.AddScoped<GreeterImpl>();          // your existing Greeter.GreeterBase subclass

var app = builder.Build();
app.MapConnectService<GreeterImpl>(Greeter.BindService);   // protoc's own method group
app.Run();
```

`Greeter.BindService` is the method `protoc` generated. Nothing here reflects: the service's methods
and marshallers are read from that descriptor at startup, and the handlers are resolved per call from
DI.

`MarshallerConnectCodec` carries no marshalling of its own — it exists to *name* the codec, since the
name is what selects it from a content-type. The actual encoding comes from the `Marshaller<T>` each
generated method already carries.

### JSON

Add the Google codec and the same service also speaks `application/json`, in the canonical protobuf
JSON mapping:

```csharp
builder.Services.AddConnect(o =>
{
    o.Codecs.Add(MarshallerConnectCodec.Instance);
    o.Codecs.Add(GoogleJsonConnectCodec.Instance);   // protobuf-net.Connect.Google
});
```

See [JSON](json) for what that mapping is and why it is not "some JSON".

## The client

`protoc`'s generated client, with a different `CallInvoker` underneath it:

```csharp
using var http = new HttpClient();
var client = new Greeter.GreeterClient(new ConnectCallInvoker(http, new Uri(address)));

var reply = await client.SayHelloAsync(new HelloRequest { Name = "Marc" });
```

`RpcException`, `ServerCallContext`, `IServerStreamWriter<T>`, `IAsyncStreamReader<T>`, leading and
trailing metadata, and deadlines all behave as they do under gRPC.

**Nothing is re-encoded.** For `application/proto`, a Connect body and a gRPC body are the same bytes,
and the marshallers `protoc` generated are used unchanged.

## One thing to know about authorization

> ⚠️ **`[Authorize]` is not inferred on this path** — and only on this one.
>
> `Grpc.AspNetCore.Server` collects endpoint metadata by reflecting over your implementation type.
> This deliberately does not reflect — that is what makes it AOT-safe — so an authorization attribute
> on your service class would be **silently dropped**, leaving a more permissive endpoint with no
> error anywhere.

The [code-first path](getting-started) does carry them, because there is a generator in it: the
attributes are reconstructed at build time and emitted into the binding. Here there is no generator —
`MapConnectService` is an ordinary library call and your implementation type reaches it as a runtime
type argument, so reading its attributes *is* the reflection that was removed. Hence the explicit
forms below.

Two ways to say it explicitly:

```csharp
// chain the convention
app.MapConnectService<GreeterImpl>(Greeter.BindService)
   .RequireAuthorization("my-policy");

// or pass endpoint metadata directly
app.MapConnectService<GreeterImpl>(Greeter.BindService,
    metadata: [new AuthorizeAttribute("my-policy")]);
```

The analyzer **PBN5007** warns when your implementation carries an authorization attribute and the
call supplies neither. It is the one rule here worth escalating:

```xml
<WarningsAsErrors>$(WarningsAsErrors);PBN5007</WarningsAsErrors>
```

## Idempotency, and GET

A method declared `option idempotency_level = NO_SIDE_EFFECTS;` can be served over `GET` as well as
`POST` — an ordinary cacheable HTTP request, servable from a CDN or a browser cache without reaching
the server. That is the one thing Connect does which gRPC structurally cannot.

The level reaches the generated `ServiceDescriptor` but not the `Method<,>` the binder sees, so it is
passed in explicitly:

```csharp
app.MapConnectService<GreeterImpl>(Greeter.BindService,
    isIdempotent: GoogleIdempotency.For(Greeter.Descriptor));
```

Only `NO_SIDE_EFFECTS` qualifies. `IDEMPOTENT` means "safe to retry", which is weaker than "safe to
cache and prefetch" — a delete is idempotent and must emphatically not be a `GET`.

The code-first equivalent is `[NoSideEffects]` on the contract method; see
[getting started](getting-started#cacheable-calls-nosideeffects).
