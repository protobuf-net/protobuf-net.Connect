using System;

namespace ProtoBuf.Connect
{
    /// <summary>
    /// Declares a unary operation free of side effects, so it can be served over <c>GET</c> as well as
    /// <c>POST</c> - an ordinary cacheable HTTP request.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the code-first spelling of <c>option idempotency_level = NO_SIDE_EFFECTS;</c>, and the
    /// one thing Connect does that gRPC structurally cannot: such a call can be served from a CDN or a
    /// browser cache without reaching the server at all.
    /// </para>
    /// <para>
    /// <b>The name is deliberate, and <c>[Idempotent]</c> was rejected for it.</b> The proto option has
    /// three levels and only <c>NO_SIDE_EFFECTS</c> qualifies: <c>IDEMPOTENT</c> means "safe to retry",
    /// which is strictly weaker than "safe to cache and prefetch". A delete is idempotent and must
    /// emphatically not be a <c>GET</c>, so an attribute called <c>[Idempotent]</c> would invite exactly
    /// the mistake that matters.
    /// </para>
    /// <para>
    /// Declare it on the <b>contract</b> method - it is a property of the RPC, not of one
    /// implementation of it - and note that it only takes effect with build-time tooling new enough to
    /// read it. It carries no data, and is not read at run time by anything here: the generator turns it
    /// into the method descriptor's <c>idempotent</c> flag, which is what both sides actually consult.
    /// </para>
    /// <para>
    /// Connect GET is unary-only, since there is no way to carry a stream in a query string. On any
    /// other method shape the generator reports <c>PBN5009</c> rather than ignoring it.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// [Service("mypackage.v1.Greeter")]
    /// public interface IGreeter
    /// {
    ///     [NoSideEffects]
    ///     Task&lt;HelloReply&gt; GetGreetingAsync(HelloRequest request, CallContext context = default);
    /// }
    /// </code>
    /// </example>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
    public sealed class NoSideEffectsAttribute : Attribute
    {
    }
}
