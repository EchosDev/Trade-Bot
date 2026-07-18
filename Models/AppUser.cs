using Binance.Net.Enums;
using CryptoExchange.Net.Authentication;
using Microsoft.AspNetCore.Identity;

namespace Trade_Bot.Models
{
    public class AppUser : IdentityUser
    {
        public ApiCredential? ApiCredential { get; set; }
        public ICollection<GridOrder> GridOrders { get; set; } = new List<GridOrder>();
        public BotStatus? BotStatus { get; set; }
    }
}
