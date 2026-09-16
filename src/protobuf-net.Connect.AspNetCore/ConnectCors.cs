using Microsoft.AspNetCore.Cors.Infrastructure;
using System;
using System.Collections.Generic;

namespace ProtoBuf.Connect.AspNetCore
{
    /// <summary>
    /// The CORS configuration a browser-based Connect client needs.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Browser reach is a large part of why Connect exists - a unary call is an ordinary HTTP POST, so
    /// <c>connect-es</c> can call this server directly where gRPC could not. That only works if the
    /// preflight allows the protocol's own headers, and a browser reports a CORS failure as an opaque
    /// network error, so getting it wrong is both easy and hard to diagnose.
    /// </para>
    /// <para>
    /// The lists mirror <see href="https://github.com/connectrpc/cors-go">connectrpc/cors</see>, which
    /// is the reference answer every other implementation's documentation points at. Matching it
    /// exactly means a policy copied from any Connect guide works here unchanged - including its
    /// gRPC-web entries, which cost nothing and are what a consumer who later adds gRPC-web would need.
    /// </para>
    /// <para>
    /// <b>Two things this cannot do for you</b>, both of which are ordinary CORS rather than anything
    /// Connect-specific, and both of which are silent when missed:
    /// </para>
    /// <list type="bullet">
    /// <item><description>
    /// <b>Your own headers.</b> <c>Authorization</c>, an API-key header, a tenant header - none of them
    /// are protocol headers, so none are here. Add them with <c>WithHeaders</c>.
    /// </description></item>
    /// <item><description>
    /// <b>Trailing metadata.</b> On a unary call this server writes trailing metadata as
    /// <c>trailer-</c> prefixed response headers, and CORS has no prefix wildcard - each one has to be
    /// exposed <em>by name</em> with <c>WithExposedHeaders</c>, or the client silently sees none of it.
    /// </description></item>
    /// </list>
    /// </remarks>
    public static class ConnectCors
    {
        /// <summary>
        /// The methods a Connect client uses: <c>POST</c> always, and <c>GET</c> for a method declared
        /// side-effect-free.
        /// </summary>
        public static IReadOnlyList<string> Methods { get; } = new[] { "POST", "GET" };

        /// <summary>
        /// The request headers a Connect or gRPC-web client sends, which the preflight must allow.
        /// </summary>
        /// <remarks>
        /// <c>Connect-Accept-Encoding</c> and <c>Connect-Content-Encoding</c> are deliberately absent:
        /// they carry <em>Connect-level</em> compression for streaming, which browser clients do not
        /// negotiate - the browser applies <c>Content-Encoding</c> itself - and the reference list omits
        /// them for the same reason. Add them if you have a non-browser client that sets them and is
        /// nevertheless subject to CORS.
        /// </remarks>
        public static IReadOnlyList<string> AllowedHeaders { get; } = new[]
        {
            "Content-Type",              // every protocol
            "Connect-Protocol-Version",  // Connect
            "Connect-Timeout-Ms",        // Connect
            "Grpc-Timeout",              // gRPC-web
            "X-Grpc-Web",                // gRPC-web
            "X-User-Agent",              // every protocol
        };

        /// <summary>
        /// The response headers a client must be able to read, which the response must expose.
        /// </summary>
        /// <remarks>
        /// All three are gRPC-web's status channel. A Connect error carries its code and details in the
        /// <em>body</em> instead, so a Connect-only deployment does not need them - they are here
        /// because the reference list has them and they cost nothing.
        /// </remarks>
        public static IReadOnlyList<string> ExposedHeaders { get; } = new[]
        {
            "Grpc-Status",
            "Grpc-Message",
            "Grpc-Status-Details-Bin",
        };

        /// <summary>
        /// Applies <see cref="Methods"/>, <see cref="AllowedHeaders"/> and <see cref="ExposedHeaders"/>
        /// to a CORS policy. Compose the rest - origins, credentials, your own headers - as usual.
        /// </summary>
        /// <example>
        /// <code>
        /// builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
        ///     .WithOrigins("https://app.example.com")
        ///     .WithConnect()));
        /// </code>
        /// </example>
        public static CorsPolicyBuilder WithConnect(this CorsPolicyBuilder builder)
        {
            if (builder is null) throw new ArgumentNullException(nameof(builder));

            // ToArray per call: CorsPolicyBuilder keeps what it is handed, and handing out the same
            // array to every policy would let one policy's later mutation reach another
            builder.WithMethods(ToArray(Methods));
            builder.WithHeaders(ToArray(AllowedHeaders));
            builder.WithExposedHeaders(ToArray(ExposedHeaders));
            return builder;
        }

        private static string[] ToArray(IReadOnlyList<string> values)
        {
            var result = new string[values.Count];
            for (var i = 0; i < values.Count; i++) result[i] = values[i];
            return result;
        }
    }
}
