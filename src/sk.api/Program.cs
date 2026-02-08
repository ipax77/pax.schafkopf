using sk.api.Hubs;
using sk.api.Services;

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


app.Run();

