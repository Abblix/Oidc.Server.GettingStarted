// #region usings
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
// #endregion

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();

// #region authentication
var configuration = builder.Configuration;

builder.Services
    .AddAuthentication(options => configuration.Bind("Authentication", options))
    .AddCookie(options => configuration.Bind(CookieAuthenticationDefaults.AuthenticationScheme, options))
    .AddOpenIdConnect(options => configuration.Bind(OpenIdConnectDefaults.AuthenticationScheme, options));
// #endregion

// #region pipeline
var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();  // Enable authentication middleware
app.UseAuthorization();   // Enable authorization middleware

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
// #endregion
