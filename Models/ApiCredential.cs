using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Trade_Bot.Models
{
    public class ApiCredential
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string UserId { get; set; } = default!;

        [ForeignKey(nameof(UserId))]
        public AppUser User { get; set; } = default!;

        [Required, MaxLength(128)]
        public string ApiKey { get; set; } = default!;

        [Required]
        public string EncryptedSecret { get; set; } = default!;

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
