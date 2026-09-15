# protobuf-net.Connect — notes for agents

Only non-obvious things live here; the code is the reference for everything else.

## Usage policy

This project does not exclude LLM etc tool usage under human guidance. All responsibility for
code-quality rests with the human submitter/reviewer; "slop" will be culled without mercy.

## `docs/` is published; `notes/` is not

`docs/` is the Jekyll source for the documentation site, with `jekyll-sitemap` on and no `exclude:`
in `_config.yml` - so **a file put there is built into the site and handed to search engines, however
internal it reads**. That is not hypothetical: four working-notes files were live on the protobuf-net
site for a while before anyone noticed.

Working notes, handovers and measurements go in **`notes/`**, which is not published. `findings.md`
is the running log; it is numbered, and new entries append.

Keep the two straight when adding anything: if it would embarrass you on a search results page, it
belongs in `notes/`.

## Layout

- **`src/` is the published surface and nothing else is.** A project's folder decides whether it
  packs: `src/Directory.Build.props` sets `IsPackable=true` and all the package metadata, and
  `tests/` and `benchmarks/` set it false. Do not add packaging properties to individual projects.
- `Build.csproj` is a `Microsoft.Build.Traversal` project. It names the three shipping projects
  explicitly and globs `tests/*` and `benchmarks/*`, so a new project in either is built by CI
  automatically. `-p:Packing=true` narrows it to the shipping set.
- **Central package management is on.** Versions go in `/Directory.Packages.props`; leave `Version=`
  off the `PackageReference`.
- `notes/` is working notes and is not published. `findings.md` is the running log - it is numbered,
  and new entries append.

## The build-time tooling lives in the other repository

`ProtoModelGenerator` (serializers) and `ProtoConnectGenerator` (proxies and bindings) are in
**protobuf-net.BuildTools**, which stays in
[protobuf-net](https://github.com/protobuf-net/protobuf-net). It is *not a package of its own*: it
ships inside `protobuf-net.Core` as `analyzers/dotnet/cs/protobuf-net.BuildTools.dll` plus
`build/protobuf-net.Core.props`, so an ordinary package reference brings it along. That is what makes
this split viable.

**`PrivateAssets="none"` on the protobuf-net references is load-bearing**, in both the package and
the source form. NuGet's default dependency edge excludes `Build,Analyzers`, so without it a consumer
of protobuf-net.Connect gets no generators and no analyzers - and PBN5007, which catches
authorization being silently dropped from a contract-first service, protects nobody. It is only
visible in the generated nuspec (`include="All"` versus `exclude="Build,Analyzers"`), which is where
it was caught.

### Building against a protobuf-net checkout

`protobuf-net.Core` 3.4.28 was the first release carrying `ProtoConnectGenerator` and
`ProtoModelGenerator`'s JSON half, so an ordinary restore is enough now. To develop a generator
change alongside this runtime, build against a checkout instead:

```bash
dotnet build Build.csproj -p:ProtoBufSourcePath=../protobuf-net
```

`Directory.Build.targets` turns that into project references, via the per-project opt-ins
`UseProtoBufCore`, `UseProtoBuf` and `UseProtoBufTooling`. CI does the same, by checking protobuf-net
out alongside.

**A package packed in that mode pins its protobuf-net dependency to the local build's version** and
must never be published; the build warns, and `release.yml` refuses outright.

CI no longer checks protobuf-net out - it restores 3.4.28 like any other consumer, which is the first
thing that actually exercises the package boundary the split introduced. If a generator change is
needed here, it has to ship in a protobuf-net release first; that is the cost, and it is the reason
the generator **probes** for types rather than naming them.

## What the checks are for

Nothing here is a unit-test suite, and that is deliberate: every defect this code has had was a place
where **both of our own ends agreed with each other and neither agreed with the protocol**, which no
self-test can find by construction. So the gates are external oracles:

- `tests/ConnectConformance` - connectrpc/conformance drives a real client against our server and our
  client against its reference server. 1492/1700, both directions.
- `tests/ConnectJsonDifferential` - our canonical JSON against **Google's own formatter**, over
  protoc's C# for a `.proto` re-derived from the contracts on every run, so the schema cannot quietly
  stop describing the types. Verify any new check can *fail* before trusting it; three have passed
  vacuously so far.
- `tests/AotDualHostSmoke` - one contract over gRPC and Connect on one host, which is the headline
  claim and was asserted in three places and demonstrated nowhere before it existed.

  It is also the **endpoint-metadata oracle**, and that check is the reason to keep the two transports
  in one process. `Grpc.AspNetCore.Server` finds an `[Authorize]` by reflecting over the
  implementation at startup; `ProtoConnectGenerator` reconstructs the same list at build time and
  emits constructor calls, because `MapConnectService` does not reflect. The check compares the two
  endpoints' metadata for the same method, so our build-time answer is measured against grpc-dotnet's
  reflective one rather than against itself. A dropped attribute has **no symptom** - the build
  succeeds, the service answers, and the only difference is that anybody may call it - so nothing else
  here could notice. `IGreeter.AdminAsync` exists solely to carry the attribute and is never called;
  keep it that way, or registering authorization services would start enforcing it on a method the
  other checks use.

Everything under `tests/` that publishes native is **run**, not merely published: under AOT the
reflective fallbacks are simply absent, so a pass means the generated path carried the whole thing.
