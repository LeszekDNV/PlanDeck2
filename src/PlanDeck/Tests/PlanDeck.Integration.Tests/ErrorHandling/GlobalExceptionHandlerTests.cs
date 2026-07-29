using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using PlanDeck.Application.Abstractions;
using PlanDeck.Application.Account;
using PlanDeck.Server;
using PlanDeck.Server.Diagnostics;

namespace PlanDeck.ErrorHandling.IntegrationTests;

[TestFixture]
public sealed class GlobalExceptionHandlerTests
{
    private const string ExceptionMessage = "sensitive-test-exception";

    [Test]
    public async Task StartedResponse_IsNotRewrittenOrLogged()
    {
        var logs = new CapturingLoggerProvider();
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(logs));
        var problemDetails = new TrackingProblemDetailsService();
        var responseFeature = new StartedResponseFeature();
        var features = new FeatureCollection();
        features.Set<IHttpResponseFeature>(responseFeature);
        var context = new DefaultHttpContext(features);
        var handler = new GlobalExceptionHandler(
            loggerFactory.CreateLogger<GlobalExceptionHandler>(),
            problemDetails);

        var handled = await handler.TryHandleAsync(
            context,
            new InvalidOperationException(ExceptionMessage),
            CancellationToken.None);

        responseFeature.Body.Position = 0;
        using var reader = new StreamReader(responseFeature.Body, leaveOpen: true);
        var body = await reader.ReadToEndAsync();

