
using MentorBooking.Api.Data;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Register API controllers.
builder.Services.AddControllers();

// Swagger API documentation.
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// SQLite database configuration.
var connectionString = builder.Configuration
    .GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException(
        "Database connection string is missing.");

builder.Services.AddDbContext<AppDbContext>(options =>
{
    options.UseSqlite(connectionString);
});

var app = builder.Build();

// Explicit development-only seeding command.
if (builder.Configuration.GetValue<bool>("seed"))
{
    if (!app.Environment.IsDevelopment())
    {
        throw new InvalidOperationException(
            "Database seeding is allowed only in Development.");
    }

    await using var scope = app.Services.CreateAsyncScope();

    var db = scope.ServiceProvider
        .GetRequiredService<AppDbContext>();

    await DevelopmentSeeder.RunAsync(db);

    // Exit without starting the web server.
    return;
}

// Enable Swagger only in development.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
else
{
    app.UseHttpsRedirection();
}

app.UseAuthorization();

app.MapControllers();

app.Run();
