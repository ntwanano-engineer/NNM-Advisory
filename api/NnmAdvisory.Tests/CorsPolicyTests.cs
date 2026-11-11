using CsCheck;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace NnmAdvisory.Tests;

/// <summary>
/// Property-based test for CORS rejection of non-configured origins (Task 23.1).
///
/// <para><b>Property 9: CORS Policy Rejects Non-Configured Origins</b></para>
/// <para>
/// For any cross-origin request whose <c>Origin</c> header does not exactly
/// match the single configured frontend origin, the API does not grant the
/// origin: the response carries no <c>Access-Control-Allow-Origin</c> header
/// naming that origin (so a browser blocks the response) and the application
/// endpoint does not treat the request as cross-origin-approved. The configured
/// origin, by contrast, IS granted — included here as a positive control.
/// </para>
///
/// <para><b>Validates: Requirements 10.5</b></para>
///
/// <para><b>Why a minimal host rather than the full <c>Program</c>.</b></para>
/// <para>
/// The production <see cref="Program"/> fails fast at startup via
/// <c>ValidateOnStart()</c> on database, JWT, email, and S3 settings, opens an
/// Npgsql connection, loads a <c>.env</c> file, and bootstraps Serilog — none
/// of which bears on CORS behaviour. Standing all of that up (or stubbing it)
/// to exercise one middleware is brittle. Instead this test builds a minimal
/// in-memory host via <see cref="TestServer"/> that registers the identical
/// named CORS policy Program.cs defines — <c>WithOrigins(configuredOrigin)
/// .AllowAnyMethod().AllowAnyHeader()</c> applied through <c>UseCors</c> — over
/// a single trivial endpoint. The subject under test (ASP.NET Core's CORS
/// middleware driven by exactly the same policy configuration) is therefore
/// genuinely exercised; only the unrelated startup scaffolding is omitted.
/// </para>
///
/// <para><b>What "rejects" means for ASP.NET Core CORS.</b></para>
/// <para>
/// ASP.NET Core does not return HTTP 403 for a disallowed origin; the CORS
/// middleware simply omits the <c>Access-Control-Allow-Origin</c> header, and
/// the browser enforces the block by refusing to expose the response to script.
/// "Rejection" is therefore asserted as the absence of an allow-origin header
/// for the disallowed origin (and its presence for the configured origin).
/// </para>
/// </summary>
public sealed class CorsPolicyTests
{
    // Minimum property-test iterations mandated by the design specification.
    private const int Iterations = 100;

    /// <summary>The single origin the policy is configured to allow.</summary>
    private const string ConfiguredOrigin = "http://localhost:3000";

    private const string PolicyName = "NnmFrontend";

    /// <summary>
    /// Shared flag the terminal endpoint sets when it executes, letting a test
    /// confirm whether the request reached the application handler.
    /// </summary>
    private volatile bool _handlerRan;

    /// <summary>
    /// Builds an in-memory <see cref="TestServer"/> whose pipeline mirrors the
    /// CORS configuration in <c>Program.cs</c>: a named policy restricted to
    /// <see cref="ConfiguredOrigin"/> applied via <c>UseCors</c> ahead of a
    /// trivial terminal endpoint. The endpoint sets <see cref="_handlerRan"/>
    /// so a test can observe that the handler ran; the CORS allow-origin
    /// header (if any) is added by the middleware wrapping that handler.
    /// </summary>
    private TestServer BuildServer()
    {
        var host = new HostBuilder()
            .ConfigureWebHost(webHost =>
            {
                webHost
                    .UseTestServer()
                    .ConfigureServices(services =>
                    {
                        services.AddCors(options =>
                        {
                            options.AddPolicy(PolicyName, policy =>
                                policy
                                    .WithOrigins(ConfiguredOrigin)
                                    .AllowAnyMethod()
                                    .AllowAnyHeader());
                        });
                    })
                    .Configure(app =>
                    {
                        app.UseCors(PolicyName);

                        app.Run(context =>
                        {
                            _handlerRan = true;
                            context.Response.StatusCode = StatusCodes.Status200OK;
                            return context.Response.WriteAsync("ok");
                        });
                    });
            })
            .Start();

        return host.GetTestServer();
    }

