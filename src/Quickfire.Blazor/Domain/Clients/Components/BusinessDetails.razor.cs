using Quickfire.Blazor.Domain.Shared.Helpers;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Quickfire.Blazor.Domain.Attachments.Models;
using Quickfire.Blazor.Domain.Attachments.Services;
using Quickfire.Blazor.Domain.Clients.Models;
using Quickfire.Blazor.Domain.Clients.Services;
using Quickfire.Blazor.Domain.Ember;
using Quickfire.Blazor.Domain.Shared.Services;
using Syncfusion.Blazor.Inputs;
using System.Text.Json;


public partial class BusinessDetailsEditorBase : ComponentBase
{
    [Parameter]
    public int ClientId { get; set; }

    [Inject] protected AppJsInterop JsInterop { get; set; } = null!;
    [Inject] protected ClientService ClientService { get; set; } = null!;
    [Inject] protected AttachmentService AttachmentService { get; set; } = null!;
    [Inject] protected EmberService EmberService { get; set; } = null!;

    public BusinessDetails? BusinessDetails { get; set; }
    public EditContext? MyEditContext { get; set; }
    public JsonDocument? CurrentJsonDocument { get; set; }
    public Attachment? SupplementalAttachment { get; set; }
    public IEnumerable<LocalEnumData<LegalEntityType>> LegalEntityTypeList { get; set; } = Enumerable.Empty<LocalEnumData<LegalEntityType>>();
    public IEnumerable<LocalEnumData<BusinessType>> BusinessTypeList { get; set; } = Enumerable.Empty<LocalEnumData<BusinessType>>();
    public IEnumerable<LocalEnumData<LicenseType>> LicenseTypeList { get; set; } = Enumerable.Empty<LocalEnumData<LicenseType>>();
    // Strings
    public string ExtractedTextAttachmentUrl { get; set; } = string.Empty;
    public string ExtractedJsonAttachmentUrl { get; set; } = string.Empty;
    // UI mode: Focus (true) vs Everything (false). Default to Focus per request.
    public bool FocusMode { get; set; } = false;

    
    //                                                                 |
    // MAIN METHODS ---------------------------------------------------|
    //                                                                 |
    protected override async Task OnParametersSetAsync()
    {
        await LoadBusinessDetailsAsync();
        LoadEnumData();
        if (BusinessDetails != null)
        {
            MyEditContext = new EditContext(BusinessDetails);
        }
    }
    private async Task LoadBusinessDetailsAsync()
    {
        BusinessDetails = await ClientService.GetBusinessDetailsByClientId(ClientId);
        if (BusinessDetails == null)
        {
            BusinessDetails = new BusinessDetails { ClientId = ClientId };
            await ClientService.AddBusinessDetailsAsync(BusinessDetails);
            BusinessDetails = await ClientService.GetBusinessDetailsByClientId(ClientId);
        }
    }

    // Form Fields UI                                                --|
    public async Task OnFieldChanged(string fieldName)
    {
        await SaveChanges();
    }
    public async Task SaveChanges()
    {
        if (BusinessDetails != null)
        {
            bool isValid = MyEditContext?.Validate() ?? false;
            await ClientService.UpdateBusinessDetailsAsync(BusinessDetails);
            StateHasChanged();
        }
    }
    public async Task CopyItem(string? textToCopy)
    {
        if (!string.IsNullOrEmpty(textToCopy))
        {
            await JsInterop.CopyToClipboardAsync(textToCopy);
            // Optionally, add a notification "Copied!"
        }
    }
    
    // -----------------------------------------------------------------
    // Focus/Everything helpers used by the Razor markup for visibility
    // -----------------------------------------------------------------
    public void OnModeSwitchChanged(bool value)
    {
        FocusMode = value; // true = Focus, false = Everything
        StateHasChanged();
    }

    public bool ShouldShowField(object? value)
        => !FocusMode || HasValue(value);

    public bool ShouldShowSection(params object?[] values)
        => !FocusMode || values.Any(v => HasValue(v));

    private static bool HasValue(object? value)
    {
        if (value is null) return false;
        switch (value)
        {
            case string s:
                return !string.IsNullOrWhiteSpace(s);
            case int i:
                return true; // any int value is considered a value
            case decimal d:
                return true;
            case double dbl:
                return true;
            case DateTime dt:
                return true;
            case bool b:
                return true; // false is still a meaningful value
            default:
                // Nullable enums, etc.
                var type = value.GetType();
                if (Nullable.GetUnderlyingType(type)?.IsEnum == true)
                {
                    return true; // has a value if not null
                }
                if (type.IsEnum)
                {
                    return true;
                }
                return true; // treat other non-null objects as having value
        }
    }


    //                                                                 |
    // MISC STUFF -----------------------------------------------------|
    //                                                                 |
    private void LoadEnumData()
    {
        LegalEntityTypeList = Enum.GetValues(typeof(LegalEntityType)).Cast<LegalEntityType>().Select(e => new LocalEnumData<LegalEntityType> { Value = e, Text = e.ToString().Replace("_", " ") });
        BusinessTypeList = Enum.GetValues(typeof(BusinessType)).Cast<BusinessType>().Select(e => new LocalEnumData<BusinessType> { Value = e, Text = e.ToString().Replace("_", " ") });
        LicenseTypeList = Enum.GetValues(typeof(LicenseType)).Cast<LicenseType>().Select(e => new LocalEnumData<LicenseType> { Value = e, Text = e.ToString().Replace("_", " ") });
    }
    public class LocalEnumData<T> where T : struct, Enum
    {
        public T Value { get; set; }
        public string Text { get; set; } = string.Empty;
    }
    public void Dispose()
    {
        CurrentJsonDocument?.Dispose();
    }
}
