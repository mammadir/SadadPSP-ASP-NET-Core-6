using Microsoft.AspNetCore.Mvc;
using Sadad.Psp.Models;
using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace Sadad.Psp.Controllers
{
    public class HomeController : Controller
    {
        private readonly ILogger<HomeController> _logger;
        private readonly IConfiguration _config;

        public HomeController(ILogger<HomeController> logger, IConfiguration config)
        {
            _logger = logger;
            _config = config;
        }

        public IActionResult Index()
        {
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Index(PaymentRequest request)
        {
            // فایل
            // appsettings.json
            // یا
            // appsettings.Development.json
            // ویرایش شود
            var TerminalId = _config["terminalId"];
            var TerminalKey = _config["terminalKey"];
            var MerchantId = _config["merchantId"];
            var ReturnUrl = _config["returnUrl"];
            var PurchasePage = _config["purchasePage"];
            
            if (!ModelState.IsValid)
                return View(request);
            try
            {
                var OrderId = new Random().Next(1000, int.MaxValue).ToString(); // شماره فاکتور - نباید تکراری باشد

                // رمزنگاری TripleDes
                string SignData = TripleDes($"{TerminalId};{OrderId};{request.Amount}", TerminalKey);

                var data = new
                {
                    TerminalId,
                    MerchantId,
                    request.Amount,
                    SignData,
                    ReturnUrl,
                    LocalDateTime = DateTime.Now,
                    OrderId,
                };

                var ipgUri = string.Format("{0}/api/v0/Request/PaymentRequest", PurchasePage);

                // درخواست توکن به سرور
                var res = await CallApi<PayResultData>(ipgUri, data);

                if (res != null)
                {
                    if (res.ResCode == "0")
                    {
                        // ذخیره
                        // res.Token
                        // در پایگاه داده

                        Response.Redirect(string.Format("{0}/Purchase/Index?token={1}", PurchasePage, res.Token));
                    }
                    ViewBag.Message = res.Description;
                    return View();
                }
            }
            catch (Exception ex)
            {
                ViewBag.Message = ex.ToString();
            }
            return View();
        }

        /// <summary>
        /// تایید تراکنش بعد از پرداخت، مستقیم توسط درگاه صدا زده می شود این متد.
        /// </summary>
        /// <param name="result"></param>
        /// <returns></returns>
        [HttpPost]
        public IActionResult Verify(PurchaseResult result)
        {
            // مقایسه و اعتبار سنجی توکن و شماره فاکتور در پایگاه داده            
            // result.Token
            // result.OrderId
            // در صورت مجاز ادامه میدهیم

            // چک کردن مقدار زیر در صورت 0 بودن ادامه خواهیم داد
            // result.ResCode

            try
            {
                // فایل
                // appsettings.json
                // یا
                // appsettings.Development.json
                // ویرایش شود
                var TerminalId = _config["terminalId"];
                var TerminalKey = _config["terminalKey"];
                var MerchantId = _config["merchantId"];
                var ReturnUrl = _config["returnUrl"];
                var PurchasePage = _config["purchasePage"];

                // رمزنگاری TripleDes
                var signedData = TripleDes(result.Token ?? "", TerminalKey);

                var data = new
                {
                    token = result.Token,
                    SignData = signedData
                };

                var ipgUri = string.Format("{0}/api/v0/Advice/Verify", PurchasePage);

                // بررسی صحت اطلاعات دریافتی با درگاه
                var res = CallApi<VerifyResultData>(ipgUri, data);
                if (res != null && res.Result != null)
                {
                    if (res.Result.ResCode == "0")
                    {
                        // مقایسه اطلاعات دریافتی با پایگاه داده

                        // ذخیره اطلاعات در پایگاه داده
                        res.Result.Succeed = true;                        
                        return View(res.Result);
                    }
                    ViewBag.Message = res.Result.Description;
                    return View(res.Result);
                }
            }
            catch (Exception ex)
            {
                ViewBag.Message = ex.ToString();
            }

            return View(new VerifyResultData());
        }

        public IActionResult Privacy()
        {
            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }

        private static async Task<T?> CallApi<T>(string apiUrl, object value)
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls;
            using var client = new HttpClient();
            client.BaseAddress = new Uri(apiUrl);
            client.DefaultRequestHeaders.Accept.Clear();
            var w = client.PostAsJsonAsync(apiUrl, value);
            w.Wait();
            HttpResponseMessage response = w.Result;
            if (response.IsSuccessStatusCode)
            {
                var result = response.Content.ReadFromJsonAsync<T>();
                result.Wait();
                return result.Result;
            }
            return default;
        }

        private static string TripleDes(string data, string TerminalKey)
        {
            var dataBytes = Encoding.UTF8.GetBytes(data);

            var symmetric = SymmetricAlgorithm.Create("TripleDes");
            symmetric.Mode = CipherMode.ECB;
            symmetric.Padding = PaddingMode.PKCS7;

            var encryptor = symmetric.CreateEncryptor(Convert.FromBase64String(TerminalKey), new byte[32]);

            var SignData = Convert.ToBase64String(encryptor.TransformFinalBlock(dataBytes, 0, dataBytes.Length));
            return SignData;
        }

        public class PaymentRequest
        {
            public long Amount { get; set; } = 1000;
        }

        public class PayResultData
        {
            public string? ResCode { get; set; }
            public string? Token { get; set; }
            public string? Description { get; set; }
        }

        public class PurchaseResult
        {
            public string? OrderId { get; set; }
            public string? Token { get; set; }
            public string? ResCode { get; set; }
        }

        public class VerifyResultData
        {
            public bool Succeed { get; set; }
            public string? ResCode { get; set; }
            public string? Description { get; set; }
            public string? Amount { get; set; }
            public string? RetrivalRefNo { get; set; }
            public string? SystemTraceNo { get; set; }
            public string? OrderId { get; set; }
        }
    }
}