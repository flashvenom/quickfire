using Microsoft.EntityFrameworkCore;
using Quickfire.Blazor.Infrastructure.Bridge;

namespace Quickfire.Blazor.Data;

public partial class ApplicationDbContext
{
    public DbSet<BridgeDevice> BridgeDevices => Set<BridgeDevice>();
}
