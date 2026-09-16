using ProtoBuf;
using ProtoBuf.Connect;
using ProtoBuf.Grpc;
using ProtoBuf.Grpc.Configuration;
using ProtoBuf.Meta;

namespace ProtoBuf.ConnectCorsChecks;

[Service("cors.v1.Greeter")]
public interface IGreeter
{
    /// <summary>Side-effect-free, so it is bound for GET as well as POST - Connect's cacheable form.</summary>
    [NoSideEffects]
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

// No explicit seeds: [ProtoConnect] seeds the model from its [ProtoService] contracts, exactly as
// [ProtoGrpc] does. This project is the regression test for that - it was written from
// docs/getting-started.md step by step, which is how the gap was found in the first place.
[ProtoModel]
public partial class CorsModel : TypeModel { }

[ProtoConnect(Model = typeof(CorsModel))]
[ProtoService(typeof(IGreeter), typeof(GreeterService))]
internal static partial class CorsServices { }
