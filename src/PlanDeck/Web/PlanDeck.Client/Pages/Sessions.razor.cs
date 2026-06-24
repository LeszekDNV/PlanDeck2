using Grpc.Core;
using Microsoft.JSInterop;
using MudBlazor;
using System.Globalization;
using PlanDeck.Client.Components;
using PlanDeck.Core.Shared.Contracts;
using PlanDeck.Core.Shared.Validation;

namespace PlanDeck.Client.Pages;

public partial class Sessions
{
    private bool _loading = true;
    private bool _createOpen;
    private bool _createSubmitting;
    private bool _savingConfig;
    private bool _addingTask;
    private bool _activating;

    private List<SessionDto> _sessions = [];
    private List<TeamDto> _teams = [];
    private SessionDto? _selected;

    private string _newName = string.Empty;
    private Guid? _newTeamId;
    private VotingScaleTypeDto _newScaleType = VotingScaleTypeDto.Fibonacci;
    private string _newCustomValues = string.Empty;
    private string _adHocTitle = string.Empty;
    private string _adHocDescription = string.Empty;
    private string _bulkText = string.Empty;
    private bool _bulkExpanded;
    private readonly List<NewSessionTaskDto> _stagedTasks = [];

    private string _configName = string.Empty;
    private Guid? _configTeamId;
    private List<TeamMemberDto> _teamMembers = [];
    private VotingScaleTypeDto _configScaleType = VotingScaleTypeDto.Fibonacci;
    private string _configCustomValues = string.Empty;
    private string _configTaskTitle = string.Empty;
    private string _configTaskDescription = string.Empty;
    private string _configBulkText = string.Empty;
    private bool _configBulkExpanded;

    private bool _editOpen;
    private bool _savingEdit;
    private Guid _editTaskId;
    private string _editTitle = string.Empty;
    private string _editDescription = string.Empty;
    private bool _editIsAdo;
    private readonly HashSet<Guid> _expandedTaskIds = [];
    private readonly HashSet<Guid> _writingEstimateTaskIds = [];

    private List<SessionMemberDto> _members = [];
    private string _memberEmail = string.Empty;
    private string _memberDisplayName = string.Empty;
    private bool _assigningMember;

    private bool _isLocked => _selected?.Status == SessionStatusDto.Active;

    private IReadOnlyCollection<int> StagedAdoIds =>
        _stagedTasks.Where(t => t.AdoWorkItemId.HasValue).Select(t => t.AdoWorkItemId!.Value).ToArray();

    private IReadOnlyCollection<int> SelectedSessionAdoIds =>
        _selected?.Tasks.Where(t => t.AdoWorkItemId.HasValue).Select(t => t.AdoWorkItemId!.Value).ToArray() ?? [];

    protected override async Task OnInitializedAsync()
    {
        var state = await AuthState.GetAuthenticationStateAsync();
        if (state.User.Identity?.IsAuthenticated == true)
        {
            await LoadSessionsAsync();
            await LoadTeamsAsync();
        }

        _loading = false;
    }

    private async Task LoadSessionsAsync()
    {
        try
        {
            _sessions = (await SessionService.GetSessionsAsync()).ToList();
        }
        catch (RpcException ex)
        {
            ShowError(ex);
        }
    }

    private async Task LoadTeamsAsync()
    {
        try
        {
            _teams = (await TeamService.GetTeamsAsync()).ToList();
        }
        catch (RpcException ex)
        {
            ShowError(ex);
        }
    }

    private void OpenCreate()
    {
        _newName = string.Empty;
        _newTeamId = null;
        _newScaleType = VotingScaleTypeDto.Fibonacci;
        _newCustomValues = string.Empty;
        _adHocTitle = string.Empty;
        _adHocDescription = string.Empty;
        _bulkText = string.Empty;
        _bulkExpanded = false;
        _stagedTasks.Clear();
        _createOpen = true;
    }

