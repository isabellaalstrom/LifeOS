using Microsoft.AspNetCore.Identity;

namespace LifeOS.Infrastructure.Identity;

public class AppUser : IdentityUser
{
    /// <summary>IANA time zone used for weekly/monthly/quarterly boundaries.</summary>
    public string TimeZoneId { get; set; } = "Europe/Stockholm";
}
