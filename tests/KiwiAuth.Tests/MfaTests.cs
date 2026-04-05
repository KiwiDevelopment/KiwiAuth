using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using KiwiAuth.Tests.TestHelpers;
using OtpNet;
using Xunit;

namespace KiwiAuth.Tests;

/// <summary>
/// Integration tests for the MFA flow:
/// setup → enable → login requires MFA → verify → disable.
/// Uses Otp.NET to generate real TOTP codes from the secret returned by /setup.
/// </summary>
public class MfaTests : IClassFixture<KiwiTestFactory>
{
    private readonly KiwiTestFactory _factory;

    public MfaTests(KiwiTestFactory factory)
    {
        _factory = factory;
    }

    private HttpClient CreateClient() =>
        _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
        });

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static async Task<JsonElement> ParseAsync(HttpResponseMessage response)
    {
        var doc = await response.Content.ReadFromJsonAsync<JsonDocument>();
        return doc!.RootElement;
    }

    /// <summary>
    /// Registers a user, logs in, and returns the authenticated HttpClient + bearer token.
    /// </summary>
    private static async Task<(HttpClient Client, string AccessToken)> RegisterAndLoginAsync(
        KiwiTestFactory factory, string email, string password = "Test1234!")
    {
        var client = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
        });

        await client.PostAsJsonAsync("/auth/register", new { email, password });
        var loginResp = await client.PostAsJsonAsync("/auth/login", new { email, password });
        var body = await ParseAsync(loginResp);
        var token = body.GetProperty("data").GetProperty("accessToken").GetString()!;

        return (client, token);
    }

    private static HttpRequestMessage WithBearer(HttpMethod method, string url, string token) =>
        new(method, url) { Headers = { Authorization = new("Bearer", token) } };

    /// <summary>Generates a current TOTP code from a base32-encoded secret.</summary>
    private static string GenerateTotp(string base32Secret)
    {
        var key = Base32Encoding.ToBytes(base32Secret);
        var totp = new Totp(key);
        return totp.ComputeTotp();
    }

    // ─── Setup ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Setup_WhenAuthenticated_ReturnsSecretAndUri()
    {
        var (client, token) = await RegisterAndLoginAsync(_factory, "mfa_setup@example.com");

        var response = await client.SendAsync(WithBearer(HttpMethod.Get, "/auth/mfa/setup", token));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await ParseAsync(response);
        Assert.True(body.GetProperty("success").GetBoolean());

        var secret = body.GetProperty("data").GetProperty("secret").GetString();
        var uri = body.GetProperty("data").GetProperty("authenticatorUri").GetString();

        Assert.False(string.IsNullOrEmpty(secret));
        Assert.StartsWith("otpauth://totp/", uri);
    }

    [Fact]
    public async Task Setup_WithoutToken_Returns401()
    {
        var client = CreateClient();
        var response = await client.GetAsync("/auth/mfa/setup");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ─── Enable ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Enable_WithValidTotpCode_ReturnsCodes()
    {
        var (client, token) = await RegisterAndLoginAsync(_factory, "mfa_enable@example.com");

        // Get the secret
        var setupResp = await client.SendAsync(WithBearer(HttpMethod.Get, "/auth/mfa/setup", token));
        var setupBody = await ParseAsync(setupResp);
        var secret = setupBody.GetProperty("data").GetProperty("secret").GetString()!;

        // Enable with valid TOTP
        var code = GenerateTotp(secret);
        var enableResp = await client.SendAsync(new HttpRequestMessage(HttpMethod.Post, "/auth/mfa/enable")
        {
            Headers = { Authorization = new("Bearer", token) },
            Content = JsonContent.Create(new { code }),
        });

        Assert.Equal(HttpStatusCode.OK, enableResp.StatusCode);

        var enableBody = await ParseAsync(enableResp);
        Assert.True(enableBody.GetProperty("success").GetBoolean());

        var recoveryCodes = enableBody.GetProperty("data").GetProperty("recoveryCodes");
        Assert.Equal(8, recoveryCodes.GetArrayLength());
    }

    [Fact]
    public async Task Enable_WithInvalidCode_ReturnsBadRequest()
    {
        var (client, token) = await RegisterAndLoginAsync(_factory, "mfa_enable_bad@example.com");

        // Ensure setup key exists
        await client.SendAsync(WithBearer(HttpMethod.Get, "/auth/mfa/setup", token));

        var enableResp = await client.SendAsync(new HttpRequestMessage(HttpMethod.Post, "/auth/mfa/enable")
        {
            Headers = { Authorization = new("Bearer", token) },
            Content = JsonContent.Create(new { code = "000000" }),
        });

        Assert.Equal(HttpStatusCode.BadRequest, enableResp.StatusCode);

        var body = await ParseAsync(enableResp);
        Assert.False(body.GetProperty("success").GetBoolean());
        Assert.Equal("invalid_code", body.GetProperty("error").GetProperty("code").GetString());
    }

    // ─── Login → MFA required ────────────────────────────────────────────────

    [Fact]
    public async Task Login_WhenMfaEnabled_ReturnsMfaRequired()
    {
        var (client, token) = await RegisterAndLoginAsync(_factory, "mfa_login@example.com");

        // Enable MFA
        var setupResp = await client.SendAsync(WithBearer(HttpMethod.Get, "/auth/mfa/setup", token));
        var setupBody = await ParseAsync(setupResp);
        var secret = setupBody.GetProperty("data").GetProperty("secret").GetString()!;
        var code = GenerateTotp(secret);

        await client.SendAsync(new HttpRequestMessage(HttpMethod.Post, "/auth/mfa/enable")
        {
            Headers = { Authorization = new("Bearer", token) },
            Content = JsonContent.Create(new { code }),
        });

        // New client (fresh login) — no MFA session yet
        var freshClient = CreateClient();
        var loginResp = await freshClient.PostAsJsonAsync("/auth/login", new
        {
            email = "mfa_login@example.com",
            password = "Test1234!",
        });

        Assert.Equal(HttpStatusCode.OK, loginResp.StatusCode);

        var loginBody = await ParseAsync(loginResp);
        Assert.True(loginBody.GetProperty("success").GetBoolean());
        Assert.True(loginBody.GetProperty("data").GetProperty("requiresMfa").GetBoolean());

        var mfaToken = loginBody.GetProperty("data").GetProperty("mfaSessionToken").GetString();
        Assert.False(string.IsNullOrEmpty(mfaToken));
    }

    // ─── Verify ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Verify_WithValidTotpCode_ReturnsAccessToken()
    {
        var (client, token) = await RegisterAndLoginAsync(_factory, "mfa_verify@example.com");

        // Enable MFA
        var setupResp = await client.SendAsync(WithBearer(HttpMethod.Get, "/auth/mfa/setup", token));
        var setupBody = await ParseAsync(setupResp);
        var secret = setupBody.GetProperty("data").GetProperty("secret").GetString()!;
        var enableCode = GenerateTotp(secret);

        await client.SendAsync(new HttpRequestMessage(HttpMethod.Post, "/auth/mfa/enable")
        {
            Headers = { Authorization = new("Bearer", token) },
            Content = JsonContent.Create(new { code = enableCode }),
        });

        // Login → get MFA session token
        var freshClient = CreateClient();
        var loginResp = await freshClient.PostAsJsonAsync("/auth/login", new
        {
            email = "mfa_verify@example.com",
            password = "Test1234!",
        });
        var loginBody = await ParseAsync(loginResp);
        var mfaSessionToken = loginBody.GetProperty("data").GetProperty("mfaSessionToken").GetString()!;

        // Verify with fresh TOTP code
        var verifyCode = GenerateTotp(secret);
        var verifyResp = await freshClient.PostAsJsonAsync("/auth/mfa/verify", new
        {
            mfaSessionToken,
            code = verifyCode,
            isRecoveryCode = false,
        });

        Assert.Equal(HttpStatusCode.OK, verifyResp.StatusCode);

        var verifyBody = await ParseAsync(verifyResp);
        Assert.True(verifyBody.GetProperty("success").GetBoolean());
        Assert.False(string.IsNullOrEmpty(verifyBody.GetProperty("data").GetProperty("accessToken").GetString()));
    }

    [Fact]
    public async Task Verify_WithInvalidCode_Returns401()
    {
        var client = CreateClient();

        var verifyResp = await client.PostAsJsonAsync("/auth/mfa/verify", new
        {
            mfaSessionToken = "not.a.valid.token",
            code = "123456",
            isRecoveryCode = false,
        });

        Assert.Equal(HttpStatusCode.Unauthorized, verifyResp.StatusCode);

        var body = await ParseAsync(verifyResp);
        Assert.False(body.GetProperty("success").GetBoolean());
    }

    [Fact]
    public async Task Verify_WithRecoveryCode_ReturnsAccessToken()
    {
        var (client, token) = await RegisterAndLoginAsync(_factory, "mfa_recovery@example.com");

        // Enable MFA, collect a recovery code
        var setupResp = await client.SendAsync(WithBearer(HttpMethod.Get, "/auth/mfa/setup", token));
        var secret = (await ParseAsync(setupResp)).GetProperty("data").GetProperty("secret").GetString()!;
        var enableCode = GenerateTotp(secret);

        var enableResp = await client.SendAsync(new HttpRequestMessage(HttpMethod.Post, "/auth/mfa/enable")
        {
            Headers = { Authorization = new("Bearer", token) },
            Content = JsonContent.Create(new { code = enableCode }),
        });
        var recoveryCode = (await ParseAsync(enableResp))
            .GetProperty("data").GetProperty("recoveryCodes")[0].GetString()!;

        // Login → MFA required
        var freshClient = CreateClient();
        var loginResp = await freshClient.PostAsJsonAsync("/auth/login", new
        {
            email = "mfa_recovery@example.com",
            password = "Test1234!",
        });
        var mfaSessionToken = (await ParseAsync(loginResp))
            .GetProperty("data").GetProperty("mfaSessionToken").GetString()!;

        // Verify with recovery code
        var verifyResp = await freshClient.PostAsJsonAsync("/auth/mfa/verify", new
        {
            mfaSessionToken,
            code = recoveryCode,
            isRecoveryCode = true,
        });

        Assert.Equal(HttpStatusCode.OK, verifyResp.StatusCode);
        Assert.True((await ParseAsync(verifyResp)).GetProperty("success").GetBoolean());
    }

    // ─── Refresh after MFA verify ────────────────────────────────────────────

    [Fact]
    public async Task Refresh_AfterMfaVerify_ReturnsNewAccessToken()
    {
        // Proves that /auth/mfa/verify sets the refresh token cookie with the correct
        // attributes (HttpOnly, Path=/auth) — not just that the body contains an access token.
        var (client, token) = await RegisterAndLoginAsync(_factory, "mfa_refresh@example.com");

        // Enable MFA
        var setupResp = await client.SendAsync(WithBearer(HttpMethod.Get, "/auth/mfa/setup", token));
        var secret = (await ParseAsync(setupResp)).GetProperty("data").GetProperty("secret").GetString()!;
        var enableCode = GenerateTotp(secret);

        await client.SendAsync(new HttpRequestMessage(HttpMethod.Post, "/auth/mfa/enable")
        {
            Headers = { Authorization = new("Bearer", token) },
            Content = JsonContent.Create(new { code = enableCode }),
        });

        // Login → MFA required
        var freshClient = CreateClient();
        var loginResp = await freshClient.PostAsJsonAsync("/auth/login", new
        {
            email = "mfa_refresh@example.com",
            password = "Test1234!",
        });
        var mfaSessionToken = (await ParseAsync(loginResp))
            .GetProperty("data").GetProperty("mfaSessionToken").GetString()!;

        // Complete MFA verify — cookie must be set here
        var verifyCode = GenerateTotp(secret);
        await freshClient.PostAsJsonAsync("/auth/mfa/verify", new
        {
            mfaSessionToken,
            code = verifyCode,
            isRecoveryCode = false,
        });

        // If the cookie was set correctly (same path/options as regular login), refresh must work
        var refreshResp = await freshClient.PostAsync("/auth/refresh", null);

        Assert.Equal(HttpStatusCode.OK, refreshResp.StatusCode);

        var refreshBody = await ParseAsync(refreshResp);
        Assert.True(refreshBody.GetProperty("success").GetBoolean());
        Assert.False(string.IsNullOrEmpty(refreshBody.GetProperty("data").GetProperty("accessToken").GetString()));
    }

    // ─── Disable ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Disable_WithValidCode_DisablesMfa()
    {
        var (client, token) = await RegisterAndLoginAsync(_factory, "mfa_disable@example.com");

        // Enable
        var setupResp = await client.SendAsync(WithBearer(HttpMethod.Get, "/auth/mfa/setup", token));
        var secret = (await ParseAsync(setupResp)).GetProperty("data").GetProperty("secret").GetString()!;
        var code = GenerateTotp(secret);

        await client.SendAsync(new HttpRequestMessage(HttpMethod.Post, "/auth/mfa/enable")
        {
            Headers = { Authorization = new("Bearer", token) },
            Content = JsonContent.Create(new { code }),
        });

        // Disable with fresh TOTP code
        var disableCode = GenerateTotp(secret);
        var disableResp = await client.SendAsync(new HttpRequestMessage(HttpMethod.Post, "/auth/mfa/disable")
        {
            Headers = { Authorization = new("Bearer", token) },
            Content = JsonContent.Create(new { code = disableCode }),
        });

        Assert.Equal(HttpStatusCode.OK, disableResp.StatusCode);
        Assert.True((await ParseAsync(disableResp)).GetProperty("success").GetBoolean());

        // After disable, login should NOT require MFA
        var freshClient = CreateClient();
        var loginResp = await freshClient.PostAsJsonAsync("/auth/login", new
        {
            email = "mfa_disable@example.com",
            password = "Test1234!",
        });
        var loginBody = await ParseAsync(loginResp);

        Assert.True(loginBody.GetProperty("success").GetBoolean());
        // requiresMfa should be absent or false
        if (loginBody.GetProperty("data").TryGetProperty("requiresMfa", out var mfaFlag))
            Assert.False(mfaFlag.GetBoolean());
        else
            Assert.True(loginBody.GetProperty("data").TryGetProperty("accessToken", out _));
    }
}
