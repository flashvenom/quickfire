using Quickfire.Blazor.Data;
using Quickfire.Blazor.Domain.Carriers.Models;
using Quickfire.Blazor.Domain.Clients.Models;
using Quickfire.Blazor.Domain.Clients.Services;
using Quickfire.Blazor.Domain.Policies.Models;
using Quickfire.Blazor.Domain.Renewals.Models;
using Quickfire.Blazor.Domain.Renewals.ViewModels;
using Quickfire.Blazor.Domain.Shared.Models;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;

namespace Quickfire.Blazor.Domain.Shared.Services
{
    public class StateService : IDisposable
    {
        // Database context and service provider
        private readonly IServiceProvider _serviceProvider;
        private readonly IDbContextFactory<ApplicationDbContext> _dbContextFactory;
        private readonly IConfiguration _configuration;

        private readonly ClientStateService _clientStateService;

        public StateService(IServiceProvider serviceProvider, IDbContextFactory<ApplicationDbContext> dbContextFactory, IConfiguration configuration, ClientStateService clientStateService)
        {
            _serviceProvider = serviceProvider;
            _dbContextFactory = dbContextFactory;
            _configuration = configuration;
            _clientStateService = clientStateService;
            _organizationTimeZone = ResolveTimeZone(GetDefaultTimeZoneId());
        }

        //_____________________________________________________________________________________//
        //=====================================================================================//
        // SHARED COMMON DATA -----------------------------------------------------------------//
        public string SurefireVersion;
        public event Action? OnOrganizationTimeZoneChanged;
        private bool _isInitialized = false;
        private bool _timeZoneInitialized;
        private readonly SemaphoreSlim _timeZoneLock = new(1, 1);
        private string _organizationTimeZoneId = GetDefaultTimeZoneId();
        public bool IsInitialized => _isInitialized;
        public string OrganizationTimeZoneId => _organizationTimeZoneId;
        public string DatabaseProvider { get; private set; } = string.Empty;
        private TimeZoneInfo _organizationTimeZone;
        public TimeZoneInfo OrganizationTimeZone => _organizationTimeZone;

        // Static lists Props ----------------------------------------------------------------//
        private Task<List<Carrier>>? _allCarriersTask;
        private Task<List<Carrier>>? _allWholesalersTask;
        private Task<List<Product>>? _allProductsTask;
        private Task<List<ApplicationUser>>? _allUsersTask;
        private Task<List<ClientListItem>>? _allClientsTask;
        public Task<List<Carrier>> AllCarriers => _allCarriersTask ??= LoadCarriersAsync();
        public Task<List<Carrier>> AllWholesalers => _allWholesalersTask ??= LoadWholesalersAsync();
        public Task<List<Product>> AllProducts => _allProductsTask ??= LoadProductsAsync();
        public Task<List<ApplicationUser>> AllUsers => _allUsersTask ??= LoadUsersAsync();
        public Task<List<ClientListItem>> AllClients => _allClientsTask ??= LoadClientsAsync();

        // Static lists Methods---------------------------------------------------------------//
        private readonly SemaphoreSlim _initializationSemaphore = new(1, 1);
        private readonly Dictionary<int, int> _lastSelectedSubmissionPerRenewal = new();


