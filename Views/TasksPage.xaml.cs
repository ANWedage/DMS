using System.Windows;
using System.Windows.Controls;
using DMS.Helpers;
using DMS.Models;
using DMS.Services;

namespace DMS.Views
{
    public partial class TasksPage : Page
    {
    private readonly IUserService _userService;
    private List<TaskProject> _projects = new();
    private List<User> _users = new();
    private TaskProject? _selectedProject;
    private TaskComponent? _selectedComponent;

    public TasksPage(IUserService userService)
    {
        InitializeComponent();
        _userService = userService;
        ProjectStartDatePicker.SelectedDate = DateTime.Today;
        ProjectDueDatePicker.SelectedDate = DateTime.Today.AddDays(30);
        ComponentDueDatePicker.SelectedDate = DateTime.Today.AddDays(7);
        ProjectStatusComboBox.SelectedIndex = 0;
        ComponentPriorityComboBox.SelectedIndex = 1;
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
        var assigned = _selectedComponent == null
            ? new List<ComponentAssignment>()
            : await Task.Run(() => _userService.GetComponentAssignments(_selectedComponent.Id));
        foreach (var option in (MembersListBox.ItemsSource as IEnumerable<MemberOption>) ?? Enumerable.Empty<MemberOption>())
            option.IsSelected = assigned.Any(a => a.UserId == option.User.Id);
        MembersListBox.Items.Refresh();
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
            UpdatesListView.ItemsSource = await Task.Run(() => _userService.GetTaskUpdates(
                _selectedComponent.Id, AppSession.CurrentUserId ?? string.Empty, true));
        }
        catch (Exception ex) { MessageBox.Show($"Unable to load daily updates: {ex.Message}", "Daily updates", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private sealed class MemberOption
    {
        public User User { get; }
        public bool IsSelected { get; set; }
        public string DisplayName => User.Username ?? User.Email;
        public MemberOption(User user) => User = user;
    }
}
}
