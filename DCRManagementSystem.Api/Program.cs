using System.IO.Compression;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using DCRManagementSystem.Data;
using DCRManagementSystem.Helpers;
using DCRManagementSystem.Models;
using DCRManagementSystem.Services;
using DCRManagementSystem.Api.WebPortal.Services;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.EntityFrameworkCore;
using QuestPDF.Infrastructure;

namespace DCRManagementSystem.Api;

public sealed class ApiSession
{
    public required User User { get; init; }
    public string AuthMethod { get; init; } = string.Empty;
    public string WindowsIdentity { get; init; } = string.Empty;
    public string MachineName { get; init; } = string.Empty;
    public string SessionId { get; init; } = string.Empty;
    public bool IsAdmin => User.Role == RoleNames.Administrator;
}

public static class Program
{
    private const string SessionItemKey = "DCR.Api.Session";
    private const long MaxAttachmentBytes = 64L * 1024 * 1024;
    private const int MaxAttachmentsPerDcr = 50;
    private const long MaxAttachmentTotalBytesPerDcr = 256L * 1024 * 1024;

    [STAThread]
    public static async Task Main(string[] args)
    {
        var apiSettingsPath = Path.Combine(AppContext.BaseDirectory, "api.appsettings.json");
        if (args.Any(x => string.Equals(x, ServerDatabaseConfiguration.ConfigureArgument, StringComparison.OrdinalIgnoreCase)))
        {
            Environment.ExitCode = ServerDatabaseConfiguration.RunStandaloneConfigurator(apiSettingsPath);
            return;
        }

        ServerDatabaseConfiguration.RefreshProcessEnvironmentFromMachine();

        using var singleInstance = new Mutex(initiallyOwned: true, "Local\\DCRManagementSystem.Api", out var createdNew);
        if (!createdNew)
        {
            if (Environment.UserInteractive)
            {
                System.Windows.Forms.MessageBox.Show(
                    "DCR API đang chạy. Hãy mở icon DCR API trong System Tray để xem trạng thái hoặc thoát server.",
                    "DCR API",
                    System.Windows.Forms.MessageBoxButtons.OK,
                    System.Windows.Forms.MessageBoxIcon.Information);
            }
            return;
        }

        try
        {
            if (Environment.UserInteractive && !ServerDatabaseConfiguration.EnsureConfiguredBeforeStartup(apiSettingsPath))
                return;

            await RunServerAsync(args);
        }
        catch (Exception ex)
        {
            var message = ex.GetBaseException().Message;
            ApiLog.WriteEmergency(ex.ToString());
            if (Environment.UserInteractive)
            {
                if (ServerDatabaseConfiguration.IsLikelyDatabaseStartupError(ex))
                {
                    var choice = System.Windows.Forms.MessageBox.Show(
                        $"Không thể khởi động DCR API do kết nối SQL Server.\r\n\r\n{message}\r\n\r\n" +
                        "Bạn có muốn mở cấu hình SQL Server ngay bây giờ không?",
                        "DCR API - Cấu hình SQL Server",
                        System.Windows.Forms.MessageBoxButtons.YesNo,
                        System.Windows.Forms.MessageBoxIcon.Warning);

                    if (choice == System.Windows.Forms.DialogResult.Yes &&
                        ServerDatabaseConfiguration.OpenConfiguratorAndWait(apiSettingsPath))
                    {
                        System.Windows.Forms.MessageBox.Show(
                            "Đã lưu cấu hình SQL Server. Hãy mở lại DCR API để áp dụng cấu hình mới.",
                            "DCR API",
                            System.Windows.Forms.MessageBoxButtons.OK,
                            System.Windows.Forms.MessageBoxIcon.Information);
                        return;
                    }
                }

                System.Windows.Forms.MessageBox.Show(
                    $"Không thể khởi động DCR API.\r\n\r\n{message}\r\n\r\nChi tiết đã được ghi tại:\r\n{ApiLog.ApiLogFile}",
                    "DCR API - Lỗi khởi động",
                    System.Windows.Forms.MessageBoxButtons.OK,
                    System.Windows.Forms.MessageBoxIcon.Error);
            }
            else
            {
                throw;
            }
        }
    }

