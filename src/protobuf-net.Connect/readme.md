# protobuf-net.Connect

**Experimental.** A **.NET implementation** of the [Connect protocol](https://connectrpc.com)
client — Connect being the cross-language protocol with implementations for Go, TypeScript, Swift and
others — built for native AOT from the start: no ref-emit, no runtime marshaller lookup, no
reflection on any calling path. It calls any conforming Connect server, whatever it is written in.

**Documentation: [connect.protobuf-net.dev](https://connect.protobuf-net.dev)**

Connect is gRPC's wire semantics over ordinary HTTP. Because it carries trailing metadata in the body
rather than in HTTP trailers, unary and the one-way streaming shapes all work over **HTTP/1.1** —
through proxies, CDNs and middleboxes that will not carry gRPC.

```csharp
using var http = new HttpClient();
var channel = new ConnectChannel(http, new ProtoConnectCodec(MyModel.Instance), new Uri(address));

IGreeter client = MyServices.CreateClient<IGreeter>(channel);
var reply = await client.SayHelloAsync(new HelloRequest { Name = "Marc" });
```

The contract is an ordinary [protobuf-net.Grpc](https://protobuf-net.github.io/protobuf-net.Grpc/)
`[Service]` interface — the same one still serves gRPC. Proxies and serializers are generated at
build time from `[ProtoConnect]` and `[ProtoModel]`.

Speaks binary protobuf and the canonical protobuf JSON mapping. Passes the
[connectrpc/conformance](https://github.com/connectrpc/conformance) suite in full, both directions.

For the **server**, add `protobuf-net.Connect.AspNetCore`. For contract-first services whose messages
are `Google.Protobuf` types, add `protobuf-net.Connect.Google` for JSON.
