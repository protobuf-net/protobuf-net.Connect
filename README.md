# protobuf-net.Connect

**Experimental.** A **.NET implementation** of the [Connect protocol](https://connectrpc.com) — the
cross-language protocol with implementations for Go, TypeScript, Swift and others — built for AOT
from the start: no ref-emit, no runtime marshaller lookup, no reflection on any serving or calling
path. Nothing here is .NET-specific on the wire; these services and clients interoperate with every
other Connect implementation.

### 📖 Documentation: **[connect.protobuf-net.dev](https://connect.protobuf-net.dev)**

[Getting started](https://connect.protobuf-net.dev/getting-started) ·
[Contract-first](https://connect.protobuf-net.dev/contract-first) ·
[JSON](https://connect.protobuf-net.dev/json) ·
[Performance](https://connect.protobuf-net.dev/performance)

Connect is gRPC's wire semantics over ordinary HTTP. The difference that matters operationally:
Connect puts trailing metadata **in the body** rather than in HTTP trailers — and HTTP trailers are
the only reason gRPC insists on HTTP/2.

So unary, client-streaming and server-streaming all work over **HTTP/1.1**, through the proxies, CDNs,
load balancers and corporate middleboxes that will not carry gRPC. Only full-duplex bidirectional
streaming still needs HTTP/2, for the reason it always did: interleaving two bodies is not something
HTTP/1.1 can express.

A unary request is also just an HTTP POST with a protobuf body, so `curl` works:

```bash
curl --http1.1 -H 'Content-Type: application/proto' \
     --data-binary @request.bin http://localhost:5000/mypackage.v1.Greeter/SayHello
```

## Performance

One `[Service]` contract, one implementation, one Kestrel, one DI container, one protobuf-net model
marshalling for **both** sides — so the only variable is the protocol. 32 workers, 5s per scenario,
256 B payload, .NET 8, 24 cores, client and server in one process. Reproduce with
`dotnet run -c Release --project benchmarks/ConnectLoad`.

| scenario | ops/sec | p50 | p99 |
| --- | ---: | ---: | ---: |
| unary, gRPC over HTTP/2 | 98,700 | 314µs | 606µs |
| unary, **Connect** over HTTP/2 | **160,900** | 190µs | 343µs |
| unary, Connect over HTTP/1.1 | 327,100 | 93µs | 160µs |
| stream ×10, gRPC over HTTP/2 | 134,300 | 229µs | 407µs |
| stream ×10, **Connect** over HTTP/2 | **138,900** | 222µs | 387µs |
| stream ×10, Connect over HTTP/1.1 | 244,300 | 126µs | 206µs |

Read honestly, that says three things:

- **Streaming is parity.** ~3%, barely outside the run-to-run spread. The two framings cost the same.
- **Unary, Connect is ~1.6×** — but that is mostly a *gRPC-unary cost* rather than a Connect win. A
  single-message server-*streaming* gRPC call runs at 162k against 104k for a gRPC unary call carrying
  identical data; Connect shows no such gap (unary 164k, one-message stream 160k). Something in
  grpc-dotnet's unary path costs ~60%. Observed and reproducible, **not diagnosed**.
- **The HTTP/1.1 row is not a protocol result.** At this concurrency `HttpClient` gives HTTP/1.1 a
  socket per worker while HTTP/2 multiplexes onto one, so it measures connection parallelism as much
  as anything. What it shows without qualification is that the option *exists* — gRPC cannot serve
  that port at all.

Client and server share one process and one set of cores, so none of these are capacity estimates;
they are fair *between the rows*, which is the comparison being made.

At the codec layer, against the same message: binary writes in 338 ns with **zero allocation**, JSON
in 484 ns / 264 B. See `notes/findings.md` §56–§57 for the full measurements and their traps.

## Two ways in

### 1. Code-first — your existing protobuf-net.Grpc contracts

The contract is the one you already have. Nothing about it is Connect-specific, and **the same
interface still serves gRPC** — simultaneously, from the same host, if you want.

```csharp
[Service("mypackage.v1.Greeter")]
public interface IGreeter
{
    Task<HelloReply> SayHelloAsync(HelloRequest request, CallContext context = default);
    IAsyncEnumerable<HelloReply> Subscribe(HelloRequest request, CallContext context = default);
    Task<HelloReply> CollectAsync(IAsyncEnumerable<HelloRequest> requests, CallContext context = default);
    IAsyncEnumerable<HelloReply> Chat(IAsyncEnumerable<HelloRequest> requests, CallContext context = default);
}
```

Declare a container, and the generator writes the bindings and the client factory:

```csharp
[ProtoConnect(Model = typeof(MyModel))]
[ProtoService(typeof(IGreeter), typeof(GreeterService))]
public static partial class MyServices { }
```

```csharp
// server
builder.Services.AddMyServices();   // generated: registers the codecs and the implementations
var app = builder.Build();
app.BindMyServices();               // generated: one endpoint per method

// client
var channel = new ConnectChannel(httpClient, new ProtoConnectCodec(MyModel.Instance), new Uri(address));
IGreeter client = MyServices.CreateClient<IGreeter>(channel);
var reply = await client.SayHelloAsync(new HelloRequest { Name = "Marc" });
```

Both halves are generated at build time; the serializers come from a `[ProtoModel]`. Nothing is
emitted at runtime and nothing is reflected over.

### 2. Contract-first — your existing `protoc`-generated service, unchanged

If you already have a `.proto` and `Grpc.Tools`, there is **no generator, no attribute and no change
to your contracts**.

```csharp
// server - the whole opt-in
builder.Services.AddConnect(o => o.Codecs.Add(MarshallerConnectCodec.Instance));
builder.Services.AddScoped<GreeterImpl>();
app.MapConnectService<GreeterImpl>(Greeter.BindService);   // protoc's own method group

// client - protoc's generated client, with a different CallInvoker under it
var client = new Greeter.GreeterClient(new ConnectCallInvoker(httpClient, new Uri(address)));
```

`RpcException`, `ServerCallContext`, `IServerStreamWriter<T>`, `IAsyncStreamReader<T>`, metadata and
deadlines all behave as they do under gRPC. Nothing is re-encoded: for `application/proto` a Connect
body and a gRPC body are the same bytes.

> ⚠️ **`[Authorize]` is not inferred on this path** — and only on this one. `Grpc.AspNetCore.Server`
> collects endpoint metadata by reflecting over your implementation; this deliberately does not
> reflect, so an authorization attribute would be silently dropped. Chain `.RequireAuthorization(...)`,
> or pass the `metadata` argument. The analyzer **PBN5007** warns when your implementation carries an
> authorization attribute and the call supplies neither — the one rule here worth escalating with
> `<WarningsAsErrors>PBN5007</WarningsAsErrors>`.
>
> The **code-first** path does carry them: there is a generator in it, so the attributes are
> reconstructed at build time and emitted into the binding. Nothing reflects either way.

## JSON

Both paths speak the **canonical protobuf JSON mapping** — not "some JSON". Contract-first goes
through Google's own `JsonFormatter`/`JsonParser` (`protobuf-net.Connect.Google`); code-first is
generated from the same `[ProtoModel]` plan as the binary serializer, over `Utf8JsonWriter`, and is
verified field-by-field against Google's formatter across 106 differential cases.

The code-first path has no transcode, which shows: **2.6× faster to write and 11× less garbage** than
the contract-first path, which goes bytes → `string` → bytes.

## Conformance

Measured against [`connectrpc/conformance`](https://github.com/connectrpc/conformance) v1.0.5, over
HTTP/1.1 and HTTP/2, both codecs, all compressions, and Connect GET — **both directions pass in full**:

| mode | result |
| --- | --- |
| **server** | **1492 / 1492** |
| **client** | **1700 / 1700** |

Server mode drives a real Connect client against our server; client mode drives our client against the
suite's reference server. Between them there is no step where both ends are ours — which matters,
because every defect it has found was a place where both of our own ends agreed with each other and
neither agreed with the protocol.

Compression is `gzip`, `br` and `deflate`, all in-box. Declared unsupported and therefore not counted:
`zstd`, `snappy`, TLS client certificates.

## Native AOT

Every smoke test here is published with `PublishAot` and **run**, not merely published. No warning in
any of them names this library's code.

## Status

Experimental, and versioned `0.1-alpha` to say so. The protocol surface is complete — all four method
shapes, both codecs, compression, deadlines, metadata, errors with details, and Connect GET — and the
remaining gaps are recorded rather than hidden in `notes/findings.md`:

- auto-tuple contracts have no code-first JSON mapping yet (refused with a diagnostic, not silently);
- endpoint metadata inference for contract-first (see the warning above);
- `zstd`, `snappy`, TLS client certificates.

## Layout

**`src/` is the published surface and nothing else is** — a project's folder decides whether it packs,
rather than a property someone has to remember.

| `src/` | what it is |
| --- | --- |
| `protobuf-net.Connect` | client, codecs, framing, the call invoker |
| `protobuf-net.Connect.AspNetCore` | server, on endpoint routing — Kestrel, HTTP.sys, IIS or TestServer |
| `protobuf-net.Connect.Google` | canonical protobuf JSON for contract-first messages |

| `tests/` | what it proves |
| --- | --- |
| `ConnectConformance` | the conformance harness, both modes |
| `ConnectJsonDifferential` | code-first JSON against Google's formatter, 106 cases |
| `ConnectContractFirst` | the contract-first sample, and its 27 checks |
| `AotConnectSmoke` | the code-first smoke test, native-AOT published and run |
| `AotConnectJsonSmoke` | code-first JSON under native AOT |
| `AotDualHostSmoke` | one contract over gRPC **and** Connect, one host |
| `ConnectProbe` | live probes against connect-go's reference server |

| `benchmarks/` | what it measures |
| --- | --- |
| `ConnectLoad` | the throughput comparison above |
| `ConnectBenchmark` | codec microbenchmarks |

## Building

```bash
dotnet build Build.csproj
dotnet test Build.csproj
```

The build-time generators live in **protobuf-net.BuildTools**, which stays in the
[protobuf-net](https://github.com/protobuf-net/protobuf-net) repository and is not a package of its
own — it ships inside `protobuf-net.Core` as analyzer assets, so the ordinary package reference brings
it along. Nothing else is needed.

To develop a generator change alongside this runtime, point the build at a protobuf-net checkout:

```bash
dotnet build Build.csproj -p:ProtoBufSourcePath=../protobuf-net
```

That swaps every protobuf-net package reference for a project reference, including the tooling. See
`Directory.Build.targets`. A package **packed in that mode pins its protobuf-net dependency to the
local build's version**, so it is for development only — the build warns, and `release.yml` refuses
outright.

## Licence

Apache-2.0 — see [Licence.txt](Licence.txt).
