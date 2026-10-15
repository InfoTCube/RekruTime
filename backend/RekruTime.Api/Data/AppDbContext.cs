using Microsoft.EntityFrameworkCore;

namespace RekruTime.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
}
