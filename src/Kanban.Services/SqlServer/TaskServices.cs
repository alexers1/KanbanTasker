using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using KanbanTasker.Model;
using KanbanTasker.Model.Dto;
using KanbanTasker.Model.Services;

namespace KanbanTasker.Services.SqlServer
{
    public class TaskServices : BaseService, ITaskServices
    {
        public TaskServices(Database.Db db) : base(db)
        {
        }

        public async Task<List<TaskDTO>> GetTasksAsync()
        {
            return await db.Tasks
                .AsNoTracking()
                .OrderBy(t => t.ColumnIndex)
                .ToListAsync();
        }

        public async Task<RowOpResult<TaskDTO>> SaveTaskAsync(TaskDTO task)
        {
            if (task == null)
                throw new ArgumentNullException(nameof(task));

            var result = new RowOpResult<TaskDTO>(task);

            ValidateTask(result);
            if (!result.Success)
                return result;

            if (task.Id == 0)
                db.Tasks.Add(task);
            else
                db.Tasks.Update(task);

            await db.SaveChangesAsync();
            result.Success = true;
            return result;
        }

        public async Task<RowOpResult> DeleteTaskAsync(int id)
        {
            var result = new RowOpResult();

            var task = await db.Tasks.FirstOrDefaultAsync(t => t.Id == id);
            if (task != null)
            {
                db.Tasks.Remove(task);
                await db.SaveChangesAsync();

                // Renumber the remaining cards in that column so ColumnIndex stays
                // contiguous. This data-integrity step used to run as a loop of calls
                // from the WinUI client; it belongs on the server, done atomically.
                var remaining = await db.Tasks
                    .Where(t => t.BoardId == task.BoardId && t.Category == task.Category)
                    .OrderBy(t => t.ColumnIndex)
                    .ToListAsync();
                for (int i = 0; i < remaining.Count; i++)
                    remaining[i].ColumnIndex = i;
                await db.SaveChangesAsync();
            }

            result.Success = true;
            return result;
        }

        public async Task UpdateCardIndexAsync(int id, int currentCardIndex)
        {
            var task = await db.Tasks.FirstOrDefaultAsync(t => t.Id == id);
            if (task != null)
            {
                task.ColumnIndex = currentCardIndex;
                await db.SaveChangesAsync();
            }
        }

        public async Task UpdateColumnDataAsync(TaskDTO task)
        {
            var existing = await db.Tasks.FirstOrDefaultAsync(t => t.Id == task.Id);
            if (existing != null)
            {
                existing.Category = task.Category;
                existing.ColumnIndex = task.ColumnIndex;
                await db.SaveChangesAsync();
            }
        }

        public async Task UpdateColumnNameAsync(int id, string newColName)
        {
            var task = await db.Tasks.FirstOrDefaultAsync(t => t.Id == id);
            if (task != null)
            {
                task.Category = newColName;
                await db.SaveChangesAsync();
            }
        }

        public override RowOpResult<TaskDTO> ValidateTask(RowOpResult<TaskDTO> result)
        {
            return base.ValidateTask(result);
        }
    }
}
