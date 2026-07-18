using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Trade_Bot.Models
{
    public class BotStatus
    {
        [Key]
        public string UserId { get; set; } = default!;

        [ForeignKey(nameof(UserId))]
        public AppUser User { get; set; } = default!;

        public bool IsRunning { get; set; }
        public DateTime? StartedAt { get; set; }
        public DateTime? StoppedAt { get; set; }
    }
}
