using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Universa.Desktop.Models;

namespace Universa.Desktop.Interfaces
{
    public interface IToDoService
    {
        event EventHandler<TodoChangedEventArgs> TodoChanged;

        string FilePath { get; }

        Task<IEnumerable<ToDo>> GetAllTodosAsync();
        Task<ToDo> GetTodoByIdAsync(string id);
        Task<IEnumerable<ToDo>> GetTodosByTagAsync(string tag);
        Task<IEnumerable<ToDo>> GetTodosByCategoryAsync(string category);
        Task<IEnumerable<ToDo>> GetTodosByPriorityAsync(string priority);
        Task<IEnumerable<ToDo>> GetTodosByAssigneeAsync(string assignee);
        Task<IEnumerable<ToDo>> GetOverdueTodosAsync();
        Task<IEnumerable<ToDo>> GetDueTodayTodosAsync();
        Task<IEnumerable<ToDo>> GetDueThisWeekTodosAsync();
        Task<IEnumerable<ToDo>> GetCompletedTodosAsync();
        Task<IEnumerable<ToDo>> GetIncompleteTodosAsync();
        Task<IEnumerable<ToDo>> GetRecurringTodosAsync();
        Task<IEnumerable<ToDo>> GetSubTasksAsync(string parentId);
        Task<IEnumerable<string>> GetAllTagsAsync();
        Task<IEnumerable<string>> GetAllCategoriesAsync();
        Task<IEnumerable<string>> GetAllPrioritiesAsync();
        Task<IEnumerable<string>> GetAllAssigneesAsync();

        Task<ToDo> CreateTodoAsync(ToDo todo);
        Task UpdateTodoAsync(ToDo todo);
        Task UpdateAllTodosAsync(IEnumerable<ToDo> todos);
        Task DeleteTodoAsync(string id);
        Task CompleteTodoAsync(string id);
        Task UncompleteTodoAsync(string id);
        Task ArchiveTodoAsync(string id);
        Task AddSubTaskAsync(string parentId, ToDo subTask);
        Task RemoveSubTaskAsync(string parentId, string subTaskId);
        Task AddTagAsync(string todoId, string tag);
        Task RemoveTagAsync(string todoId, string tag);
        Task<bool> HasTagAsync(string todoId, string tag);

        Task<bool> IsOverdueAsync(string todoId);
        Task<bool> IsDueTodayAsync(string todoId);
        Task<bool> IsDueThisWeekAsync(string todoId);
        Task<DateTime?> GetNextDueDateAsync(string todoId);
    }

    public class TodoChangedEventArgs : EventArgs
    {
        public string TodoId { get; set; }
        public TodoChangeType ChangeType { get; set; }
    }

    public enum TodoChangeType
    {
        Created,
        Updated,
        Deleted,
        Completed,
        Uncompleted,
        TagAdded,
        TagRemoved,
        SubTaskAdded,
        SubTaskRemoved,
        Modified,
        Archived
    }
}