    private static async Task RunServerAsync(string[] args)
    {
        QuestPDF.Settings.License = LicenseType.Community;
        var apiSettingsPath = Path.Combine(AppContext.BaseDirectory, "api.appsettings.json");
        var builder = WebApplication.CreateBuilder(args);
        builder.Configuration.AddJsonFile(apiSettingsPath, optional: false, reloadOnChange: false);
        builder.Logging.AddProvider(new ApiFileLoggerProvider());
        builder.Services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
            options.SerializerOptions.PropertyNameCaseInsensitive = true;
        });
        builder.Services.Configure<FormOptions>(options =>
        {
            options.MultipartBodyLengthLimit = 72L * 1024 * 1024;
        });
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.Limits.MaxRequestBodySize = 72L * 1024 * 1024;
            options.Limits.KeepAliveTimeout = TimeSpan.FromMinutes(5);
            options.Limits.RequestHeadersTimeout = TimeSpan.FromSeconds(60);
        });
        builder.Services.AddSingleton<ApiTokenService>();
        builder.Services.AddSingleton<ApiConcurrencyGate>();
        builder.Services.AddMemoryCache();
        builder.Services.AddSingleton<ApiUserSessionCache>();
        builder.Services.AddResponseCompression(options =>
        {
            options.EnableForHttps = true;
            options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(new[] { "application/json" });
        });
        builder.Services.AddHostedService<ApiReminderBackgroundService>();
        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy("auth", context =>
            {
                var clientAddress = GetTrustedClientAddress(context);
                return RateLimitPartition.GetFixedWindowLimiter(
                    clientAddress,
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 12,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                        AutoReplenishment = true
                    });
            });
        });

        // DCR Web Portal is hosted by the DCR API process itself.
        // This keeps dcr.ggpcontrol.cloud fully independent from WebDashboard (:5000).
        builder.Services.AddRazorPages();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddDistributedMemoryCache();
        builder.Services.AddSession(options =>
        {
            options.Cookie.Name = ".DCRManagement.WebPortal";
            options.Cookie.HttpOnly = true;
            options.Cookie.IsEssential = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
            options.IdleTimeout = TimeSpan.FromHours(8);
        });
        var webPortalApiBaseUrl = builder.Configuration["DcrWebPortal:ApiBaseUrl"] ?? "http://127.0.0.1:5080";
        var webPortalTimeoutSeconds = int.TryParse(builder.Configuration["DcrWebPortal:TimeoutSeconds"], out var parsedWebPortalTimeout)
            ? Math.Clamp(parsedWebPortalTimeout, 30, 900)
            : 600;
        builder.Services.AddHttpClient("DcrApi", client =>
        {
            client.BaseAddress = new Uri(webPortalApiBaseUrl.TrimEnd('/') + "/");
            client.Timeout = TimeSpan.FromSeconds(webPortalTimeoutSeconds);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("DCRManagement-WebPortal/1.0");
        });
        builder.Services.AddScoped<DcrApiClient>();

        var appSettings = AppSettings.LoadFromFile(apiSettingsPath);
        if (appSettings.UseRemoteApi)
            throw new InvalidOperationException("DCRManagementSystem.Api phải chạy với DataAccessMode=DirectSql.");
        AppServices.Initialize(appSettings);

        NetworkResilienceService.ExecuteSql(() =>
        {
            using var db = AppServices.CreateDbContext();
            db.Database.EnsureCreated();
            DatabaseUpgradeService.Apply(db);
            SecurityKeyGuard.EnsureSigningKeyMatches(db, appSettings);
            // DbSeeder.Seed(db); // Disabled: never create default business data on startup/build.
        });

        var app = builder.Build();
        var tokenService = app.Services.GetRequiredService<ApiTokenService>();
        var concurrencyGate = app.Services.GetRequiredService<ApiConcurrencyGate>();
        var userSessionCache = app.Services.GetRequiredService<ApiUserSessionCache>();
        var memoryCache = app.Services.GetRequiredService<IMemoryCache>();
        var apiOptions = builder.Configuration.GetSection("ApiServer").Get<ApiServerOptions>() ?? new ApiServerOptions();
        var referenceCacheLifetime = TimeSpan.FromSeconds(Math.Clamp(apiOptions.ReferenceCacheSeconds, 10, 600));
        app.UseResponseCompression();
        app.UseStaticFiles();
        app.UseSession();
        app.UseRateLimiter();

        app.Use(async (context, next) =>
        {
            try
            {
                await next();
            }
            catch (Exception ex)
            {
                if (context.Response.HasStarted) throw;
                context.Response.Clear();

                var traceId = context.TraceIdentifier;
                var error = MapExceptionForClient(ex, context.Request.Path);
                if (error.StatusCode >= StatusCodes.Status500InternalServerError)
                {
                    app.Logger.LogError(
                        ex,
                        "DCR API request failed. TraceId={TraceId}, Method={Method}, Path={Path}",
                        traceId, context.Request.Method, context.Request.Path);
                }
                else
                {
                    app.Logger.LogWarning(
                        ex,
                        "DCR API request rejected. TraceId={TraceId}, Status={Status}, Method={Method}, Path={Path}",
                        traceId, error.StatusCode, context.Request.Method, context.Request.Path);
                }

                context.Response.StatusCode = error.StatusCode;
                await context.Response.WriteAsJsonAsync(new ApiErrorResponse
                {
                    Error = error.Message,
                    ErrorType = error.ErrorType,
                    TraceId = traceId
                });
            }
        });

        app.Use(async (context, next) =>
        {
            if (!context.Request.Path.StartsWithSegments("/api"))
            {
                await next();
                return;
            }

            using var lease = await concurrencyGate.TryEnterAsync(context.RequestAborted).ConfigureAwait(false);
            if (lease is null)
            {
                context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                context.Response.Headers["Retry-After"] = "2";
                await context.Response.WriteAsJsonAsync(new ApiErrorResponse
                {
                    Error = "DCR Server đang xử lý nhiều yêu cầu cùng lúc. Vui lòng thử lại sau vài giây.",
                    ErrorType = "ServerBusy",
                    TraceId = context.TraceIdentifier
                });
                return;
            }

            await next();
        });

        app.Use(async (context, next) =>
        {
            var path = context.Request.Path;
            var anonymous = path.StartsWithSegments("/api/health") ||
                            path.StartsWithSegments("/api/web-portal/status") ||
                            path.StartsWithSegments("/api/internal/ggp-user") ||
                            path.StartsWithSegments("/api/auth/login") ||
                            path.StartsWithSegments("/api/auth/refresh");
            if (!path.StartsWithSegments("/api") || anonymous)
            {
                await next();
                return;
            }

            var auth = context.Request.Headers.Authorization.ToString();
            if (!auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ||
                !tokenService.TryValidate(auth[7..].Trim(), ApiTokenService.AccessPurpose, out var payload))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(new ApiErrorResponse
                {
                    Error = "Phiên đăng nhập API không hợp lệ hoặc đã hết hạn.",
                    ErrorType = "Unauthorized",
                    TraceId = context.TraceIdentifier
                });
                return;
            }

            var user = await userSessionCache.GetActiveUserAsync(payload.UserId, context.RequestAborted).ConfigureAwait(false);
            if (user is null)
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(new ApiErrorResponse
                {
                    Error = "Tài khoản không còn tồn tại hoặc đã bị khóa.",
                    ErrorType = "Unauthorized",
                    TraceId = context.TraceIdentifier
                });
                return;
            }

            var session = new ApiSession
            {
                User = user,
                AuthMethod = payload.AuthMethod,
                WindowsIdentity = payload.WindowsIdentity,
                MachineName = payload.MachineName,
                SessionId = payload.SessionId
            };
            context.Items[SessionItemKey] = session;

            var ipAddress = GetTrustedClientAddress(context);
            using var operationContext = RequestExecutionContext.Push(
                session.SessionId,
                session.AuthMethod,
                session.WindowsIdentity,
                session.MachineName,
                ipAddress);
            await next();
        });

        app.MapGet("/api", () => Results.Ok(new
        {
            service = "DCR Management System API",
            status = "running",
            webPortal = "https://dcr.ggpcontrol.cloud/",
            utc = DateTime.UtcNow
        }));

        app.MapGet("/api/health", async (CancellationToken cancellationToken) =>
        {
            var databaseState = await memoryCache.GetOrCreateAsync("health:database", async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(5);
                await using var db = AppServices.CreateDbContext();
                await db.OpenSqlConnectionWithRetryAsync(cancellationToken);
                var database = db.Database.GetDbConnection().Database;
                return string.IsNullOrWhiteSpace(database) ? "unavailable" : "connected";
            });
            return Results.Ok(new ApiHealthResponse
            {
                Status = databaseState == "connected" ? "ok" : "degraded",
                Server = "DCR API",
                ServerTimeUtc = DateTime.UtcNow,
                Database = databaseState ?? "unavailable"
            });
        });

        app.MapGet("/api/web-portal/status", async () =>
        {
            var settings = await new WebPortalConfigurationService(AppServices.CreateDbContext).GetAsync();
            return Results.Ok(settings);
        });

        // WebDashboard and DCR API run on the same server. This loopback-only endpoint exposes
        // current DCR user status/permissions for internal server integration only.
        // DCR remains the sole identity source; password data is never returned.
        app.MapGet("/api/internal/ggp-user/{username}", async (HttpContext context, string username, CancellationToken cancellationToken) =>
        {
            var remoteAddress = context.Connection.RemoteIpAddress;
            var isLoopback = remoteAddress is not null &&
                             (IPAddress.IsLoopback(remoteAddress) ||
                              (remoteAddress.IsIPv4MappedToIPv6 && IPAddress.IsLoopback(remoteAddress.MapToIPv4())));
            // cloudflared also connects to Kestrel over loopback. Reject proxy identity headers so
            // this internal endpoint cannot be reached through the public Cloudflare hostname.
            var cameThroughProxy = context.Request.Headers.ContainsKey("CF-Connecting-IP") ||
                                   context.Request.Headers.ContainsKey("X-Forwarded-For");
            if (!isLoopback || cameThroughProxy) return Results.NotFound();

            var normalizedUsername = (username ?? string.Empty).Trim();
            if (normalizedUsername.Length == 0) return Results.NotFound();

            await using var db = AppServices.CreateDbContext();
            await db.OpenSqlConnectionWithRetryAsync(cancellationToken);
            var user = await db.Users.AsNoTracking()
                .Include(x => x.Department)
                .SingleOrDefaultAsync(
                    x => x.Username == normalizedUsername && x.IsActive && !x.IsDeleted,
                    cancellationToken);
            if (user is null) return Results.NotFound();

            return Results.Ok(new
            {
                user.UserId,
                user.Username,
                user.WindowsAccount,
                user.FullName,
                user.Email,
                user.Phone,
                user.DepartmentId,
                user.BusinessUnitId,
                user.DirectManagerUserId,
                user.Role,
                user.IsActive,
                user.CanUseProductionTracking,
                user.CanUseWarehouseManagement,
                Department = user.Department is null
                    ? null
                    : new
                    {
                        user.Department.Id,
                        user.Department.DepartmentCode,
                        user.Department.DepartmentName,
                        user.Department.IsActive
                    }
            });
        });

        app.MapPost("/api/auth/login", async (HttpContext context, ApiLoginRequest request, CancellationToken cancellationToken) =>
        {
            var authService = new AuthService(AppServices.CreateDbContext, appSettings);
            var result = await authService.LoginAsync(request.Username, request.Password);
            if (result is null)
            {
                return Results.Json(new ApiErrorResponse
                {
                    Error = appSettings.Authentication.Mode.Equals(AuthenticationModes.WindowsOnly, StringComparison.OrdinalIgnoreCase)
                        ? "DCR API đang ở chế độ WindowsOnly nên không thể đăng nhập DCR Web bằng Username/Password. Hãy chuyển sang WindowsPreferred hoặc LocalOnly và bật AllowLocalFallback."
                        : "Tên đăng nhập hoặc mật khẩu DCR/LDAP không đúng, hoặc tài khoản đã bị khóa. Nếu tài khoản trước đây chỉ dùng Windows Integrated, Administrator cần đặt mật khẩu DCR tại Quản lý người dùng.",
                    ErrorType = "InvalidCredentials",
                    TraceId = context.TraceIdentifier
                }, statusCode: StatusCodes.Status401Unauthorized);
            }

            var access = tokenService.IssueAccess(
                result.User.UserId,
                result.User.Username,
                result.User.Role,
                result.AuthMethod,
                request.WindowsIdentity,
                request.MachineName);
            var sessionId = ExtractSessionId(tokenService, access.Token, ApiTokenService.AccessPurpose);
            var refresh = tokenService.IssueRefresh(
                result.User.UserId,
                result.User.Username,
                result.User.Role,
                result.AuthMethod,
                request.WindowsIdentity,
                request.MachineName,
                sessionId);

            return Results.Ok(new ApiLoginResponse
            {
                Authentication = new AuthenticationResult
                {
                    User = result.User,
                    AuthMethod = result.AuthMethod,
                    WindowsIdentity = request.WindowsIdentity
                },
                AccessToken = access.Token,
                RefreshToken = refresh.Token,
                AccessTokenExpiresUtc = access.ExpiresUtc,
                RefreshTokenExpiresUtc = refresh.ExpiresUtc
            });
        }).RequireRateLimiting("auth");

        app.MapPost("/api/auth/refresh", async (ApiRefreshRequest request, CancellationToken cancellationToken) =>
        {
            if (!tokenService.TryValidate(request.RefreshToken, ApiTokenService.RefreshPurpose, out var refreshPayload))
                return Results.Unauthorized();

            await using var db = AppServices.CreateDbContext();
            await db.OpenSqlConnectionWithRetryAsync(cancellationToken);
            var user = await db.Users.AsNoTracking()
                .Include(x => x.Department)
                .Include(x => x.BusinessUnit)
                .SingleOrDefaultAsync(x => x.UserId == refreshPayload.UserId && x.IsActive && !x.IsDeleted, cancellationToken);
            if (user is null) return Results.Unauthorized();

            var windowsIdentity = string.IsNullOrWhiteSpace(request.WindowsIdentity)
                ? refreshPayload.WindowsIdentity
                : request.WindowsIdentity;
            var machineName = string.IsNullOrWhiteSpace(request.MachineName)
                ? refreshPayload.MachineName
                : request.MachineName;
            var sessionId = Guid.NewGuid().ToString("N");
            var access = tokenService.IssueAccess(
                user.UserId,
                user.Username,
                user.Role,
                refreshPayload.AuthMethod,
                windowsIdentity,
                machineName,
                sessionId);
            var refresh = tokenService.IssueRefresh(
                user.UserId,
                user.Username,
                user.Role,
                refreshPayload.AuthMethod,
                windowsIdentity,
                machineName,
                sessionId);

            return Results.Ok(new ApiLoginResponse
            {
                Authentication = new AuthenticationResult
                {
                    User = user,
                    AuthMethod = refreshPayload.AuthMethod,
                    WindowsIdentity = windowsIdentity
                },
                AccessToken = access.Token,
                RefreshToken = refresh.Token,
                AccessTokenExpiresUtc = access.ExpiresUtc,
                RefreshTokenExpiresUtc = refresh.ExpiresUtc
            });
        }).RequireRateLimiting("auth");

        app.MapPost("/api/auth/change-password", async (HttpContext context, ApiChangePasswordRequest request) =>
        {
            var session = GetSession(context);
            var authService = new AuthService(AppServices.CreateDbContext, appSettings);
            await authService.ChangePasswordAsync(session.User.UserId, request.CurrentPassword, request.NewPassword);
            userSessionCache.Invalidate(session.User.UserId);
            return Results.NoContent();
        }).RequireRateLimiting("auth");

        app.MapPost("/api/auth/reauthenticate", async (HttpContext context) =>
        {
            var session = GetSession(context);
            var authService = new AuthService(AppServices.CreateDbContext, appSettings);
            var authentication = await authService.AuthenticateDecisionForSessionAsync(
                session.User.UserId, session.WindowsIdentity);

            var proof = tokenService.IssueDecisionProof(
                session.User.UserId,
                session.User.Username,
                session.User.Role,
                authentication.AuthMethod,
                authentication.WindowsIdentity,
                session.MachineName,
                session.SessionId);
            authentication.ProofToken = proof.Token;
            return Results.Ok(authentication);
        }).RequireRateLimiting("auth");

        var dcr = app.MapGroup("/api/dcr");

        dcr.MapGet("/reference/departments", async () => Results.Ok(await memoryCache.GetOrCreateAsync("ref:departments", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = referenceCacheLifetime;
            return await CreateDcrService().GetActiveDepartmentsAsync();
        })));
        dcr.MapGet("/reference/product-lines", async () => Results.Ok(await memoryCache.GetOrCreateAsync("ref:product-lines", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = referenceCacheLifetime;
            return await CreateDcrService().GetActiveProductLinesAsync();
        })));
        dcr.MapGet("/reference/change-types", async () => Results.Ok(await memoryCache.GetOrCreateAsync("ref:change-types", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = referenceCacheLifetime;
            return await CreateDcrService().GetActivePartChangeTypesAsync();
        })));
        dcr.MapGet("/approvers/search", async (string? q, int? maxResults) =>
            Results.Ok(await CreateDcrService().SearchApproversAsync(q ?? string.Empty, Math.Clamp(maxResults ?? 40, 1, 100))));
        dcr.MapGet("/approval-plan/suggested", async (HttpContext context, string? rank) =>
        {
            var session = GetSession(context);
            return Results.Ok(await CreateDcrService().GetSuggestedApprovalPlanAsync(session.User.UserId, rank ?? DcrRanks.C));
        });
        dcr.MapGet("/approval-plan/templates", async (string? rank) =>
            Results.Ok(await CreateDcrService().GetApprovalPlanTemplatesAsync(rank ?? DcrRanks.C)));
        dcr.MapGet("/approval-plan/saved", async (HttpContext context, string? rank) =>
        {
            var session = GetSession(context);
            return Results.Ok(await CreateDcrService().GetSavedApprovalPlanAsync(session.User.UserId, rank ?? DcrRanks.C));
        });
        dcr.MapPost("/approval-plan/saved", async (HttpContext context, ApiSaveUserApprovalPlanRequest request) =>
        {
            var session = GetSession(context);
            return Results.Ok(await CreateDcrService().SaveUserApprovalPlanAsync(
                session.User.UserId,
                request.Rank,
                request.ApprovalPlan));
        });
        app.MapGet("/api/dcr", async (HttpContext context, string? scope, string? search) =>
        {
            var session = GetSession(context);
            return Results.Ok(await CreateDcrService().GetListAsync(scope ?? DcrListScopes.All, session.User.UserId, search ?? string.Empty, session.IsAdmin));
        });
        dcr.MapGet("/pending-count", async (HttpContext context) =>
        {
            var session = GetSession(context);
            return Results.Ok(await CreateDcrService().GetPendingApprovalCountAsync(session.User.UserId));
        });
        dcr.MapGet("/new", async (HttpContext context) =>
        {
            var session = GetSession(context);
            return Results.Ok(await CreateDcrService().CreateNewModelAsync(session.User.UserId));
        });
        dcr.MapPost("/save-draft", async (HttpContext context, ApiSaveDraftRequest request) =>
        {
            var session = GetSession(context);
            var service = CreateDcrService();
            try
            {
                var requestId = await service.SaveDraftAsync(request.Model, session.User.UserId, session.IsAdmin, request.DraftStep, request.IsAutoSave);
                return Results.Ok(new ApiSaveDraftResponse { RequestId = requestId, Model = request.Model });
            }
            catch (DcrAlreadyCreatedException ex)
            {
                return Results.Ok(new ApiSaveDraftResponse
                {
                    RequestId = ex.RequestId,
                    Model = await service.LoadEditDataAsync(ex.RequestId),
                    AlreadyExisted = true
                });
            }
        });
        dcr.MapGet("/{requestId:int}", async (HttpContext context, int requestId) =>
        {
            await EnsureCanViewDcrAsync(context, requestId);
            return Results.Ok(await CreateDcrService().LoadEditDataAsync(requestId));
        });
        dcr.MapDelete("/{requestId:int}", async (HttpContext context, int requestId) =>
        {
            var session = GetSession(context);
            await new AdminService(AppServices.CreateDbContext, appSettings)
                .DeleteDcrAsync(requestId, session.User.UserId, session.IsAdmin);
            return Results.NoContent();
        });
        dcr.MapGet("/{requestId:int}/can-view", async (HttpContext context, int requestId) =>
        {
            var session = GetSession(context);
            return Results.Ok(await CreateDcrService().CanViewAsync(requestId, session.User.UserId, session.IsAdmin));
        });
        dcr.MapGet("/{requestId:int}/can-approve", async (HttpContext context, int requestId) =>
        {
            var session = GetSession(context);
            return Results.Ok(await CreateDcrService().CanApproveAsync(requestId, session.User.UserId));
        });
        dcr.MapPost("/{requestId:int}/submit", async (HttpContext context, int requestId, ApiSubmitRequest request) =>
        {
            var session = GetSession(context);
            var result = await CreateDcrService().SubmitForApiAsync(
                requestId,
                session.User.UserId,
                session.IsAdmin,
                request.ExpectedRowVersion,
                session.AuthMethod,
                session.WindowsIdentity);
            return Results.Ok(result);
        });
        dcr.MapPost("/{requestId:int}/decision", async (HttpContext context, int requestId, ApiDecisionRequest request) =>
        {
            var session = GetSession(context);
            if (!tokenService.TryValidate(request.Authentication.ProofToken, ApiTokenService.DecisionPurpose, out var proof) ||
                proof.UserId != session.User.UserId ||
                proof.SessionId != session.SessionId)
            {
                throw new UnauthorizedAccessException("Xác thực quyết định không hợp lệ hoặc đã hết hạn. Vui lòng tải lại DCR và thử lại.");
            }

            var authentication = new DecisionAuthentication
            {
                AuthMethod = proof.AuthMethod,
                AuthenticatedAt = DateTime.Now,
                WindowsIdentity = proof.WindowsIdentity,
                ProofToken = string.Empty
            };
            var result = await CreateDcrService().ProcessDecisionAsync(
                requestId,
                session.User.UserId,
                request.Decision,
                request.Comment,
                authentication,
                request.ExpectedRowVersion);
            return Results.Ok(result);
        });
        dcr.MapGet("/{requestId:int}/approval-history", async (HttpContext context, int requestId) =>
        {
            await EnsureCanViewDcrAsync(context, requestId);
            return Results.Ok(await CreateDcrService().GetApprovalHistoryAsync(requestId));
        });
        dcr.MapGet("/{requestId:int}/attachments", async (HttpContext context, int requestId) =>
        {
            await EnsureCanViewDcrAsync(context, requestId);
            return Results.Ok(await CreateDcrService().GetAttachmentsAsync(requestId));
        });
        dcr.MapGet("/{requestId:int}/audit-logs", async (HttpContext context, int requestId) =>
        {
            await EnsureCanViewDcrAsync(context, requestId);
            return Results.Ok(await CreateDcrService().GetAuditLogsAsync(requestId));
        });
        dcr.MapGet("/{requestId:int}/number", async (HttpContext context, int requestId) =>
        {
            await EnsureCanViewDcrAsync(context, requestId);
            return Results.Ok(await CreateDcrService().GetDcrNumberAsync(requestId));
        });

        dcr.MapPost("/{requestId:int}/attachments/upload/start", async (HttpContext context, int requestId, ApiUploadStartRequest request, CancellationToken cancellationToken) =>
        {
            var session = GetSession(context);
            await EnsureCanUploadAttachmentAsync(context, requestId, cancellationToken);
            var attachmentType = AttachmentTypes.Normalize(request.AttachmentType)
                ?? throw new InvalidOperationException("Attachment Type không hợp lệ.");
            if (request.FileSize <= 0)
                throw new InvalidOperationException("Dung lượng attachment phải lớn hơn 0 byte.");
            if (request.FileSize > MaxAttachmentBytes)
                throw new InvalidOperationException($"Mỗi attachment tối đa {MaxAttachmentBytes / 1024 / 1024} MB.");
            await EnsureAttachmentQuotaAsync(requestId, request.FileSize, cancellationToken);

            var safeFileName = Path.GetFileName(request.FileName ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(safeFileName))
                throw new InvalidOperationException("Tên attachment không hợp lệ.");

            CleanupStaleUploadFolders();
            var uploadId = Guid.NewGuid().ToString("N");
            var folder = GetUploadFolder(uploadId);
            Directory.CreateDirectory(folder);
            var metadata = new ApiUploadStagingMetadata
            {
                RequestId = requestId,
                UserId = session.User.UserId,
                FileName = safeFileName,
                FileSize = request.FileSize,
                AttachmentType = attachmentType,
                CreatedUtc = DateTime.UtcNow
            };
            await File.WriteAllTextAsync(
                Path.Combine(folder, "metadata.json"),
                JsonSerializer.Serialize(metadata),
                cancellationToken);

            return Results.Ok(new ApiUploadStartResponse
            {
                UploadId = uploadId,
                ChunkSizeBytes = 8 * 1024 * 1024
            });
        });

        dcr.MapPost("/{requestId:int}/attachments/upload/{uploadId}/chunk", async (HttpContext context, int requestId, string uploadId, long? offset, CancellationToken cancellationToken) =>
        {
            var session = GetSession(context);
            await EnsureCanUploadAttachmentAsync(context, requestId, cancellationToken);
            var folder = GetUploadFolder(uploadId);
            var metadata = await ReadUploadMetadataAsync(folder, cancellationToken);
            if (metadata.RequestId != requestId || metadata.UserId != session.User.UserId)
                throw new UnauthorizedAccessException("Phiên upload attachment không thuộc người dùng/DCR hiện tại.");

            var requestedOffset = offset ?? -1;
            if (requestedOffset < 0)
                throw new InvalidOperationException("Offset upload không hợp lệ.");
            var targetPath = Path.Combine(folder, metadata.FileName);
            const int maxChunkSize = 8 * 1024 * 1024;
            await using var target = new FileStream(
                targetPath,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None,
                1024 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            if (target.Length != requestedOffset)
                throw new InvalidOperationException($"Offset upload không khớp. Server đang ở {target.Length} bytes, client gửi {requestedOffset} bytes.");

            target.Position = target.Length;
            var originalLength = target.Length;
            var buffer = new byte[1024 * 1024];
            var received = 0;
            while (true)
            {
                var read = await context.Request.Body.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
                if (read == 0) break;
                received += read;
                if (received > maxChunkSize)
                {
                    target.SetLength(originalLength);
                    throw new InvalidDataException("Chunk upload vượt quá 8 MB.");
                }
                await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            }
            if (received <= 0)
                throw new InvalidDataException("Chunk upload đang rỗng.");
            if (target.Length > metadata.FileSize)
            {
                target.SetLength(originalLength);
                throw new InvalidDataException("Dữ liệu upload vượt quá dung lượng file đã khai báo.");
            }
            await target.FlushAsync(cancellationToken);
            return Results.NoContent();
        });

        dcr.MapPost("/{requestId:int}/attachments/upload/{uploadId}/complete", async (HttpContext context, int requestId, string uploadId, CancellationToken cancellationToken) =>
        {
            var session = GetSession(context);
            await EnsureCanUploadAttachmentAsync(context, requestId, cancellationToken);
            var folder = GetUploadFolder(uploadId);
            var metadata = await ReadUploadMetadataAsync(folder, cancellationToken);
            if (metadata.RequestId != requestId || metadata.UserId != session.User.UserId)
                throw new UnauthorizedAccessException("Phiên upload attachment không thuộc người dùng/DCR hiện tại.");

            var stagedFile = Path.Combine(folder, metadata.FileName);
            if (!File.Exists(stagedFile))
                throw new FileNotFoundException("Không tìm thấy dữ liệu attachment đã upload.", stagedFile);
            var actualSize = new FileInfo(stagedFile).Length;
            if (actualSize != metadata.FileSize)
                throw new InvalidDataException($"Attachment upload chưa hoàn tất: {actualSize}/{metadata.FileSize} bytes.");
            if (actualSize > MaxAttachmentBytes)
                throw new InvalidDataException($"Mỗi attachment tối đa {MaxAttachmentBytes / 1024 / 1024} MB.");
            await EnsureAttachmentQuotaAsync(requestId, actualSize, cancellationToken);

            await new AttachmentService(AppServices.CreateDbContext, appSettings)
                .UploadAsync(requestId, stagedFile, metadata.AttachmentType, session.User.UserId, session.IsAdmin);
            TryDeleteDirectory(folder);
            return Results.Ok();
        });

        dcr.MapPost("/{requestId:int}/attachments/upload", async (HttpContext context, int requestId, CancellationToken cancellationToken) =>
        {
            var session = GetSession(context);
            await EnsureCanUploadAttachmentAsync(context, requestId, cancellationToken);
            var form = await context.Request.ReadFormAsync(cancellationToken);
            var file = form.Files.GetFile("file") ?? throw new InvalidOperationException("Request không có file upload.");
            var attachmentType = form["attachmentType"].ToString();
            if (file.Length <= 0) throw new InvalidOperationException("File upload đang rỗng.");
            if (file.Length > MaxAttachmentBytes)
                throw new InvalidOperationException($"Mỗi attachment tối đa {MaxAttachmentBytes / 1024 / 1024} MB.");
            await EnsureAttachmentQuotaAsync(requestId, file.Length, cancellationToken);

            var safeFileName = Path.GetFileName(file.FileName).Trim();
            if (string.IsNullOrWhiteSpace(safeFileName))
                throw new InvalidOperationException("Tên attachment không hợp lệ.");

            var stagingRoot = Path.Combine(ApplicationDataPaths.PersistentRoot, "Api", "UploadStaging");
            var stagingFolder = Path.Combine(stagingRoot, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(stagingFolder);
            var tempPath = Path.Combine(stagingFolder, safeFileName);
            try
            {
                await using (var output = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1024 * 1024, useAsync: true))
                    await file.CopyToAsync(output, cancellationToken);
                await new AttachmentService(AppServices.CreateDbContext, appSettings)
                    .UploadAsync(requestId, tempPath, attachmentType, session.User.UserId, session.IsAdmin);
                return Results.Ok();
            }
            finally
            {
                TryDeleteDirectory(stagingFolder);
            }
        });

        app.MapGet("/api/attachments/{attachmentId:int}/download", async (HttpContext context, int attachmentId) =>
        {
            var session = GetSession(context);
            var service = new AttachmentService(AppServices.CreateDbContext, appSettings);
            var path = await service.GetAbsolutePathAsync(attachmentId, session.User.UserId, session.IsAdmin);
            await using var db = AppServices.CreateDbContext();
            var metadata = await db.DCRAttachments.AsNoTracking().SingleOrDefaultAsync(x => x.Id == attachmentId && !x.IsDeleted);
            var downloadName = metadata?.OriginalFileName;
            if (string.IsNullOrWhiteSpace(downloadName)) downloadName = Path.GetFileName(path);
            return Results.File(path, "application/octet-stream", downloadName, enableRangeProcessing: true);
        });
        app.MapDelete("/api/attachments/{attachmentId:int}", async (HttpContext context, int attachmentId) =>
        {
            var session = GetSession(context);
            await new AttachmentService(AppServices.CreateDbContext, appSettings)
                .DeleteAsync(attachmentId, session.User.UserId, session.IsAdmin);
            return Results.NoContent();
        });

        dcr.MapGet("/{requestId:int}/pdf/export", async (HttpContext context, int requestId, CancellationToken cancellationToken) =>
        {
            await EnsureCanViewDcrAsync(context, requestId);
            var temp = Path.Combine(Path.GetTempPath(), "DCRManagementSystem", "ApiPdf", $"{Guid.NewGuid():N}.pdf");
            Directory.CreateDirectory(Path.GetDirectoryName(temp)!);
            await new PdfService(AppServices.CreateDbContext, appSettings).ExportAsync(requestId, temp);
            var number = await CreateDcrService().GetDcrNumberAsync(requestId);
            return Results.File(temp, "application/pdf", $"{number}.pdf", enableRangeProcessing: true);
        });
        dcr.MapGet("/{requestId:int}/pdf/preview", async (HttpContext context, int requestId, CancellationToken cancellationToken) =>
        {
            await EnsureCanViewDcrAsync(context, requestId);
            var path = await new PdfService(AppServices.CreateDbContext, appSettings).GeneratePreviewPdfAsync(requestId, cancellationToken);
            return Results.File(path, "application/pdf", $"DCR_{requestId}_Preview.pdf", enableRangeProcessing: true);
        });
        dcr.MapPost("/{requestId:int}/pdf/final/generate", async (HttpContext context, int requestId) =>
        {
            var session = GetSession(context);
            await EnsureCanViewDcrAsync(context, requestId);
            await new PdfService(AppServices.CreateDbContext, appSettings).GenerateFinalApprovedPdfAsync(requestId, session.User.UserId);
            return Results.Ok();
        });
        dcr.MapGet("/{requestId:int}/pdf/final", async (HttpContext context, int requestId) =>
        {
            await EnsureCanViewDcrAsync(context, requestId);
            var path = await new PdfService(AppServices.CreateDbContext, appSettings).GetVerifiedFinalApprovedPdfPathAsync(requestId);
            var number = await CreateDcrService().GetDcrNumberAsync(requestId);
            return Results.File(path, "application/pdf", $"{number}_Approved.pdf", enableRangeProcessing: true);
        });

        var admin = app.MapGroup("/api/admin");
        admin.AddEndpointFilter(async (invocationContext, next) =>
        {
            var httpContext = invocationContext.HttpContext;
            if (!GetSession(httpContext).IsAdmin)
                throw new UnauthorizedAccessException("Chỉ Administrator được sử dụng chức năng quản trị.");
            return await next(invocationContext);
        });

        admin.MapDelete("/dcr/{requestId:int}", async (HttpContext context, int requestId) =>
        {
            var session = GetSession(context);
            await new AdminService(AppServices.CreateDbContext, appSettings).DeleteDcrAsync(requestId, session.User.UserId, true);
            return Results.NoContent();
        });
        admin.MapGet("/users", async () => Results.Ok(await new AdminService(AppServices.CreateDbContext, appSettings).GetUsersAsync()));
        admin.MapPost("/users", async (ApiSaveUserRequest request) =>
        {
            var saved = await new AdminService(AppServices.CreateDbContext, appSettings).SaveUserAsync(
                request.UserId, request.Username, request.FullName, request.Email, request.Phone,
                request.BusinessUnitId, request.DepartmentId, request.BusinessUnitIds, request.DepartmentIds, request.DirectManagerUserId,
                request.Role, request.IsActive, request.NewPassword);
            if (saved.UserId > 0) userSessionCache.Invalidate(saved.UserId);
            return Results.Ok(saved);
        });
        admin.MapDelete("/users/{userId:int}", async (HttpContext context, int userId) =>
        {
            var session = GetSession(context);
            await new AdminService(AppServices.CreateDbContext, appSettings).DeleteUserAsync(userId, session.User.UserId);
            userSessionCache.Invalidate(userId);
            return Results.NoContent();
        });
        admin.MapPost("/users/{userId:int}/module-permissions", async (int userId, ApiSaveUserModulePermissionsRequest request) =>
        {
            var saved = await new AdminService(AppServices.CreateDbContext, appSettings)
                .SaveUserModulePermissionsAsync(userId, request.CanUseProductionTracking, request.CanUseWarehouseManagement);
            userSessionCache.Invalidate(userId);
            return Results.Ok(saved);
        });

        admin.MapGet("/product-lines", async (bool? activeOnly) => Results.Ok(await new AdminService(AppServices.CreateDbContext, appSettings).GetProductLinesAsync(activeOnly ?? false)));
        admin.MapPost("/product-lines", async (ApiSaveProductLineRequest request) =>
        {
            var saved = await new AdminService(AppServices.CreateDbContext, appSettings).SaveProductLineAsync(request.Id, request.Name, request.SortOrder, request.IsActive);
            memoryCache.Remove("ref:product-lines");
            return Results.Ok(saved);
        });
        admin.MapDelete("/product-lines/{id:int}", async (int id) =>
        {
            await new AdminService(AppServices.CreateDbContext, appSettings).DeleteProductLineAsync(id);
            memoryCache.Remove("ref:product-lines");
            return Results.NoContent();
        });

        admin.MapGet("/change-types", async (bool? activeOnly) => Results.Ok(await new AdminService(AppServices.CreateDbContext, appSettings).GetPartChangeTypesAsync(activeOnly ?? false)));
        admin.MapPost("/change-types", async (ApiSavePartChangeTypeRequest request) =>
        {
            var saved = await new AdminService(AppServices.CreateDbContext, appSettings).SavePartChangeTypeAsync(request.Id, request.Name, request.SortOrder, request.IsActive);
            memoryCache.Remove("ref:change-types");
            return Results.Ok(saved);
        });
        admin.MapDelete("/change-types/{id:int}", async (int id) =>
        {
            await new AdminService(AppServices.CreateDbContext, appSettings).DeletePartChangeTypeAsync(id);
            memoryCache.Remove("ref:change-types");
            return Results.NoContent();
        });

        admin.MapGet("/roles", async (bool? activeOnly) => Results.Ok(await new AdminService(AppServices.CreateDbContext, appSettings).GetRolesAsync(activeOnly ?? false)));
        admin.MapPost("/roles", async (ApiSaveRoleRequest request) => Results.Ok(await new AdminService(AppServices.CreateDbContext, appSettings).SaveRoleAsync(request.RoleId, request.RoleName, request.Description, request.HierarchyLevel, request.IsActive)));
        admin.MapDelete("/roles/{id:int}", async (int id) => { await new AdminService(AppServices.CreateDbContext, appSettings).DeleteRoleAsync(id); return Results.NoContent(); });

        admin.MapGet("/business-units", async (bool? activeOnly) => Results.Ok(await new AdminService(AppServices.CreateDbContext, appSettings).GetBusinessUnitsAsync(activeOnly ?? false)));
        admin.MapPost("/business-units", async (ApiSaveBusinessUnitRequest request) => Results.Ok(await new AdminService(AppServices.CreateDbContext, appSettings).SaveBusinessUnitAsync(request.Id, request.Code, request.Name, request.DirectorUserId, request.IsActive, request.ParentBusinessUnitId, request.UnitType, request.SortOrder)));
        admin.MapDelete("/business-units/{id:int}", async (int id) => { await new AdminService(AppServices.CreateDbContext, appSettings).DeleteBusinessUnitAsync(id); return Results.NoContent(); });

        admin.MapGet("/departments", async (bool? activeOnly) => Results.Ok(await new AdminService(AppServices.CreateDbContext, appSettings).GetDepartmentsAsync(activeOnly ?? false)));
        admin.MapPost("/departments", async (ApiSaveDepartmentRequest request) =>
        {
            var saved = await new AdminService(AppServices.CreateDbContext, appSettings).SaveDepartmentAsync(request.Id, request.Code, request.Name, request.BusinessUnitId, request.ManagerUserId, request.IsActive);
            memoryCache.Remove("ref:departments");
            return Results.Ok(saved);
        });
        admin.MapDelete("/departments/{id:int}", async (int id) =>
        {
            await new AdminService(AppServices.CreateDbContext, appSettings).DeleteDepartmentAsync(id);
            memoryCache.Remove("ref:departments");
            return Results.NoContent();
        });

        admin.MapPost("/organization/assignment", async (ApiSaveOrganizationAssignmentRequest request) =>
        {
            await new AdminService(AppServices.CreateDbContext, appSettings).SaveOrganizationAssignmentDetailsAsync(
                request.UserId, request.BusinessUnitId, request.DepartmentId, request.JobTitle,
                request.ReportsToUserId, request.IsActing, request.SortOrder);
            return Results.NoContent();
        });
        admin.MapPost("/organization/normalize", async () =>
            Results.Ok(await new AdminService(AppServices.CreateDbContext, appSettings).NormalizeOrganizationLinksAsync()));

        admin.MapGet("/workflow-templates", async () => Results.Ok(await new AdminService(AppServices.CreateDbContext, appSettings).GetWorkflowTemplatesAsync()));
        admin.MapPost("/workflow-templates", async (ApiWorkflowTemplatesRequest request) =>
        {
            await new AdminService(AppServices.CreateDbContext, appSettings).SaveWorkflowTemplatesAsync(request.Templates);
            return Results.NoContent();
        });
        admin.MapGet("/approval-plan-templates", async (bool? activeOnly, string? rank) =>
            Results.Ok(await new AdminService(AppServices.CreateDbContext, appSettings)
                .GetApprovalPlanTemplatesAsync(activeOnly ?? false, rank)));
        admin.MapPost("/approval-plan-templates", async (ApiSaveApprovalPlanTemplateRequest request) =>
            Results.Ok(await new AdminService(AppServices.CreateDbContext, appSettings)
                .SaveApprovalPlanTemplateAsync(request.Template)));
        admin.MapDelete("/approval-plan-templates/{templateId:int}", async (int templateId) =>
        {
            await new AdminService(AppServices.CreateDbContext, appSettings).DeleteApprovalPlanTemplateAsync(templateId);
            return Results.NoContent();
        });
        admin.MapGet("/approval-matrix", async () => Results.Ok(await new AdminService(AppServices.CreateDbContext, appSettings).GetApprovalMatrixRulesAsync()));
        admin.MapPost("/approval-matrix", async (ApiApprovalMatrixRequest request) =>
        {
            await new AdminService(AppServices.CreateDbContext, appSettings).ReplaceApprovalMatrixRulesAsync(request.Rules);
            return Results.NoContent();
        });

        admin.MapGet("/web-portal", async () =>
            Results.Ok(await new WebPortalConfigurationService(AppServices.CreateDbContext).GetAsync()));
        admin.MapPost("/web-portal", async (ApiWebPortalSettingsRequest request) =>
        {
            await new WebPortalConfigurationService(AppServices.CreateDbContext).SaveAsync(request.Settings);
            return Results.NoContent();
        });

        admin.MapGet("/storage", async () => Results.Ok(await new StorageConfigurationService(AppServices.CreateDbContext, appSettings).GetAsync()));
        admin.MapPost("/storage", async (ApiStorageSettingsRequest request) =>
        {
            await new StorageConfigurationService(AppServices.CreateDbContext, appSettings).SaveAsync(request.Settings);
            return Results.NoContent();
        });
        admin.MapPost("/storage/test", async (ApiStorageSettingsRequest request) => Results.Ok(await new StorageConfigurationService(AppServices.CreateDbContext, appSettings).TestConnectionAsync(request.Settings)));

        admin.MapGet("/email/settings", async () => Results.Ok(await new EmailConfigurationService(AppServices.CreateDbContext, appSettings).GetAsync()));
        admin.MapPost("/email/settings", async (ApiEmailSettingsRequest request) =>
        {
            await new EmailConfigurationService(AppServices.CreateDbContext, appSettings).SaveAsync(request.Settings);
            return Results.NoContent();
        });
        admin.MapPost("/email/test", async (ApiEmailSettingsRequest request) => Results.Ok(await new EmailConfigurationService(AppServices.CreateDbContext, appSettings).TestAsync(request.Settings, request.Recipient)));
        admin.MapGet("/email/signed-account", async () =>
        {
            var email = await new EmailConfigurationService(AppServices.CreateDbContext, appSettings).GetAsync();
            return Results.Ok(await new EmailConfigurationService(AppServices.CreateDbContext, appSettings).GetSignedInGraphAccountAsync(email));
        });
        admin.MapPost("/email/outbox/test", async (EmailRecipientRequest request) => Results.Ok(await new EmailOutboxService(AppServices.CreateDbContext).EnqueueTestAsync(request.Recipient)));
        admin.MapPost("/email/outbox/requeue-auth", async () => Results.Ok(await new EmailOutboxService(AppServices.CreateDbContext).RequeueAuthenticationBlockedAsync()));
        admin.MapGet("/email/outbox/snapshot", async () => Results.Ok(await new EmailOutboxService(AppServices.CreateDbContext).GetSnapshotAsync()));
        admin.MapGet("/email/worker/state", async () => Results.Ok(await new MailWorkerStateService(AppServices.CreateDbContext).GetAsync()));
        admin.MapPost("/email/worker/run", async (int? batchSize) => Results.Ok(await new MailWorkerService(AppServices.CreateDbContext, appSettings).RunOnceAsync(Math.Clamp(batchSize ?? 25, 1, 200))));
        admin.MapPost("/email/worker/run-pending", async (int? batchSize) => Results.Ok(await new MailWorkerService(AppServices.CreateDbContext, appSettings).RunPendingNowAsync(Math.Clamp(batchSize ?? 25, 1, 200))));

        // Clean public DCR routes: /, /Login, /Edit, /Request/{id}, ...
        // No /Dcr rewrite and no WebDashboard middleware are required.
        app.MapRazorPages();

        await app.StartAsync();

        var mailPump = new ServerMailBackgroundPump(AppServices.CreateDbContext, appSettings);
        mailPump.Start();
        app.Lifetime.ApplicationStopping.Register(mailPump.Dispose);

        var tunnelOptions = builder.Configuration.GetSection("CloudflareTunnel").Get<CloudflareTunnelOptions>() ?? new CloudflareTunnelOptions();
        var trayOptions = builder.Configuration.GetSection("ApiTray").Get<ApiTrayOptions>() ?? new ApiTrayOptions();
        using var tunnelManager = new CloudflareTunnelManager(tunnelOptions);
        if (tunnelOptions.Enabled && tunnelOptions.AutoStart)
            tunnelManager.EnsureRunning();
        tunnelManager.StartMonitoring();

        using var trayHost = ApiTrayHost.Start(app, tunnelManager, tunnelOptions, trayOptions);
        await app.WaitForShutdownAsync();
    }

    private static ApiSession GetSession(HttpContext context) =>
        context.Items.TryGetValue(SessionItemKey, out var value) && value is ApiSession session
            ? session
            : throw new UnauthorizedAccessException("Không tìm thấy phiên đăng nhập API.");

    private const int UploadStagingLifetimeHours = 24;

    private static string GetUploadStagingRoot() =>
        Path.Combine(ApplicationDataPaths.PersistentRoot, "Api", "UploadStagingChunks");

    private static string GetUploadFolder(string uploadId)
    {
        if (!Guid.TryParseExact(uploadId, "N", out var parsed))
            throw new InvalidOperationException("UploadId không hợp lệ.");
        var root = GetUploadStagingRoot();
        Directory.CreateDirectory(root);
        return Path.Combine(root, parsed.ToString("N"));
    }

    private static async Task<ApiUploadStagingMetadata> ReadUploadMetadataAsync(string folder, CancellationToken cancellationToken)
    {
        var metadataPath = Path.Combine(folder, "metadata.json");
        if (!File.Exists(metadataPath))
            throw new FileNotFoundException("Phiên upload attachment không tồn tại hoặc đã hết hạn.", metadataPath);
        var json = await File.ReadAllTextAsync(metadataPath, cancellationToken);
        var metadata = JsonSerializer.Deserialize<ApiUploadStagingMetadata>(json)
                       ?? throw new InvalidDataException("Metadata của phiên upload không hợp lệ.");
        if (metadata.RequestId <= 0 || metadata.UserId <= 0 || metadata.FileSize <= 0 || string.IsNullOrWhiteSpace(metadata.FileName))
            throw new InvalidDataException("Metadata của phiên upload không đầy đủ.");
        return metadata;
    }

    private static async Task EnsureAttachmentQuotaAsync(int requestId, long incomingBytes, CancellationToken cancellationToken)
    {
        await using var db = AppServices.CreateDbContext();
        await db.OpenSqlConnectionWithRetryAsync(cancellationToken);
        var existing = db.DCRAttachments.AsNoTracking().Where(x => x.RequestId == requestId && !x.IsDeleted);
        var count = await existing.CountAsync(cancellationToken);
        if (count >= MaxAttachmentsPerDcr)
            throw new InvalidOperationException($"Mỗi DCR tối đa {MaxAttachmentsPerDcr} attachment.");

        var total = await existing.SumAsync(x => (long?)x.FileSize, cancellationToken) ?? 0L;
        if (incomingBytes > MaxAttachmentBytes || total + incomingBytes > MaxAttachmentTotalBytesPerDcr)
            throw new InvalidOperationException($"Tổng dung lượng attachment của một DCR tối đa {MaxAttachmentTotalBytesPerDcr / 1024 / 1024} MB.");
    }

    private static async Task EnsureCanUploadAttachmentAsync(HttpContext context, int requestId, CancellationToken cancellationToken)
    {
        var session = GetSession(context);
        await using var db = AppServices.CreateDbContext();
        await db.OpenSqlConnectionWithRetryAsync(cancellationToken);
        var request = await db.DCRRequests.AsNoTracking().SingleOrDefaultAsync(x => x.Id == requestId, cancellationToken)
                      ?? throw new KeyNotFoundException("Không tìm thấy DCR.");
        if (!RequestStatuses.IsEditable(request.Status))
            throw new InvalidOperationException("Chỉ được thêm attachment khi DCR ở trạng thái Bản nháp hoặc Trả về.");
        if (request.CreatedBy != session.User.UserId && !session.IsAdmin)
            throw new UnauthorizedAccessException("Bạn không có quyền upload file vào DCR này.");
    }

    private static void CleanupStaleUploadFolders()
    {
        var root = GetUploadStagingRoot();
        if (!Directory.Exists(root)) return;
        var cutoff = DateTime.UtcNow.AddHours(-UploadStagingLifetimeHours);
        foreach (var folder in Directory.EnumerateDirectories(root))
        {
            try
            {
                if (Directory.GetLastWriteTimeUtc(folder) < cutoff)
                    Directory.Delete(folder, true);
            }
            catch
            {
                // Best effort cleanup only.
            }
        }
    }

    private static void TryDeleteDirectory(string folder)
    {
        try
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
        }
        catch
        {
            // Completed upload is already stored/audited. Staging cleanup can be retried later.
        }
    }

    private static async Task EnsureCanViewDcrAsync(HttpContext context, int requestId)
    {
        var session = GetSession(context);
        if (!await CreateDcrService().CanViewAsync(requestId, session.User.UserId, session.IsAdmin))
            throw new UnauthorizedAccessException("Bạn không có quyền xem DCR này.");
    }

    private static DcrService CreateDcrService()
    {
        var notifications = new NotificationService(AppServices.CreateDbContext, AppServices.Settings);
        return new DcrService(
            AppServices.CreateDbContext,
            AppServices.Settings,
            notifications,
            new ApprovalRoutingService(),
            new ApprovalSignatureService(AppServices.Settings),
            new PdfService(AppServices.CreateDbContext, AppServices.Settings));
    }

    private static string ExtractSessionId(ApiTokenService tokenService, string token, string purpose)
    {
        if (!tokenService.TryValidate(token, purpose, out var payload))
            throw new InvalidOperationException("Không đọc được session token vừa tạo.");
        return payload.SessionId;
    }

    private static string GetTrustedClientAddress(HttpContext context)
    {
        var remoteAddress = context.Connection.RemoteIpAddress;
        var isLoopback = remoteAddress is not null &&
                         (IPAddress.IsLoopback(remoteAddress) ||
                          (remoteAddress.IsIPv4MappedToIPv6 && IPAddress.IsLoopback(remoteAddress.MapToIPv4())));

        // cloudflared connects to the DCR API over loopback. Only in that case is
        // CF-Connecting-IP accepted. Direct LAN clients cannot spoof their audit/rate-limit IP.
        if (isLoopback)
        {
            var cloudflareAddress = context.Request.Headers["CF-Connecting-IP"].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(cloudflareAddress) &&
                IPAddress.TryParse(cloudflareAddress.Trim(), out var parsedCloudflareAddress))
                return parsedCloudflareAddress.ToString();
        }

        return remoteAddress?.ToString() ?? "unknown";
    }

    private static ApiClientError MapExceptionForClient(Exception exception, PathString requestPath)
    {
        var statusCode = exception switch
        {
            DcrConcurrencyException => StatusCodes.Status409Conflict,
            UnauthorizedAccessException when requestPath.StartsWithSegments("/api/auth") => StatusCodes.Status401Unauthorized,
            UnauthorizedAccessException => StatusCodes.Status403Forbidden,
            FileNotFoundException => StatusCodes.Status404NotFound,
            KeyNotFoundException => StatusCodes.Status404NotFound,
            InvalidDataException => StatusCodes.Status422UnprocessableEntity,
            ArgumentException => StatusCodes.Status400BadRequest,
            InvalidOperationException => StatusCodes.Status400BadRequest,
            _ => StatusCodes.Status500InternalServerError
        };

        var errorType = statusCode switch
        {
            StatusCodes.Status400BadRequest => "InvalidRequest",
            StatusCodes.Status401Unauthorized => "Unauthorized",
            StatusCodes.Status403Forbidden => "Forbidden",
            StatusCodes.Status404NotFound => "NotFound",
            StatusCodes.Status409Conflict => "ConcurrencyConflict",
            StatusCodes.Status422UnprocessableEntity => "InvalidData",
            _ => "ServerError"
        };

        var safeMessage = GetSafeClientMessage(exception, statusCode);
        return new ApiClientError(statusCode, errorType, safeMessage);
    }

    private static string GetSafeClientMessage(Exception exception, int statusCode)
    {
        if (statusCode >= StatusCodes.Status500InternalServerError)
            return "DCR Server gặp lỗi khi xử lý yêu cầu. Vui lòng thử lại hoặc gửi TraceId cho Administrator.";

        if (statusCode == StatusCodes.Status404NotFound)
            return "Không tìm thấy dữ liệu được yêu cầu.";

        var message = exception.GetBaseException().Message?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(message) || message.Length > 600 || ContainsSensitiveDiagnosticText(message))
        {
            return statusCode switch
            {
                StatusCodes.Status400BadRequest => "Dữ liệu yêu cầu không hợp lệ hoặc thao tác không thể thực hiện ở trạng thái hiện tại.",
                StatusCodes.Status401Unauthorized => "Không thể xác thực yêu cầu.",
                StatusCodes.Status403Forbidden => "Bạn không có quyền thực hiện thao tác này.",
                StatusCodes.Status409Conflict => "Dữ liệu đã được thay đổi bởi phiên làm việc khác. Vui lòng tải lại và thử lại.",
                StatusCodes.Status422UnprocessableEntity => "Dữ liệu gửi lên không hợp lệ.",
                _ => "Không thể xử lý yêu cầu."
            };
        }

        // Business/validation messages authored by the application are kept so the existing
        // DCR workflow remains understandable; infrastructure/SQL/path diagnostics are filtered above.
        return message;
    }

    private static bool ContainsSensitiveDiagnosticText(string message)
    {
        var sensitiveMarkers = new[]
        {
            "DATA SOURCE=", "INITIAL CATALOG=", "USER ID=", "PASSWORD=", "SQLCONNECTION",
            "SQLEXCEPTION", "MICROSOFT.DATA.SQLCLIENT", "SYSTEM.DATA.SQLCLIENT", "STACK TRACE",
            " INNER EXCEPTION", "\\\\", ":\\"
        };
        var upper = message.ToUpperInvariant();
        return sensitiveMarkers.Any(marker => upper.Contains(marker));
    }

    private sealed record ApiClientError(int StatusCode, string ErrorType, string Message);

    private sealed class ApiUploadStagingMetadata
    {
        public int RequestId { get; set; }
        public int UserId { get; set; }
        public string FileName { get; set; } = string.Empty;
        public long FileSize { get; set; }
        public string AttachmentType { get; set; } = string.Empty;
        public DateTime CreatedUtc { get; set; }
    }

    public sealed class EmailRecipientRequest
    {
        public string Recipient { get; set; } = string.Empty;
    }
}

public sealed class ApiReminderBackgroundService : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await RunOnceSafeAsync(stoppingToken);
            using var timer = new PeriodicTimer(TimeSpan.FromMinutes(5));
            while (await timer.WaitForNextTickAsync(stoppingToken))
                await RunOnceSafeAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    private static async Task RunOnceSafeAsync(CancellationToken cancellationToken)
    {
        try
        {
            var notifications = new NotificationService(AppServices.CreateDbContext, AppServices.Settings);
            await new ReminderService(AppServices.CreateDbContext, AppServices.Settings, notifications).RunOnceAsync();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch
        {
            // Durable reminder/email state is stored in SQL. The next interval retries.
        }
    }
}
