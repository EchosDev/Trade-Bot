using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Trade_Bot.Data;
using Trade_Bot.Models;

namespace Trade_Bot.Controllers
{
    [Authorize]
    [Route("api/bot")]
    [ApiController]
    public class BotController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly UserManager<AppUser> _userManager;

        public BotController(AppDbContext db, UserManager<AppUser> userManager)
        {
            _db = db;
            _userManager = userManager;
        }

        [HttpPost("start")]
        public async Task<IActionResult> Start()
        {
            var userId = _userManager.GetUserId(User);

            var hasCredentials = await _db.ApiCredentials.AnyAsync(c => c.UserId == userId);
            if (!hasCredentials)
                return BadRequest(new { message = "Əvvəlcə API açarlarını yadda saxlayın." });

            var hasOrders = await _db.GridOrders.AnyAsync(o => o.UserId == userId);
            if (!hasOrders)
                return BadRequest(new { message = "Ən azı bir sifariş əlavə edin." });

            var status = await _db.BotStatuses.FirstOrDefaultAsync(b => b.UserId == userId);
            if (status == null)
            {
                status = new BotStatus { UserId = userId! };
                _db.BotStatuses.Add(status);
            }

            status.IsRunning = true;
            status.StartedAt = DateTime.UtcNow;
            status.StoppedAt = null;
            await _db.SaveChangesAsync();

            return Ok(new { message = "Bot başladıldı." });
        }

        [HttpPost("stop")]
        public async Task<IActionResult> Stop()
        {
            var userId = _userManager.GetUserId(User);
            var status = await _db.BotStatuses.FirstOrDefaultAsync(b => b.UserId == userId);
            if (status == null) return Ok(new { message = "Bot onsuz da dayandırılıb." });

            status.IsRunning = false;
            status.StoppedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            return Ok(new { message = "Bot dayandırıldı." });
        }

        [HttpGet("status")]
        public async Task<IActionResult> GetStatus()
        {
            var userId = _userManager.GetUserId(User);
            var status = await _db.BotStatuses.FirstOrDefaultAsync(b => b.UserId == userId);
            return Ok(new { isRunning = status?.IsRunning ?? false });
        }
    }
}
