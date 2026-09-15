JSON
-

# JSON

Connect's JSON is not "some JSON". The specification says it outright — *choose the `json` types to
use the **canonical JSON mapping*** — so it is Google's protojson, the same mapping every other
implementation produces.

That single sentence decides the design, because it means JSON here is a **specified transformation of
a protobuf schema**, not a serialization of whatever object graph happens to be in front of us. Which
is why neither `System.Text.Json`'s serializer nor Newtonsoft can produce it, and no amount of
configuration gets there:

- 64-bit integers are **strings** (`"BigNumber": "42"`), because JSON numbers are doubles in most
  readers and an `int64` above 2⁵³ would not survive the trip;
- enums are **names**, and readers accept numbers too;
- `bytes` is base64; map keys are **always** strings, whatever the schema says;
- `NaN` and the infinities are strings; default-valued fields are omitted;
- the well-known types are special-cased throughout — `Timestamp` → RFC 3339, `Duration` → `"1.5s"`.

## Contract-first

Your messages are `Google.Protobuf` types, so the mapping is Google's own implementation:

```csharp
builder.Services.AddConnect(o =>
{
    o.Codecs.Add(MarshallerConnectCodec.Instance);
    o.Codecs.Add(GoogleJsonConnectCodec.Instance);   // protobuf-net.Connect.Google
});
```

`protobuf-net.Connect.Google` is a separate package so that `Google.Protobuf` stays off
`protobuf-net.Connect`'s dependency graph — a code-first consumer has no Google messages and no reason
to carry it.

## Code-first

A `[ProtoModel]` gets the mapping generated alongside its binary serializer, from the same plan, over
`Utf8JsonWriter`. Nothing extra to declare — referencing `protobuf-net.Connect` is the opt-in:

```csharp
var jsonChannel = new ConnectChannel(http, new JsonConnectCodec((IJsonModel)MyModel.Instance), uri);
IGreeter client = MyServices.CreateClient<IGreeter>(jsonChannel);
```

On the server the generated registration adds it automatically when the model carries it.

### It is verified against Google, not against itself

The mapping is checked field-by-field against Google's own `JsonFormatter`, over `protoc`'s C# for a
`.proto` re-derived from the contracts on every run — 106 differential cases covering every scalar
kind in every container. That matters because a JSON writer and reader of ours would round-trip
perfectly against each other while spelling every key differently from everyone else.

### Field names

Canonical JSON keys off the **proto** field name, and protobuf-net puts the C# member name into the
schema verbatim. The two ends therefore agree automatically, whatever the spelling — but the rule is
not what most summaries say:

> `ToJsonName` removes underscores and uppercases what follows. **It does not lowercase the first
> letter.**

So `UserName` stays `UserName`, and `already_snake` becomes `alreadySnake`. "Canonical JSON is
lowerCamelCase" is the universal summary and it is wrong at character one.

Pinning `[ProtoMember(Name = "user_name")]` gives a `.proto` that reads like everyone else's, and a
JSON key of `userName`. Not pinning gives `UserName` on both sides and interoperates just as
correctly. It is a schema-aesthetics choice, not a correctness one.

## What has no JSON form

The JSON surface is a **subset** of the binary one, and necessarily so — several shapes protobuf-net
serializes perfectly well have no canonical JSON at all:

| shape | why |
| --- | --- |
| `[ProtoInclude]` hierarchies | sub-type framing is a protobuf-net extension to protobuf |
| extensible contracts | retained unknown fields are raw bytes with no schema |
| null-wrapped members | a protobuf-net extension |
| `DateTime`/`TimeSpan` below `CompatibilityLevel.Level240`, `Guid`/`decimal` below `Level300` | at those levels they are protobuf-net messages, not well-known types |
| auto-tuples | the read needs a shape not yet written |

Each is reported at build time as **PBN3005**, at `Info` severity — unlike a missing binary
serializer, this leaves `GetJsonSerializer<T>()` returning `null`, which the codec turns into a clear
error naming the type. Such a contract is still served over `application/proto`.

## Cost

Code-first JSON writes UTF-8 straight through `Utf8JsonWriter`; the contract-first path goes
bytes → `string` → bytes, because Google's formatter and parser speak `string`. Measured on one
message:

| | write | read |
| --- | --- | --- |
| code-first | 484 ns / 264 B | 1,065 ns / 712 B |
| contract-first | 1,263 ns / 3,016 B | 2,375 ns / 4,088 B |

Against binary, code-first JSON costs 1.43× to write and 1.81× to read, and about 1.9× the bytes. See
[performance](performance).
