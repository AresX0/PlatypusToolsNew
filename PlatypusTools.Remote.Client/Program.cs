using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using PlatypusTools.Remote.Client;
using PlatypusTools.Remote.Client.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// Configure the API base address (port 47392)
var apiBaseAddress = builder.Configuration["ApiBaseAddress"] 
    ?? builder.HostEnvironment.BaseAddress.Replace(":5000", ":47392")
                                          .Replace(":5001", ":47392");

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(apiBaseAddress) });

// Add MSAL authentication for Entra ID
// The ClientId comes from appsettings.json - update it with your App Registration Client ID
// See DOCS/ENTRA_ID_SETUP.md for full setup instructions
var clientId = builder.Configuration["AzureAd:ClientId"] ?? "00000000-0000-0000-0000-000000000000";
builder.Services.AddMsalAuthentication(options =>
{
    builder.Configuration.Bind("AzureAd", options.ProviderOptions.Authentication);
    // The scope format is: api://{ApiScopeId}/access_as_user
    // This must match the scope you exposed in Azure Portal -> Expose an API
    // The ApiScopeId comes from appsettings.json or defaults to the ClientId
    var apiScopeId = builder.Configuration["AzureAd:ApiScopeId"] ?? clientId;
    if (apiScopeId != "00000000-0000-0000-0000-000000000000" && !string.IsNullOrEmpty(apiScopeId))
    {
        options.ProviderOptions.DefaultAccessTokenScopes.Add($"api://{apiScopeId}/access_as_user");
    }
    options.ProviderOptions.LoginMode = "redirect";
});

// Add SignalR connection service
builder.Services.AddScoped<PlatypusHubConnection>();

// Add local audio player for streaming mode
builder.Services.AddScoped<LocalAudioPlayerService>();

// Add state management
builder.Services.AddScoped<PlayerStateService>(sp =>
{
    var hub = sp.GetRequiredService<PlatypusHubConnection>();
    var localPlayer = sp.GetRequiredService<LocalAudioPlayerService>();
    var service = new PlayerStateService(hub, localPlayer);
    service.SetServerBaseUrl(apiBaseAddress);
    return service;
});

await builder.Build().RunAsync();
