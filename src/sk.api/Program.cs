using sk.api.Hubs;
using sk.api.Services;

var MyAllowSpecificOrigins = "skOrigin";

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi

builder.Services.AddCors(options =>
{
    options.AddPolicy(name: MyAllowSpecificOrigins,
                      policy =>
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

                          policy.WithOrigins([.. allowedOrigins])
                                .AllowAnyHeader()
                                .AllowAnyMethod();
                      });
});

builder.Services.AddOpenApi();
builder.Services.AddSignalR();
builder.Services.AddSingleton<GameHubService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// app.UseHttpsRedirection();
app.UseCors(MyAllowSpecificOrigins);
app.MapHub<GameHub>("/gameHub");


app.Run();

