using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using KiwiAuth.Tests.TestHelpers;
using Xunit;

namespace KiwiAuth.Tests;

/// <summary>
/// Tests for email confirmation and password reset flows.
/// Uses FakeEmailSender to inspect sent emails without a real mail server.
/// </summary>
public class EmailFlowTests : IClassFixture<KiwiTestFactory>
{
    private readonly KiwiTestFactory _factory;

    public EmailFlowTests(KiwiTestFactory factory)
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
    /// Extracts userId and token from a URL like /auth/confirm-email?userId=X&amp;token=Y
    /// </summary>
    private static (string UserId, string Token) ExtractQueryParams(string url)
    {
        var idx = url.IndexOf('?');
        var query = idx >= 0 ? url[(idx + 1)..] : url;
        var parts = query.Split('&')
            .Select(p => p.Split('=', 2))
            .ToDictionary(p => p[0], p => Uri.UnescapeDataString(p[1]));
        return (parts["userId"], parts["token"]);
    }

    /// <summary>Extracts the href value from the first anchor tag in an HTML string.</summary>
    private static string ExtractHref(string html)
    {
        var match = Regex.Match(html, @"href=""([^""]+)""");
        return match.Groups[1].Value;
    }

    // ─── Email confirmation ───────────────────────────────────────────────────

