using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Simcag.IngestionService.Tests.Integration;

public sealed class IngestionApiHealthTests : IClassFixture<IngestionApiTestFactory>
{
    private readonly IngestionApiTestFactory _factory;

    public IngestionApiHealthTests(IngestionApiTestFactory factory) => _factory = factory;

    [Fact]
    public async Task Get_Health_Returns_200()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/health");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Get_Health_Live_Returns_200()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/health/live");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Get_Health_Ready_Returns_200()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/health/ready");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}

public sealed class IngestionApiTestFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder) => builder.UseEnvironment("Testing");
}
