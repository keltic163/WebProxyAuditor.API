using Microsoft.Playwright;

namespace WebProxyAuditor.API.Services
{
    /// <summary>
    /// 定義動態網頁擷取服務的公開合約。
    /// </summary>
    public interface IWebScraperService
    {
        /// <summary>
        /// 以無頭瀏覽器載入指定網址，並回傳 JavaScript 執行完成後的最終 HTML。
        /// </summary>
        /// <param name="targetUrl">欲擷取的目標網址。</param>
        /// <param name="cancellationToken">要求取消權杖。</param>
        /// <returns>包含擷取結果、錯誤碼與錯誤訊息的服務回應。</returns>
        Task<WebScraperResult> RenderHtmlAsync(string targetUrl, CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// 提供 Playwright 動態渲染擷取能力的服務實作。
    /// </summary>
    public sealed class WebScraperService : IWebScraperService
    {
        private const int NavigationTimeoutMilliseconds = 45000;
        private static readonly string[] BrowserExecutableCandidates =
        {
            "/usr/bin/chromium",
            "/usr/bin/chromium-browser",
            "/usr/bin/google-chrome",
            "/usr/bin/google-chrome-stable",
            @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
            @"C:\Program Files\Microsoft\Edge\Application\msedge.exe",
            @"C:\Program Files\Google\Chrome\Application\chrome.exe",
            @"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe"
        };
        private readonly ILogger<WebScraperService> _logger;
        private readonly IConfiguration _configuration;

        /// <summary>
        /// 建立 <see cref="WebScraperService"/> 實例。
        /// </summary>
        /// <param name="logger">用於輸出稽核日誌的記錄器。</param>
        /// <param name="configuration">應用程式組態來源。</param>
        public WebScraperService(ILogger<WebScraperService> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
        }

        /// <inheritdoc />
        public async Task<WebScraperResult> RenderHtmlAsync(string targetUrl, CancellationToken cancellationToken = default)
        {
            IPlaywright? playwright = null;
            IBrowser? browser = null;
            IBrowserContext? browserContext = null;
            IPage? page = null;

            _logger.LogInformation("動態網頁渲染作業開始，目標 URL：{TargetUrl}", targetUrl);

            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                _logger.LogInformation("正在初始化 Playwright 執行個體。");
                playwright = await Playwright.CreateAsync();

                var browserExecutablePath = ResolveBrowserExecutablePath();

                if (string.IsNullOrWhiteSpace(browserExecutablePath))
                {
                    _logger.LogError("找不到可用的本機 Edge 或 Chrome 瀏覽器執行檔。");
                    return WebScraperResult.Failure(
                        StatusCodes.Status500InternalServerError,
                        "BROWSER_NOT_FOUND",
                        "伺服器找不到可用的本機 Edge 或 Chrome 瀏覽器，無法執行動態渲染。");
                }

                _logger.LogInformation("正在啟動本機 Chromium 核心瀏覽器，ExecutablePath：{ExecutablePath}", browserExecutablePath);
                browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
                {
                    Headless = true,
                    ExecutablePath = browserExecutablePath,
                    Args = GetBrowserLaunchArguments()
                });

                _logger.LogInformation("正在建立獨立瀏覽內容環境。");
                browserContext = await browser.NewContextAsync(new BrowserNewContextOptions
                {
                    IgnoreHTTPSErrors = false
                });

                _logger.LogInformation("正在建立新頁面。");
                page = await browserContext.NewPageAsync();
                page.SetDefaultNavigationTimeout(NavigationTimeoutMilliseconds);
                page.SetDefaultTimeout(NavigationTimeoutMilliseconds);

                _logger.LogInformation("正在導向目標頁面並等待網路閒置。");
                var response = await page.GotoAsync(targetUrl, new PageGotoOptions
                {
                    WaitUntil = WaitUntilState.NetworkIdle,
                    Timeout = NavigationTimeoutMilliseconds
                });

                if (response is null)
                {
                    _logger.LogWarning("頁面導向完成但未取得 HTTP 回應，URL：{TargetUrl}", targetUrl);
                }
                else
                {
                    _logger.LogInformation(
                        "目標頁面載入完成，HTTP 狀態碼：{StatusCode}，URL：{TargetUrl}",
                        response.Status,
                        targetUrl);
                }

                _logger.LogInformation("正在等待 DOM 與 JavaScript 執行穩定。");
                await page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
                await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

                _logger.LogInformation("正在讀取最終渲染 HTML。");
                var renderedHtml = await page.ContentAsync();

                _logger.LogInformation(
                    "動態網頁渲染作業成功完成，HTML 長度：{HtmlLength}，URL：{TargetUrl}",
                    renderedHtml.Length,
                    targetUrl);

                return WebScraperResult.Success(renderedHtml);
            }
            catch (TimeoutException ex)
            {
                _logger.LogError(ex, "目標頁面載入逾時，URL：{TargetUrl}", targetUrl);
                return WebScraperResult.Failure(
                    StatusCodes.Status504GatewayTimeout,
                    "PAGE_LOAD_TIMEOUT",
                    "目標網頁載入逾時，請稍後重試或確認網站回應時間。");
            }
            catch (PlaywrightException ex) when (IsCertificateError(ex))
            {
                _logger.LogError(ex, "目標頁面憑證驗證失敗，URL：{TargetUrl}", targetUrl);
                return WebScraperResult.Failure(
                    StatusCodes.Status502BadGateway,
                    "INVALID_CERTIFICATE",
                    "目標網站的 TLS/SSL 憑證無效，無法安全完成渲染。");
            }
            catch (PlaywrightException ex) when (IsNetworkBlockedError(ex))
            {
                _logger.LogError(ex, "目標頁面疑似遭網路阻擋或連線失敗，URL：{TargetUrl}", targetUrl);
                return WebScraperResult.Failure(
                    StatusCodes.Status503ServiceUnavailable,
                    "NETWORK_BLOCKED",
                    "無法連線至目標網站，可能遭到封鎖、DNS 解析失敗或遠端主機拒絕連線。");
            }
            catch (OperationCanceledException ex)
            {
                _logger.LogWarning(ex, "動態網頁渲染作業已被取消，URL：{TargetUrl}", targetUrl);
                return WebScraperResult.Failure(
                    StatusCodes.Status499ClientClosedRequest,
                    "REQUEST_CANCELLED",
                    "用戶端已取消本次稽核請求。");
            }
            catch (PlaywrightException ex)
            {
                _logger.LogError(ex, "Playwright 執行失敗，URL：{TargetUrl}", targetUrl);
                return WebScraperResult.Failure(
                    StatusCodes.Status502BadGateway,
                    "PLAYWRIGHT_RUNTIME_ERROR",
                    "無法完成動態頁面渲染，請檢查目標網站或稍後再試。");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "動態網頁渲染作業發生未預期例外，URL：{TargetUrl}", targetUrl);
                return WebScraperResult.Failure(
                    StatusCodes.Status500InternalServerError,
                    "UNEXPECTED_RENDER_ERROR",
                    "伺服器在處理動態渲染稽核時發生未預期錯誤。");
            }
            finally
            {
                _logger.LogInformation("開始釋放 Playwright 資源，URL：{TargetUrl}", targetUrl);

                if (page is not null)
                {
                    try
                    {
                        await page.CloseAsync();
                        _logger.LogInformation("IPage 已成功關閉。");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "關閉 IPage 時發生例外。");
                    }
                }

