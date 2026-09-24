using Microsoft.EntityFrameworkCore;
using Quickfire.Blazor.Data;
using Quickfire.Blazor.Domain.Clients.Models;
using Quickfire.Blazor.Domain.Home.Models;
using Quickfire.Blazor.Domain.Policies.Models;
using Quickfire.Blazor.Domain.Renewals.Models;
using Quickfire.Blazor.Domain.Renewals.ViewModels;

namespace Quickfire.Blazor.Domain.Shared.Services
{
    public class HomeService
    {
        private readonly StateService _stateService;
        private readonly IDbContextFactory<ApplicationDbContext> _dbContextFactory;

        public HomeService(StateService stateService, IDbContextFactory<ApplicationDbContext> dbContextFactory)
        {
            _stateService = stateService;
            _dbContextFactory = dbContextFactory;
        }

        // - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - -//
        // GET trackTasks [CurrentUser]
        public async Task<List<HomePageTasksViewModel>> GetHomePageTasksAsync()
        {
            var today = DateTime.Today;
            var currentUser = _stateService.CurrentUser;

            using var context = _dbContextFactory.CreateDbContext();
            // Fetch all TrackTasks from the database for the current user
            var tasks = await context.TrackTasks
                .AsNoTracking()
                .AsSplitQuery()
                .Where(t => t.Completed == false)
                .Where(t => t.GoalDate != null || t.Highlighted == true)
                .Where(t => t.AssignedTo == null && t.Renewal.AssignedTo == currentUser)
                .Select(t => new
                {
                    t.Renewal.RenewalId,
                    t.TaskName,
                    t.Notes,
                    t.Highlighted,
                    t.GoalDate,
                    ClientName = t.Renewal.Client.Name,
                    t.Renewal.Client.ClientId,
                    PolicyProduct = t.Renewal.Policy.Product.LineCode,
                    t.Renewal.RenewalDate
                })
                .ToListAsync();

            // Separate highlighted tasks
            var highlightedTasks = tasks
                .Where(t => t.Highlighted)
                .ToList();

            // For non-highlighted tasks, select the nearest task per renewal
            var nearestTasks = tasks
                .Where(t => !t.Highlighted)
                .GroupBy(t => t.RenewalId)
                .Select(g => g.OrderBy(t => t.GoalDate ?? DateTime.MaxValue).FirstOrDefault())
                .ToList();

            // Combine highlighted tasks and nearest non-highlighted tasks
            var combinedTasks = highlightedTasks
                .Union(nearestTasks)
                .ToList();

            // Create the ViewModel list
            var result = combinedTasks.Select(t => new HomePageTasksViewModel
            {
                RenewalId = t.RenewalId,
                TaskName = t.TaskName,
                TaskNote = t.Notes,
                Highlighted = t.Highlighted,
                GoalDate = t.GoalDate,
                ClientName = t.ClientName,
                ClientId = t.ClientId,
                PolicyProduct = t.PolicyProduct ?? "10",
                RenewalDate = t.RenewalDate,
                Priority = t.GoalDate.HasValue
            ? (t.GoalDate < today
                ? $"<span style='color:red'>{(today - t.GoalDate.Value).Days} Days Late</span>"
                : $"{(t.GoalDate.Value - today).Days} Days Left")
            : "ASAP"
            }).OrderByDescending(t => t.Highlighted)
              .ThenByDescending(t => t.GoalDate.HasValue && t.GoalDate < today)
              .ThenBy(t => t.GoalDate)
              .Take(20)
              .ToList();

            return result;
        }
        // GET trackTasks [CurrentUser IS subassigned]
        public async Task<List<HomePageTasksViewModel>> GetHomePageSubTasksAsync()
        {
            var today = DateTime.Today;

            var currentUser = _stateService.CurrentUser;
            if (currentUser == null)
            {
                throw new InvalidOperationException("User initialization failed or user is not authenticated.");
            }

            using var context = _dbContextFactory.CreateDbContext();
            // Fetch all TrackTasks for the current user that are incomplete
            var tasks = await context.TrackTasks
                .Include(t => t.Renewal)
                    .ThenInclude(r => r.Policy)
                        .ThenInclude(p => p.Product)
                .Include(t => t.Renewal)
                    .ThenInclude(r => r.Client)
                .Where(t => t.Completed == false) // Only incomplete tasks
                .Where(t => t.AssignedTo == currentUser) // Assigned to the current user
                .AsNoTracking()
                .Select(t => new
                {
                    t.Renewal.RenewalId,
                    t.TaskName,
                    t.Notes,
                    t.Highlighted,
                    t.GoalDate,
                    ClientName = t.Renewal.Client.Name,
                    t.Renewal.Client.ClientId,
                    PolicyProduct = t.Renewal.Policy.Product.LineCode,
                    PolicyNumber = t.Renewal.Policy.PolicyNumber,
                    t.Renewal.RenewalDate
                })
                .ToListAsync();

            // Separate highlighted tasks (to appear at the top)
            var highlightedTasks = tasks
                .Where(t => t.Highlighted)
                .ToList();

            // For non-highlighted tasks, order by nearest GoalDate or RenewalDate
            var nonHighlightedTasks = tasks
                .Where(t => !t.Highlighted)
                .OrderBy(t => t.GoalDate ?? t.RenewalDate)
                .ToList();

            // Combine highlighted tasks with non-highlighted ones
            var combinedTasks = highlightedTasks
                .Concat(nonHighlightedTasks)
                .Take(40) // Take only 40 tasks
                .ToList();

            // Create the ViewModel list
            var result = combinedTasks.Select(t => new HomePageTasksViewModel
            {
                RenewalId = t.RenewalId,
                TaskName = t.TaskName,
                TaskNote = t.Notes,
                Highlighted = t.Highlighted,
                GoalDate = t.GoalDate,
                ClientName = t.ClientName,
                ClientId = t.ClientId,
                PolicyProduct = t.PolicyProduct ?? "10", // Default if null
                RenewalDate = t.RenewalDate,
                PolicyNumber = t.PolicyNumber,
                Priority = t.GoalDate.HasValue
                    ? (t.GoalDate < today
                        ? $"<span style='color:red'>{(today - t.GoalDate.Value).Days} Days Late</span>"
                        : $"{(t.GoalDate.Value - today).Days} Days Left")
                    : "ASAP"
            }).ToList();

            // Order by Highlighted tasks first, then the rest by GoalDate or RenewalDate
            return result;
        }
        // GET trackTasks [CurrentUser AND incomplete]
        public async Task<List<HomePageTasksViewModel>> GetIncompleteTasksForCurrentUserAsync()
        {
            var today = DateTime.Today;
            var currentUser = _stateService.CurrentUser;

            using var context = _dbContextFactory.CreateDbContext();
            // Fetch all incomplete tasks assigned to the current user where RenewalDate is before today
            var tasks = await context.TrackTasks
                .Include(t => t.Renewal)
                    .ThenInclude(r => r.Policy)
                        .ThenInclude(s => s.Product)
                .Include(t => t.Renewal)
                    .ThenInclude(r => r.Client)
                .Where(t => t.Completed == false) // Only incomplete tasks
                .Where(t => t.AssignedTo == currentUser || t.Renewal.AssignedTo == currentUser) // Assigned to current user
                .Where(t => t.Renewal.RenewalDate < today) // Only tasks with past RenewalDate (before today)
                .OrderBy(t => t.Renewal.RenewalDate) // Order by RenewalDate with oldest at the top
                .Take(20) // Limit to 20 tasks
                .AsNoTracking()
                .Select(t => new
                {
                    t.Renewal.RenewalId,
                    t.TaskName,
                    t.Notes,
                    t.Highlighted,
                    t.GoalDate,
                    ClientName = t.Renewal.Client.Name ?? "NA",
                    t.Renewal.Client.ClientId,
                    PolicyProduct = t.Renewal.Policy != null && t.Renewal.Policy.Product != null ? t.Renewal.Policy.Product.LineCode : "Unknown",
                    t.Renewal.RenewalDate
                })
                .ToListAsync();

            // Create the ViewModel list
            var result = tasks.Select(t => new HomePageTasksViewModel
            {
                RenewalId = t.RenewalId,
                TaskName = t.TaskName,
                Highlighted = t.Highlighted,
                GoalDate = t.GoalDate,
                ClientName = t.ClientName,
                ClientId = t.ClientId,
                PolicyProduct = t.PolicyProduct,
                RenewalDate = t.RenewalDate,
                Priority = $"Renewal Date: {t.RenewalDate.ToShortDateString()}"
            }).ToList();

            return result;
        }
        // GET dailyTasks [CurrentUser]
        public async Task<List<DailyTask>> GetDailyTasksAsync()
        {
            var currentUser = _stateService.CurrentUser;
            if (currentUser is null) return [];
            var today = DateTime.Today;

            using var context = _dbContextFactory.CreateDbContext();

            var tasks = await context.DailyTasks
                .Where(task => !task.Completed)
                .Where(t => t.AssignedToId == currentUser.Id)
                .OrderBy(t => t.Order)
                .ToListAsync();

            return tasks;
        }
        // GET dailyTasks [CurrentUser AND completed]
        public async Task<List<DailyTask>> GetDailyCompletedTasksAsync()
        {
            var currentUser = _stateService.CurrentUser;
            if (currentUser is null) return [];
            var today = DateTime.Today;

            using var context = _dbContextFactory.CreateDbContext();

            var tasks = await context.DailyTasks
                .Where(task => task.Completed && task.CompletedDate.HasValue && task.CompletedDate.Value.Date == today)
                .Where(t => t.AssignedToId == currentUser.Id)
                .ToListAsync();

            return tasks;
        }
        // GET renewal policies [CurrentUser AND expiringSoon]
        public async Task<List<Policy>> GetUpcomingRenewalsAsync()
        {
            var currentDate = DateTime.UtcNow;
            var upcomingDate = currentDate.AddDays(30);

            using var context = _dbContextFactory.CreateDbContext();
            var upcomingRenewals = await context.Policies
                .Include(p => p.Product)
                .Include(p => p.Client)
                .Where(p => p.ExpirationDate >= currentDate && p.ExpirationDate <= upcomingDate)
                .OrderBy(p => p.ExpirationDate) // Order by closest expiration date first
                .AsNoTracking()
                .ToListAsync();

            return upcomingRenewals;
        }
        // GET LEADS [All]
        public async Task<List<Lead>> GetAllLeadsAsync()
        {
            using var context = _dbContextFactory.CreateDbContext();
            var theleads = await context.Leads
                .Include(l => l.CreatedBy)
                .Include(l => l.Submissions)
                .Include(l => l.LeadNotes)
                .Include(l => l.Product)
                .Where(l => l.Stage < 3)
                .OrderBy(x => x.Stage).ToListAsync();
            return theleads;
        }
        // - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - - -//

