using System.ComponentModel.DataAnnotations;

namespace Trade_Bot.ViewModels
{
    public class RegisterViewModel
    {
        [Required]
        public string Username { get; set; } = default!;

        [Required, DataType(DataType.Password)]
        public string Password { get; set; } = default!;

        [Required, DataType(DataType.Password), Compare(nameof(Password))]
        public string ConfirmPassword { get; set; } = default!;
    }
}
