using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using DMS.Helpers;
using DMS.Models;
using DMS.Services;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace DMS.Views
{
    public partial class TasksPage : Page
    {
    private readonly IUserService _userService;
    private List<TaskProject> _projects = new();
    private List<User> _users = new();
    private TaskProject? _selectedProject;
    private TaskComponent? _selectedComponent;
    private List<ProjectDailyTaskReportRow> _projectReportRows = new();

    public TasksPage(IUserService userService)
    {
        InitializeComponent();
        _userService = userService;
        ProjectStartDatePicker.SelectedDate = DateTime.Today;
        ProjectDueDatePicker.SelectedDate = DateTime.Today.AddDays(30);
        ComponentDueDatePicker.SelectedDate = DateTime.Today.AddDays(7);
        ProjectStatusComboBox.SelectedIndex = 0;
        ComponentPriorityComboBox.SelectedIndex = 1;
        ProjectReportDatePicker.SelectedDate = DateTime.Today;
        EditComponentPriorityComboBox.SelectedIndex = 1;
        EditComponentStatusComboBox.SelectedIndex = 0;
        Loaded += async (_, _) => await LoadProjectsAsync();
        }

    private async Task LoadProjectsAsync()
    {
        try
        {
            var projectsTask = Task.Run(_userService.GetProjects);
            var usersTask = Task.Run(_userService.GetAllUsers);
            await Task.WhenAll(projectsTask, usersTask);
            _projects = projectsTask.Result;
            _users = usersTask.Result.Where(u => u.IsActive).OrderBy(u => u.Username ?? u.Email).ToList();
            ProjectListBox.ItemsSource = _projects;
            MembersListBox.ItemsSource = _users.Select(user => new MemberOption(user)).ToList();
        }
        catch (Exception ex) { MessageBox.Show($"Unable to load tasks: {ex.Message}", "Task management", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private async void CreateProjectButton_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(ProjectNameTextBox.Text)) { MessageBox.Show("Enter a project name.", "Project", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
        if (MessageBox.Show("Create this project?", "Confirm project", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        try
        {
            var project = new TaskProject
            {
                Name = ProjectNameTextBox.Text,
                Description = ProjectDescriptionTextBox.Text,
                StartDate = ProjectStartDatePicker.SelectedDate ?? DateTime.Today,
                DueDate = ProjectDueDatePicker.SelectedDate ?? DateTime.Today.AddDays(30),
                Status = (ProjectStatusComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? ProjectStatuses.Draft,
                CreatedByAdminId = AppSession.CurrentUserId ?? string.Empty
            };
            await Task.Run(() => _userService.CreateProject(project));
            ProjectNameTextBox.Clear(); ProjectDescriptionTextBox.Clear(); await LoadProjectsAsync();
        }
        catch (Exception ex) { MessageBox.Show($"Unable to create project: {ex.Message}", "Project", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private async void ProjectListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _selectedProject = ProjectListBox.SelectedItem as TaskProject;
        _selectedComponent = null;
        SelectedProjectText.Text = _selectedProject?.Name ?? "Select a project";
        SelectedProjectDescriptionText.Text = _selectedProject?.Description ?? string.Empty;
        EditProjectNameTextBox.Text = _selectedProject?.Name ?? string.Empty;
        EditProjectDescriptionTextBox.Text = _selectedProject?.Description ?? string.Empty;
        EditProjectStartDatePicker.SelectedDate = _selectedProject?.StartDate;
        EditProjectDueDatePicker.SelectedDate = _selectedProject?.DueDate;
        EditProjectStatusComboBox.SelectedIndex = _selectedProject == null
            ? -1
            : Array.FindIndex(new[] { ProjectStatuses.Draft, ProjectStatuses.Active, ProjectStatuses.Completed, ProjectStatuses.Archived }, status => status == _selectedProject.Status);
        SelectedComponentText.Text = "Select a component";
        ComponentListView.ItemsSource = _selectedProject == null ? null : await Task.Run(() => _userService.GetProjectComponents(_selectedProject.Id));
    }

    private async void UpdateProjectButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedProject == null) { MessageBox.Show("Select a project first.", "Project", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
        if (string.IsNullOrWhiteSpace(EditProjectNameTextBox.Text)) { MessageBox.Show("Enter a project name.", "Project", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
        if (MessageBox.Show("Update this project?", "Confirm project update", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        var project = new TaskProject
        {
            Id = _selectedProject.Id,
            Name = EditProjectNameTextBox.Text,
            Description = EditProjectDescriptionTextBox.Text,
            StartDate = EditProjectStartDatePicker.SelectedDate ?? _selectedProject.StartDate,
            DueDate = EditProjectDueDatePicker.SelectedDate ?? _selectedProject.DueDate,
            Status = (EditProjectStatusComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? ProjectStatuses.Draft,
            CreatedByAdminId = _selectedProject.CreatedByAdminId,
            CreatedAt = _selectedProject.CreatedAt
        };

        try
        {
            var updated = await Task.Run(() => _userService.UpdateProject(project));
            _selectedProject = updated;
            SelectedProjectText.Text = updated.Name;
            SelectedProjectDescriptionText.Text = updated.Description;
            await LoadProjectsAsync();
            ProjectListBox.SelectedItem = _projects.FirstOrDefault(item => item.Id == updated.Id);
            MessageBox.Show("Project updated successfully.", "Project", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex) { MessageBox.Show($"Unable to update project: {ex.Message}", "Project", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private async void AddComponentButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedProject == null) { MessageBox.Show("Select a project first.", "Component", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
        if (string.IsNullOrWhiteSpace(ComponentNameTextBox.Text)) { MessageBox.Show("Enter a component name.", "Component", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
        if (MessageBox.Show("Add this component?", "Confirm component", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        try
        {
            var component = new TaskComponent
            {
                ProjectId = _selectedProject.Id, Name = ComponentNameTextBox.Text,
                Description = ComponentDescriptionTextBox.Text,
                Priority = (ComponentPriorityComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? TaskPriorities.Medium,
                DueDate = ComponentDueDatePicker.SelectedDate ?? DateTime.Today.AddDays(7)
            };
            await Task.Run(() => _userService.CreateTaskComponent(component));
            ComponentNameTextBox.Clear(); ComponentDescriptionTextBox.Clear();
            ComponentListView.ItemsSource = await Task.Run(() => _userService.GetProjectComponents(_selectedProject.Id));
        }
        catch (Exception ex) { MessageBox.Show($"Unable to add component: {ex.Message}", "Component", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private async void ComponentListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _selectedComponent = ComponentListView.SelectedItem as TaskComponent;
        SelectedComponentText.Text = _selectedComponent?.Name ?? "Select a component";
        EditComponentNameTextBox.Text = _selectedComponent?.Name ?? string.Empty;
        EditComponentDescriptionTextBox.Text = _selectedComponent?.Description ?? string.Empty;
        EditComponentDueDatePicker.SelectedDate = _selectedComponent?.DueDate;
        EditComponentPriorityComboBox.SelectedIndex = _selectedComponent == null
            ? -1
            : Array.FindIndex(new[] { TaskPriorities.Low, TaskPriorities.Medium, TaskPriorities.High }, priority => priority == _selectedComponent.Priority);
        EditComponentStatusComboBox.SelectedIndex = _selectedComponent == null
            ? -1
            : Array.FindIndex(new[] { TaskStatuses.NotStarted, TaskStatuses.InProgress, TaskStatuses.Blocked, TaskStatuses.Completed }, status => status == _selectedComponent.Status);
        var assigned = _selectedComponent == null
            ? new List<ComponentAssignment>()
            : await Task.Run(() => _userService.GetComponentAssignments(_selectedComponent.Id));
        foreach (var option in (MembersListBox.ItemsSource as IEnumerable<MemberOption>) ?? Enumerable.Empty<MemberOption>())
            option.IsSelected = assigned.Any(a => a.UserId == option.User.Id);
        MembersListBox.Items.Refresh();
    }

    private async void UpdateComponentButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedComponent == null) { MessageBox.Show("Select a component first.", "Component", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
        if (string.IsNullOrWhiteSpace(EditComponentNameTextBox.Text)) { MessageBox.Show("Enter a component name.", "Component", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
        if (MessageBox.Show("Update this component?", "Confirm component update", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        var component = new TaskComponent
        {
            Id = _selectedComponent.Id,
            ProjectId = _selectedComponent.ProjectId,
            Name = EditComponentNameTextBox.Text,
            Description = EditComponentDescriptionTextBox.Text,
            Priority = (EditComponentPriorityComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? TaskPriorities.Medium,
            Status = (EditComponentStatusComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? TaskStatuses.NotStarted,
            DueDate = EditComponentDueDatePicker.SelectedDate ?? _selectedComponent.DueDate,
            CreatedAt = _selectedComponent.CreatedAt
        };

        try
        {
            var updated = await Task.Run(() => _userService.UpdateTaskComponent(component));
            _selectedComponent = updated;
            SelectedComponentText.Text = updated.Name;
            ComponentListView.ItemsSource = await Task.Run(() => _userService.GetProjectComponents(updated.ProjectId));
            ComponentListView.SelectedItem = (ComponentListView.ItemsSource as IEnumerable<TaskComponent>)?.FirstOrDefault(item => item.Id == updated.Id);
            MessageBox.Show("Component updated successfully.", "Component", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex) { MessageBox.Show($"Unable to update component: {ex.Message}", "Component", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private async void SaveAssignmentsButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedComponent == null) { MessageBox.Show("Select a component first.", "Assignments", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
        var options = (MembersListBox.ItemsSource as IEnumerable<MemberOption>)?.ToList() ?? new List<MemberOption>();
        var selected = options.Where(o => o.IsSelected).Select(o => o.User.Id).ToList();
        if (selected.Count == 0) { MessageBox.Show("Select at least one active member.", "Assignments", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
        if (MessageBox.Show($"Assign this component to {selected.Count} member(s)?", "Confirm assignments", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        try { await Task.Run(() => _userService.SetComponentAssignments(_selectedComponent.Id, selected, AppSession.CurrentUserId ?? string.Empty)); MessageBox.Show("Assignments saved.", "Assignments", MessageBoxButton.OK, MessageBoxImage.Information); }
        catch (Exception ex) { MessageBox.Show($"Unable to save assignments: {ex.Message}", "Assignments", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private async void ViewUpdatesButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedComponent == null) { MessageBox.Show("Select a component first.", "Daily updates", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
        try
        {
            var updates = await Task.Run(() => _userService.GetTaskUpdates(
                _selectedComponent.Id, AppSession.CurrentUserId ?? string.Empty, true));
            var userNames = _users.ToDictionary(user => user.Id, user => user.Username ?? user.Email);
            UpdatesListView.ItemsSource = updates.Select(update => new DailyUpdateDisplayRow
            {
                UserName = userNames.TryGetValue(update.UserId, out var userName) ? userName : "Unknown member",
                UpdateDate = update.UpdateDate,
                Status = update.Status,
                Description = update.Description
            }).ToList();
        }
        catch (Exception ex) { MessageBox.Show($"Unable to load daily updates: {ex.Message}", "Daily updates", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private async void LoadProjectReportButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedProject == null) { ProjectReportMessageText.Text = "Select a project first."; return; }
        var projectId = _selectedProject.Id;
        var date = ProjectReportDatePicker.SelectedDate ?? DateTime.Today;
        try
        {
            _projectReportRows = await Task.Run(() => _userService.GetProjectDailyTaskReport(projectId, date));
            ProjectReportGrid.ItemsSource = _projectReportRows;
            ProjectReportMessageText.Text = _projectReportRows.Count == 0
                ? "No assigned components were found for this project."
                : $"Loaded {_projectReportRows.Count} component-member row(s) for {date:yyyy-MM-dd}.";
        }
        catch (Exception ex) { ProjectReportMessageText.Text = $"Unable to load project report: {ex.Message}"; }
    }

    private void GenerateProjectReportButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedProject == null) { ProjectReportMessageText.Text = "Select a project first."; return; }
        if (_projectReportRows.Count == 0)
        {
            ProjectReportMessageText.Text = "Load the project report before generating the PDF.";
            return;
        }

        var date = ProjectReportDatePicker.SelectedDate ?? DateTime.Today;
        if (MessageBox.Show($"Generate the project daily report for {date:yyyy-MM-dd}?", "Confirm PDF report", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        var dialog = new SaveFileDialog
        {
            Title = "Save project daily task report",
            Filter = "PDF files (*.pdf)|*.pdf",
            FileName = $"{_selectedProject.Name}-Daily-Tasks-{date:yyyy-MM-dd}.pdf",
            AddExtension = true,
            OverwritePrompt = true
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            QuestPDF.Settings.License = LicenseType.Community;
            var projectName = _selectedProject.Name;
            var rows = _projectReportRows.ToList();
            Document.Create(document => document.Page(page =>
            {
                page.Margin(30);
                page.Header().Column(column =>
                {
                    column.Item().Text("DMS Project Daily Task Report").FontSize(20).Bold();
                    column.Item().Text($"Project: {projectName}").FontSize(11);
                    column.Item().Text($"Date: {date:yyyy-MM-dd}").FontSize(11);
                });
                page.Content().PaddingTop(18).Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.RelativeColumn(1.5f);
                        columns.RelativeColumn(2);
                        columns.RelativeColumn(1.3f);
                        columns.RelativeColumn(1.1f);
                        columns.RelativeColumn(3);
                    });
                    table.Header(header =>
                    {
                        header.Cell().Element(ReportHeaderCell).Text("Component");
                        header.Cell().Element(ReportHeaderCell).Text("Description");
                        header.Cell().Element(ReportHeaderCell).Text("Member");
                        header.Cell().Element(ReportHeaderCell).Text("Status");
                        header.Cell().Element(ReportHeaderCell).Text("Today's Work");
                    });
                    foreach (var row in rows)
                    {
                        table.Cell().Element(ReportBodyCell).Text(row.ComponentName);
                        table.Cell().Element(ReportBodyCell).Text(row.ComponentDescription);
                        table.Cell().Element(ReportBodyCell).Text(row.UserName);
                        table.Cell().Element(ReportBodyCell).Text(row.Status);
                        table.Cell().Element(ReportBodyCell).Text(row.DailyWork);
                    }
                });
                page.Footer().AlignCenter().Text(text =>
                {
                    text.Span("Generated by ");
                    text.Span(AppSession.CurrentDisplayName ?? AppSession.CurrentUsername ?? "Administrator").Bold();
                });
            })).GeneratePdf(dialog.FileName);
            ProjectReportMessageText.Text = $"PDF report saved to {dialog.FileName}";
        }
        catch (Exception ex) { ProjectReportMessageText.Text = $"Unable to generate PDF report: {ex.Message}"; }
    }

    private static IContainer ReportHeaderCell(IContainer container) =>
        container.Background(Colors.Grey.Darken2).Padding(5).DefaultTextStyle(style => style.FontColor(Colors.White).Bold());

    private static IContainer ReportBodyCell(IContainer container) =>
        container.BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(5);

    private sealed class MemberOption
    {
        public User User { get; }
        public bool IsSelected { get; set; }
        public string DisplayName => User.Username ?? User.Email;
        public MemberOption(User user) => User = user;
    }

    private sealed class DailyUpdateDisplayRow
    {
        public string UserName { get; init; } = string.Empty;
        public DateTime UpdateDate { get; init; }
        public string Status { get; init; } = string.Empty;
        public string Description { get; init; } = string.Empty;
    }
}
}
