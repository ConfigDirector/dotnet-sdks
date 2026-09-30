using ConfigDirector;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddConfigDirector();

var app = builder.Build();

app.MapGet("/checkout", (IConfigDirectorClient client) => client.GetValue("new-checkout", false) ? "new" : "classic");

app.Run();

public partial class Program;