        //_____________________________________________________________________________________//
        //=====================================================================================//
        // Initialization --------------------------------------------------------------------//
        private readonly TaskCompletionSource<bool> _initializationTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task InitializationTask => _initializationTcs.Task;
        public async Task InitializeStateAsync(Task<AuthenticationState> authStateTask)
        {
            if (_isInitialized || _isDisposed)
            {
                return;
            }

            await _initializationSemaphore.WaitAsync();
            try
            {
                // Double-check after acquiring the semaphore
                if (_isInitialized)
                {
                    return;
                }

                // User initialization
                SurefireVersion = _configuration["Surefire:System:Version"] ?? $"v{typeof(StateService).Assembly.GetName().Version?.ToString(3)}";
                var authState = await authStateTask;
                var user = authState.User;

                using var context = _dbContextFactory.CreateDbContext();
                DatabaseProvider = context.Database.ProviderName ?? string.Empty;

                CurrentUser = null;
                UserPreferences = null;
                var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
                if (user.Identity?.IsAuthenticated == true && !string.IsNullOrWhiteSpace(userId))
                {
                    CurrentUser = await context.Users.FirstOrDefaultAsync(u => u.Id == userId);

                    if (CurrentUser != null)
                    {
                        // Update LastLogin timestamp
                        CurrentUser.LastLogin = DateTime.UtcNow;
                        context.Users.Update(CurrentUser);
                        await context.SaveChangesAsync();

                        // Initialize UserPreferences from CurrentUser
                        UserPreferences = Models.UserPreferences.FromApplicationUser(CurrentUser);
                    }
                }

                if (CurrentUser is null)
                {
                    // Unblock components waiting for this attempt, but do not expose
                    // application data or mark a missing account as ready to render.
                    _initializationTcs.TrySetResult(false);
                    return;
                }

                var settings = await GetSystemSettingsAsync();
                _disablePlugins = settings?.DisablePlugins ?? false;
                _sandbagMode = settings?.SandbagMode ?? false;
                _fakeyMode = settings?.FakeyMode ?? false;

                // CORE INITIALIZATION - Only essential app data that blocks UI
                var coreInitializationTasks = new List<Task>
                {
                    LoadCarriersAsync(),
                    LoadWholesalersAsync(),
                    LoadProductsAsync(),
                    LoadUsersAsync(),
                    SetMostRecentlyOpenedClientIdAsync()
                };

                await Task.WhenAll(coreInitializationTasks);

                // Mark as initialized BEFORE background tasks
                _isInitialized = true;
                _initializationTcs.TrySetResult(true);

            }
            finally
            {
                if (!_isDisposed)
                {
                    _initializationSemaphore.Release();
                }
            }
        }

        // Load Common Lists -----------------------------------------------------------------//
        private async Task<List<Carrier>> LoadCarriersAsync()
        {
            using var context = _dbContextFactory.CreateDbContext();
            try
            {
                return await context.Carriers
                    .AsNoTracking()
                    .Where(c => c.IssuingCarrier)
                    .ToListAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading carriers: {ex.Message}");
                return new List<Carrier>();
            }
        }
        private async Task<List<Carrier>> LoadWholesalersAsync()
        {
            using var context = _dbContextFactory.CreateDbContext();
            try
            {
                return await context.Carriers
                    .AsNoTracking()
                    .Where(c => c.Wholesaler)
                    .ToListAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading wholesalers: {ex.Message}");
                return new List<Carrier>();
            }
        }
        private async Task<List<Product>> LoadProductsAsync()
        {
            using var context = _dbContextFactory.CreateDbContext();
            try
            {
                return await context.Products
                    .AsNoTracking()
                    .OrderBy(p => p.LineName)
                    .ToListAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading products: {ex.Message}");
                return new List<Product>();
            }
        }
        private async Task<List<ApplicationUser>> LoadUsersAsync()
        {
            // Create a fresh context for each request to avoid threading issues
            using var context = _dbContextFactory.CreateDbContext();
            try
            {
                return await context.Users
                    .AsNoTracking() // Use AsNoTracking to prevent EF from tracking these entities
                    .ToListAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading users: {ex.Message}");
                return new List<ApplicationUser>();
            }
        }
        private async Task<List<ClientListItem>> LoadClientsAsync()
        {
            if (_clientStateService.IsClientsCacheValid())
            {
                return _clientStateService.AllClients ?? new List<ClientListItem>();
            }

            using var context = _dbContextFactory.CreateDbContext();
            try
            {
                var clients = await context.Clients
                    .AsNoTracking()
                    .OrderByDescending(c => c.DateOpened)
                    .Select(c => new ClientListItem
                    {
                        ClientId = c.ClientId,
                        Name = c.Name,
                        DateOpened = c.DateOpened
                    })
                    .ToListAsync();

                _clientStateService.SetClientsCache(clients);
                return clients;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading clients: {ex.Message}");
                return new List<ClientListItem>();
            }
        }

        // CurrentUser and State Props --------------------------------------------------------//
        public ApplicationUser? CurrentUser { get; private set; }
        public Models.UserPreferences? UserPreferences { get; private set; }
        public Models.HomepageLayout HomepageLayout => UserPreferences?.HomepageLayout ?? new Models.HomepageLayout();
        public int ContactId { get; set; } = 0;
        public int CarrierId { get; set; } = 0;
        public string ProfileTab { get; set; } = "tab-profile";

