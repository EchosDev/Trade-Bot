using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Trade_Bot.Models
{
    public class BotLog
    {
        [Key]
        public long Id { get; set; }

        [Required]
        public string UserId { get; set; } = default!;

        [ForeignKey(nameof(UserId))]
        public AppUser User { get; set; } = default!;

        [Required]
        public string Message { get; set; } = default!;

        [MaxLength(10)]
        public string Level { get; set; } = "info";

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
