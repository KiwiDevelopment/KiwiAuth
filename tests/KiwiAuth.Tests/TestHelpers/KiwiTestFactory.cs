using KiwiAuth.Data;
using KiwiAuth.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace KiwiAuth.Tests.TestHelpers;

/// <summary>
/// Boots the SampleApi in test mode with an in-memory SQLite database.
/// A single shared connection keeps the schema alive for the lifetime of the factory.
/// </summary>
public class KiwiTestFactory : WebApplicationFactory<Program>
{
    private SqliteConnection? _connection;

    public FakeEmailSender EmailSender { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // appsettings.Testing.json in the SampleApi project provides the signing key.
        builder.UseEnvironment("Testing");

        builder.ConfigureServices(services =>
        {
            // Replace the real DbContext with an in-memory SQLite one.
            // The shared connection keeps the schema alive across requests.
            services.RemoveAll<DbContextOptions<KiwiDbContext>>();

            _connection = new SqliteConnection("Data Source=:memory:");
            _connection.Open();

            services.AddDbContext<KiwiDbContext>(options =>
                options.UseSqlite(_connection));

            // Register the fake email sender so tests can inspect sent emails.
            services.AddSingleton<IEmailSender>(EmailSender);
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
            _connection?.Dispose();
    }
}
