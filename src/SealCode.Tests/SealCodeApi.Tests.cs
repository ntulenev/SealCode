using FluentAssertions;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace SealCode.Tests;

public sealed class SealCodeApiTests
{
    [Fact(DisplayName = "GET /health returns ok")]
    [Trait("Category", "Integration")]
    public async Task GetHealthReturnsOk()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health");
        var payload = await response.Content.ReadFromJsonAsync<string>();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        payload.Should().Be("ok");
    }

    [Fact(DisplayName = "GET /admin/rooms returns unauthorized without cookie")]
    [Trait("Category", "Integration")]
    public async Task GetAdminRoomsWithoutCookieReturnsUnauthorized()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/admin/rooms");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact(DisplayName = "POST /admin/rooms returns unauthorized without cookie")]
    [Trait("Category", "Integration")]
    public async Task CreateRoomWithoutCookieReturnsUnauthorized()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/admin/rooms", new { Name = "Room", Language = "csharp" });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact(DisplayName = "POST /admin/login sets cookie and allows admin room creation")]
    [Trait("Category", "Integration")]
    public async Task LoginThenCreateRoomReturnsRoomDetails()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        using var loginContent = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["name"] = "Admin",
            ["password"] = "pass1"
        });

        var loginResponse = await client.PostAsync("/admin/login", loginContent);

        loginResponse.StatusCode.Should().Be(HttpStatusCode.Redirect);
        loginResponse.Headers.TryGetValues("Set-Cookie", out var setCookies).Should().BeTrue();
        var setCookie = setCookies!.First();
        var cookie = setCookie.Split(';', 2)[0];

        client.DefaultRequestHeaders.Add("Cookie", cookie);

        var createResponse = await client.PostAsJsonAsync("/admin/rooms", new { Name = "Room", Language = "sql" });
        var payload = await createResponse.Content.ReadFromJsonAsync<JsonElement>();

        createResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        payload.GetProperty("RoomId").GetString().Should().NotBeNullOrWhiteSpace();
        payload.GetProperty("Name").GetString().Should().Be("Room");
        payload.GetProperty("Language").GetString().Should().Be("sql");
        payload.GetProperty("CreatedBy").GetString().Should().Be("Admin");
    }

    [Fact(DisplayName = "GET /room/{id} returns not found for missing room")]
    [Trait("Category", "Integration")]
    public async Task GetRoomReturnsNotFoundForMissingRoom()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/room/missing");
        var payload = await response.Content.ReadFromJsonAsync<string>();

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        payload.Should().Be("Room not found");
    }

    private static SealCodeAppFactory CreateFactory() => new();

    private sealed class SealCodeAppFactory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                var settings = new Dictionary<string, string?>
                {
                    ["AdminUsers:0:Name"] = "Admin",
                    ["AdminUsers:0:Password"] = "pass1",
                    ["MaxUsersPerRoom"] = "3"
                };

                config.AddInMemoryCollection(settings);
            });
        }
    }
}
