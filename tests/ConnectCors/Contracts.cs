using ProtoBuf;
using ProtoBuf.Connect;
using ProtoBuf.Grpc;
using ProtoBuf.Grpc.Configuration;
using ProtoBuf.Meta;

namespace ProtoBuf.ConnectCorsChecks;

[Service("cors.v1.Greeter")]
public interface IGreeter
{
    Task<HelloReply> SayHelloAsync(HelloRequest request, CallContext context = default);

    /// <summary>Sets trailing metadata, which on a unary call becomes a <c>trailer-</c> header.</summary>
    Task<HelloReply> WithTrailerAsync(HelloRequest request, CallContext context = default);
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

public class GreeterService : IGreeter
{
    public Task<HelloReply> SayHelloAsync(HelloRequest request, CallContext context)
        => Task.FromResult(new HelloReply { Message = $"hello {request.Name}" });

    public Task<HelloReply> WithTrailerAsync(HelloRequest request, CallContext context)
    {
        context.ServerCallContext?.ResponseTrailers.Add("x-audit", "42");
        return Task.FromResult(new HelloReply { Message = "ok" });
    }
}

// The seeds are explicit, and have to be: [ProtoConnect] does NOT seed the model the way [ProtoGrpc]
// does (GrpcProxyGenerator.CollectPayloadsForModel has no Connect equivalent), so a Connect-only
// code-first project gets an empty model - and an empty model emits nothing at all, including
// `Instance`, so the failure is CS0117 in generated code rather than a diagnostic.
[ProtoModel]
[ProtoSerializable(typeof(HelloRequest))]
[ProtoSerializable(typeof(HelloReply))]
public partial class CorsModel : TypeModel { }

[ProtoConnect(Model = typeof(CorsModel))]
[ProtoService(typeof(IGreeter), typeof(GreeterService))]
internal static partial class CorsServices { }