                if (browserContext is not null)
                {
                    try
                    {
                        await browserContext.CloseAsync();
                        _logger.LogInformation("IBrowserContext 已成功關閉。");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "關閉 IBrowserContext 時發生例外。");
                    }
                }

                if (browser is not null)
                {
                    try
                    {
                        await browser.CloseAsync();
                        _logger.LogInformation("IBrowser 已成功關閉。");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "關閉 IBrowser 時發生例外。");
                    }
                }

                if (playwright is not null)
                {
                    try
                    {
                        playwright.Dispose();
                        _logger.LogInformation("IPlaywright 已成功釋放。");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "釋放 IPlaywright 時發生例外。");
                    }
                }

                _logger.LogInformation("Playwright 資源釋放流程結束，URL：{TargetUrl}", targetUrl);
            }
        }

        private static bool IsCertificateError(PlaywrightException ex)
        {
            var message = ex.Message;

            return message.Contains("CERT_", StringComparison.OrdinalIgnoreCase)
                || message.Contains("SSL", StringComparison.OrdinalIgnoreCase)
                || message.Contains("certificate", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsNetworkBlockedError(PlaywrightException ex)
        {
            var message = ex.Message;

            return message.Contains("ERR_CONNECTION", StringComparison.OrdinalIgnoreCase)
                || message.Contains("ERR_NAME_NOT_RESOLVED", StringComparison.OrdinalIgnoreCase)
                || message.Contains("ERR_NETWORK", StringComparison.OrdinalIgnoreCase)
                || message.Contains("ERR_BLOCKED", StringComparison.OrdinalIgnoreCase)
                || message.Contains("ERR_TUNNEL", StringComparison.OrdinalIgnoreCase);
        }

        private string? ResolveBrowserExecutablePath()
        {
            var configuredPath = _configuration["BrowserOptions:ExecutablePath"]
                ?? Environment.GetEnvironmentVariable("BROWSER_EXECUTABLE_PATH");

            if (!string.IsNullOrWhiteSpace(configuredPath))
            {
                if (File.Exists(configuredPath))
                {
                    _logger.LogInformation("使用組態指定的瀏覽器執行檔：{ExecutablePath}", configuredPath);
                    return configuredPath;
                }

                _logger.LogWarning("組態指定的瀏覽器執行檔不存在：{ExecutablePath}", configuredPath);
            }

            foreach (var candidate in BrowserExecutableCandidates)
            {
                if (File.Exists(candidate))
                {
                    _logger.LogInformation("已找到可用本機瀏覽器執行檔：{ExecutablePath}", candidate);
                    return candidate;
                }
            }

            _logger.LogWarning("未找到任何可用的本機 Edge 或 Chrome 瀏覽器執行檔。");
            return null;
        }

        private List<string> GetBrowserLaunchArguments()
        {
            var launchArguments = new List<string>
            {
                "--disable-dev-shm-usage"
            };

            if (OperatingSystem.IsLinux())
            {
                launchArguments.Add("--no-sandbox");
                launchArguments.Add("--disable-setuid-sandbox");
            }

            return launchArguments;
        }
    }

    /// <summary>
    /// 表示動態網頁擷取服務的執行結果。
    /// </summary>
    /// <param name="IsSuccess">是否成功取得最終渲染 HTML。</param>
    /// <param name="Html">成功時的 HTML 內容。</param>
    /// <param name="StatusCode">失敗時建議回傳的 HTTP 狀態碼。</param>
    /// <param name="ErrorCode">失敗時的系統錯誤代碼。</param>
    /// <param name="ErrorMessage">失敗時的錯誤訊息。</param>
    public sealed record WebScraperResult(
        bool IsSuccess,
        string? Html,
        int StatusCode,
        string? ErrorCode,
        string? ErrorMessage)
    {
        /// <summary>
        /// 建立成功結果。
        /// </summary>
        /// <param name="html">最終渲染 HTML。</param>
        /// <returns>成功結果物件。</returns>
        public static WebScraperResult Success(string html) =>
            new(true, html, StatusCodes.Status200OK, null, null);

        /// <summary>
        /// 建立失敗結果。
        /// </summary>
        /// <param name="statusCode">建議對應的 HTTP 狀態碼。</param>
        /// <param name="errorCode">系統錯誤代碼。</param>
        /// <param name="errorMessage">可對外揭露的錯誤訊息。</param>
        /// <returns>失敗結果物件。</returns>
        public static WebScraperResult Failure(int statusCode, string errorCode, string errorMessage) =>
            new(false, null, statusCode, errorCode, errorMessage);
    }
}
