using Microsoft.AspNetCore.Identity;
using Quickfire.Blazor.Domain.Forms.Models;
using Quickfire.Blazor.Domain.Shared.Models;
using Quickfire.Blazor.Domain.Attachments.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Quickfire.Blazor.Data
{
    public static class SeedInitialData
    {
        public static void SeedData(IServiceProvider serviceProvider)
        {
            var userManager = serviceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var context = serviceProvider.GetRequiredService<ApplicationDbContext>();
            var config = serviceProvider.GetRequiredService<IConfiguration>();
            string Setting(string key, string fallback) => string.IsNullOrWhiteSpace(config[key]) ? fallback : config[key]!.Trim();

            var defaultHomepageLayout = new HomepageLayout
            {
                Certs = new HomepageItemSettings { Vis = HomepageItemVisibility.Visible, Len = 10 },
                Leads = new HomepageItemSettings { Vis = HomepageItemVisibility.Visible, Len = 20 },
                Checklist = new HomepageItemSettings { Vis = HomepageItemVisibility.Visible, Len = 0 },
                Tasks = new HomepageItemSettings { Vis = HomepageItemVisibility.Visible, Len = 0 },
                Quicklinks = new HomepageItemSettings { Vis = HomepageItemVisibility.Visible, Len = 0 },
                Inspiration = InspirationMode.BibleVerses
            };
            var defaultHomepageLayoutJson = defaultHomepageLayout.ToJson();

            var initialAdminEmail = Setting("Admin:Email", "admin@quickfire.local");
            var initialAdminUsername = Setting("Admin:Username", initialAdminEmail);
            var initialAdminFirstName = Setting("Admin:FirstName", "Admin");
            var initialAdminLastName = Setting("Admin:LastName", "User");
            var initialAdminPassword = config["Admin:Password"];
            var initialAdminPicture = Setting("Admin:Picture", "default.jpg");

            ApplicationUser? adminUser = null;

            if (!userManager.Users.Any())
            {
                adminUser = new ApplicationUser
                {
                    UserName = initialAdminUsername,
                    FirstName = initialAdminFirstName,
                    LastName = initialAdminLastName,
                    Email = initialAdminEmail,
                    PhoneNumber = "0000000000",
                    PictureUrl = initialAdminPicture,
                    EmailConfirmed = true,
                    EnableAudio = true,
                    EnableAnimations = true,
                    EnableSimpleMode = false,
                    HomepageLayoutJSON = defaultHomepageLayoutJson
                };

                if (string.IsNullOrWhiteSpace(initialAdminPassword))
                    throw new InvalidOperationException("First-run setup requires ADMIN_PASSWORD (or Admin:Password). Set a unique password and restart; initialization will resume safely.");
                var bootstrapPassword = initialAdminPassword;
                var result = userManager.CreateAsync(adminUser, bootstrapPassword).Result;

                if (!result.Succeeded)
                {
                    throw new Exception("Failed to create the admin user: " + string.Join(", ", result.Errors.Select(e => e.Description)));
                }
            }
            else
            {
                adminUser = userManager.Users.FirstOrDefault(u => u.Email == initialAdminEmail)
                    ?? userManager.Users.FirstOrDefault(u => u.UserName == initialAdminUsername)
                    ?? userManager.Users.FirstOrDefault();
            }

            if (adminUser != null)
            {
                var adminNeedsUpdate = false;

                if (!adminUser.EnableAudio.HasValue)
                {
                    adminUser.EnableAudio = true;
                    adminNeedsUpdate = true;
                }

                if (!adminUser.EnableAnimations.HasValue)
                {
                    adminUser.EnableAnimations = true;
                    adminNeedsUpdate = true;
                }

                if (!adminUser.EnableSimpleMode.HasValue)
                {
                    adminUser.EnableSimpleMode = false;
                    adminNeedsUpdate = true;
                }

                var layoutMissing = string.IsNullOrWhiteSpace(adminUser.HomepageLayoutJSON) ||
                    !adminUser.HomepageLayoutJSON.Contains("\"Inspiration\"", StringComparison.OrdinalIgnoreCase);

                if (layoutMissing)
                {
                    HomepageLayout? parsedLayout = null;
                    if (!string.IsNullOrWhiteSpace(adminUser.HomepageLayoutJSON))
                    {
                        try
                        {
                            parsedLayout = HomepageLayout.FromJson(adminUser.HomepageLayoutJSON);
                        }
                        catch
                        {
                            parsedLayout = null;
                        }
                    }

                    adminUser.HomepageLayoutJSON = (parsedLayout ?? defaultHomepageLayout).ToJson();
                    adminNeedsUpdate = true;
                }

                if (adminNeedsUpdate)
                {
                    var updateResult = userManager.UpdateAsync(adminUser).Result;
                    if (!updateResult.Succeeded)
                    {
                        throw new Exception("Failed to update the admin user: " + string.Join(", ", updateResult.Errors.Select(e => e.Description)));
                    }
                }
            }

            // Seed Folders
            if (!context.Folders.Any())
            {
                var folders = new[]
                {
                    new Folder { Name = "Policy", Description = "For declarations pages and copies of policies" },
                    new Folder { Name = "Endorsement", Description = "For carrier endorsements and policy changes" },
                    new Folder { Name = "Quote", Description = "For quotes and documents from carriers regarding renewals and new business" },
                    new Folder { Name = "Accounting", Description = "Invoices and bills and what not" },
                    new Folder { Name = "Application", Description = "Apps and supps and supps and apps" },
                    new Folder { Name = "Claims", Description = "Loss related docs like claims, loss runs, etc" }
                };
                context.Folders.AddRange(folders);
            }

            // Seed Settings
            if (!context.Settings.Any())
            {
                var settings = new Quickfire.Blazor.Domain.Shared.Models.Settings
                {
                    FileStore = FileStoreType.Local,
                    FileStorage = CreateInitialStorage(serviceProvider),
                    DisablePlugins = false,
                    SandbagMode = false,
                    FakeyMode = false
                };
                context.Settings.Add(settings);
            }

            // Seed Products
            var referenceProducts = new List<Product>
            {
                new Product { LineName = "Other", LineCode = "N/A", LineNickname = "Other", Description = "Catch-all for specialty or non-standard products not listed elsewhere." },
                new Product { LineName = "Workers' Compensation", LineCode = "WCO", LineNickname = "Work Comp", Description = "Covers employee injuries/illnesses from work; includes employer liability." },
                new Product { LineName = "General Liability", LineCode = "GLI", LineNickname = "Gen. Liability", Description = "Protects against third-party bodily injury, property damage, and advertising injury." },
                new Product { LineName = "Commercial Auto", LineCode = "AUT", LineNickname = "Com. Auto", Description = "Liability and physical damage coverage for business-owned or used vehicles." },
                new Product { LineName = "Cyber Liability", LineCode = "CYBR", LineNickname = "Cyber", Description = "Responds to data breaches, ransomware, privacy/media liability, and incident costs." },
                new Product { LineName = "Business Owner's Package", LineCode = "BOP", LineNickname = "BOP", Description = "Bundled policy for small businesses combining property, GL, and business income." },
                new Product { LineName = "Commercial Umbrella", LineCode = "UMB", LineNickname = "Umbrella", Description = "Provides excess limits over GL, Auto, and Employer's Liability." },
                new Product { LineName = "Employer's Practice Liability Insurance", LineCode = "EPLI", LineNickname = "EPLI", Description = "Covers allegations like wrongful termination, discrimination, and harassment." },
                new Product { LineName = "Group Medical", LineCode = "MED", LineNickname = "Group Med", Description = "Employer-sponsored health insurance benefits for employees and dependents." },
                new Product { LineName = "Professional Liability", LineCode = "E&O", LineNickname = "Prof. Liab", Description = "Covers claims arising from professional errors, omissions, or negligent advice." },
                new Product { LineName = "Bond", LineCode = "BND", LineNickname = "Bond", Description = "Surety or fidelity instruments guaranteeing performance or financial obligations." },
                new Product { LineName = "Inland Marine", LineCode = "INL", LineNickname = "Inland Marine", Description = "Covers mobile property, goods in transit, or specialized equipment." },
                new Product { LineName = "Equipment Floater", LineCode = "FLT", LineNickname = "Tools", Description = "Inland-marine form for contractor tools and scheduled mobile equipment." },
                new Product { LineName = "Property", LineCode = "PROP", LineNickname = "Property", Description = "Covers buildings, contents, and business interruption from covered perils." },
                new Product { LineName = "Directors and Officers", LineCode = "DOLI", LineNickname = "DOLI", Description = "Protects executives/board for alleged wrongful acts in managing the company." },
                new Product { LineName = "Key Man Life", LineCode = "LIFE", LineNickname = "Life", Description = "Life insurance providing a death benefit; includes term or permanent options." },
                new Product { LineName = "Personal Homeowners", LineCode = "HOME", LineNickname = "Homeowners", Description = "Covers dwelling, personal property, liability, and loss of use for a residence." }
            };

            var existingCodes = new HashSet<string>(context.Products.Select(p => p.LineCode ?? string.Empty).ToList(), StringComparer.OrdinalIgnoreCase);
            var productsToAdd = referenceProducts
                .Where(p => !existingCodes.Contains(p.LineCode ?? string.Empty))
                .ToList();

            if (productsToAdd.Count > 0)
            {
                context.Products.AddRange(productsToAdd);
            }

            // Seed FormPdfs
            if (!context.FormPdf.Any())
            {
                var formPdfs = new[]
                {
                    new FormPdf { Title = "Acord 25 (2016/03)", Description = "Commercial Insurance Application", Filepath = "a25-2016-03.pdf", DateCreated = DateTime.Now, DateModified = DateTime.Now },
                    new FormPdf { Title = "Acord 125 (2016/03)", Description = "Commercial Insurance Application", Filepath = "a125-2016-03.pdf", DateCreated = DateTime.Now, DateModified = DateTime.Now },
                    new FormPdf { Title = "SL-2 (2024/01)", Description = "Diligent Search Report", Filepath = "sl2-2024-01.pdf", DateCreated = DateTime.Now, DateModified = DateTime.Now },
                    new FormPdf { Title = "D-1 (2020/01)", Description = "D-1 Surplus Lines Notice", Filepath = "d1-2020-01.pdf", DateCreated = DateTime.Now, DateModified = DateTime.Now },
                    new FormPdf { Title = "D-2 (2020/01)", Description = "D-1 California Non-Admitted Notice", Filepath = "d2-2020-01.pdf", DateCreated = DateTime.Now, DateModified = DateTime.Now },
                    new FormPdf { Title = "TRIA LMA9184", Description = "Terrorism Disclosure", Filepath = "tria-lma9184.pdf", DateCreated = DateTime.Now, DateModified = DateTime.Now }
                };
                context.FormPdf.AddRange(formPdfs);
            }

            // Save changes to the database
            context.SaveChanges();
        }

        private static FileStorageSettings CreateInitialStorage(IServiceProvider services)
        {
            var environment = services.GetRequiredService<IWebHostEnvironment>();
            var root = environment.WebRootPath ?? Path.Combine(environment.ContentRootPath, "wwwroot");
            return new FileStorageSettings
            {
                Mode = FileStorageMode.LocalDesktop,
                ServerAbsoluteRoot = root,
                LocalRootPath = root,
                NetworkSharePath = null,
                PublicBaseUrl = null,
                PreferFileSchemeLinks = false,
                StripUploadsFromMappedPath = false
            }.Normalize();
        }
    }
}