    [Fact]
    public async Task Register_SendsConfirmationEmail()
    {
        var client = CreateClient();

        await client.PostAsJsonAsync("/auth/register", new
        {
            email = "email_sent@example.com",
            password = "Test1234!",
        });

        var email = _factory.EmailSender.LastTo("email_sent@example.com");
        Assert.NotNull(email);
        Assert.Equal("Confirm your email", email.Subject);
        Assert.Contains("confirm", email.Body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ConfirmEmail_WithValidToken_Succeeds()
    {
        var client = CreateClient();

        await client.PostAsJsonAsync("/auth/register", new
        {
            email = "email_confirm_ok@example.com",
            password = "Test1234!",
        });

        var email = _factory.EmailSender.LastTo("email_confirm_ok@example.com");
        Assert.NotNull(email);

        var href = ExtractHref(email.Body);
        var (userId, token) = ExtractQueryParams(href);

        var response = await client.GetAsync($"/auth/confirm-email?userId={userId}&token={Uri.EscapeDataString(token)}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await ParseAsync(response);
        Assert.True(body.GetProperty("success").GetBoolean());
    }

    [Fact]
    public async Task ConfirmEmail_WithInvalidToken_ReturnsBadRequest()
    {
        var client = CreateClient();

        await client.PostAsJsonAsync("/auth/register", new
        {
            email = "email_confirm_bad@example.com",
            password = "Test1234!",
        });

        var email = _factory.EmailSender.LastTo("email_confirm_bad@example.com");
        var href = ExtractHref(email!.Body);
        var (userId, _) = ExtractQueryParams(href);

        var response = await client.GetAsync($"/auth/confirm-email?userId={userId}&token=invalid-token");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await ParseAsync(response);
        Assert.False(body.GetProperty("success").GetBoolean());
        Assert.Equal("invalid_token", body.GetProperty("error").GetProperty("code").GetString());
    }

    // ─── Password reset ───────────────────────────────────────────────────────

    [Fact]
    public async Task ForgotPassword_SendsResetEmail()
    {
        var client = CreateClient();

        // Register and confirm email (forgot-password only sends to confirmed accounts)
        await client.PostAsJsonAsync("/auth/register", new
        {
            email = "forgot_sends@example.com",
            password = "Test1234!",
        });

        var confirmEmail = _factory.EmailSender.LastTo("forgot_sends@example.com");
        var (userId, confirmToken) = ExtractQueryParams(ExtractHref(confirmEmail!.Body));
        await client.GetAsync($"/auth/confirm-email?userId={userId}&token={Uri.EscapeDataString(confirmToken)}");

        var response = await client.PostAsJsonAsync("/auth/forgot-password", new
        {
            email = "forgot_sends@example.com",
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var resetEmail = _factory.EmailSender.LastTo("forgot_sends@example.com");
        Assert.NotNull(resetEmail);
        Assert.Equal("Reset your password", resetEmail.Subject);
        Assert.Contains("Reset Password", resetEmail.Body);
    }

    [Fact]
    public async Task ForgotPassword_UnknownEmail_Returns200WithoutSendingEmail()
    {
        var client = CreateClient();
        var initialCount = _factory.EmailSender.SentEmails.Count;

        var response = await client.PostAsJsonAsync("/auth/forgot-password", new
        {
            email = "nonexistent_forgot@example.com",
        });

        // Must always return 200 — never reveal if the email exists
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(initialCount, _factory.EmailSender.SentEmails.Count);
    }

    [Fact]
    public async Task ResetPassword_WithValidToken_Succeeds()
    {
        var client = CreateClient();

        await client.PostAsJsonAsync("/auth/register", new
        {
            email = "reset_ok@example.com",
            password = "Test1234!",
        });

        // Confirm email first
        var confirmEmail = _factory.EmailSender.LastTo("reset_ok@example.com");
        var (userId, confirmToken) = ExtractQueryParams(ExtractHref(confirmEmail!.Body));
        await client.GetAsync($"/auth/confirm-email?userId={userId}&token={Uri.EscapeDataString(confirmToken)}");

        // Request reset
        await client.PostAsJsonAsync("/auth/forgot-password", new { email = "reset_ok@example.com" });

        var resetEmail = _factory.EmailSender.LastTo("reset_ok@example.com");
        var (resetUserId, resetToken) = ExtractQueryParams(ExtractHref(resetEmail!.Body));

        // Reset password
        var response = await client.PostAsJsonAsync("/auth/reset-password", new
        {
            userId = resetUserId,
            token = resetToken,
            newPassword = "NewPass5678!",
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True((await ParseAsync(response)).GetProperty("success").GetBoolean());
    }

    [Fact]
    public async Task ResetPassword_NewPasswordWorks()
    {
        var client = CreateClient();

        await client.PostAsJsonAsync("/auth/register", new
        {
            email = "reset_login@example.com",
            password = "OldPass1234!",
        });

        // Confirm email
        var confirmEmail = _factory.EmailSender.LastTo("reset_login@example.com");
        var (userId, confirmToken) = ExtractQueryParams(ExtractHref(confirmEmail!.Body));
        await client.GetAsync($"/auth/confirm-email?userId={userId}&token={Uri.EscapeDataString(confirmToken)}");

        // Reset password
        await client.PostAsJsonAsync("/auth/forgot-password", new { email = "reset_login@example.com" });
        var resetEmail = _factory.EmailSender.LastTo("reset_login@example.com");
        var (resetUserId, resetToken) = ExtractQueryParams(ExtractHref(resetEmail!.Body));

        await client.PostAsJsonAsync("/auth/reset-password", new
        {
            userId = resetUserId,
            token = resetToken,
            newPassword = "NewPass5678!",
        });

        // Old password must no longer work
        var oldLoginResp = await client.PostAsJsonAsync("/auth/login", new
        {
            email = "reset_login@example.com",
            password = "OldPass1234!",
        });
        Assert.Equal(HttpStatusCode.Unauthorized, oldLoginResp.StatusCode);

        // New password must work
        var newLoginResp = await client.PostAsJsonAsync("/auth/login", new
        {
            email = "reset_login@example.com",
            password = "NewPass5678!",
        });
        Assert.Equal(HttpStatusCode.OK, newLoginResp.StatusCode);
        Assert.True((await ParseAsync(newLoginResp)).GetProperty("success").GetBoolean());
    }

    [Fact]
    public async Task ResetPassword_WithInvalidToken_ReturnsBadRequest()
    {
        var client = CreateClient();

        await client.PostAsJsonAsync("/auth/register", new
        {
            email = "reset_bad@example.com",
            password = "Test1234!",
        });

        var email = _factory.EmailSender.LastTo("reset_bad@example.com");
        var (userId, _) = ExtractQueryParams(ExtractHref(email!.Body));

        var response = await client.PostAsJsonAsync("/auth/reset-password", new
        {
            userId,
            token = "invalid-token",
            newPassword = "NewPass5678!",
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("reset_failed", (await ParseAsync(response)).GetProperty("error").GetProperty("code").GetString());
    }
}
