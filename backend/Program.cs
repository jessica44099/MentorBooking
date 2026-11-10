var builder = WebApplication.CreateBuilder(args);

// Register API controllers.
builder.Services.AddControllers();

// Enable API documentation.
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Swagger is available during development.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
else
{
    // Redirect HTTP to HTTPS outside development.
    app.UseHttpsRedirection();
}

app.UseAuthorization();

// Connect controller endpoints.
app.MapControllers();

app.Run();