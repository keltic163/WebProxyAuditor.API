using Microsoft.AspNetCore.Mvc;
using WebProxyAuditor.API.Services;

namespace WebProxyAuditor.API.Controllers
{
    /// <summary>
    /// 提供動態網頁渲染稽核相關 API。
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [Produces("application/json")]
    public sealed class ProxyAuditController : ControllerBase
    {
        private readonly IWebScraperService _webScraperService;
        private readonly ILogger<ProxyAuditController> _logger;

        /// <summary>
        /// 建立 <see cref="ProxyAuditController"/> 實例。
        /// </summary>
        /// <param name="webScraperService">動態網頁渲染擷取服務。</param>
        /// <param name="logger">控制器日誌記錄器。</param>
        public ProxyAuditController(IWebScraperService webScraperService, ILogger<ProxyAuditController> logger)
        {
            _webScraperService = webScraperService;
            _logger = logger;
        }

        /// <summary>
        /// 以 Playwright 無頭瀏覽器載入目標網址，並回傳 JavaScript 執行完成後的最終 HTML。
        /// </summary>
        /// <param name="request">包含目標網址的稽核請求。</param>
        /// <param name="cancellationToken">要求取消權杖。</param>
        /// <returns>成功時回傳最終渲染 HTML，失敗時回傳標準化錯誤 JSON。</returns>
        [HttpPost("render-html")]
        [Consumes("application/json")]
        [ProducesResponseType(typeof(ProxyAuditSuccessResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ProxyAuditErrorResponse), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ProxyAuditErrorResponse), StatusCodes.Status499ClientClosedRequest)]
        [ProducesResponseType(typeof(ProxyAuditErrorResponse), StatusCodes.Status500InternalServerError)]
        [ProducesResponseType(typeof(ProxyAuditErrorResponse), StatusCodes.Status502BadGateway)]
        [ProducesResponseType(typeof(ProxyAuditErrorResponse), StatusCodes.Status503ServiceUnavailable)]
        [ProducesResponseType(typeof(ProxyAuditErrorResponse), StatusCodes.Status504GatewayTimeout)]
        public async Task<IActionResult> RenderHtmlAsync(
            [FromBody] ProxyAuditRequest request,
            CancellationToken cancellationToken)
        {
            if (!ModelState.IsValid)
            {
                return ValidationProblem(ModelState);
            }

            _logger.LogInformation("收到動態網頁渲染稽核請求，URL：{TargetUrl}", request.TargetUrl);

            var result = await _webScraperService.RenderHtmlAsync(request.TargetUrl, cancellationToken);

            if (result.IsSuccess)
            {
                _logger.LogInformation("動態網頁渲染稽核成功，URL：{TargetUrl}", request.TargetUrl);

                return Ok(new ProxyAuditSuccessResponse
                {
                    TargetUrl = request.TargetUrl,
                    RenderedHtml = result.Html ?? string.Empty
                });
            }

            _logger.LogWarning(
                "動態網頁渲染稽核失敗，URL：{TargetUrl}，ErrorCode：{ErrorCode}",
                request.TargetUrl,
                result.ErrorCode);

            return StatusCode(result.StatusCode, new ProxyAuditErrorResponse
            {
                ErrorCode = result.ErrorCode ?? "UNKNOWN_ERROR",
                ErrorMessage = result.ErrorMessage ?? "發生未知錯誤。",
                TargetUrl = request.TargetUrl
            });
        }
    }

    /// <summary>
    /// 表示動態網頁渲染稽核請求內容。
    /// </summary>
    public sealed class ProxyAuditRequest
    {
        /// <summary>
        /// 欲以無頭瀏覽器載入並擷取最終 HTML 的目標網址。
        /// </summary>
        [System.ComponentModel.DataAnnotations.Required(ErrorMessage = "請提供目標 URL。")]
        [System.ComponentModel.DataAnnotations.Url(ErrorMessage = "請提供有效的絕對 URL。")]
        public string TargetUrl { get; set; } = string.Empty;
    }

    /// <summary>
    /// 表示動態網頁渲染稽核成功回應。
    /// </summary>
    public sealed class ProxyAuditSuccessResponse
    {
        /// <summary>
        /// 本次稽核的目標網址。
        /// </summary>
        public string TargetUrl { get; set; } = string.Empty;

        /// <summary>
        /// JavaScript 執行完畢後取得的最終 HTML 字串。
        /// </summary>
        public string RenderedHtml { get; set; } = string.Empty;
    }

    /// <summary>
    /// 表示動態網頁渲染稽核失敗回應。
    /// </summary>
    public sealed class ProxyAuditErrorResponse
    {
        /// <summary>
        /// 系統定義的錯誤代碼。
        /// </summary>
        public string ErrorCode { get; set; } = string.Empty;

        /// <summary>
        /// 可供呼叫端與稽核記錄使用的錯誤說明。
        /// </summary>
        public string ErrorMessage { get; set; } = string.Empty;

        /// <summary>
        /// 本次稽核的目標網址。
        /// </summary>
        public string TargetUrl { get; set; } = string.Empty;
    }
}
