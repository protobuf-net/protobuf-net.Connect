Performance
-

# Performance

Two measurements: what the protocol costs end to end against gRPC, and what the codecs cost on their
own. Both are reproducible from the repository, and both are quoted here with the caveats that make
them mean something — a benchmark without its caveats is a number, not a result.

## Against gRPC, end to end

The only comparison worth making is one that holds everything else still: **one `[Service]` contract,
one implementation, one Kestrel, one DI container, one protobuf-net model marshalling for both
sides.** The single variable is the protocol.

32 workers, 5 s per scenario, 256 B payload, .NET 8, 24 cores, client and server in one process.
Stable to ~3% across runs.

```bash
dotnet run -c Release --project benchmarks/ConnectLoad
```

| scenario | ops/sec | p50 | p99 |
| --- | ---: | ---: | ---: |
| unary, gRPC over HTTP/2 | 98,700 | 314µs | 606µs |
| unary, **Connect** over HTTP/2 | **160,900** | 190µs | 343µs |
| unary, Connect over HTTP/1.1 | 327,100 | 93µs | 160µs |
| stream ×10, gRPC over HTTP/2 | 134,300 | 229µs | 407µs |
| stream ×10, **Connect** over HTTP/2 | **138,900** | 222µs | 387µs |
| stream ×10, Connect over HTTP/1.1 | 244,300 | 126µs | 206µs |

Read honestly, that says three things:

**Streaming is parity.** About 3%, barely outside the run-to-run spread. The two framings cost the
same.

**Unary, Connect is ~1.6× — but that is mostly a gRPC-unary cost, not a Connect win.** A
single-message server-*streaming* gRPC call runs at 162k, against 104k for a gRPC unary call carrying
identical data. Connect shows no such gap: its unary (164k) and its one-message stream (160k) are the
same number. So something in grpc-dotnet's unary path costs roughly 60%, and Connect's advantage is
largely the absence of it. Observed and reproducible; **not diagnosed**.

**The HTTP/1.1 rows are not a protocol result.** At this concurrency `HttpClient` gives HTTP/1.1 a
socket per worker while HTTP/2 multiplexes onto one, so those rows measure connection parallelism as
much as anything. What they show without qualification is that the option *exists* — gRPC cannot serve
that port at all.

Client and server share one process and one set of cores, so none of these are capacity estimates.
They are fair *between the rows*, which is the comparison being made.

## The codecs

Serialization alone, one ordinary ten-member message:

```bash
dotnet run -c Release --project benchmarks/ConnectBenchmark -- --filter '*CodecBenchmarks*'
```

| | mean | vs binary write | allocated |
| --- | ---: | ---: | ---: |
| binary, write | **338 ns** | 1.00 | **0 B** |
| JSON (code-first), write | 484 ns | 1.43 | 264 B |
| JSON (contract-first), write | 1,263 ns | 3.74 | 3,016 B |
| binary, read | 590 ns | 1.75 | 664 B |
| JSON (code-first), read | 1,065 ns | 3.15 | 712 B |
| JSON (contract-first), read | 2,375 ns | 7.03 | 4,088 B |

Encoded sizes: **proto 173 B, JSON 321 B** — about 1.9×.

The contract-first JSON gap is the transcode: Google's `JsonFormatter` and `JsonParser` speak
`string`, so that path goes bytes → `string` → bytes, where the generated code-first path writes UTF-8
directly. **2.6× the time and 11× the garbage** to write. The two rows serialize different CLR types
through different implementations of the same mapping, so it is not an isolation of the transcode
alone — but it is the choice a consumer actually faces.

JSON against binary is cheaper than a text format might be expected to be: 1.43× to write, 1.81× to
read. That is the generated-code path doing its job.

## One result that reversed a prediction

The streaming framing prefixes each message with its length. Binary can state that length without
encoding twice (protobuf-net can measure); JSON cannot, so it encodes into a scratch buffer and
copies. The expectation was that JSON would pay for that. It does not:

| | plain write | enveloped | framing costs |
| --- | ---: | ---: | ---: |
| binary | 338 ns | 614 ns | **+276 ns**, +0 B |
| JSON | 484 ns | 548 ns | **+64 ns**, +32 B |

Measuring is a near-full serialization pass, so binary encodes **twice** and pays ~82% on top of its
write, while JSON encodes once and copies for ~13%. Enveloped JSON is therefore *faster* than
enveloped binary for this message.

It is a trade rather than a straight loss — the measured path allocates nothing, where buffering costs
296 B per message, and under streaming load allocation may matter more than 66 ns. And none of it
touches unary, where measuring buys a real `Content-Length`.

## Native AOT

Every smoke test in the repository is published with `PublishAot` and **run**, not merely published —
under a native publish the reflective fallbacks are simply absent, so a pass means the generated path
carried the whole thing. No IL warning in any of them names this library's code.