        // MAIN LAYOUT ---------------------------------------------------------------- //
        public async Task<CallInfoMatchResult> GetCallerInfo(CallInfo callInfo)
        {
            using var context = _dbContextFactory.CreateDbContext();

            // Clean the caller ID phone number (remove non-numeric characters)
            string cleanedCallerId = CleanPhoneNumber(callInfo.CallerId);

            if(cleanedCallerId != "")
            {
                // First search in the new PhoneNumber model for contacts
                var contactsFromPhoneNumbers = await context.PhoneNumbers
                    .Where(p => p.Number.Contains(cleanedCallerId))
                    .Include(p => p.Contact)
                        .ThenInclude(c => c.Client)
                    .Select(p => p.Contact)
                    .ToListAsync();
                
                if (contactsFromPhoneNumbers.Any())
                {
                    return new CallInfoMatchResult { MatchedContact = contactsFromPhoneNumbers.First() };
                }
                
                // Fallback to the old search methods
                
                // Search for matching clients using the Contains method
                var matchingClients = await context.Clients
                    .Where(c => c.PhoneNumber != null && c.PhoneNumber.Contains(cleanedCallerId))
                    .ToListAsync();

                // Search for matching contacts using the Contains method
                var matchingContacts = await context.Contacts
                    .Include(c => c.PhoneNumbers)
                    .Include(c => c.Client)
                    .Where(c => c.PhoneNumbers.Any(p => p.Number != null && p.Number.Contains(cleanedCallerId)))
                    .ToListAsync();
                    
                if (matchingContacts.Any())
                {
                    // Return the first matching contact
                    return new CallInfoMatchResult { MatchedContact = matchingContacts.First() };
                }
                if (matchingClients.Any())
                {
                    // Return the first matching client
                    return new CallInfoMatchResult { MatchedClient = matchingClients.First() };
                }
            }
            return null;
        }
        private string CleanPhoneNumber(string phoneNumber)
        {
            if (string.IsNullOrEmpty(phoneNumber))
                return string.Empty;

            return new string(phoneNumber.Where(char.IsDigit).ToArray());
        }
        public async Task<List<HomePageRenFlowTasksViewModel>> GetHomePageRenFlowTasksAsync(bool showAllTasks)
        {
            var today = DateTime.Today;
            var currentUser = _stateService.CurrentUser;
            var take = showAllTasks ? 35 : 20;

            using var context = _dbContextFactory.CreateDbContext();

            // Load all tasks that are either:
            // - directly assigned to the user
            // - OR subtasks with no AssignedToId but their parent is assigned to the user
            var tasks = await context.TrackTasks
                .Include(t => t.Renewal)
                    .ThenInclude(r => r.Policy)
                        .ThenInclude(p => p.Product)
                .Include(t => t.Renewal)
                    .ThenInclude(r => r.Client)
                .Include(t => t.ParentTask)
                .AsNoTracking()
                .AsSplitQuery()
                .Where(t => t.Completed == false)
                .Where(t => t.GoalDate != null || t.Highlighted == true)
                .Where(t =>
                    t.AssignedToId == currentUser.Id ||
                    (t.AssignedToId == null && t.ParentTask != null && t.ParentTask.AssignedToId == currentUser.Id)
                )
                .ToListAsync();

            List<TrackTask> finalTasks;

            if (showAllTasks)
            {
                // No filtering, return everything that matched above
                finalTasks = tasks;
            }
            else
            {
                // Highlighted first
                var highlightedTasks = tasks
                    .Where(t => t.Highlighted)
                    .ToList();

                // Then nearest per renewal for non-highlighted
                var nearestTasks = tasks
                    .Where(t => !t.Highlighted)
                    .GroupBy(t => t.Renewal.RenewalId)
                    .Select(g => g.OrderBy(t => t.GoalDate ?? DateTime.MaxValue).FirstOrDefault())
                    .ToList();

                finalTasks = highlightedTasks
                    .Union(nearestTasks)
                    .ToList();
            }

            var result = finalTasks.Select(t => new HomePageRenFlowTasksViewModel
            {
                TrackTaskId = t.Id,
                DailyCheckOff = t.DailyCheckOff,
                RenewalId = t.Renewal.RenewalId,
                TaskName = t.TaskName,
                ParentTaskName = t.ParentTask?.TaskName,
                TaskNote = t.Notes,
                Highlighted = t.Highlighted,
                GoalDate = t.GoalDate,
                ClientName = t.Renewal.Client.Name,
                ClientId = t.Renewal.Client.ClientId,
                PolicyProduct = t.Renewal.Policy?.Product?.LineCode ?? "10",
                PolicyNumber = t.Renewal.Policy?.PolicyNumber,
                RenewalDate = t.Renewal.RenewalDate,
                Priority = t.GoalDate.HasValue
                    ? (t.GoalDate.Value - today).Days.ToString()
                    : "ASAP"
            })
            .OrderByDescending(t => t.Highlighted)
            .ThenByDescending(t => t.GoalDate.HasValue && t.GoalDate < today)
            .ThenBy(t => t.GoalDate)
            .Take(take)
            .ToList();

            return result;
        }
    }
}