        Assert.Multiple(() =>
        {
            Assert.That(handled, Is.False);
            Assert.That(responseFeature.StatusCode, Is.EqualTo(StatusCodes.Status202Accepted));
            Assert.That(responseFeature.Headers.ContentType.ToString(), Is.EqualTo("text/plain"));
            Assert.That(body, Is.EqualTo(StartedResponseFeature.OriginalBody));
            Assert.That(problemDetails.WasCalled, Is.False);
            Assert.That(logs.ErrorEntries, Is.Empty);
        });
    }

    [Test]
    public async Task ApiFailure_ReturnsSafeProblemDetailsWithCorrelatedTraceId()
    {
        var logs = new CapturingLoggerProvider();
        using var factory = CreateFactory(logs);
        using var client = CreateClient(factory);

        using var response = await client.PostAsJsonAsync(
            "/account/register",
            CreateRegisterRequest());

        Assert.That(
            response.StatusCode,
            Is.EqualTo(HttpStatusCode.InternalServerError));
        Assert.That(
            response.Content.Headers.ContentType?.MediaType,
            Is.EqualTo("application/problem+json"));

        var body = await response.Content.ReadAsStringAsync();
        using var problem = JsonDocument.Parse(body);
        var root = problem.RootElement;
        var traceId = root.GetProperty("traceId").GetString();

        Assert.Multiple(() =>
        {
            Assert.That(root.GetProperty("status").GetInt32(), Is.EqualTo(500));
            Assert.That(root.GetProperty("title").GetString(), Is.EqualTo("Server error"));
            Assert.That(traceId, Is.Not.Null.And.Not.Empty);
            Assert.That(body, Does.Not.Contain(ExceptionMessage));
            Assert.That(body, Does.Not.Contain("<!doctype html>"));
        });
        AssertSingleCorrelatedLog(logs, traceId!);
    }

    [Test]
    public async Task BrowserNavigationFailure_ReturnsSafeHtmlWithCorrelatedTraceId()
    {
        var logs = new CapturingLoggerProvider();
        using var factory = CreateFactory(logs);
        using var client = CreateClient(factory);
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/account/register");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
        request.Headers.Add("Sec-Fetch-Mode", "navigate");
        request.Content = new StringContent(
            JsonSerializer.Serialize(CreateRegisterRequest()),
            Encoding.UTF8,
            "application/json");

        using var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();
        var log = logs.ErrorEntries.Single();
        var traceId = log.Properties["TraceId"];

        Assert.Multiple(() =>
        {
            Assert.That(
                response.StatusCode,
                Is.EqualTo(HttpStatusCode.InternalServerError));
            Assert.That(
                response.Content.Headers.ContentType?.MediaType,
                Is.EqualTo("text/html"));
            Assert.That(body, Does.Contain("<!doctype html>"));
            Assert.That(body, Does.Contain(traceId));
            Assert.That(body, Does.Not.Contain(ExceptionMessage));
            Assert.That(body, Does.Not.Contain("blazor.webassembly"));
            Assert.That(log.Exception, Is.TypeOf<InvalidOperationException>());
        });
        AssertSingleCorrelatedLog(logs, traceId);
    }

    private static WebApplicationFactory<ServerEntryPoint> CreateFactory(
        CapturingLoggerProvider logs)
    {
        return new WebApplicationFactory<ServerEntryPoint>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Testing");
                builder.UseSetting(
                    "ConnectionStrings:DefaultConnection",
                    "Server=localhost;Database=PlanDeckErrorTests;"
                    + "User Id=sa;Password=LocalOnly_123!;TrustServerCertificate=True");
                builder.UseSetting("Authentication:Microsoft:Required", bool.FalseString);
                builder.UseSetting("RateLimiting:Disable", bool.TrueString);
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<ILocalAccountService>();
                    services.AddScoped<ILocalAccountService, ThrowingLocalAccountService>();
                    services.AddSingleton<ILoggerProvider>(logs);
                });
            });
    }

    private static HttpClient CreateClient(
        WebApplicationFactory<ServerEntryPoint> factory)
    {
        return factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
    }

    private static LocalRegisterRequest CreateRegisterRequest() =>
        new(
            "error-test@example.com",
            "Error",
            "Test",
            "errortest",
            "StrongPass123!");

    private static void AssertSingleCorrelatedLog(
        CapturingLoggerProvider logs,
        string traceId)
    {
        var entries = logs.ErrorEntries;
        Assert.That(entries, Has.Count.EqualTo(1));
        Assert.Multiple(() =>
        {
            Assert.That(entries[0].Properties["TraceId"], Is.EqualTo(traceId));
            Assert.That(entries[0].Exception, Is.TypeOf<InvalidOperationException>());
        });
    }

    private sealed class ThrowingLocalAccountService : ILocalAccountService
    {
        public Task<LocalRegisterResult> RegisterAsync(
            LocalRegisterRequest request,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException(ExceptionMessage);
        }
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly ConcurrentQueue<LogEntry> _entries = new();

        public IReadOnlyList<LogEntry> ErrorEntries =>
            _entries.Where(entry => entry.Level == LogLevel.Error).ToList();

        public ILogger CreateLogger(string categoryName) =>
            new CapturingLogger(_entries);

        public void Dispose()
        {
        }

        private sealed class CapturingLogger(
            ConcurrentQueue<LogEntry> entries) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                var properties = state is IEnumerable<KeyValuePair<string, object?>> values
                    ? values
                        .Where(value => value.Key != "{OriginalFormat}")
                        .ToDictionary(
                            value => value.Key,
                            value => value.Value?.ToString() ?? string.Empty)
                    : new Dictionary<string, string>();

                entries.Enqueue(new LogEntry(logLevel, exception, properties));
            }
        }
    }

    private sealed record LogEntry(
        LogLevel Level,
        Exception? Exception,
        IReadOnlyDictionary<string, string> Properties);

    private sealed class TrackingProblemDetailsService : IProblemDetailsService
    {
        public bool WasCalled { get; private set; }

        public ValueTask WriteAsync(ProblemDetailsContext context)
        {
            WasCalled = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class StartedResponseFeature : IHttpResponseFeature
    {
        public const string OriginalBody = "response-already-started";

        public StartedResponseFeature()
        {
            Headers.ContentType = "text/plain";
            Body.Write(Encoding.UTF8.GetBytes(OriginalBody));
        }

        public int StatusCode { get; set; } = StatusCodes.Status202Accepted;

        public string? ReasonPhrase { get; set; }

        public IHeaderDictionary Headers { get; set; } = new HeaderDictionary();

        public Stream Body { get; set; } = new MemoryStream();

        public bool HasStarted => true;

        public void OnStarting(Func<object, Task> callback, object state)
        {
        }

        public void OnCompleted(Func<object, Task> callback, object state)
        {
        }
    }
}
