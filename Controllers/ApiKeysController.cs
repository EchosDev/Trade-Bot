using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Trade_Bot.Data;
using Trade_Bot.Models;
using Trade_Bot.Services;

namespace Trade_Bot.Controllers
{
    [Authorize]
    [Route("api/keys")]
    [ApiController]
    public class ApiKeysController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly CredentialProtector _protector;
        private readonly UserManager<AppUser> _userManager;

        public ApiKeysController(AppDbContext db, CredentialProtector protector,
                                  UserManager<AppUser> userManager)
        {
            _db = db;
            _protector = protector;
            _userManager = userManager;
        }

        [HttpGet]
        public async Task<IActionResult> Get()
        {
            var userId = _userManager.GetUserId(User);
            var cred = await _db.ApiCredentials.FirstOrDefaultAsync(c => c.UserId == userId);

            if (cred == null)
                return Ok(new { apiKey = "", hasSecret = false });

            return Ok(new { apiKey = cred.ApiKey, hasSecret = true });
        }

        [HttpPost]
        public async Task<IActionResult> Save([FromBody] SaveKeysRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.ApiKey) || string.IsNullOrWhiteSpace(request.ApiSecret))
                return BadRequest(new { message = "API Key və Secret boş ola bilməz." });

            var userId = _userManager.GetUserId(User);
            var cred = await _db.ApiCredentials.FirstOrDefaultAsync(c => c.UserId == userId);

            if (cred == null)
            {
                cred = new ApiCredential { UserId = userId! };
                _db.ApiCredentials.Add(cred);
            }

            cred.ApiKey = request.ApiKey;
            cred.EncryptedSecret = _protector.Encrypt(request.ApiSecret);
            cred.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();
            return Ok(new { message = "Saxlanıldı." });
        }
    }

    public class SaveKeysRequest
    {
        public string ApiKey { get; set; } = default!;
        public string ApiSecret { get; set; } = default!;
    }
}
