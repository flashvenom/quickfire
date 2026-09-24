using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Quickfire.Blazor.Data;

namespace Quickfire.Blazor.Infrastructure.Desktop
{
    public static class DesktopRuntimeInitializer
    {
        public static async Task InitializeAsync(
            IServiceProvider services,
            DesktopRuntimeOptions options,
            ILogger logger,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(services);
            options ??= new DesktopRuntimeOptions();

            logger?.LogInformation("Desktop runtime initialization requested.");

            using var scope = services.CreateScope();
            var scopedProvider = scope.ServiceProvider;

            ApplicationUser? ensuredAdmin = null;

            try
            {
                await ApplyMigrationsAsync(scopedProvider, logger, cancellationToken).ConfigureAwait(false);
                SeedInitialData.SeedData(scopedProvider);
                if (options.Admin is { } adminOptions)
                {
                    ensuredAdmin = await EnsureAdminAccountAsync(scopedProvider, adminOptions, logger).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Desktop runtime initialization failed.");
                throw;
            }

            logger?.LogInformation("Desktop runtime initialization completed.");
        }

        private static async Task ApplyMigrationsAsync(IServiceProvider services, ILogger logger, CancellationToken cancellationToken)
        {
            var factory = services.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
            await using var context = await factory.CreateDbContextAsync(cancellationToken).ConfigureAwait(false);

            if (!context.Database.IsSqlite())
            {
                logger?.LogInformation("Skipping desktop database migrations for provider {Provider}.", context.Database.ProviderName);
                return;
            }

            logger?.LogInformation("Applying desktop database migrations...");
            await context.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
        }

        private static async Task<ApplicationUser?> EnsureAdminAccountAsync(IServiceProvider services, DesktopAdminOptions adminOptions, ILogger logger)
        {
            var email = ResolveAdminEmail(adminOptions);
            if (string.IsNullOrWhiteSpace(email))
            {
                logger?.LogWarning("Skipping desktop admin bootstrap because no email or username was provided.");
                return null;
            }

            var desiredPicture = ResolveAdminPictureUrl(adminOptions);
            var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await userManager.FindByEmailAsync(email).ConfigureAwait(false);

            if (user is null)
            {
                user = new ApplicationUser
                {
                    UserName = email,
                    Email = email,
                    FirstName = adminOptions.FirstName,
                    LastName = adminOptions.LastName,
                    EmailConfirmed = true,
                    PictureUrl = desiredPicture
                };

                var password = string.IsNullOrWhiteSpace(adminOptions.TemporaryPassword)
                    ? throw new InvalidOperationException("First-run desktop setup requires an explicit administrator password.")
                    : adminOptions.TemporaryPassword!;

                var createResult = await userManager.CreateAsync(user, password).ConfigureAwait(false);
                if (!createResult.Succeeded)
                {
                    logger?.LogWarning("Failed to create desktop admin account: {Errors}", string.Join(", ", createResult.Errors.Select(e => e.Description)));
                    return null;
                }

                logger?.LogInformation("Created desktop admin account for {Email}", email);
                return user;
            }

            var updated = false;
            if (!string.IsNullOrWhiteSpace(adminOptions.FirstName) && !string.Equals(user.FirstName, adminOptions.FirstName, StringComparison.Ordinal))
            {
                user.FirstName = adminOptions.FirstName;
                updated = true;
            }

            if (!string.IsNullOrWhiteSpace(adminOptions.LastName) && !string.Equals(user.LastName, adminOptions.LastName, StringComparison.Ordinal))
            {
                user.LastName = adminOptions.LastName;
                updated = true;
            }

            if (!string.Equals(user.PictureUrl, desiredPicture, StringComparison.Ordinal))
            {
                user.PictureUrl = desiredPicture;
                updated = true;
            }

            if (updated)
            {
                await userManager.UpdateAsync(user).ConfigureAwait(false);
                logger?.LogInformation("Updated desktop admin profile for {Email}", email);
            }

            // A persisted bootstrap password must never reset an existing account.
            return user;
        }

        private static string ResolveAdminEmail(DesktopAdminOptions adminOptions)
        {
            if (!string.IsNullOrWhiteSpace(adminOptions.Email))
            {
                return adminOptions.Email.Trim();
            }

            if (!string.IsNullOrWhiteSpace(adminOptions.UserName))
            {
                return adminOptions.UserName.Trim();
            }

            return string.Empty;
        }

        private static string ResolveAdminPictureUrl(DesktopAdminOptions adminOptions)
        {
            return string.IsNullOrWhiteSpace(adminOptions.PictureUrl)
                ? "default.jpg"
                : adminOptions.PictureUrl.Trim();
        }

    }

    public sealed class DesktopRuntimeOptions
    {
        public DesktopAdminOptions? Admin { get; set; }
    }

    public sealed class DesktopAdminOptions
    {
        public string Email { get; set; } = string.Empty;
        public string UserName { get; set; } = string.Empty;
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string? TemporaryPassword { get; set; }
        public string PictureUrl { get; set; } = "default.jpg";
    }
}
