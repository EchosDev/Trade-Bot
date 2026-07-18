using Binance.Net;
using Binance.Net.Clients;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json.Serialization;
using Trade_Bot.Data;
using Trade_Bot.Models;
using Trade_Bot.Services;

namespace Trade_Bot.Controllers
{
    [Authorize]
    [Route("api/orders")]
    [ApiController]
    public class OrdersController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly UserManager<AppUser> _userManager;
        private readonly CredentialProtector _protector;

        public OrdersController(AppDbContext db, UserManager<AppUser> userManager, CredentialProtector protector)
        {
            _db = db;
            _userManager = userManager;
            _protector = protector;
        }

        [HttpGet]
        public async Task<IActionResult> GetAll()
        {
            var userId = _userManager.GetUserId(User);

            var orders = await _db.GridOrders
                .Where(o => o.UserId == userId)
                .OrderByDescending(o => o.CreatedAt)
                .Select(o => new
                {
                    id = o.Id,
                    symbol = o.Symbol,
                    buy = o.BuyPrice,
                    qty = o.BuyQty,
                    sell = o.SellPrice,
                    s_qty = o.SellQty,
                    repeat = o.Repeat,
                    status = o.Status,
                    marketType = o.MarketType
                })
                .ToListAsync();

            return Ok(orders);
        }

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] OrderRequest request)
        {
            var err = Validate(request);
            if (err != null) return BadRequest(new { message = err });

            var userId = _userManager.GetUserId(User);

            var order = new GridOrder
            {
                UserId = userId!,
                Symbol = request.Symbol.ToUpper(),
                BuyPrice = request.Buy,
                BuyQty = request.Qty,
                SellPrice = request.Sell,
                SellQty = request.S_Qty,
                Repeat = request.Repeat,
                MarketType = string.IsNullOrWhiteSpace(request.MarketType) ? "spot" : request.MarketType.ToLower(),
                Status = "waiting",
                CreatedAt = DateTime.UtcNow,
                Leverage = request.MarketType == "futures" ? Math.Clamp(request.Leverage, 1, 125) : 1,
            };

            _db.GridOrders.Add(order);
            await _db.SaveChangesAsync();

            return Ok(new { id = order.Id });
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> Update(int id, [FromBody] OrderRequest request)
        {
            var err = Validate(request);
            if (err != null) return BadRequest(new { message = err });

            var userId = _userManager.GetUserId(User);
            var order = await _db.GridOrders.FirstOrDefaultAsync(o => o.Id == id && o.UserId == userId);
            if (order == null) return NotFound();

            bool sellChanged = order.SellPrice != request.Sell || order.SellQty != request.S_Qty;

            var cred = await _db.ApiCredentials.FirstOrDefaultAsync(c => c.UserId == userId);
            BinanceRestClient? client = null;
            if (cred != null)
            {
                var secret = _protector.Decrypt(cred.EncryptedSecret);
                client = new BinanceRestClient(o =>
                {
                    o.ApiCredentials = new BinanceCredentials(cred.ApiKey, secret);
                    //o.Environment = BinanceEnvironment.Testnet;
                });
            }

            // Diqqət: MarketType burda DƏYİŞDİRİLMİR — order artıq hansı bazarda
            // yaradılıbsa (spot/futures), sonuna qədər elə qalır. Əks halda,
            // mövcud BuyOrderId/SellOrderId səhv API-yə (spot/futures) göndərilər.
            bool isFutures = order.MarketType == "futures";

            if (order.Status is "waiting" or "buy_placed" or "updated")
            {
                if (order.BuyOrderId.HasValue && client != null)
                {
                    if (isFutures)
                        await client.UsdFuturesApi.Trading.CancelOrderAsync(order.Symbol, order.BuyOrderId.Value);
                    else
                        await client.SpotApi.Trading.CancelOrderAsync(order.Symbol, order.BuyOrderId.Value);

                    await Log(userId, $"{order.Symbol}: köhnə BUY order-i ləğv edildi.", "info");
                }
                order.Status = "waiting";
                order.BuyOrderId = null;
            }
            else
            {
                // BUY artıq icra olunub. Hazırkı dövrü YENİ SELL qiymətiylə davam etdiririk,
                // BUY-u geri qaytarmırıq. BuyPrice/BuyQty DB-də yenilənir ki,
                // növbəti "repeat" dövründə YENİ BUY qiymətiylə alsın.
                if (sellChanged && order.SellOrderId.HasValue && client != null)
                {
                    if (isFutures)
                        await client.UsdFuturesApi.Trading.CancelOrderAsync(order.Symbol, order.SellOrderId.Value);
                    else
                        await client.SpotApi.Trading.CancelOrderAsync(order.Symbol, order.SellOrderId.Value);

                    await Log(userId, $"{order.Symbol}: köhnə SELL order-i ləğv edildi, yeni qiymətlə yenidən qoyulacaq.", "info");
                }
                order.Status = "buy_filled"; // worker növbəti tick-də yeni SELL-i qoyacaq
                order.SellOrderId = null;
            }

            order.Symbol = request.Symbol.ToUpper();
            order.BuyPrice = request.Buy;
            order.BuyQty = request.Qty;
            order.SellPrice = request.Sell;
            order.SellQty = request.S_Qty;
            order.Repeat = request.Repeat;
            order.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();
            client?.Dispose();

            return Ok(new { message = "Yeniləndi." });
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(int id)
        {
            var userId = _userManager.GetUserId(User);
            var order = await _db.GridOrders.FirstOrDefaultAsync(o => o.Id == id && o.UserId == userId);
            if (order == null) return NotFound();

            var cred = await _db.ApiCredentials.FirstOrDefaultAsync(c => c.UserId == userId);
            if (cred != null && (order.BuyOrderId.HasValue || order.SellOrderId.HasValue))
            {
                try
                {
                    var secret = _protector.Decrypt(cred.EncryptedSecret);
                    using var client = new BinanceRestClient(o =>
                    {
                        o.ApiCredentials = new BinanceCredentials(cred.ApiKey, secret);
                        //o.Environment = BinanceEnvironment.Testnet;
                    });

                    bool isFutures = order.MarketType == "futures";

                    if (order.BuyOrderId.HasValue)
                    {
                        if (isFutures)
                            await client.UsdFuturesApi.Trading.CancelOrderAsync(order.Symbol, order.BuyOrderId.Value);
                        else
                            await client.SpotApi.Trading.CancelOrderAsync(order.Symbol, order.BuyOrderId.Value);
                    }
                    if (order.SellOrderId.HasValue)
                    {
                        if (isFutures)
                            await client.UsdFuturesApi.Trading.CancelOrderAsync(order.Symbol, order.SellOrderId.Value);
                        else
                            await client.SpotApi.Trading.CancelOrderAsync(order.Symbol, order.SellOrderId.Value);
                    }
                }
                catch
                {
                    // Ləğv uğursuz olsa belə, order-i DB-dən silməyə davam edirik —
                    // ən pis halda Binance-də "yetim" bir order qalar, istifadəçi əl ilə ləğv edə bilər.
                }
            }

            _db.GridOrders.Remove(order);
            await _db.SaveChangesAsync();

            return Ok(new { message = "Silindi." });
        }

        private async Task Log(string userId, string message, string level)
        {
            _db.BotLogs.Add(new BotLog { UserId = userId, Message = message, Level = level });
            await _db.SaveChangesAsync();
        }

        private static string? Validate(OrderRequest r)
        {
            if (string.IsNullOrWhiteSpace(r.Symbol)) return "Simvol boş ola bilməz.";
            if (r.Buy <= 0 || r.Qty <= 0 || r.Sell <= 0 || r.S_Qty <= 0)
                return "Bütün qiymət və miqdar sahələri müsbət ədəd olmalıdır.";
            if (r.Buy >= r.Sell) return "BUY qiyməti SELL qiymətindən böyük və ya bərabər ola bilməz!";
            return null;
        }
    }

    public class OrderRequest
    {
        public string Symbol { get; set; } = default!;
        public decimal Buy { get; set; }
        public decimal Qty { get; set; }
        public decimal Sell { get; set; }

        [JsonPropertyName("s_qty")]
        public decimal S_Qty { get; set; }

        public bool Repeat { get; set; }
        public string MarketType { get; set; } = "spot";
        public int Leverage { get; set; } = 1;
    }
}