    private void StageAdHocTask()
    {
        var title = _adHocTitle?.Trim() ?? string.Empty;
        if (title.Length == 0)
        {
            Snackbar.Add(L["Sessions_TaskTitleRequired"], Severity.Error);
            return;
        }

        _stagedTasks.Add(new NewSessionTaskDto
        {
            Title = title,
            Description = string.IsNullOrWhiteSpace(_adHocDescription) ? null : _adHocDescription.Trim(),
            Source = TaskSourceDto.AdHoc
        });
        _adHocTitle = string.Empty;
        _adHocDescription = string.Empty;
    }

    private void StageBulkTasks()
    {
        var parsed = ParseBulkTasks(_bulkText);
        if (parsed.Count == 0)
        {
            Snackbar.Add(L["Sessions_BulkEmpty"], Severity.Error);
            return;
        }

        foreach (var task in parsed)
        {
            if (_stagedTasks.Any(t => string.Equals(t.Title, task.Title, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            _stagedTasks.Add(task);
        }

        _bulkText = string.Empty;
    }

    private async Task OpenAdoImportForCreate()
    {
        var parameters = new DialogParameters<AdoImportDialog>
        {
            { x => x.AlreadyPresentIds, StagedAdoIds }
        };

        var dialog = await Dialog.ShowAsync<AdoImportDialog>(L["Sessions_ImportAdo"], parameters);
        var result = await dialog.Result;
        if (result is { Canceled: false, Data: IReadOnlyList<AzureDevOpsWorkItemDto> items })
        {
            StageAdoItems(items);
        }
    }

    private void StageAdoItems(IReadOnlyList<AzureDevOpsWorkItemDto> items)
    {
        foreach (var item in items)
        {
            if (_stagedTasks.Any(t => t.AdoWorkItemId == item.Id))
            {
                continue;
            }

            _stagedTasks.Add(new NewSessionTaskDto
            {
                Title = item.Title,
                Description = item.Description,
                Source = TaskSourceDto.AzureDevOps,
                AdoWorkItemId = item.Id,
                AdoRevision = item.Revision,
                WorkItemType = item.WorkItemType,
                State = item.State
            });
        }
    }

    private async Task SubmitCreateAsync()
    {
        if (string.IsNullOrWhiteSpace(_newName))
        {
            Snackbar.Add(L["Sessions_NameRequired"], Severity.Error);
            return;
        }

        _createSubmitting = true;
        try
        {
            var session = await SessionService.CreateSessionAsync(
                _newName.Trim(),
                _newTeamId,
                _newScaleType,
                ParseCustomValues(_newCustomValues),
                _stagedTasks);

            _sessions.Insert(0, session);
            _createOpen = false;
            await SelectAsync(session);
        }
        catch (RpcException ex)
        {
            ShowError(ex);
        }
        finally
        {
            _createSubmitting = false;
        }
    }

    private async Task SelectAsync(SessionDto session)
    {
        try
        {
            _selected = await SessionService.GetSessionAsync(session.Id);
            _configName = _selected.Name;
            _configTeamId = _selected.TeamId;
            _configScaleType = _selected.ScaleType;
            _configCustomValues = string.Join(", ", _selected.ScaleValues);
            _configTaskTitle = string.Empty;
            _configTaskDescription = string.Empty;
            _configBulkText = string.Empty;
            _configBulkExpanded = false;
            _expandedTaskIds.Clear();
            await LoadMembersAsync();
            await LoadTeamMembersAsync();
        }
        catch (RpcException ex)
        {
            ShowError(ex);
        }
    }

    private async Task OnConfigTeamChangedAsync()
    {
        await LoadTeamMembersAsync();
    }

    private async Task LoadTeamMembersAsync()
    {
        if (_configTeamId is null)
        {
            _teamMembers = [];
            return;
        }

        try
        {
            _teamMembers = (await TeamService.GetMembersAsync(_configTeamId.Value)).ToList();
        }
        catch (RpcException ex)
        {
            ShowError(ex);
        }
    }

    private async Task LoadMembersAsync()
    {
        if (_selected is null)
        {
            return;
        }

        _memberEmail = string.Empty;
        _memberDisplayName = string.Empty;
        _members = (await MemberService.GetMembersAsync(_selected.Id)).ToList();
    }

    private async Task AssignMemberAsync()
    {
        if (_selected is null)
        {
            return;
        }

        var email = _memberEmail?.Trim() ?? string.Empty;
        if (!EmailValidator.IsValid(email))
        {
            Snackbar.Add(L["Sessions_MemberInvalidEmail"], Severity.Error);
            return;
        }

        _assigningMember = true;
        try
        {
            var displayName = string.IsNullOrWhiteSpace(_memberDisplayName) ? null : _memberDisplayName.Trim();
            var member = await MemberService.AssignMemberAsync(_selected.Id, email, displayName);
            _members.Add(member);
            _memberEmail = string.Empty;
            _memberDisplayName = string.Empty;
        }
        catch (RpcException ex)
        {
            ShowError(ex);
        }
        finally
        {
            _assigningMember = false;
        }
    }

    private async Task RemoveMemberAsync(SessionMemberDto member)
    {
        if (_selected is null)
        {
            return;
        }

        var confirmed = await Dialog.ShowMessageBoxAsync(
            L["Sessions_RemoveMemberConfirmTitle"],
            string.Format(L["Sessions_RemoveMemberConfirmText"], member.Email),
            yesText: L["Sessions_RemoveMember"],
            cancelText: L["Sessions_Cancel"]);

        if (confirmed != true)
        {
            return;
        }

        try
        {
            var removed = await MemberService.RemoveMemberAsync(_selected.Id, member.Id);
            if (removed)
            {
                _members.Remove(member);
            }
        }
        catch (RpcException ex)
        {
            ShowError(ex);
        }
    }

    private async Task SaveConfigAsync()
    {
        if (_selected is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(_configName))
        {
            Snackbar.Add(L["Sessions_NameRequired"], Severity.Error);
            return;
        }

        _savingConfig = true;
        try
        {
            var updated = await SessionService.UpdateSessionConfigAsync(
                _selected.Id,
                _configName.Trim(),
                _configTeamId,
                _configScaleType,
                ParseCustomValues(_configCustomValues));

            ReplaceSelected(updated);
        }
        catch (RpcException ex)
        {
            ShowError(ex);
        }
        finally
        {
            _savingConfig = false;
        }
    }

    private async Task AddTaskAsync()
    {
        if (_selected is null)
        {
            return;
        }

        var title = _configTaskTitle?.Trim() ?? string.Empty;
        if (title.Length == 0)
        {
            Snackbar.Add(L["Sessions_TaskTitleRequired"], Severity.Error);
            return;
        }

        _addingTask = true;
        try
        {
            var updated = await SessionService.AddTaskAsync(
                _selected.Id,
                new NewSessionTaskDto
                {
                    Title = title,
                    Description = string.IsNullOrWhiteSpace(_configTaskDescription) ? null : _configTaskDescription.Trim(),
                    Source = TaskSourceDto.AdHoc
                });

            ReplaceSelected(updated);
            _configTaskTitle = string.Empty;
            _configTaskDescription = string.Empty;
        }
        catch (RpcException ex)
        {
            ShowError(ex);
        }
        finally
        {
            _addingTask = false;
        }
    }

    private async Task AddBulkTasksAsync()
    {
        if (_selected is null)
        {
            return;
        }

        var parsed = ParseBulkTasks(_configBulkText);
        if (parsed.Count == 0)
        {
            Snackbar.Add(L["Sessions_BulkEmpty"], Severity.Error);
            return;
        }

        _addingTask = true;
        try
        {
            var updated = await SessionService.AddTasksAsync(_selected.Id, parsed);
            ReplaceSelected(updated);
            _configBulkText = string.Empty;
        }
        catch (RpcException ex)
        {
            ShowError(ex);
        }
        finally
        {
            _addingTask = false;
        }
    }

    private void ToggleDescription(Guid taskId)
    {
        if (!_expandedTaskIds.Add(taskId))
        {
            _expandedTaskIds.Remove(taskId);
        }
    }

    private void OpenEditTask(SessionTaskDto task)
    {
        _editTaskId = task.Id;
        _editTitle = task.Title;
        _editDescription = task.Description ?? string.Empty;
        _editIsAdo = task.Source == TaskSourceDto.AzureDevOps;
        _editOpen = true;
    }

    private async Task SaveEditTaskAsync()
    {
        if (_selected is null)
        {
            return;
        }

        var title = _editTitle?.Trim() ?? string.Empty;
        if (title.Length == 0)
        {
            Snackbar.Add(L["Sessions_TaskTitleRequired"], Severity.Error);
            return;
        }

        _savingEdit = true;
        try
        {
            var description = string.IsNullOrWhiteSpace(_editDescription) ? null : _editDescription;
            var updated = await SessionService.UpdateTaskAsync(_selected.Id, _editTaskId, title, description);
            ReplaceSelected(updated);
            _editOpen = false;
        }
        catch (RpcException ex)
        {
            ShowError(ex);
        }
        finally
        {
            _savingEdit = false;
        }
    }

    private async Task RemoveTaskAsync(SessionTaskDto task)
    {
        if (_selected is null)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(task.AgreedEstimate))
        {
            var confirmed = await Dialog.ShowMessageBoxAsync(
                L["Sessions_RemoveTaskConfirmTitle"],
                string.Format(L["Sessions_RemoveTaskConfirmText"], task.Title, task.AgreedEstimate),
                yesText: L["Sessions_RemoveTask"],
                cancelText: L["Sessions_Cancel"]);

            if (confirmed != true)
            {
                return;
            }
        }

        try
        {
            var updated = await SessionService.RemoveTaskAsync(_selected.Id, task.Id);
            ReplaceSelected(updated);
        }
        catch (RpcException ex)
        {
            ShowError(ex);
        }
    }

    private bool CanWriteEstimate(SessionTaskDto task) =>
        task.Source == TaskSourceDto.AzureDevOps
        && task.AdoWorkItemId is not null
        && !string.IsNullOrWhiteSpace(task.AgreedEstimate)
        && double.TryParse(task.AgreedEstimate, NumberStyles.Any, CultureInfo.InvariantCulture, out _);

    private bool IsWritingEstimate(Guid taskId) => _writingEstimateTaskIds.Contains(taskId);

    private async Task WriteEstimateToAdoAsync(SessionTaskDto task)
    {
        if (_selected is null)
        {
            return;
        }

        _writingEstimateTaskIds.Add(task.Id);
        try
        {
            var reply = await SessionService.WriteTaskEstimateToAdoAsync(_selected.Id, task.Id);
            ReplaceSelected(reply.Session);
            Snackbar.Add(L["Sessions_WriteEstimateSuccess"], Severity.Success);
        }
        catch (RpcException ex)
        {
            var message = ex.StatusCode switch
            {
                StatusCode.Aborted => L["Sessions_WriteEstimateConflict"],
                StatusCode.ResourceExhausted => L["Sessions_WriteEstimateRateLimited"],
                _ => L["Sessions_WriteEstimateFailed"]
            };
            Snackbar.Add(message, Severity.Error);
        }
        finally
        {
            _writingEstimateTaskIds.Remove(task.Id);
        }
    }

    private async Task OpenAdoImportForConfig()
    {
        if (_selected is null)
        {
            return;
        }

        var parameters = new DialogParameters<AdoImportDialog>
        {
            { x => x.AlreadyPresentIds, SelectedSessionAdoIds }
        };

        var dialog = await Dialog.ShowAsync<AdoImportDialog>(L["Sessions_ImportAdo"], parameters);
        var result = await dialog.Result;
        if (result is { Canceled: false, Data: IReadOnlyList<AzureDevOpsWorkItemDto> items })
        {
            await AddAdoItemsAsync(items);
        }
    }

    private async Task AddAdoItemsAsync(IReadOnlyList<AzureDevOpsWorkItemDto> items)
    {
        if (_selected is null)
        {
            return;
        }

        var newTasks = items
            .Where(item => _selected.Tasks.All(t => t.AdoWorkItemId != item.Id))
            .Select(item => new NewSessionTaskDto
            {
                Title = item.Title,
                Description = item.Description,
                Source = TaskSourceDto.AzureDevOps,
                AdoWorkItemId = item.Id,
                AdoRevision = item.Revision,
                WorkItemType = item.WorkItemType,
                State = item.State
            })
            .ToList();

        if (newTasks.Count == 0)
        {
            return;
        }

        _addingTask = true;
        try
        {
            var updated = await SessionService.AddTasksAsync(_selected.Id, newTasks);
            ReplaceSelected(updated);
        }
        catch (RpcException ex)
        {
            ShowError(ex);
        }
        finally
        {
            _addingTask = false;
        }
    }

    private async Task ActivateAsync()
    {
        if (_selected is null)
        {
            return;
        }

        _activating = true;
        try
        {
            var updated = await SessionService.ActivateSessionAsync(_selected.Id);
            ReplaceSelected(updated);
            Snackbar.Add(L["Sessions_Activated"], Severity.Success);
        }
        catch (RpcException ex)
        {
            ShowError(ex);
        }
        finally
        {
            _activating = false;
        }
    }

    private void ReplaceSelected(SessionDto updated)
    {
        _selected = updated;
        var index = _sessions.FindIndex(s => s.Id == updated.Id);
        if (index >= 0)
        {
            _sessions[index] = updated;
        }
    }

    private static List<string> ParseCustomValues(string raw) =>
        (raw ?? string.Empty)
            .Split([',', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

    private static List<NewSessionTaskDto> ParseBulkTasks(string raw)
    {
        var result = new List<NewSessionTaskDto>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in (raw ?? string.Empty).Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = line.IndexOf('|');
            var title = (separator >= 0 ? line[..separator] : line).Trim();
            if (title.Length == 0 || !seen.Add(title))
            {
                continue;
            }

            var description = separator >= 0 ? line[(separator + 1)..].Trim() : string.Empty;
            result.Add(new NewSessionTaskDto
            {
                Title = title,
                Description = description.Length == 0 ? null : description,
                Source = TaskSourceDto.AdHoc
            });
        }

        return result;
    }

    private string StatusLabel(SessionStatusDto status) =>
        status == SessionStatusDto.Active ? L["Sessions_Active"] : L["Sessions_Draft"];

    private string ShareLink(string code) => $"{Navigation.BaseUri}join/{code}";

    private async Task CopyShareLinkAsync()
    {
        if (_selected?.ShareCode is not { Length: > 0 } code)
        {
            return;
        }

        try
        {
            await JS.InvokeVoidAsync("navigator.clipboard.writeText", ShareLink(code));
            Snackbar.Add(L["Sessions_ShareCopied"], Severity.Success);
        }
        catch (JSException)
        {
            Snackbar.Add(L["Error_Generic"], Severity.Error);
        }
    }

    private void Login() =>
        Navigation.NavigateTo(
            $"/auth/login?returnUrl={Uri.EscapeDataString(Navigation.Uri)}",
            forceLoad: true);

    private void ShowError(RpcException ex)
    {
        var message = ex.StatusCode switch
        {
            StatusCode.InvalidArgument => MapInvalidArgument(ex.Status.Detail),
            StatusCode.AlreadyExists => L["Sessions_MemberDuplicate"],
            StatusCode.FailedPrecondition => L["Sessions_ActiveLocked"],
            StatusCode.NotFound => L["Error_Generic"],
            _ => L["Error_Generic"]
        };

        Snackbar.Add(message, Severity.Error);
    }

    private string MapInvalidArgument(string detail) => detail switch
    {
        SessionValidationMessages.NameRequired => L["Sessions_NameRequired"],
        SessionValidationMessages.CustomScaleRequired => L["Sessions_CustomScaleRequired"],
        SessionValidationMessages.TaskTitleRequired => L["Sessions_TaskTitleRequired"],
        SessionMemberValidationMessages.EmailRequired => L["Sessions_MemberInvalidEmail"],
        _ => L["Error_Generic"]
    };
}