    /// <summary>
    /// Sends a cross-origin GET carrying the given <c>Origin</c> header and
    /// returns the <c>Access-Control-Allow-Origin</c> values on the response
    /// (empty when the origin was not granted).
    /// </summary>
    private static string[] SendCrossOrigin(TestServer server, string origin)
    {
        var client = server.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/");
        request.Headers.Add("Origin", origin);

        using var response = client.SendAsync(request).GetAwaiter().GetResult();

        return response.Headers.TryGetValues(
            "Access-Control-Allow-Origin", out var values)
            ? values.ToArray()
            : Array.Empty<string>();
    }

    /// <summary>
    /// Sends a CORS preflight (<c>OPTIONS</c> with
    /// <c>Access-Control-Request-Method</c>) for the given origin and returns
    /// the granted <c>Access-Control-Allow-Origin</c> values.
    /// </summary>
    private static string[] SendPreflight(TestServer server, string origin)
    {
        var client = server.CreateClient();

        using var request = new HttpRequestMessage(HttpMethod.Options, "/");
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", "GET");

        using var response = client.SendAsync(request).GetAwaiter().GetResult();

        return response.Headers.TryGetValues(
            "Access-Control-Allow-Origin", out var values)
            ? values.ToArray()
            : Array.Empty<string>();
    }

    // ------------------------------------------------------------------
    // Generator: well-formed, URL-ish Origin header strings that are
    // guaranteed NOT to equal the configured origin. Varying scheme, host,
    // and port independently covers the realistic ways an origin can differ
    // (different host, different port, http vs https) while staying a valid
    // origin string.
    // ------------------------------------------------------------------
    private static readonly Gen<string> GenNonMatchingOrigin =
        Gen.Select(
            Gen.OneOfConst("http", "https"),
            Gen.Char['a', 'z'].Array[1, 20].Select(c => new string(c)),
            Gen.OneOfConst("com", "net", "org", "io", "dev", "local"),
            Gen.Int[1, 65535],
            (scheme, host, tld, port) => $"{scheme}://{host}.{tld}:{port}")
        .Where(origin => !string.Equals(
            origin, ConfiguredOrigin, StringComparison.OrdinalIgnoreCase));

    // ------------------------------------------------------------------
    // Property 9 — a cross-origin request from any origin other than the
    // configured one is not granted: no Access-Control-Allow-Origin naming
    // that origin (and none wildcarding it, since the policy is not a
    // wildcard policy).
    // ------------------------------------------------------------------

    [Fact]
    public void NonConfiguredOrigin_IsNotGrantedByCorsPolicy()
    {
        using var server = BuildServer();

        GenNonMatchingOrigin.Sample(origin =>
        {
            var allowOrigin = SendCrossOrigin(server, origin);

            // The disallowed origin must not be echoed back, and because the
            // policy is a specific-origin policy, no wildcard is emitted either.
            return !allowOrigin.Contains(origin)
                   && !allowOrigin.Contains("*");
        }, iter: Iterations);
    }

    // ------------------------------------------------------------------
    // Preflight companion — an OPTIONS preflight for a disallowed origin is
    // likewise not granted CORS access.
    // ------------------------------------------------------------------

    [Fact]
    public void NonConfiguredOrigin_PreflightIsNotGranted()
    {
        using var server = BuildServer();

        GenNonMatchingOrigin.Sample(origin =>
        {
            var allowOrigin = SendPreflight(server, origin);

            return !allowOrigin.Contains(origin)
                   && !allowOrigin.Contains("*");
        }, iter: Iterations);
    }

    // ------------------------------------------------------------------
    // Positive control — the configured origin IS granted, proving the
    // policy is live and the negative result above is meaningful rather
    // than a pipeline that never emits the header at all.
    // ------------------------------------------------------------------

    [Fact]
    public void ConfiguredOrigin_IsGranted()
    {
        using var server = BuildServer();

        _handlerRan = false;
        var allowOrigin = SendCrossOrigin(server, ConfiguredOrigin);

        Assert.Contains(ConfiguredOrigin, allowOrigin);
        Assert.True(_handlerRan);
    }
}