        // User Preferences -------------------------------------------------------------------//
        public event Action? OnUserPreferencesChanged;
        private readonly ConcurrentDictionary<string, string> _userSettings = new();
        public void UpdateUserPreferences(Models.UserPreferences preferences)
        {
            UserPreferences = preferences;
            OnUserPreferencesChanged?.Invoke();
        }


        //_____________________________________________________________________________________//
        //=====================================================================================//
        // RENEWAL STATE ----------------------------------------------------------------------//
        public int HtmlRenewalId { get; set; } = 0;
        public int HtmlMonth { get; set; } = DateTime.Now.Month;
        public int HtmlYear { get; set; } = DateTime.Now.Year;
        public string HtmlTab { get; set; } = "tab-1";
        public int HtmlSubTaskId { get; set; } = 0;
        public int HtmlTabId { get; set; } = 0;
        public string HtmlUser { get; set; } = "Everyone";
        public string HtmlView { get; set; } = "list";
        public bool IsLoading { get; set; } = false;
        public List<RenewalListItemViewModel> RenewalList { get; set; } = new();
        public List<Policy> PolicyOrphanList { get; set; } = new();
        public Func<int, Task>? LoadRenewalFromSearch { get; set; }
        public void UpdateCachedRenFlowTasks(List<HomePageRenFlowTasksViewModel> tasks)
        {
            _cachedRenFlowTasks = tasks;
            RaiseHomepageDataUpdated();
        }
        public async Task RefreshRenFlowTasksAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                var homeService = _serviceProvider.GetRequiredService<HomeService>();
                _cachedRenFlowTasks = await homeService.GetHomePageRenFlowTasksAsync(false);
                RaiseHomepageDataUpdated();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[STATE] Error refreshing renflow tasks: {ex.Message}");
                _cachedRenFlowTasks = new();
            }
        }

        //_____________________________________________________________________________________//
        //=====================================================================================//
        // CLIENT SCREEN ----------------------------------------------------------------------//
        public event Action? OnAttachmentListUpdated;
        public void NotifyAttachmentListUpdated() => OnAttachmentListUpdated?.Invoke();
        public event Func<int, Task>? OnClientUpdated;
        public Func<int, Task>? LoadClientFromSearch { get; set; }
        public string ClientTab { get; set; } = "tab-1";
        public int ClientId { get; set; } = 0;
        public int? LastFormsLibraryEntryId { get; set; }
        public async Task SetMostRecentlyOpenedClientIdAsync()
        {
            using var context = _dbContextFactory.CreateDbContext();
            // Fetch the most recently opened client ID
            var mostRecentClientId = await context.Clients
                .OrderByDescending(c => c.DateOpened)
                .Select(c => (int?)c.ClientId)
                .FirstOrDefaultAsync();

            if (mostRecentClientId.HasValue && mostRecentClientId.Value != 0)
            {
                ClientId = mostRecentClientId.Value;
                _clientStateService.SelectedClientId = mostRecentClientId.Value;
                _clientStateService.ActiveTab = "tab-1";
            }
            else
            {
                // No recent client found, try to get any client
                var anyClientId = await context.Clients
                    .OrderBy(c => c.ClientId)
                    .Select(c => (int?)c.ClientId)
                    .FirstOrDefaultAsync();

                if (anyClientId.HasValue && anyClientId.Value != 0)
                {
                    ClientId = anyClientId.Value;
                    _clientStateService.SelectedClientId = anyClientId.Value;
                    _clientStateService.ActiveTab = "tab-1";
                }
                else
                {
                    ClientId = 0;
                    _clientStateService.SelectedClientId = null;
                    _clientStateService.ActiveTab = "tab-1";
                }
            }
        }
        public async Task InvalidateClientsCacheAsync()
        {
            _allClientsTask = null;
            await _clientStateService.InvalidateClientsCacheAsync();
        }
        public async Task InvalidateFormPdfsCacheAsync()
        {
            await _clientStateService.InvalidateFormPdfsCacheAsync();
        }


        //_____________________________________________________________________________________//
        //=====================================================================================//
        // STATUS MESSAGES --------------------------------------------------------------------//
        public event Action? OnStatusChanged;
        public event Action? OnSectionChanged;
        private string _statusMessage = "Loading...";
        private bool _statusLoading = false;
        public string StatusMessage
        {
            get => _statusMessage;
            private set
            {
                if (_statusMessage != value)
                {
                    _statusMessage = value;
                    NotifyStatusChanged();
                }
            }
        }
        public bool StatusLoading
        {
            get => _statusLoading;
            private set
            {
                if (_statusLoading != value)
                {
                    _statusLoading = value;
                    NotifyStatusChanged();
                }
            }
        }
        public void UpdateStatus(string newStatus, bool? isLoading = null)
        {
            StatusMessage = newStatus;
            if (isLoading.HasValue)
            {
                StatusLoading = isLoading.Value;
            }
        }
        private void NotifyStatusChanged() => OnStatusChanged?.Invoke();


        //_____________________________________________________________________________________//
        //=====================================================================================//
        // SYSTEM SETTINGS --------------------------------------------------------------------//
        private bool _disablePlugins;
        private bool _sandbagMode;
        private bool _fakeyMode;
        public bool DisablePlugins => _disablePlugins;
        public bool SandbagMode => _sandbagMode;
        public bool FakeyMode => _fakeyMode;
        private FileStorageSettings _fileStorageSettings = FileStorageSettings.CreateDefault();
        public FileStorageSettings FileStorageSettings => _fileStorageSettings;
        public async Task<Settings?> GetSystemSettingsAsync()
        {
            try
            {
                using var context = _dbContextFactory.CreateDbContext();
                var settings = await context.Settings
                    .OrderBy(s => s.SettingsId) // Add ordering to prevent EF warning
                    .FirstOrDefaultAsync();

                var hasChanges = false;
                if (settings == null)
                {
                    settings = new Settings();
                    context.Settings.Add(settings);
                    hasChanges = true;
                }

                var normalizedTimeZoneId = NormalizeTimeZoneId(settings.OrganizationTimeZoneId);
                if (!string.Equals(settings.OrganizationTimeZoneId, normalizedTimeZoneId, StringComparison.Ordinal))
                {
                    settings.OrganizationTimeZoneId = normalizedTimeZoneId;
                    hasChanges = true;
                }

                var originalStorageJson = settings.FileStorageSettingsJson;
                var storageSettings = settings.FileStorage ?? FileStorageSettings.CreateDefault();
                ApplyFileStorageOverrides(storageSettings);
                settings.FileStorage = storageSettings;
                if (!string.Equals(originalStorageJson, settings.FileStorageSettingsJson, StringComparison.Ordinal))
                {
                    hasChanges = true;
                }

                if (hasChanges)
                {
                    await context.SaveChangesAsync();
                }

                SetOrganizationTimeZoneInternal(normalizedTimeZoneId);
                _fileStorageSettings = settings.FileStorage;
                FileStorageResolverAccessor.Resolver.Update(_fileStorageSettings);

                _disablePlugins = settings.DisablePlugins;
                _sandbagMode = settings.SandbagMode;
                _fakeyMode = settings.FakeyMode;

                return settings;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[STATE] Error fetching system settings: {ex.Message}");
                return null;
            }
        }
        public async Task SaveSystemSettingsAsync(Settings settings)
        {
            try
            {
                var normalizedTimeZoneId = NormalizeTimeZoneId(settings.OrganizationTimeZoneId);
                settings.OrganizationTimeZoneId = normalizedTimeZoneId;
                var storageSettings = settings.FileStorage ?? FileStorageSettings.CreateDefault();
                ApplyFileStorageOverrides(storageSettings);
                storageSettings.Normalize();
                settings.FileStorage = storageSettings;

                using var context = _dbContextFactory.CreateDbContext();
                var existingSettings = await context.Settings
                    .OrderBy(s => s.SettingsId) // Add ordering to prevent EF warning
                    .FirstOrDefaultAsync();

                if (existingSettings == null)
                {
                    // If no settings exist, add the new settings
                    settings.FileStore = MapFileStoreType(storageSettings);
                    settings.FileServerMappedPath ??= storageSettings.NetworkSharePath;
                    context.Settings.Add(settings);
                }
                else
                {
                    // Update existing settings
                    existingSettings.DbType = settings.DbType;
                    existingSettings.DbConnectionString = settings.DbConnectionString;
                    existingSettings.PayLinkStringTemplate = settings.PayLinkStringTemplate;
                    existingSettings.BlastmailProviderKey = settings.BlastmailProviderKey;
                    existingSettings.BlastmailDefaultSenderName = settings.BlastmailDefaultSenderName;
                    existingSettings.BlastmailDefaultSender = settings.BlastmailDefaultSender;
                    existingSettings.AzureBlobConnectionString = settings.AzureBlobConnectionString;
                    existingSettings.AzureBlobContainerName = settings.AzureBlobContainerName;
                    existingSettings.FileServerMappedPath = storageSettings.NetworkSharePath ?? settings.FileServerMappedPath;
                    existingSettings.FileStorage = storageSettings;
                    existingSettings.FileStore = MapFileStoreType(storageSettings);
                    existingSettings.DisablePlugins = settings.DisablePlugins;
                    existingSettings.SandbagMode = settings.SandbagMode;
                    existingSettings.FakeyMode = settings.FakeyMode;
                    existingSettings.OrganizationTimeZoneId = normalizedTimeZoneId;
                    existingSettings.CompanyManualAdminUserId = settings.CompanyManualAdminUserId;

                    context.Settings.Update(existingSettings);
                }

                // Update the local state before saving to DB
                _disablePlugins = settings.DisablePlugins;
                _sandbagMode = settings.SandbagMode;
                _fakeyMode = settings.FakeyMode;
                _fileStorageSettings = storageSettings;
                FileStorageResolverAccessor.Resolver.Update(_fileStorageSettings);
                await context.SaveChangesAsync();
                SetOrganizationTimeZoneInternal(normalizedTimeZoneId);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[STATE] Error saving system settings: {ex.Message}");
                throw; // Re-throw the exception to handle it in the UI
            }
        }
        public async Task LoadClient(int clientId)
        {
            ClientId = clientId;
            // Notify any components that need to refresh their client data
            if (OnClientUpdated != null)
            {
                await OnClientUpdated.Invoke(clientId);
            }
        }
        private readonly SemaphoreSlim _userSettingsLock = new(1, 1);
        public void SetUserSetting(string key, string value)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            _userSettings[key] = value;
        }
        public string GetUserSetting(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return string.Empty;
            }

            return _userSettings.TryGetValue(key, out var value) ? value : string.Empty;
        }
        public Models.UserPreferences GetUserPreferences()
        {
            if (UserPreferences != null)
                return UserPreferences;

            // Fallback: create from CurrentUser if available
            if (CurrentUser != null)
            {
                UserPreferences = Models.UserPreferences.FromApplicationUser(CurrentUser);
                return UserPreferences;
            }

            // Final fallback: return default preferences
            return new Models.UserPreferences();
        }
        private void ApplyFileStorageOverrides(FileStorageSettings storage)
        {
            if (storage is null)
            {
                return;
            }

            var modeOverride = _configuration["FileStorage:Mode"];
            if (!string.IsNullOrWhiteSpace(modeOverride) &&
                Enum.TryParse<FileStorageMode>(modeOverride, true, out var parsedMode))
            {
                storage.Mode = parsedMode;
            }

            var mappedOverride = _configuration["FileStorage:MappedRoot"];
            if (!string.IsNullOrWhiteSpace(mappedOverride))
            {
                storage.NetworkSharePath = mappedOverride;
            }

            var absoluteOverride = _configuration["FileStorage:ServerRoot"];
            if (!string.IsNullOrWhiteSpace(absoluteOverride))
            {
                storage.ServerAbsoluteRoot = absoluteOverride;
            }

            var baseUrlOverride = _configuration["FileStorage:PublicBaseUrl"];
            if (!string.IsNullOrWhiteSpace(baseUrlOverride))
            {
                storage.PublicBaseUrl = baseUrlOverride;
            }

            var localRootOverride = _configuration["FileStorage:LocalRoot"];
            if (!string.IsNullOrWhiteSpace(localRootOverride))
            {
                storage.LocalRootPath = localRootOverride;
            }

            var preferFileScheme = _configuration["FileStorage:PreferFileLinks"];
            if (!string.IsNullOrWhiteSpace(preferFileScheme) && bool.TryParse(preferFileScheme, out var preferFile))
            {
                storage.PreferFileSchemeLinks = preferFile;
            }

            var stripUploads = _configuration["FileStorage:StripUploadsFromMapped"];
            if (!string.IsNullOrWhiteSpace(stripUploads) && bool.TryParse(stripUploads, out var stripFlag))
            {
                storage.StripUploadsFromMappedPath = stripFlag;
            }
        }
        private static FileStoreType MapFileStoreType(FileStorageSettings storage)
        {
            return storage.Mode switch
            {
                FileStorageMode.LocalDesktop => FileStoreType.Local,
                FileStorageMode.Network => FileStoreType.FileServer,
                FileStorageMode.ExternalSelfHosted => FileStoreType.FileServer,
                _ => FileStoreType.FileServer
            };
        }

        // Timezones --------------------------------------------------------------------------//
        public async Task<string> GetOrganizationTimeZoneIdAsync(CancellationToken cancellationToken = default)
        {
            await EnsureTimeZoneLoadedAsync(cancellationToken);
            return _organizationTimeZoneId;
        }
        private async Task EnsureTimeZoneLoadedAsync(CancellationToken cancellationToken = default)
        {
            if (_timeZoneInitialized)
            {
                return;
            }

            await _timeZoneLock.WaitAsync(cancellationToken);
            try
            {
                if (_timeZoneInitialized)
                {
                    return;
                }

                using var context = _dbContextFactory.CreateDbContext();
                var settings = await context.Settings
                    .OrderBy(s => s.SettingsId)
                    .FirstOrDefaultAsync(cancellationToken);

                var normalized = NormalizeTimeZoneId(settings?.OrganizationTimeZoneId);
                if (settings == null)
                {
                    settings = new Settings
                    {
                        OrganizationTimeZoneId = normalized
                    };
                    context.Settings.Add(settings);
                    await context.SaveChangesAsync(cancellationToken);
                }
                else if (!string.Equals(settings.OrganizationTimeZoneId, normalized, StringComparison.Ordinal))
                {
                    settings.OrganizationTimeZoneId = normalized;
                    context.Settings.Update(settings);
                    await context.SaveChangesAsync(cancellationToken);
                }

                SetOrganizationTimeZoneInternal(normalized);
            }
            finally
            {
                _timeZoneLock.Release();
            }
        }
        private static string NormalizeTimeZoneId(string? timeZoneId)
        {
            if (string.IsNullOrWhiteSpace(timeZoneId))
            {
                return GetDefaultTimeZoneId();
            }

            var trimmed = timeZoneId.Trim();
            return ResolveTimeZone(trimmed).Id;
        }
        private void SetOrganizationTimeZoneInternal(string timeZoneId)
        {
            if (string.Equals(_organizationTimeZoneId, timeZoneId, StringComparison.Ordinal))
            {
                _timeZoneInitialized = true;
                return;
            }

            var resolved = ResolveTimeZone(timeZoneId);
            if (string.Equals(_organizationTimeZoneId, resolved.Id, StringComparison.Ordinal))
            {
                _organizationTimeZone = resolved;
                _timeZoneInitialized = true;
                return;
            }

            _organizationTimeZoneId = resolved.Id;
            _organizationTimeZone = resolved;
            _timeZoneInitialized = true;
            OnOrganizationTimeZoneChanged?.Invoke();
        }
        public static string DefaultOrganizationTimeZoneId => GetDefaultTimeZoneId();
        private static string GetDefaultTimeZoneId()
        {
            return RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? "Pacific Standard Time"
                : "America/Los_Angeles";
        }
        private static TimeZoneInfo ResolveTimeZone(string? timeZoneId)
        {
            Console.WriteLine("[STAT] Resolving Timezone");
            var candidates = new List<string>();
            if (!string.IsNullOrWhiteSpace(timeZoneId))
            {
                var trimmed = timeZoneId.Trim();
                candidates.Add(trimmed);
                candidates.AddRange(GetEquivalentTimeZoneIds(trimmed));
            }

            candidates.Add(GetDefaultTimeZoneId());
            candidates.Add("UTC");
            candidates.Add("Etc/UTC");

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var candidate in candidates)
            {
                if (string.IsNullOrWhiteSpace(candidate) || !seen.Add(candidate))
                {
                    continue;
                }

                try
                {
                    return TimeZoneInfo.FindSystemTimeZoneById(candidate);
                }
                catch (TimeZoneNotFoundException)
                {
                }
                catch (InvalidTimeZoneException)
                {
                }
            }

            return TimeZoneInfo.Utc;
        }
        private static IEnumerable<string> GetEquivalentTimeZoneIds(string timeZoneId)
        {
            if (string.IsNullOrWhiteSpace(timeZoneId))
            {
                yield break;
            }

            foreach (var alias in EnumerateTimeZoneAliases(timeZoneId))
            {
                if (!string.IsNullOrWhiteSpace(alias))
                {
                    yield return alias;
                }
            }
        }
        private static IEnumerable<string> EnumerateTimeZoneAliases(string timeZoneId)
        {
            if (string.IsNullOrWhiteSpace(timeZoneId))
            {
                yield break;
            }

            var trimmed = timeZoneId.Trim();

            foreach (var alias in TryConvertTimeZoneId(trimmed))
            {
                if (!string.IsNullOrWhiteSpace(alias))
                {
                    yield return alias;
                }
            }

            if (string.Equals(trimmed, "Pacific Standard Time", StringComparison.OrdinalIgnoreCase))
            {
                yield return "America/Los_Angeles";
            }
            else if (string.Equals(trimmed, "America/Los_Angeles", StringComparison.OrdinalIgnoreCase))
            {
                yield return "Pacific Standard Time";
            }
        }
        private static IEnumerable<string> TryConvertTimeZoneId(string timeZoneId)
        {
            if (string.IsNullOrWhiteSpace(timeZoneId))
            {
                yield break;
            }

            if (TryConvertAlias(() =>
                    TimeZoneInfo.TryConvertIanaIdToWindowsId(timeZoneId, out var windowsId)
                        ? windowsId
                        : null,
                out var convertedWindowsId))
            {
                yield return convertedWindowsId!;
            }

            if (TryConvertAlias(() =>
                    TimeZoneInfo.TryConvertWindowsIdToIanaId(timeZoneId, out var ianaId)
                        ? ianaId
                        : null,
                out var convertedIanaId))
            {
                yield return convertedIanaId!;
            }
        }
        private static bool TryConvertAlias(Func<string?> converter, out string? result)
        {
            try
            {
                result = converter();
                return !string.IsNullOrWhiteSpace(result);
            }
            catch (TimeZoneNotFoundException)
            {
                result = null;
                return false;
            }
            catch (InvalidTimeZoneException)
            {
                result = null;
                return false;
            }
            catch (PlatformNotSupportedException)
            {
                result = null;
                return false;
            }
            catch (ArgumentException)
            {
                result = null;
                return false;
            }
        }

        //_____________________________________________________________________________________//
        //=====================================================================================//
        // HOMEPAGE DATA CACHE ----------------------------------------------------------------//
        public bool ShowAllTasks => _showAllTasks;
        public bool IsHomepageDataCached => _cachedRenFlowTasks != null;
        public event Action? OnShowAllTasksChanged;
        public event Action? OnHomepageDataUpdated;
        private bool _showAllTasks = false;
        private DateTime _lastHomepageDataFetchTime = DateTime.MinValue;
        private readonly SemaphoreSlim _homepageDataSemaphore = new(1, 1);
        private readonly TimeSpan _homepageDataCacheDuration = TimeSpan.FromMinutes(5);
        private List<HomePageRenFlowTasksViewModel>? _cachedRenFlowTasks;
        private List<HomePageTasksViewModel>? _cachedIncompleteTasks;
        private List<Policy>? _cachedUpcomingRenewals;
        private List<Lead>? _cachedLeads;
        public List<HomePageRenFlowTasksViewModel> CachedRenFlowTasks => _cachedRenFlowTasks ?? new();
        public List<HomePageTasksViewModel> CachedIncompleteTasks => _cachedIncompleteTasks ?? new();
        public List<Policy> CachedUpcomingRenewals => _cachedUpcomingRenewals ?? new();
        public List<Lead> CachedLeads => _cachedLeads ?? new();
        private readonly object _eventLock = new object(); // event debouncing
        private System.Threading.Timer? _homepageDataUpdateTimer;
        private volatile bool _pendingHomepageDataUpdate = false;
        private volatile bool _isDisposed = false;

        // Home Methods -----------------------------------------------------------------------//
        public void SetShowAllTasks(bool value)
        {
            if (_showAllTasks != value)
            {
                _showAllTasks = value;
                OnShowAllTasksChanged?.Invoke();
            }
        }
        private void RaiseHomepageDataUpdated()
        {
            lock (_eventLock)
            {
                if (_pendingHomepageDataUpdate)
                {
                    return;
                }

                _pendingHomepageDataUpdate = true;
                _homepageDataUpdateTimer?.Dispose();
                _homepageDataUpdateTimer = new System.Threading.Timer(FireHomepageDataUpdatedEvent, null, 50, Timeout.Infinite);
            }
        }
        private void FireHomepageDataUpdatedEvent(object? state)
        {
            lock (_eventLock)
            {
                if (_pendingHomepageDataUpdate)
                {
                    _pendingHomepageDataUpdate = false;
                    _homepageDataUpdateTimer?.Dispose();
                    _homepageDataUpdateTimer = null;

                    try
                    {
                        OnHomepageDataUpdated?.Invoke();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[STATE][{DateTime.Now:HH:mm:ss.fff}] StateService: Error firing OnHomepageDataUpdated: {ex.Message}");
                    }
                }
            }
        }
        public async Task<bool> GetHomepageDataAsync(CancellationToken cancellationToken = default)
        {
            var now = DateTime.UtcNow;

            // Always refresh renflowtasks
            await RefreshRenFlowTasksAsync(cancellationToken);

            // Check if we need to refresh the rest of the cache
            if (_lastHomepageDataFetchTime.Add(_homepageDataCacheDuration) < now || !IsHomepageDataCached)
            {
                return await RefreshHomepageDataAsync(cancellationToken);
            }

            return true; // Data is already cached and fresh
        }
        public async Task<bool> RefreshHomepageDataAsync(CancellationToken cancellationToken = default)
        {
            if (CurrentUser == null || _isDisposed) return false;

            await _homepageDataSemaphore.WaitAsync(cancellationToken);
            try
            {
                var homeService = _serviceProvider.GetRequiredService<HomeService>();

                // Load all homepage data in parallel
                var loadingTasks = new List<Task>
                {
                    LoadCachedTaskData(homeService, cancellationToken),
                    LoadCachedRenewalData(homeService, cancellationToken),
                    LoadCachedUserSpecificData(homeService, cancellationToken)
                };

                await Task.WhenAll(loadingTasks);

                _lastHomepageDataFetchTime = DateTime.UtcNow;

                // Notify subscribers that data has been updated
                RaiseHomepageDataUpdated();

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[STATE] Error refreshing homepage data: {ex.Message}");
                return false;
            }
            finally
            {
                if (!_isDisposed)
                {
                    _homepageDataSemaphore.Release();
                }
            }
        }
        private async Task LoadCachedTaskData(HomeService homeService, CancellationToken cancellationToken)
        {
            try
            {
                var renflowtasksTask = homeService.GetHomePageRenFlowTasksAsync(false);
                var incompleteTasksTask = homeService.GetIncompleteTasksForCurrentUserAsync();
                await Task.WhenAll(renflowtasksTask, incompleteTasksTask);

                _cachedRenFlowTasks = await renflowtasksTask;
                _cachedIncompleteTasks = await incompleteTasksTask;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[STATE] Error loading cached task data: {ex.Message}");
                _cachedRenFlowTasks = new();
                _cachedIncompleteTasks = new();
            }
        }
        private async Task LoadCachedRenewalData(HomeService homeService, CancellationToken cancellationToken)
        {
            try
            {
                _cachedUpcomingRenewals = await homeService.GetUpcomingRenewalsAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[STATE] Error loading cached renewal data: {ex.Message}");
                _cachedUpcomingRenewals = new();
            }
        }
        
        private async Task LoadCachedUserSpecificData(HomeService homeService, CancellationToken cancellationToken)
        {
            if (CurrentUser?.UserName != "john@quickfireams.com")
            {
                _cachedLeads = new();
                return;
            }

            try
            {
                _cachedLeads = await homeService.GetAllLeadsAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[STATE] Error loading cached user-specific data: {ex.Message}");
                _cachedLeads = new();
            }
        }
        public void InvalidateHomepageCache()
        {
            _lastHomepageDataFetchTime = DateTime.MinValue;
        }

        //_____________________________________________________________________________________//
        //=====================================================================================//
        // CLEANUP ----------------------------------------------------------------------------//
        public void Dispose()
        {
            if (_isDisposed) return;

            _isDisposed = true;

            // Dispose timer resources
            lock (_eventLock)
            {
                _homepageDataUpdateTimer?.Dispose();
                _homepageDataUpdateTimer = null;
            }

            // Dispose semaphore resources
            _initializationSemaphore?.Dispose();
            _homepageDataSemaphore?.Dispose();

            _timeZoneLock?.Dispose();
        }
    }

}
