using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using KiwiAuth.Tests.TestHelpers;
using Xunit;

namespace KiwiAuth.Tests;

/// <summary>
/// Tests for account lockout after repeated failed login attempts.
/// Default: locks after 5 failed attempts.
/// </summary>
public class LockoutTests : IClassFixture<KiwiTestFactory>
{
    private readonly KiwiTestFactory _factory;

    public LockoutTests(KiwiTestFactory factory)
    {
        _factory = factory;
    }

    private HttpClient CreateClient() =>
        _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
        });

    private static async Task<JsonElement> ParseAsync(HttpResponseMessage response)
    {
        var doc = await response.Content.ReadFromJsonAsync<JsonDocument>();
        return doc!.RootElement;
    }

    [Fact]
    public async Task Login_AfterMaxFailedAttempts_LocksAccount()
    {
        var client = CreateClient();

        await client.PostAsJsonAsync("/auth/register", new
        {
            email = "lockout_trigger@example.com",
            password = "Correct1234!",
        });

        // 5 failed attempts — the 5th one triggers the lockout and returns account_locked
        for (var i = 0; i < 4; i++)
        {
            var resp = await client.PostAsJsonAsync("/auth/login", new
            {
                email = "lockout_trigger@example.com",
                password = "WrongPassword!",
            });
            Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
            Assert.Equal("invalid_credentials", (await ParseAsync(resp)).GetProperty("error").GetProperty("code").GetString());
        }

        // 5th failure triggers lockout
        var lockedResp = await client.PostAsJsonAsync("/auth/login", new
        {
            email = "lockout_trigger@example.com",
            password = "WrongPassword!",
        });
        Assert.Equal(HttpStatusCode.Unauthorized, lockedResp.StatusCode);
        Assert.Equal("account_locked", (await ParseAsync(lockedResp)).GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Login_LockedAccount_RejectsEvenCorrectPassword()
    {
        var client = CreateClient();

        await client.PostAsJsonAsync("/auth/register", new
        {
            email = "lockout_correct@example.com",
            password = "Correct1234!",
        });

        // Trigger lockout
        for (var i = 0; i < 5; i++)
        {
            await client.PostAsJsonAsync("/auth/login", new
            {
                email = "lockout_correct@example.com",
                password = "WrongPassword!",
            });
        }

        // Now try with correct password — must still be locked
        var response = await client.PostAsJsonAsync("/auth/login", new
        {
            email = "lockout_correct@example.com",
            password = "Correct1234!",
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("account_locked", (await ParseAsync(response)).GetProperty("error").GetProperty("code").GetString());
    }
}
