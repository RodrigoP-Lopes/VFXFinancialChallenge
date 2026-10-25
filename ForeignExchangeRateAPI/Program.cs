using ForeignExchangeRateAPI.Common;
using ForeignExchangeRateAPI.Data;
using ForeignExchangeRateAPI.Events;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

var connectionString = "Host=localhost;Port=5432;Database=vfx_exrates;Username=vfx;Password=" + builder.Configuration["POSTGRES_PASSWORD"];

builder.Services.AddControllers();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddDbContext<AppDbContext>(options => options.UseNpgsql(connectionString));
builder.Services.AddSingleton<IExchangeRateEventPublisher, RabbitMqExchangeRateEventPublisher>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

app.UseExceptionHandler();

if (app.Environment.IsDevelopment()) {
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
