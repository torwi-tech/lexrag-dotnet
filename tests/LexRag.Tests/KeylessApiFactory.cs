using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace LexRag.Tests;

// Boots the API in the Testing environment, where Program skips the local appsettings.secrets.local.json
// override. That keeps the end-to-end tests running on the keyless deterministic fakes, so they stay
// reproducible and never make a paid call even on a developer machine that has a real key configured locally.
public sealed class KeylessApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder) => builder.UseEnvironment("Testing");
}
