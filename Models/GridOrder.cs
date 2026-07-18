using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Trade_Bot.Models
{
    public class GridOrder
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string UserId { get; set; } = default!;

        [ForeignKey(nameof(UserId))]
        public AppUser User { get; set; } = default!;

        [Required, MaxLength(20)]
        public string Symbol { get; set; } = default!;

        [Column(TypeName = "decimal(18,8)")]
        public decimal BuyPrice { get; set; }

        [Column(TypeName = "decimal(18,8)")]
        public decimal BuyQty { get; set; }

        [Column(TypeName = "decimal(18,8)")]
        public decimal SellPrice { get; set; }

        [Column(TypeName = "decimal(18,8)")]
        public decimal SellQty { get; set; }

        public bool Repeat { get; set; }

        [MaxLength(20)]
        public string Status { get; set; } = "waiting";
        public long? BuyOrderId { get; set; }
        public long? SellOrderId { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }

        public string MarketType { get; set; } = "spot"; // "spot" | "futures"
        public int Leverage { get; set; } = 1; // yalnız futures üçün mənalıdır
    }
}
