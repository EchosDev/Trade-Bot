using Binance.Net;
using Binance.Net.Clients;
using Binance.Net.Enums;
using Microsoft.EntityFrameworkCore;
using Trade_Bot.Data;

namespace Trade_Bot.Services
{
    public class GridBotWorker : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<GridBotWorker> _logger;

        public GridBotWorker(IServiceScopeFactory scopeFactory, ILogger<GridBotWorker> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ProcessTick(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "GridBotWorker tick xətası");
                }

                await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken); // orijinal time.sleep(2) əvəzi
            }
        }

        private async Task ProcessTick(CancellationToken ct)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var protector = scope.ServiceProvider.GetRequiredService<CredentialProtector>();

            var runningUserIds = await db.BotStatuses
                .Where(b => b.IsRunning)
                .Select(b => b.UserId)
                .ToListAsync(ct);

            foreach (var userId in runningUserIds)
            {
                var cred = await db.ApiCredentials.FirstOrDefaultAsync(c => c.UserId == userId, ct);
                if (cred == null) continue;

                string secret;
                try { secret = protector.Decrypt(cred.EncryptedSecret); }
                catch { continue; } // açarlar korlanıbsa, bu user-i keç, digərlərini dayandırma

                using var client = new BinanceRestClient(o =>
                {
                    o.ApiCredentials = new BinanceCredentials(cred.ApiKey, secret);
                    //o.Environment = BinanceEnvironment.Testnet;
                });

                var activeOrders = await db.GridOrders
                    .Where(o => o.UserId == userId && o.Status != "sell_filled")
                    .ToListAsync(ct);

                var repeatables = await db.GridOrders
                    .Where(o => o.UserId == userId && o.Status == "sell_filled" && o.Repeat)
                    .ToListAsync(ct);

                foreach (var order in repeatables)
                {
                    order.Status = "waiting";
                    order.BuyOrderId = null;
                    order.SellOrderId = null;
                    await Log(db, userId, $"{order.Symbol}: dövr təkrarlanır.", "info");
                }
                if (repeatables.Any()) await db.SaveChangesAsync(ct);

                foreach (var order in activeOrders)
                {
                    await ProcessOrder(client, db, userId, order, ct);
                }
            }
        }

        private async Task ProcessOrder(BinanceRestClient client, AppDbContext db,
                                 string userId, Models.GridOrder order, CancellationToken ct)
        {
            try
            {
                if (order.Status is "waiting" or "updated")
                {
                    var placeResult = order.MarketType == "futures"
                        ? await PlaceFuturesBuy(client, order, ct)
                        : await PlaceSpotBuy(client, order, ct);

                    if (!placeResult.success)
                    {
                        await Log(db, userId, $"{order.Symbol}: BUY sifarişi göndərilmədi — {placeResult.error}", "error");
                        return;
                    }

                    order.BuyOrderId = placeResult.orderId;
                    order.Status = "buy_placed";
                    await Log(db, userId, $"{order.Symbol}: BUY {order.BuyQty} @ {order.BuyPrice} yerləşdirildi ({order.MarketType}).", "info");
                    await db.SaveChangesAsync(ct);
                    return;
                }

                if (order.Status == "buy_placed" && order.BuyOrderId.HasValue)
                {
                    bool filled = order.MarketType == "futures"
                        ? await IsFuturesOrderFilled(client, order.Symbol, order.BuyOrderId.Value, ct)
                        : await IsSpotOrderFilled(client, order.Symbol, order.BuyOrderId.Value, ct);

                    if (filled)
                    {
                        order.Status = "buy_filled";
                        await Log(db, userId, $"{order.Symbol}: BUY icra olundu.", "info");
                        await db.SaveChangesAsync(ct);
                    }
                    return;
                }

                if (order.Status == "buy_filled")
                {
                    var placeResult = order.MarketType == "futures"
                        ? await PlaceFuturesSell(client, order, ct)
                        : await PlaceSpotSell(client, order, ct);

                    if (!placeResult.success)
                    {
                        await Log(db, userId, $"{order.Symbol}: SELL sifarişi göndərilmədi — {placeResult.error}", "error");
                        return;
                    }

                    order.SellOrderId = placeResult.orderId;
                    order.Status = "sell_placed";
                    await Log(db, userId, $"{order.Symbol}: SELL {order.SellQty} @ {order.SellPrice} yerləşdirildi ({order.MarketType}).", "info");
                    await db.SaveChangesAsync(ct);
                    return;
                }

                if (order.Status == "sell_placed" && order.SellOrderId.HasValue)
                {
                    bool filled = order.MarketType == "futures"
                        ? await IsFuturesOrderFilled(client, order.Symbol, order.SellOrderId.Value, ct)
                        : await IsSpotOrderFilled(client, order.Symbol, order.SellOrderId.Value, ct);

                    if (filled)
                    {
                        order.Status = "sell_filled";
                        await Log(db, userId, $"{order.Symbol}: SELL icra olundu.", "info");
                        await db.SaveChangesAsync(ct);
                    }
                    return;
                }
            }
            catch (Exception ex)
            {
                await Log(db, userId, $"{order.Symbol}: xəta — {ex.Message}", "error");
            }
        }

        // ---- Spot helper-ləri ----
        private async Task<(bool success, long? orderId, string? error)> PlaceSpotBuy(BinanceRestClient client, Models.GridOrder o, CancellationToken ct)
        {
            var r = await client.SpotApi.Trading.PlaceOrderAsync(o.Symbol, OrderSide.Buy, SpotOrderType.Limit,
                quantity: o.BuyQty, price: o.BuyPrice, timeInForce: TimeInForce.GoodTillCanceled, ct: ct);
            return (r.Success, r.Data?.Id, r.Error?.Message);
        }
        private async Task<(bool success, long? orderId, string? error)> PlaceSpotSell(BinanceRestClient client, Models.GridOrder o, CancellationToken ct)
        {
            var r = await client.SpotApi.Trading.PlaceOrderAsync(o.Symbol, OrderSide.Sell, SpotOrderType.Limit,
                quantity: o.SellQty, price: o.SellPrice, timeInForce: TimeInForce.GoodTillCanceled, ct: ct);
            return (r.Success, r.Data?.Id, r.Error?.Message);
        }
        private async Task<bool> IsSpotOrderFilled(BinanceRestClient client, string symbol, long orderId, CancellationToken ct)
        {
            var check = await client.SpotApi.Trading.GetOrderAsync(symbol, orderId, ct: ct);
            return check.Success && check.Data.Status == Binance.Net.Enums.OrderStatus.Filled;
        }

        private async Task<(bool success, long? orderId, string? error)> PlaceFuturesBuy(BinanceRestClient client, Models.GridOrder o, CancellationToken ct)
        {
            var leverageResult = await client.UsdFuturesApi.Account.ChangeInitialLeverageAsync(o.Symbol, o.Leverage, ct: ct);
            if (!leverageResult.Success)
            {
                return (false, null, $"Leverage təyin edilmədi: {leverageResult.Error?.Message}");
            }

            var r = await client.UsdFuturesApi.Trading.PlaceOrderAsync(o.Symbol, OrderSide.Buy, FuturesOrderType.Limit,
                quantity: o.BuyQty, price: o.BuyPrice, timeInForce: TimeInForce.GoodTillCanceled, ct: ct);
            return (r.Success, r.Data?.Id, r.Error?.Message);
        }
        private async Task<(bool success, long? orderId, string? error)> PlaceFuturesSell(BinanceRestClient client, Models.GridOrder o, CancellationToken ct)
        {
            var r = await client.UsdFuturesApi.Trading.PlaceOrderAsync(o.Symbol, OrderSide.Sell, FuturesOrderType.Limit,
                quantity: o.SellQty, price: o.SellPrice, timeInForce: TimeInForce.GoodTillCanceled, ct: ct);
            return (r.Success, r.Data?.Id, r.Error?.Message);
        }
        private async Task<bool> IsFuturesOrderFilled(BinanceRestClient client, string symbol, long orderId, CancellationToken ct)
        {
            var check = await client.UsdFuturesApi.Trading.GetOrderAsync(symbol, orderId, ct: ct);
            return check.Success && check.Data.Status == Binance.Net.Enums.OrderStatus.Filled;
        }

        private async Task Log(AppDbContext db, string userId, string message, string level)
        {
            db.BotLogs.Add(new Models.BotLog { UserId = userId, Message = message, Level = level });
            await db.SaveChangesAsync();
        }
    }
}
