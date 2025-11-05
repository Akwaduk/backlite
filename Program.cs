using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using backlite.Components;
using backlite.Components.Account;
using backlite.Data;
using backlite.Models;
using backlite.Services;
using backlite.Hubs;
using MudBlazor.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Add MudBlazor
builder.Services.AddMudServices();

// Minimal Identity setup for now
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<IdentityRedirectManager>();
builder.Services.AddScoped<AuthenticationStateProvider, IdentityRevalidatingAuthenticationStateProvider>();

builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = IdentityConstants.ApplicationScheme;
        options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
    })
    .AddIdentityCookies();

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ?? "Data Source=app.db";
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlite(connectionString));

builder.Services.AddIdentityCore<ApplicationUser>(options =>
    {
        options.SignIn.RequireConfirmedAccount = false; // Simplified for testing
        options.Stores.SchemaVersion = IdentitySchemaVersions.Version3;
    })
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddSignInManager()
    .AddDefaultTokenProviders();

builder.Services.AddSingleton<IEmailSender<ApplicationUser>, IdentityNoOpEmailSender>();

// Add SignalR and Controllers
builder.Services.AddSignalR();
builder.Services.AddControllers();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseMigrationsEndPoint();
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

// Map API controllers
app.MapControllers();

// Map SignalR hub
app.MapHub<JobHub>("/jobHub");

// Add additional endpoints required by the Identity /Account Razor components.
app.MapAdditionalIdentityEndpoints();

// Subscribe job event handler to job queue (commented out temporarily)
// TODO: Move this to a hosted service or configure differently
// var jobQueue = app.Services.GetRequiredService<IJobQueueService>();
// var jobEventHandler = app.Services.GetRequiredService<IJobEventHandler>();
// jobQueue.Subscribe(jobEventHandler);

// Ensure required directories exist (commented out for now)
// TODO: Re-enable directory creation once configuration is working
// try 
// {
//     var config = app.Services.GetRequiredService<IConfiguration>();
//     var dbConfig = config.GetSection("DbBackupManager").Get<DbBackupManagerConfig>();
//     if (dbConfig != null)
//     {
//         Directory.CreateDirectory(dbConfig.Storage.TempWorkspaceDirectory);
//         Directory.CreateDirectory(dbConfig.Storage.BackupRootDirectory);
//     }
// }
// catch (Exception ex)
// {
//     // Log error but don't fail startup
//     Console.WriteLine($"Warning: Could not create directories: {ex.Message}");
// }

app.Run();
