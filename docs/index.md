protobuf-net.Connect
-

# What is protobuf-net.Connect?

It is the [Connect protocol](https://connectrpc.com) for .NET — gRPC's wire semantics over ordinary
HTTP — built for native AOT from the start. No ref-emit, no runtime marshaller lookup, and no
reflection on any serving or calling path.

The difference that matters operationally: **Connect puts trailing metadata in the body** rather than
in HTTP trailers, and HTTP trailers are the only reason gRPC insists on HTTP/2. So unary,
client-streaming and server-streaming all work over **HTTP/1.1** — through the proxies, CDNs, load
balancers and corporate middleboxes that will not carry gRPC. Only full-duplex bidirectional streaming
still needs HTTP/2, for the reason it always did.

A unary request is also just an HTTP POST with a protobuf body, so `curl` works:

```bash
curl --http1.1 -H 'Content-Type: application/proto' \
     --data-binary @request.bin http://localhost:5000/mypackage.v1.Greeter/SayHello
```

> **Experimental.** The protocol surface is complete and passes the full conformance suite in both
> directions, but the packages are `0.1-alpha` and the API may move.

## Install

```bash
dotnet add package protobuf-net.Connect              # client
dotnet add package protobuf-net.Connect.AspNetCore   # server
dotnet add package protobuf-net.Connect.Google       # JSON for contract-first messages
```

## Topics

- [Getting started, code-first](getting-started) — your existing protobuf-net.Grpc contracts,
  unchanged
- [Contract-first](contract-first) — your existing `.proto` and `protoc`-generated service, unchanged
- [JSON](json) — the canonical protobuf JSON mapping, on both paths
- [Performance](performance) — measured against gRPC on the same stack

## Two ways in, in one screen

**Code-first** — the contract is the one you already have, and the same interface still serves gRPC:

```csharp
[Service("mypackage.v1.Greeter")]
public interface IGreeter
{
    Task<HelloReply> SayHelloAsync(HelloRequest request, CallContext context = default);
}

[ProtoConnect(Model = typeof(MyModel))]
[ProtoService(typeof(IGreeter), typeof(GreeterService))]
public static partial class MyServices { }
```

```csharp
builder.Services.AddMyServices();   // generated
app.BindMyServices();               // generated
```

**Contract-first** — no generator, no attribute, no change to your contracts:

```csharp
builder.Services.AddConnect(o => o.Codecs.Add(MarshallerConnectCodec.Instance));
app.MapConnectService<GreeterImpl>(Greeter.BindService);   // protoc's own method group
```

## Conformance

Measured against [`connectrpc/conformance`](https://github.com/connectrpc/conformance) v1.0.5, over
HTTP/1.1 and HTTP/2, both codecs, all compressions, and Connect GET:

| mode | result |
| --- | --- |
| server | **1492 / 1492** |
| client | **1700 / 1700** |

Server mode drives a real Connect client against our server; client mode drives our client against the
suite's reference server. Between them there is no step where both ends are ours.

## Related

- [protobuf-net](https://docs.protobuf-net.dev) — the serializer, and the build-time tooling this
  depends on
- [protobuf-net.Grpc](https://protobuf-net.github.io/protobuf-net.Grpc/) — the code-first gRPC stack
  whose contracts this reuses unchanged
- [source](https://github.com/protobuf-net/protobuf-net.Connect)
