using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using KiwiAuth.Tests.TestHelpers;
using Xunit;

namespace KiwiAuth.Tests;

/// <summary>
/// Integration tests for KiwiAuth endpoints.
/// Each test gets a fresh HttpClient (and therefore a fresh cookie jar) from the shared factory.
/// Tests use unique email addresses to avoid interfering with each other via the shared database.
/// </summary>
public class AuthTests : IClassFixture<KiwiTestFactory>
{
    private readonly KiwiTestFactory _factory;

    public AuthTests(KiwiTestFactory factory)
    {
        _factory = factory;
    }

    // Use HTTPS so the browser/CookieContainer honours Secure cookies set by the server.
    private HttpClient CreateClient() =>
        _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
        });

    // ─── Register ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Register_WithValidData_ReturnsSuccessAndAccessToken()
    {
        var client = CreateClient();

        var response = await client.PostAsJsonAsync("/auth/register", new
        {
            email = "register_ok@example.com",
            password = "Test1234!",
            fullName = "Test User",
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await ParseAsync(response);
        Assert.True(body.GetProperty("success").GetBoolean());

        var token = body.GetProperty("data").GetProperty("accessToken").GetString();
        Assert.False(string.IsNullOrEmpty(token));

        var roles = body.GetProperty("data").GetProperty("user").GetProperty("roles");
        Assert.Equal("User", roles[0].GetString());
    }

    [Fact]
    public async Task Register_DuplicateEmail_ReturnsBadRequestWithCode()
    {
        var client = CreateClient();
        var payload = new { email = "register_dup@example.com", password = "Test1234!" };

        await client.PostAsJsonAsync("/auth/register", payload);
        var response = await client.PostAsJsonAsync("/auth/register", payload);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await ParseAsync(response);
        Assert.False(body.GetProperty("success").GetBoolean());
        Assert.Equal("email_taken", body.GetProperty("error").GetProperty("code").GetString());
    }

    // ─── Login ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Login_WithValidCredentials_ReturnsAccessToken()
    {
        var client = CreateClient();

        await client.PostAsJsonAsync("/auth/register", new
        {
            email = "login_ok@example.com",
            password = "Test1234!",
        });

        var response = await client.PostAsJsonAsync("/auth/login", new
        {
            email = "login_ok@example.com",
            password = "Test1234!",
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await ParseAsync(response);
        Assert.True(body.GetProperty("success").GetBoolean());
        Assert.False(string.IsNullOrEmpty(body.GetProperty("data").GetProperty("accessToken").GetString()));
    }

    [Fact]
    public async Task Login_WithWrongPassword_Returns401WithCode()
    {
        var client = CreateClient();

        await client.PostAsJsonAsync("/auth/register", new
        {
            email = "login_bad@example.com",
            password = "Test1234!",
        });

        var response = await client.PostAsJsonAsync("/auth/login", new
        {
            email = "login_bad@example.com",
            password = "WrongPassword!",
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        var body = await ParseAsync(response);
        Assert.False(body.GetProperty("success").GetBoolean());
        Assert.Equal("invalid_credentials", body.GetProperty("error").GetProperty("code").GetString());
    }

    // ─── Refresh ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Refresh_AfterLogin_ReturnsNewAccessToken()
    {
        var client = CreateClient();

        await client.PostAsJsonAsync("/auth/register", new
        {
            email = "refresh_ok@example.com",
            password = "Test1234!",
        });

        // The refresh token cookie is automatically stored in the client's cookie jar.
        await client.PostAsJsonAsync("/auth/login", new
        {
            email = "refresh_ok@example.com",
            password = "Test1234!",
        });

        var response = await client.PostAsync("/auth/refresh", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await ParseAsync(response);
        Assert.True(body.GetProperty("success").GetBoolean());
        Assert.False(string.IsNullOrEmpty(body.GetProperty("data").GetProperty("accessToken").GetString()));
    }

    [Fact]
    public async Task Refresh_WithNoCookie_Returns401()
    {
        // Fresh client has no cookies from a previous login.
        var client = CreateClient();

        var response = await client.PostAsync("/auth/refresh", null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        var body = await ParseAsync(response);
        Assert.False(body.GetProperty("success").GetBoolean());
        Assert.Equal("missing_token", body.GetProperty("error").GetProperty("code").GetString());
    }

    // ─── Me ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Me_WithoutToken_Returns401()
    {
        var client = CreateClient();

        var response = await client.GetAsync("/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Me_WithValidBearerToken_ReturnsUserInfo()
    {
        var client = CreateClient();

        await client.PostAsJsonAsync("/auth/register", new
        {
            email = "me_ok@example.com",
            password = "Test1234!",
            fullName = "Me Test User",
        });

        var loginResponse = await client.PostAsJsonAsync("/auth/login", new
        {
            email = "me_ok@example.com",
            password = "Test1234!",
        });

        var loginBody = await ParseAsync(loginResponse);
        var token = loginBody.GetProperty("data").GetProperty("accessToken").GetString()!;

        var request = new HttpRequestMessage(HttpMethod.Get, "/auth/me");
        request.Headers.Authorization = new("Bearer", token);

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await ParseAsync(response);
        Assert.True(body.GetProperty("success").GetBoolean());
        Assert.Equal("me_ok@example.com", body.GetProperty("data").GetProperty("email").GetString());
        Assert.Equal("Me Test User", body.GetProperty("data").GetProperty("fullName").GetString());
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static async Task<JsonElement> ParseAsync(HttpResponseMessage response)
    {
        var doc = await response.Content.ReadFromJsonAsync<JsonDocument>();
        return doc!.RootElement;
    }
}
