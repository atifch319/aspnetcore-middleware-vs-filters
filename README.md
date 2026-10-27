# ASP.NET Core Middleware vs Filters

A small ASP.NET Core Web API that shows where middleware, authentication, authorization, and MVC filters run. The sample is a products API with an in-memory catalog, three pieces of middleware, one action filter, and xUnit tests. It is meant to be cloned, run, and read.

The API uses the built-in host, dependency injection, and logging. It does not use a database or an external identity provider.

## Learning objectives

- Catch unhandled exceptions in middleware and return a safe HTTP 500 [ProblemDetails](https://www.rfc-editor.org/rfc/rfc9457) body.
- Log the method, path, status code, elapsed time, and correlation ID for every request.
- Accept a safe `X-Correlation-ID` or generate one, then make that value available to logs and error responses.
- Validate a create-product request in an action filter, after model binding and before the action.
- See how authentication middleware, authorization middleware, and MVC filters occupy different stages.
- Read the request pipeline in the order it actually runs.
- Test the middleware and the filter without a database or a remote service.

## Technology stack

| Piece | Choice |
| --- | --- |
| SDK | .NET 10 (`global.json` rolls forward on the 10.0 feature band) |
| Language | C# 14 |
| App | ASP.NET Core Web API, controller-based |
| Host services | Built-in dependency injection, configuration, and logging |
| Tests | xUnit 2.9.3, `Microsoft.NET.Test.Sdk` 17.14.1, `Microsoft.AspNetCore.Mvc.Testing` 10.0.12 |
| Nullable references | Enabled |
| Implicit usings | Enabled |

The API project has no extra NuGet packages. Test packages are the xUnit template plus `Microsoft.AspNetCore.Mvc.Testing`.

## Solution structure

```text
aspnetcore-middleware-vs-filters/
├── src/
│   └── MiddlewareFilters.Api/
│       ├── Authentication/
│       │   └── DemoAuthenticationHandler.cs
│       ├── Controllers/
│       │   └── ProductsController.cs
│       ├── Filters/
│       │   └── ValidateProductActionFilter.cs
│       ├── Middleware/
│       │   ├── CorrelationIdMiddleware.cs
│       │   ├── GlobalExceptionMiddleware.cs
│       │   └── RequestResponseLoggingMiddleware.cs
│       ├── Models/
│       │   ├── ApiErrorResponse.cs
│       │   ├── CreateProductRequest.cs
│       │   └── Product.cs
│       ├── Properties/
│       │   └── launchSettings.json
│       ├── Services/
│       │   └── ProductService.cs
│       ├── MiddlewareFilters.Api.csproj
│       ├── MiddlewareFilters.Api.http
│       ├── Program.cs
│       ├── appsettings.json
│       └── appsettings.Development.json
├── tests/
│   └── MiddlewareFilters.Api.Tests/
│       ├── Filters/
│       ├── Integration/
│       ├── Middleware/
│       ├── Support/
│       └── MiddlewareFilters.Api.Tests.csproj
├── .gitignore
├── global.json
├── README.md
└── aspnetcore-middleware-vs-filters.slnx
```

`ProductService` is the in-memory catalog. It is registered as a singleton. There is no repository interface, because nothing else needs to replace it.

## What is middleware?

Middleware is a component in the HTTP pipeline. Each component can run code, call the next component, and then run code again after the rest of the pipeline returns. That wrap is why one middleware can start a timer before the rest of the app and log the status code afterward.

Middleware sees an `HttpContext`. It does not receive MVC action arguments. It does not know which action was selected unless endpoint routing has already stored that endpoint on the context. If middleware needs the JSON body, it has to read the stream itself. Doing that consumes the stream, so model binding later in the pipeline cannot read it unless the middleware buffers and rewinds the body.

Middleware registered in `Program.cs` runs for requests that reach it, including requests that never enter MVC.

## What are MVC filters?

MVC filters run inside MVC, after routing has selected an action. They are the action-invocation pipeline, not a second copy of the HTTP pipeline.

[Filters in ASP.NET Core](https://learn.microsoft.com/aspnet/core/mvc/controllers/filters) run at these stages:

| Filter | When it runs |
| --- | --- |
| Authorization filters | First inside MVC. They can short-circuit the rest of the filter pipeline. They have a before method and no after method. Exceptions thrown here are not handled by exception filters. |
| Resource filters | After authorization filters. `OnResourceExecuting` runs before model binding. `OnResourceExecuted` runs after the rest of MVC, so a resource filter can wrap almost the entire MVC invocation. |
| Action filters | Immediately before and after the action method. Model binding has already created the action arguments. An action filter can inspect or replace those arguments and can set `context.Result` instead of calling the action. |
| Exception filters | Only when an exception is still unhandled during MVC action invocation. They can handle exceptions from controller creation, model binding, action filters, and the action method. They do not catch exceptions from middleware, routing, resource filters, result filters, or execution of the action result. |
| Result filters | Immediately before and after an action result runs, and only when an action or action filter produced a result. `IResultFilter` does not run when an authorization filter, resource filter, or exception filter short-circuits. `IAlwaysRunResultFilter` also runs for those short-circuits. |

An action filter can see `CreateProductRequest` because model binding already did that work. Middleware would have to parse the body again to make the same decision.

## Middleware vs filters

| Question | Middleware | MVC filter |
| --- | --- | --- |
| Where does it run? | In the HTTP pipeline, around everything registered after it. | Inside MVC, after an action is selected, at one filter stage. |
| Which requests does it see? | Requests that reach that position in the pipeline. | The MVC actions it is applied to. |
| Can it run before and after the rest? | Yes. It calls `next` and continues when `next` returns. | Yes, within its stage. An action filter's delegate runs the action and later filters. |
| Can it short-circuit? | Yes, by skipping `next` and writing the response. | Yes. An action filter sets `context.Result` and does not call `next`. |
| Can it see a bound action argument? | Only if it reads and parses the request itself. | Yes. Action filters run after model binding. |
| What exceptions can it catch? | Exceptions thrown by later middleware and by the endpoint. | Exception filters see part of MVC action invocation. They do not see the rest of the host pipeline. |
| Typical work | Correlation IDs, request logs, security headers, compression, authentication, global error responses. | Rules that need the bound model, the selected action, or the action result. |

Authentication middleware and authorization middleware are middleware. An MVC authorization filter is a filter. They are easy to conflate because both can produce 401 or 403. The [Authentication and authorization](#authentication-and-authorization) section separates them.

## Request pipeline

Registration order in `Program.cs`:

1. `CorrelationIdMiddleware`
2. `RequestResponseLoggingMiddleware`
3. `GlobalExceptionMiddleware`
4. `UseRouting`
5. `UseAuthentication`
6. `UseAuthorization`
7. `MapControllers`

`UseRouting` is explicit so the three custom middleware components stay outside endpoint matching. If the app did not call `UseRouting`, `WebApplication` would insert routing before user middleware. Calling it here keeps correlation, logging, and exception handling around routing, authentication, authorization, and MVC.

In Development, the host also places the developer exception page outside this pipeline. `GlobalExceptionMiddleware` handles the exception and does not rethrow, so the client still receives the ProblemDetails body from this sample. The real exception is written to the server log.

```text
HTTP request
    |
    |  Development only, added by the host outside this app's registrations:
    |  Developer Exception Page. Handled exceptions do not reach it.
    v
CorrelationIdMiddleware
    |  before: accept or create X-Correlation-ID
    |          store it on HttpContext.Items
    |          register the response header
    v
RequestResponseLoggingMiddleware
    |  before: start Stopwatch
    v
GlobalExceptionMiddleware
    |  before: try
    v
UseRouting
    |  select the endpoint; do not execute it yet
    v
UseAuthentication
    |  set HttpContext.User when a scheme succeeds
    v
UseAuthorization
    |  enforce [Authorize] metadata on the selected endpoint
    v
MVC action invocation
    |
    |  Authorization filters
    |  Resource filters, before model binding
    |  Model binding
    |  Action filters, before     <-- ValidateProductActionFilter on POST
    |  Action method
    |  Action filters, after
    |  Exception filters          <-- only if MVC still has an unhandled exception
    |  Result filters, before     <-- only when there is a result to execute
    |  IActionResult execution
    |  Result filters, after
    |  Resource filters, after
    v
GlobalExceptionMiddleware
    |  after: on an unhandled exception, log it and write HTTP 500 ProblemDetails
    |         on client cancellation, do not write a second response
    v
RequestResponseLoggingMiddleware
    |  after: log method, path, status, elapsed milliseconds, correlation ID
    v
CorrelationIdMiddleware
    |  after: write X-Correlation-ID when the response starts
    v
HTTP response
```

Middleware order on the way back is the reverse of registration order. The logging middleware sits outside the exception middleware so its completion log sees the 500 that the exception middleware wrote. The exception is logged once, by `GlobalExceptionMiddleware`. The logging middleware does not log that exception again.

Exception filters are not a stand-in for this middleware. An exception filter does not run for exceptions thrown in middleware, during routing, in resource filters, in result filters, or while the action result is executing. A serializer failure, an authentication middleware failure, or an exception after MVC has started writing the response can skip exception filters entirely. Global exception middleware is the component that wraps those stages. Use an exception filter only when the handling really depends on which MVC action was selected.

## Correlation ID middleware

`CorrelationIdMiddleware` reads a single `X-Correlation-ID` request header.

The value is accepted when it is 1 to 64 characters and contains only ASCII letters, digits, hyphens, and underscores. A GUID such as `3f1c0b2e-7a4d-4e91-9c2a-6b8d0e1f4a77` is valid. `portfolio-demo-001` is valid.

Anything else is discarded: a missing header, more than one header value, spaces, slashes, punctuation, or a value longer than 64 characters. The middleware then generates `Guid.NewGuid()` in `D` format. The invalid text is not written to the response and is not written to the log. That avoids reflecting untrusted header content into another header or into a log file. Raw CR and LF are already rejected by the server's header parser; this check is a second constraint so a legal-but-untrusted value still cannot be copied through.

The chosen value is stored in `HttpContext.Items` under `CorrelationId`. A logging scope carries the same name. The response header is added from `Response.OnStarting`, which still runs if a later component clears headers before the response starts.

## Request and response logging middleware

`RequestResponseLoggingMiddleware` writes one information log when the request finishes:

```text
HTTP {Method} {Path} responded {StatusCode} in {ElapsedMilliseconds} ms. CorrelationId: {CorrelationId}
```

The elapsed time comes from `Stopwatch`. The path is `Request.Path` only. The query string is omitted because queries often carry tokens. The middleware does not read the body, cookies, or `Authorization` header, and it does not wrap or replace the response stream.

If the client aborts the request, the log says the request was canceled and includes the elapsed time. If a later component throws and nobody has set an error status, the completion log records status 500. The exception object is not passed to that log line. `GlobalExceptionMiddleware` is the component that logs the exception, which keeps the pipeline from recording the same failure twice.

## Global exception middleware

`GlobalExceptionMiddleware` catches exceptions from the rest of the pipeline.

- `OperationCanceledException` while `RequestAborted` is set is treated as a client cancellation. The middleware does not write an error body.
- If `Response.HasStarted` is already true, the middleware logs the exception and rethrows it. The body that already started is left as it is.
- Otherwise it logs the exception with `ILogger.LogError`, clears the unstarted response, and writes HTTP 500 `application/problem+json`.

The response is a ProblemDetails document:

- `type` is the RFC 9110 section for HTTP 500
- `title` is `An unexpected error occurred.`
- `status` is `500`
- `detail` tells the caller to include the correlation ID
- `instance` is the request path
- `correlationId` is the id stored by the correlation middleware, when one exists

The exception message and the stack trace are not written to the response in Development or in Production. The log entry still has both, because the exception is passed to `ILogger`. `Cache-Control: no-store` is set on the error response.

`GET /api/products/diagnostics/exception` throws `InvalidOperationException` only when the host environment is `Development` or `Testing`. In every other environment the action returns 404 and does not throw. The route still exists in Production; it does not run the throwing branch. Local `dotnet run` uses the Development launch profile, so the demonstration works on a developer machine. Do not point a Production environment at this action and expect a stack trace. The middleware would still hide that stack trace, and the action will not throw outside Development and Testing.

## Action filter

`ValidateProductActionFilter` is an `IAsyncActionFilter` applied to `POST /api/products` with `[ServiceFilter(typeof(ValidateProductActionFilter))]`. The filter is registered in dependency injection as a scoped service. `ServiceFilter` is what resolves it. A plain attribute reference would not go through DI.

The filter looks at the bound `CreateProductRequest` and rejects the request before the action runs when:

- `name` is missing or whitespace
- `price` is zero or negative

The response is HTTP 400 `application/problem+json`, using `ApiErrorResponse`. The body includes the correlation ID and an `errors` object. The action method is not called, so `ProductService` does not store the invalid product.

`CreateProductRequest` deliberately has no validation attributes. `[ApiController]` already turns a missing body, malformed JSON, or a value that cannot be bound into a 400 during model binding. A price of `"not-a-number"` is rejected there, before this filter runs, and the response is the framework's validation problem. A `[Required]` or `[Range]` attribute would also make `ModelState` invalid, and the framework's model-state filter would short-circuit before this action filter. Putting the same two rules in both places would hide the filter and duplicate the rule.

Those two rules are a normal fit for data annotations in an ordinary API. They live in the filter in this project so the action-filter stage is visible and can be unit tested with the bound object. Reach for an action filter when the decision needs the bound argument, `HttpContext`, or another MVC detail, and when the controller should stay focused on the HTTP result. Reach for annotations when the rule is a property constraint and the automatic 400 response is the behavior you want.

`ProductService.Add` still refuses a blank name or a non-positive price. That guard keeps the in-memory list consistent if a future action calls the service without the filter. That path is a programming error, so it surfaces as the safe HTTP 500 from the exception middleware rather than as a validation response.

## Authentication and authorization

Three different stages show up around `GET /api/products/secure-ping`:

1. **Authentication middleware** (`UseAuthentication`) asks the registered scheme to establish `HttpContext.User`.
2. **Authorization middleware** (`UseAuthorization`) reads `[Authorize]` metadata on the endpoint selected by routing. An anonymous request is challenged here and the MVC action does not run.
3. **MVC authorization filters** run later, only if the request has already entered MVC. The built-in authorization filter calls the same authorization system. This sample does not add a custom authorization filter. Prefer authorization policies over a custom filter that reimplements allow and deny.

`DemoAuthenticationHandler` exists so `[Authorize]` has a scheme to call. It is a pipeline illustration, not an authentication system.

- There is no password, API key, signing key, or identity provider.
- In `Development` and `Testing`, one header value `X-Demo-User` is accepted when it is 1 to 64 characters of letters, digits, `.`, `_`, or `-`. That value becomes `ClaimTypes.Name`.
- In every other environment the handler authenticates nobody. `[Authorize]` then fails closed with HTTP 401, including when the demo header is present.

Do not deploy this handler as a security control. A production API needs a real scheme, such as cookies or JWT bearer authentication from a trusted issuer, plus HTTPS. Those packages and the issuer configuration are intentionally absent so the sample stays runnable and does not pretend to be an identity provider.

`UseAuthentication` and `UseAuthorization` are registered after `UseRouting` and before `MapControllers`. Authorization middleware has to run after routing so it can see the selected endpoint, and before the endpoint runs so it can block the action.

## API endpoints

The launch profile listens on `http://localhost:5133`. Commands below assume the API is already running from the repository root.

| Method | Path | What it shows |
| --- | --- | --- |
| `GET` | `/api/products` | Returns the in-memory catalog. |
| `GET` | `/api/products/{id}` | Returns one product, or 404. |
| `POST` | `/api/products` | Creates a product after the action filter accepts it. |
| `GET` | `/api/products/diagnostics/exception` | Throws in Development and Testing so the exception middleware can respond. Returns 404 otherwise. |
| `GET` | `/api/products/secure-ping` | Requires an authenticated user. |

The catalog starts with two products: Notebook at 12.50, and Mechanical pencil at 3.25.

### Example requests

List products:

```powershell
Invoke-RestMethod -Uri http://localhost:5133/api/products
```

Get one product:

```powershell
Invoke-RestMethod -Uri http://localhost:5133/api/products/1
```

Create a product:

```powershell
Invoke-RestMethod -Uri http://localhost:5133/api/products -Method Post -ContentType "application/json" -Body '{"name":"Desk lamp","price":19.99}'
```

Send a correlation ID and print the response header:

```powershell
$response = Invoke-WebRequest -Uri http://localhost:5133/api/products -Headers @{ "X-Correlation-ID" = "portfolio-demo-001" } -UseBasicParsing
$response.Headers["X-Correlation-ID"]
```

Call an endpoint that returns a non-success status. `HttpClient` is used here because Windows PowerShell throws from `Invoke-WebRequest` and `Invoke-RestMethod` when the status is 404, 400, or 500.

```powershell
if (-not ("System.Net.Http.HttpClient" -as [type])) {
    Add-Type -AssemblyName System.Net.Http
}

$client = [System.Net.Http.HttpClient]::new()

function Show-ApiResult([System.Net.Http.HttpResponseMessage]$response) {
    Write-Host ("Status: " + [int]$response.StatusCode)
    if ($response.Headers.Contains("X-Correlation-ID")) {
        Write-Host ("X-Correlation-ID: " + $response.Headers.GetValues("X-Correlation-ID"))
    }
    $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
}

$missing = $client.GetAsync("http://localhost:5133/api/products/99999").GetAwaiter().GetResult()
Show-ApiResult $missing

$invalid = New-Object System.Net.Http.HttpRequestMessage([System.Net.Http.HttpMethod]::Post, "http://localhost:5133/api/products")
$invalid.Content = New-Object System.Net.Http.StringContent('{"name":"","price":5}', [System.Text.Encoding]::UTF8, "application/json")
Show-ApiResult ($client.SendAsync($invalid).GetAwaiter().GetResult())

$failed = $client.GetAsync("http://localhost:5133/api/products/diagnostics/exception").GetAwaiter().GetResult()
Show-ApiResult $failed

$secure = New-Object System.Net.Http.HttpRequestMessage([System.Net.Http.HttpMethod]::Get, "http://localhost:5133/api/products/secure-ping")
$secure.Headers.TryAddWithoutValidation("X-Demo-User", "portfolio.reader")
Show-ApiResult ($client.SendAsync($secure).GetAwaiter().GetResult())
```

`src/MiddlewareFilters.Api/MiddlewareFilters.Api.http` has the same calls for an editor that supports `.http` files.

## Example responses

`GET /api/products/1`

```json
{
  "id": 1,
  "name": "Notebook",
  "price": 12.50
}
```

`POST /api/products` with `{"name":"Desk lamp","price":19.99}` returns `201 Created` and a `Location` header such as `/api/products/3`:

```json
{
  "id": 3,
  "name": "Desk lamp",
  "price": 19.99
}
```

`POST /api/products` with an empty name returns `400`:

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "One or more validation errors occurred.",
  "status": 400,
  "detail": "The product request is invalid.",
  "instance": "/api/products",
  "correlationId": "portfolio-demo-001",
  "errors": {
    "name": [
      "Product name is required."
    ]
  }
}
```

A non-positive price uses the same shape with an `errors.price` entry: `Price must be greater than zero.`

`GET /api/products/99999` returns `404`:

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.5",
  "title": "Product not found.",
  "status": 404,
  "detail": "No product exists with id 99999.",
  "instance": "/api/products/99999",
  "correlationId": "generated-or-supplied-id"
}
```

`GET /api/products/diagnostics/exception` in Development returns `500`:

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.6.1",
  "title": "An unexpected error occurred.",
  "status": 500,
  "detail": "The request could not be completed. Include the correlation ID if you contact support.",
  "instance": "/api/products/diagnostics/exception",
  "correlationId": "generated-or-supplied-id"
}
```

The same call outside Development and Testing returns `404` and does not throw.

`GET /api/products/secure-ping` without a demo user returns `401`. In Development, the same call with `X-Demo-User: portfolio.reader` returns `200`:

```json
{
  "message": "Authorized request reached the action.",
  "user": "portfolio.reader"
}
```

Expected errors from the filter and the missing-product path use `ApiErrorResponse`. Unhandled exceptions use `ProblemDetails`. Both are written as `application/problem+json`. Success payloads stay `application/json`.

## How to run the API

From the repository root, in PowerShell:

```powershell
dotnet restore .\aspnetcore-middleware-vs-filters.slnx
dotnet build .\aspnetcore-middleware-vs-filters.slnx
dotnet run --project .\src\MiddlewareFilters.Api\MiddlewareFilters.Api.csproj
```

The Development launch profile binds `http://localhost:5133`. Stop the process with `Ctrl+C`.

The console shows one completion line per request. That line includes the correlation ID. The correlation middleware also pushes a `CorrelationId` logging scope for providers that record scope state. The built-in simple console formatter prints a dictionary scope as its type name, so this sample leaves console scope formatting off and keeps the identifier in the message. An unhandled exception produces one error log with the exception. That detail stays on the server.

## How to execute tests

From the repository root:

```powershell
dotnet test .\aspnetcore-middleware-vs-filters.slnx
```

The tests use `WebApplicationFactory` and in-memory middleware invocations. They do not open a port, a database, or a remote service. Integration tests cover the Development pipeline and a Production host. The Production host checks that the diagnostic action does not throw and that the demo header does not authenticate anyone.

## Best practices

- Register correlation before components that log or return errors, so those components share one identifier.
- Register request logging outside exception handling when the log should record the status code written by the exception handler.
- Keep exception responses stable across environments. Put the exception and the stack trace in the log.
- Call `UseRouting` before `UseAuthentication` and `UseAuthorization`, and call both before the endpoint.
- Validate bound action input in an action filter or with model validation. Leave the controller action to perform the operation and choose the success result.
- Restrict generated or reflected header values to a small alphabet.
- Gate demonstration endpoints that throw. Returning 404 outside Development and Testing keeps the sample from being an open fault-injection route.
- Prefer the authorization system and `[Authorize]` over a hand-written authorization filter.

## Common mistakes

- Reading the request body in logging middleware and leaving an empty stream for model binding.
- Writing a second error response after `HasStarted` is true, which throws and hides the original failure.
- Returning `exception.Message` or `exception.ToString()` to the client.
- Copying an unsanitized correlation header into the response or into a log message.
- Treating an MVC exception filter as coverage for middleware, routing, and result execution.
- Applying `[Authorize]` without `UseAuthorization`, or calling `UseAuthorization` before routing, so the middleware cannot see the endpoint.
- Putting a single action's validation rule in middleware and re-implementing JSON parsing there.
- Logging the `Authorization` header, cookies, or a password field "temporarily" and leaving it in the pipeline.
- Shipping a demo authentication handler that accepts a caller-supplied user name in Production.

## When to use middleware

Use middleware when the behavior belongs to the HTTP request itself and should run whether or not MVC handles it.

- Correlation and trace identifiers.
- Request timing and status logs.
- A last-resort exception response for the whole pipeline.
- Authentication and authorization, via the built-in middleware.
- Security headers, HTTPS redirection, compression, caching, and rate limiting.

The work has to stay cheap. It runs for every request that reaches it.

## When to use filters

Use an MVC filter when the behavior needs MVC's action pipeline.

- A check on a model-bound argument, as `ValidateProductActionFilter` does.
- Short-circuiting one action with an `IActionResult` while other endpoints stay untouched.
- Changing or wrapping the action result for a controller.
- An exception policy that genuinely differs between actions. Even then, keep a middleware handler behind it for everything the filter cannot see.

Resource filters fit work that must wrap model binding, such as a cache that can skip the action. Result filters fit work that must wrap formatter execution. Authorization policies fit access control better than a custom authorization filter.

## Performance and security considerations

Every request pays for the three custom middleware components. Each one avoids extra I/O: no request-body buffering, no query-string capture, and no large log payloads. The correlation check is a short character scan. The log is one structured information event. The exception path allocates a response only after a failure.

The in-memory catalog uses a lock so concurrent requests do not corrupt the list. That lock is process-local. It is not a distributed store, and the data disappears when the process stops.

Security choices in this sample:

- Error bodies omit exception details in every environment.
- The diagnostic throw is limited to Development and Testing.
- Demo authentication fails closed outside those environments and stores no secret.
- Logs omit the body, the query, cookies, and the authorization header.
- Correlation values that fail the character check are replaced, not reflected.
- The HTTP 500 response sends `Cache-Control: no-store`.

Production still needs HTTPS, HSTS, a real authentication scheme, and an exception log store that can retain the correlation ID. None of those are configured here. `AllowedHosts` is `*` so local runs are simple; tighten it for a real deployment.

## Benchmarking or production measurement caveats

This repository does not contain benchmark results, and it does not claim that the pipeline was measured.

A micro-benchmark of middleware in isolation will mostly time allocations and a stopped `Stopwatch`. It will not predict production latency. Request time in a real API is usually dominated by the work inside the action, by logging I/O, and by downstream calls. This sample has none of those downstream calls.

Before changing a production pipeline based on this sample, measure that application. Useful signals are request duration, allocation rate, log volume, and error rate under a representative load. `dotnet-counters` and a load generator aimed at a deployed environment are a better fit than a number invented for this README. Compare traces that share a correlation ID with the access log before deciding that a middleware component is the expensive part.

Logging itself can become the cost. A log line that serializes a body, or a logger that blocks on a remote sink, will dwarf the cost of this pipeline. Keep the message small and the sink asynchronous in production.

## Key takeaways

- Middleware wraps the rest of the pipeline. Code before `next` runs on the way in. Code after `next` runs on the way out, including when a later component wrote the status code.
- MVC filters run only after an action is selected, and each filter type has its own stage around model binding, the action, or the result.
- Authentication middleware establishes the user. Authorization middleware enforces endpoint `[Authorize]` metadata. An MVC authorization filter is a later, MVC-specific stage.
- Action filters are the right place to accept or reject a bound `CreateProductRequest` without parsing the body a second time. Data annotations are the usual tool for simple property rules; this project uses a filter so that stage is explicit.
- Exception filters cover a slice of MVC. They do not replace exception middleware.
- A safe 500 response and a detailed server log can carry the same correlation ID. The client needs the ID. The client does not need the stack trace.
