using RelayHook.Core;
using RelayHook.Core.Abstractions;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("RelayHook") ??
    throw new InvalidOperationException("ConnectionStrings:RelayHook is required.");

builder.Services
    .AddRelayHook()
    .UseSqlServer(connectionString)
    .AddClient("UnauthenticatedPartner", client =>
    {
        client.AddEndpoint("primary", endpoint =>
        {
            endpoint.Url = new Uri("https://partner.example.com/callbacks");
            endpoint.UseNoAuthentication();
        });
    })
    .AddClient("ApiKeyPartner", client =>
    {
        client.AddEndpoint("primary", endpoint =>
        {
            endpoint.Url = new Uri("https://api-key-partner.example.com/callbacks");
            endpoint.UseApiKey(authentication =>
            {
                authentication.HeaderName = "X-API-Key";
                authentication.SecretReference = "Callbacks:ApiKeyPartner:ApiKey";
            });
        });
    })
    .AddClient("OAuthPartner", client =>
    {
        client.AddEndpoint("primary", endpoint =>
        {
            endpoint.Url = new Uri("https://oauth-partner.example.com/callbacks");
            endpoint.UseOAuth2ClientCredentials(authentication =>
            {
                authentication.TokenEndpoint = "https://identity.example.com/oauth/token";
                authentication.ClientId = "relayhook-sample";
                authentication.ClientSecretReference = "Callbacks:OAuthPartner:ClientSecret";
                authentication.Scopes = ["callbacks.write"];
            });
        });
    });

builder.Services.AddRelayHookHealthCheck();

var app = builder.Build();

app.MapPost(
    "/payments/{paymentId:guid}/complete",
    async (Guid paymentId, ICallbackClient callbacks, CancellationToken cancellationToken) =>
    {
        var callbackId = await callbacks.EnqueueAsync(
            "UnauthenticatedPartner",
            "payment.completed",
            new { PaymentId = paymentId, Status = "Completed" },
            new CallbackEnqueueOptions
            {
                CorrelationId = paymentId.ToString("N"),
                IdempotencyKey = $"payment-{paymentId:N}"
            },
            cancellationToken);

        return Results.Accepted(value: new { CallbackId = callbackId });
    });

app.MapHealthChecks("/health/relayhook");
app.Run();
