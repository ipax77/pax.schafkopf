using sk.api.Hubs;
using sk.api.Services;
using sk.shared;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        var allowedOrigins = new HashSet<string>
        {
            "https://schafkopf.pax77.org",
        };

        if (builder.Environment.IsDevelopment())
        {
            allowedOrigins.Add("http://localhost:5027");
            allowedOrigins.Add("https://localhost:7233");
        }
        policy
            .WithOrigins([.. allowedOrigins])
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials()
            ;
    });
});


// builder.AddServiceDefaults(); // aspire

builder.Services.AddOpenApi();
builder.Services.AddSignalR();
builder.Services.AddSingleton<GameHubService>();
builder.Services.AddSingleton<ConfiguredBotPolicy>();
builder.Services.AddSingleton<IGamePolicy>(services => services.GetRequiredService<ConfiguredBotPolicy>());
builder.Services.AddOptions<BotTurnOptions>().BindConfiguration("Bots")
    .Validate(o => o.PollInterval > TimeSpan.Zero && o.TurnDelay >= TimeSpan.Zero &&
        o.TrickDelay >= TimeSpan.Zero && o.DisconnectedTableRetention > TimeSpan.Zero)
    .ValidateOnStart();
builder.Services.AddHostedService<BotTurnScheduler>();

var app = builder.Build();

app.UseCors();
app.UseWebSockets();
// app.MapHealthChecks("/health"); // aspire

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

//app.UseHttpsRedirection();
app.MapHub<GameHub>("/gameHub");
app.MapGet("/bots/policy", (ConfiguredBotPolicy policy) => new
    { policy = policy.Id, trained = policy.Trained, fallbackCount = policy.FallbackCount });


app.Run();

