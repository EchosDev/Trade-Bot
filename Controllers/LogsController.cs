using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Trade_Bot.Data;
using Trade_Bot.Models;

namespace Trade_Bot.Controllers
{
    [Authorize]
    [Route("api/logs")]
    [ApiController]
    public class LogsController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly UserManager<AppUser> _userManager;

        public LogsController(AppDbContext db, UserManager<AppUser> userManager)
        {
            _db = db;
            _userManager = userManager;
        }

        [HttpGet]
        public async Task<IActionResult> Get([FromQuery] long afterId = 0)
        {
            var userId = _userManager.GetUserId(User);

            var logs = await _db.BotLogs
                .Where(l => l.UserId == userId && l.Id > afterId)
                .OrderBy(l => l.Id)
                .Select(l => new
                {
                    id = l.Id,
                    message = l.Message,
                    level = l.Level,
                    createdAt = DateTime.SpecifyKind(l.CreatedAt, DateTimeKind.Utc)
                })
                .ToListAsync();

            return Ok(logs);
        }
        [HttpDelete]
        public async Task<IActionResult> ClearAll()
        {
            var userId = _userManager.GetUserId(User);

            var logs = _db.BotLogs.Where(l => l.UserId == userId);
            _db.BotLogs.RemoveRange(logs);
            await _db.SaveChangesAsync();

            return Ok(new { message = "Jurnal təmizləndi." });
        }
    }
}
