using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Universa.Desktop.Models;

namespace Universa.Desktop.Interfaces
{
    public interface IToDoViewModel
    {
        ObservableCollection<ToDo> Todos { get; }
        ToDo SelectedTodo { get; set; }
        string FilterText { get; set; }
        bool ShowCompleted { get; set; }
        bool ShowArchived { get; set; }
        string NewTodoTitle { get; set; }
        string NewTodoDescription { get; set; }
        DateTime? NewTodoStartDate { get; set; }
        DateTime? NewTodoDueDate { get; set; }
        string NewTodoPriority { get; set; }
        string NewTodoCategory { get; set; }
        string NewTodoAssignee { get; set; }
        bool NewTodoIsRecurring { get; set; }
        int NewTodoRecurrenceInterval { get; set; }
        string NewTodoRecurrenceUnit { get; set; }
        string NewTodoTag { get; set; }
        bool ShowCompletedItems { get; set; }
        string SearchText { get; set; }
        bool HideFutureItems { get; set; }

        // Commands
        System.Windows.Input.ICommand AddTodoCommand { get; }
        System.Windows.Input.ICommand DeleteTodoCommand { get; }
        System.Windows.Input.ICommand CompleteTodoCommand { get; }
        System.Windows.Input.ICommand UncompleteTodoCommand { get; }
        System.Windows.Input.ICommand AddSubTaskCommand { get; }
        System.Windows.Input.ICommand RemoveSubTaskCommand { get; }
        System.Windows.Input.ICommand AddTagCommand { get; }
        System.Windows.Input.ICommand RemoveTagCommand { get; }
        System.Windows.Input.ICommand ArchiveTodoCommand { get; }

        // Methods
        Task LoadTodosAsync();
        Task AddTodoAsync();
        Task DeleteTodoAsync(ToDo todo);
        Task CompleteTodoAsync(ToDo todo);
        Task UncompleteTodoAsync(ToDo todo);
        Task AddSubTaskAsync(ToDo parentTodo, ToDo subTask);
        Task RemoveSubTaskAsync(ToDo parentTodo, ToDo subTask);
        Task AddTagAsync(ToDo todo, string tag);
        Task RemoveTagAsync(ToDo todo, string tag);
        Task<bool> HasTagAsync(ToDo todo, string tag);
        Task LoadTagsAsync();
        Task LoadCategoriesAsync();
        Task LoadPrioritiesAsync();
        Task LoadAssigneesAsync();
        Task<bool> IsOverdueAsync(ToDo todo);
        Task<bool> IsDueTodayAsync(ToDo todo);
        Task<bool> IsDueThisWeekAsync(ToDo todo);
        Task<DateTime?> GetNextDueDateAsync(ToDo todo);
        Task ArchiveTodoAsync(ToDo todo);
        void AddTodo();
        void DeleteSubTask(ToDo subtask);
        Task SaveTodosAsync();
        void RefreshTodos();
        void ApplyFilter();
    }
} 